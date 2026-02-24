param(
    [ValidateSet('All', 'Desktop', 'WebGL')]
    [string]$Target = 'All',
    [switch]$UseDocker,
    [switch]$TestOnly,
    [switch]$SkipTests,
    [ValidateRange(0, 240)]
    [int]$BuildStallTimeoutMinutes = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Format-Elapsed {
    param(
        [Parameter(Mandatory = $true)]
        [TimeSpan]$Elapsed
    )

    return '{0:mm\:ss}' -f $Elapsed
}

function Get-LogTailText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [int]$TailLines = 40
    )

    if (-not (Test-Path $LogPath)) {
        return '<log file not found>'
    }

    return (Get-Content -Path $LogPath -Tail $TailLines | Out-String).TrimEnd()
}

function Invoke-ProcessWithHeartbeat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,
        [Parameter(Mandatory = $true)]
        [string]$StepName,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [int]$ProgressIntervalSeconds = 15,
        [int]$PollIntervalSeconds = 3,
        [int]$NoLogUpdateTimeoutMinutes = 0
    )

    Write-Host "[$StepName] Started. Log: $LogPath"

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -NoNewWindow -PassThru
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $lastProgressMark = -$ProgressIntervalSeconds

    while (-not $process.HasExited) {
        Start-Sleep -Seconds $PollIntervalSeconds
        $elapsedSeconds = [int]$stopwatch.Elapsed.TotalSeconds
        if (($elapsedSeconds - $lastProgressMark) -lt $ProgressIntervalSeconds) {
            continue
        }

        $lastProgressMark = $elapsedSeconds
        if (Test-Path $LogPath) {
            $logFile = Get-Item -Path $LogPath -ErrorAction SilentlyContinue
            if ($logFile) {
                $sizeKb = [Math]::Round($logFile.Length / 1KB, 1)
                $updatedAt = $logFile.LastWriteTime.ToString('HH:mm:ss')
                $stale = (Get-Date) - $logFile.LastWriteTime
                $staleMinutes = [Math]::Round($stale.TotalMinutes, 1)
                Write-Host "[$StepName] In progress... elapsed $(Format-Elapsed -Elapsed $stopwatch.Elapsed), log ${sizeKb}KB, updated $updatedAt, stale ${staleMinutes}m"

                if ($NoLogUpdateTimeoutMinutes -gt 0 -and $stale.TotalMinutes -ge $NoLogUpdateTimeoutMinutes) {
                    try {
                        if (-not $process.HasExited) {
                            $process.Kill()
                        }
                    }
                    catch {
                    }

                    throw "[$StepName] No log updates for $staleMinutes minutes (timeout=$NoLogUpdateTimeoutMinutes). Process was terminated as likely hung. Log: $LogPath"
                }

                continue
            }
        }

        Write-Host "[$StepName] In progress... elapsed $(Format-Elapsed -Elapsed $stopwatch.Elapsed), waiting for log file"
    }

    $process.WaitForExit()
    $process.Refresh()

    $exitCode = $process.ExitCode
    $exitCodeLabel = if ($null -eq $exitCode) { '<unknown>' } else { "$exitCode" }
    Write-Host "[$StepName] Finished in $(Format-Elapsed -Elapsed $stopwatch.Elapsed), exit code: $exitCodeLabel"

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Elapsed = $stopwatch.Elapsed
    }
}

function Get-TestRunSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResultPath,
        [Parameter(Mandatory = $true)]
        [string]$Platform
    )

    if (-not (Test-Path $ResultPath)) {
        throw "Test result file was not found: $ResultPath"
    }

    [xml]$xml = Get-Content -Path $ResultPath -Raw
    $run = $xml.'test-run'
    if ($null -eq $run) {
        throw "Test result file has unexpected format: $ResultPath"
    }

    return [PSCustomObject]@{
        Platform = $Platform
        Result = [string]$run.result
        Total = [int]$run.total
        Passed = [int]$run.passed
        Failed = [int]$run.failed
        Skipped = [int]$run.skipped
        Inconclusive = [int]$run.inconclusive
        Duration = [string]$run.duration
    }
}

function Write-TestSummary {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Summary,
        [Parameter(Mandatory = $true)]
        [TimeSpan]$WallClockElapsed
    )

    $normalizedDuration = ([string]$Summary.Duration).Replace(',', '.')
    $xmlDurationSeconds = 0.0
    [void][double]::TryParse(
        $normalizedDuration,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$xmlDurationSeconds)

    $wallSeconds = [Math]::Round($WallClockElapsed.TotalSeconds, 2)
    $overheadSeconds = [Math]::Round([Math]::Max(0, $wallSeconds - $xmlDurationSeconds), 2)

    Write-Host "[Tests][$($Summary.Platform)] result=$($Summary.Result), total=$($Summary.Total), passed=$($Summary.Passed), failed=$($Summary.Failed), skipped=$($Summary.Skipped), inconclusive=$($Summary.Inconclusive), testDuration=${xmlDurationSeconds}s, wallClock=${wallSeconds}s, overhead=${overheadSeconds}s"
}

function Assert-ProjectIsNotOpenInUnity {
    $lockFilePath = Join-Path $script:ProjectRoot 'Temp\UnityLockfile'
    $unityProcesses = Get-Process Unity -ErrorAction SilentlyContinue

    if ((Test-Path $lockFilePath) -and $unityProcesses) {
        $pids = ($unityProcesses | Select-Object -ExpandProperty Id) -join ', '
        throw "Detected running Unity Editor instance(s) and project lock file '$lockFilePath'. Close Unity first, then run build.ps1 again. Active Unity PID(s): $pids"
    }
}

function Get-UnityPath {
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) {
        return $env:UNITY_PATH
    }

    $knownPaths = @(
        'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe',
        'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity'
    )

    foreach ($candidatePath in $knownPaths) {
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    $hubRoot = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path $hubRoot) {
        $latestEditor = Get-ChildItem -Path $hubRoot -Directory |
            Sort-Object Name -Descending |
            Select-Object -First 1

        if ($latestEditor) {
            $exePath = Join-Path $latestEditor.FullName 'Editor\Unity.exe'
            if (Test-Path $exePath) {
                return $exePath
            }
        }
    }

    throw "Unity Editor not found. Set UNITY_PATH environment variable or install Unity Hub editor."
}

function Get-GitShortSha {
    $sha = (& git rev-parse --short HEAD).Trim()
    if (-not $sha) {
        throw 'Failed to resolve git short SHA.'
    }

    return $sha
}

function Get-BundleVersion {
    $settingsPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectSettings.asset'
    if (-not (Test-Path $settingsPath)) {
        throw "ProjectSettings.asset not found at $settingsPath"
    }

    $content = Get-Content -Path $settingsPath -Raw
    $match = [regex]::Match($content, '^(\s*bundleVersion:\s*)(.+)$', [System.Text.RegularExpressions.RegexOptions]::Multiline)

    if (-not $match.Success) {
        throw 'bundleVersion field was not found in ProjectSettings.asset'
    }

    return $match.Groups[2].Value.Trim()
}

function Get-ActiveInputHandler {
    $settingsPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectSettings.asset'
    if (-not (Test-Path $settingsPath)) {
        throw "ProjectSettings.asset not found at $settingsPath"
    }

    $content = Get-Content -Path $settingsPath -Raw
    $match = [regex]::Match($content, '^(\s*activeInputHandler:\s*)(-?\d+)\s*$', [System.Text.RegularExpressions.RegexOptions]::Multiline)

    if (-not $match.Success) {
        throw 'activeInputHandler field was not found in ProjectSettings.asset'
    }

    return [int]$match.Groups[2].Value
}

function Set-ActiveInputHandler {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Value
    )

    $settingsPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectSettings.asset'
    if (-not (Test-Path $settingsPath)) {
        throw "ProjectSettings.asset not found at $settingsPath"
    }

    $content = Get-Content -Path $settingsPath -Raw
    $match = [regex]::Match($content, '^(\s*activeInputHandler:\s*)(-?\d+)\s*$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        throw 'activeInputHandler field was not found in ProjectSettings.asset'
    }

    $current = [int]$match.Groups[2].Value
    if ($current -eq $Value) {
        return
    }

    $updated = [regex]::Replace(
        $content,
        '^(\s*activeInputHandler:\s*)(-?\d+)\s*$',
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($m)
            return $m.Groups[1].Value + $Value
        },
        [System.Text.RegularExpressions.RegexOptions]::Multiline)

    Set-Content -Path $settingsPath -Value $updated -NoNewline
}

function Set-BundleVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $settingsPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectSettings.asset'
    $content = Get-Content -Path $settingsPath -Raw
    $match = [regex]::Match($content, '^(\s*bundleVersion:\s*)(.+)$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $match.Success) {
        throw 'bundleVersion field was not found in ProjectSettings.asset'
    }

    $currentVersion = $match.Groups[2].Value.Trim()
    if ([string]::Equals($currentVersion, $Version, [StringComparison]::Ordinal)) {
        return
    }

    $updated = [regex]::Replace(
        $content,
        '^(\s*bundleVersion:\s*).+$',
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($m)
            return $m.Groups[1].Value + $Version
        },
        [System.Text.RegularExpressions.RegexOptions]::Multiline)

    Set-Content -Path $settingsPath -Value $updated -NoNewline
}

function Invoke-UnityTests {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnityPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('EditMode', 'PlayMode')]
        [string]$Platform
    )

    $resultsPath = Join-Path $script:ProjectRoot "TestResults\$Platform-results.xml"
    $logPath = Join-Path $script:ProjectRoot "Logs\$Platform-tests.log"

    New-Item -ItemType Directory -Path (Split-Path $resultsPath -Parent) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $logPath -Parent) -Force | Out-Null

    if (Test-Path $resultsPath) {
        Remove-Item -Path $resultsPath -Force
    }

    $testArgs = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $script:ProjectRoot,
        '-runTests',
        '-testPlatform', $Platform,
        '-testResults', $resultsPath,
        '-logFile', $logPath
    )

    $runInfo = Invoke-ProcessWithHeartbeat -FilePath $UnityPath -ArgumentList $testArgs -StepName "Tests/$Platform" -LogPath $logPath
    if ($null -ne $runInfo.ExitCode -and $runInfo.ExitCode -ne 0) {
        $tail = Get-LogTailText -LogPath $logPath
        throw "Unity $Platform tests failed with exit code $($runInfo.ExitCode). See $logPath`n--- last log lines ---`n$tail"
    }

    if (-not (Test-Path $resultsPath)) {
        throw "Unity $Platform tests did not produce results file: $resultsPath. See $logPath"
    }

    $summary = Get-TestRunSummary -ResultPath $resultsPath -Platform $Platform
    Write-TestSummary -Summary $summary -WallClockElapsed $runInfo.Elapsed

    if ($summary.Failed -gt 0 -or -not $summary.Result.StartsWith('Passed', [StringComparison]::OrdinalIgnoreCase)) {
        $tail = Get-LogTailText -LogPath $logPath
        throw "Unity $Platform tests reported failures in XML (result=$($summary.Result), failed=$($summary.Failed)). See $resultsPath and $logPath`n--- last log lines ---`n$tail"
    }

    return $summary
}

function Resolve-ExecuteMethod {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Desktop', 'WebGL')]
        [string]$ResolvedTarget
    )

    switch ($ResolvedTarget) {
        'All' { return 'BuildScript.BuildAll' }
        'Desktop' { return 'BuildScript.BuildDesktop' }
        'WebGL' { return 'BuildScript.BuildWebGL' }
        default { throw "Unsupported target '$ResolvedTarget'" }
    }
}

function Assert-BuildOutputsExist {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Desktop', 'WebGL')]
        [string]$ResolvedTarget
    )

    $desktopExePath = Join-Path $script:ProjectRoot 'Builds\Desktop\ultimate-tic-tac-toe.exe'
    $webGlIndexPath = Join-Path $script:ProjectRoot 'Builds\WebGL\index.html'

    switch ($ResolvedTarget) {
        'Desktop' {
            if (-not (Test-Path $desktopExePath)) {
                throw "Desktop build artifact not found: $desktopExePath"
            }
        }
        'WebGL' {
            if (-not (Test-Path $webGlIndexPath)) {
                throw "WebGL build artifact not found: $webGlIndexPath"
            }
        }
        'All' {
            if (-not (Test-Path $desktopExePath)) {
                throw "Desktop build artifact not found: $desktopExePath"
            }

            if (-not (Test-Path $webGlIndexPath)) {
                throw "WebGL build artifact not found: $webGlIndexPath"
            }
        }
        default {
            throw "Unsupported target '$ResolvedTarget'"
        }
    }
}

function Invoke-UnityBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnityPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Desktop', 'WebGL')]
        [string]$ResolvedTarget,
        [Parameter(Mandatory = $true)]
        [string]$BundleVersion,
        [Parameter(Mandatory = $true)]
        [int]$StallTimeoutMinutes
    )

    $originalBundleVersion = Get-BundleVersion
    $executeMethod = Resolve-ExecuteMethod -ResolvedTarget $ResolvedTarget
    $previousAllowSkip = $env:ALLOW_SKIP_TESTS

    try {
        Set-BundleVersion -Version $BundleVersion
        $env:ALLOW_SKIP_TESTS = 'true'

        $buildLogPath = Join-Path $script:ProjectRoot 'Logs\build-player.log'
        New-Item -ItemType Directory -Path (Split-Path $buildLogPath -Parent) -Force | Out-Null

        $buildArgs = @(
            '-batchmode',
            '-nographics',
            '-quit',
            '-projectPath', $script:ProjectRoot,
            '-executeMethod', $executeMethod,
            '-buildPath', 'Builds',
            '-skipTests',
            '-logFile', $buildLogPath
        )

        $runInfo = Invoke-ProcessWithHeartbeat -FilePath $UnityPath -ArgumentList $buildArgs -StepName "Build/$ResolvedTarget" -LogPath $buildLogPath -NoLogUpdateTimeoutMinutes $StallTimeoutMinutes
        if ($null -ne $runInfo.ExitCode -and $runInfo.ExitCode -ne 0) {
            $tail = Get-LogTailText -LogPath $buildLogPath
            throw "Unity build failed with exit code $($runInfo.ExitCode). See $buildLogPath`n--- last log lines ---`n$tail"
        }

        if (Test-Path $buildLogPath) {
            $fullLog = Get-Content -Path $buildLogPath -Raw
            if ($fullLog -match '\[BUILD FAILED:') {
                $tail = Get-LogTailText -LogPath $buildLogPath
                throw "Unity build log contains explicit build failure marker. See $buildLogPath`n--- last log lines ---`n$tail"
            }
        }

        Assert-BuildOutputsExist -ResolvedTarget $ResolvedTarget

        if ($null -eq $runInfo.ExitCode) {
            Write-Host "[Build/$ResolvedTarget] Unity returned unknown exit code, but log and build artifacts are valid."
        }
    }
    finally {
        Set-BundleVersion -Version $originalBundleVersion

        if ($null -eq $previousAllowSkip) {
            Remove-Item Env:ALLOW_SKIP_TESTS -ErrorAction SilentlyContinue
        }
        else {
            $env:ALLOW_SKIP_TESTS = $previousAllowSkip
        }
    }
}

function Invoke-DockerBuild {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Desktop', 'WebGL')]
        [string]$ResolvedTarget,
        [switch]$RunTestsOnly,
        [switch]$SkipTestRun,
        [Parameter(Mandatory = $true)]
        [string]$BundleVersion
    )

    if ($RunTestsOnly) {
        & docker compose run --rm test
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose run test failed with exit code $LASTEXITCODE"
        }

        return
    }

    if (-not $SkipTestRun) {
        & docker compose run --rm test
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose run test failed with exit code $LASTEXITCODE"
        }
    }

    if ($ResolvedTarget -eq 'All') {
        $services = @('build-desktop', 'build-webgl')
    }
    elseif ($ResolvedTarget -eq 'Desktop') {
        $services = @('build-desktop')
    }
    else {
        $services = @('build-webgl')
    }

    foreach ($service in $services) {
        & docker compose run --rm -e "BUNDLE_VERSION=$BundleVersion" $service
        if ($LASTEXITCODE -ne 0) {
            throw "docker compose run $service failed with exit code $LASTEXITCODE"
        }
    }
}

$script:ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -Path $script:ProjectRoot

if ($TestOnly -and $SkipTests) {
    throw 'Parameters -TestOnly and -SkipTests cannot be used together.'
}

$bundleVersion = "0.0.0-ci.$(Get-GitShortSha)"

if ($UseDocker) {
    Invoke-DockerBuild -ResolvedTarget $Target -RunTestsOnly:$TestOnly -SkipTestRun:$SkipTests -BundleVersion $bundleVersion
    return
}

$unityPath = Get-UnityPath
Assert-ProjectIsNotOpenInUnity

Write-Host "[Pipeline] Unity: $unityPath"
Write-Host "[Pipeline] Target: $Target (TestOnly=$($TestOnly.IsPresent), SkipTests=$($SkipTests.IsPresent), UseDocker=$($UseDocker.IsPresent))"
Write-Host "[Pipeline] Build stall timeout (no log updates): $BuildStallTimeoutMinutes min"

$originalInputHandler = Get-ActiveInputHandler
try {
    if (-not $SkipTests) {
        # UI Toolkit tests can emit legacy Input API exceptions when Input System is active.
        # Use Both (2) for stable CLI PlayMode tests and restore after pipeline run.
        Set-ActiveInputHandler -Value 2
        Write-Host "[Pipeline] activeInputHandler set to 2 (Both) for test run; original value: $originalInputHandler"

        $editSummary = Invoke-UnityTests -UnityPath $unityPath -Platform 'EditMode'
        $playSummary = Invoke-UnityTests -UnityPath $unityPath -Platform 'PlayMode'

        $total = $editSummary.Total + $playSummary.Total
        $passed = $editSummary.Passed + $playSummary.Passed
        $failed = $editSummary.Failed + $playSummary.Failed
        $skipped = $editSummary.Skipped + $playSummary.Skipped
        $inconclusive = $editSummary.Inconclusive + $playSummary.Inconclusive
        Write-Host "[Tests][Total] total=$total, passed=$passed, failed=$failed, skipped=$skipped, inconclusive=$inconclusive"

        if ($TestOnly) {
            Write-Host 'TestOnly mode completed successfully.'
            return
        }
    }
    else {
        Write-Host '[Pipeline] Tests are skipped by -SkipTests.'
    }

    Invoke-UnityBuild -UnityPath $unityPath -ResolvedTarget $Target -BundleVersion $bundleVersion -StallTimeoutMinutes $BuildStallTimeoutMinutes
    Write-Host "Build completed successfully for target '$Target'."
}
finally {
    if (-not $SkipTests) {
        Set-ActiveInputHandler -Value $originalInputHandler
        Write-Host "[Pipeline] activeInputHandler restored to $originalInputHandler"
    }
}
