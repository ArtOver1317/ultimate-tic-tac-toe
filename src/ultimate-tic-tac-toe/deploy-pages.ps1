param(
    [string]$WebGlPath = "Builds/WebGL",
    [string]$Branch = "gh-pages",
    [string]$CommitMessage = "Deploy WebGL build to GitHub Pages"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -Path $projectRoot

$fullWebGlPath = Join-Path $projectRoot $WebGlPath
$indexPath = Join-Path $fullWebGlPath 'index.html'

if (-not (Test-Path $fullWebGlPath)) {
    throw "WebGL build folder not found: $fullWebGlPath"
}

if (-not (Test-Path $indexPath)) {
    throw "WebGL build seems invalid (index.html not found): $indexPath"
}

$gitRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $gitRoot) {
    throw 'Failed to detect git repository root.'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tempWorktree = Join-Path $env:TEMP ("gh-pages-worktree-$timestamp")
$remoteBranchRef = "origin/$Branch"
$remoteBranchExists = $false

$lsRemote = & git ls-remote --heads origin $Branch
if ($LASTEXITCODE -eq 0 -and $lsRemote) {
    $remoteBranchExists = $true
}

try {
    Push-Location $gitRoot

    if ($remoteBranchExists) {
        & git worktree add --force $tempWorktree $remoteBranchRef
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create worktree for $remoteBranchRef"
        }
    }
    else {
        & git worktree add --detach $tempWorktree
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to create detached worktree for gh-pages initialization.'
        }

        Push-Location $tempWorktree
        & git checkout --orphan $Branch
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create orphan branch '$Branch'."
        }
        Pop-Location
    }

    Push-Location $tempWorktree

    Get-ChildItem -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $fullWebGlPath '*') -Destination $tempWorktree -Recurse -Force

    $noJekyllPath = Join-Path $tempWorktree '.nojekyll'
    if (-not (Test-Path $noJekyllPath)) {
        New-Item -ItemType File -Path $noJekyllPath | Out-Null
    }

    & git add -A
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to stage files for gh-pages deployment.'
    }

    & git diff --cached --quiet
    $hasChanges = $LASTEXITCODE -ne 0

    if (-not $hasChanges) {
        Write-Host "[deploy-pages] No changes to publish on '$Branch'."
        Pop-Location
        Pop-Location
        return
    }

    & git commit -m $CommitMessage
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to commit gh-pages deployment.'
    }

    if ($remoteBranchExists) {
        & git push origin "HEAD:$Branch"
    }
    else {
        & git push -u origin $Branch
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push '$Branch' branch to origin."
    }

    Write-Host "[deploy-pages] Published WebGL build to '$Branch'."
    Write-Host "[deploy-pages] URL: https://<owner>.github.io/<repo>/"

    Pop-Location
    Pop-Location
}
finally {
    if (Test-Path $tempWorktree) {
        Push-Location $gitRoot
        & git worktree remove $tempWorktree --force | Out-Null
        Pop-Location
    }
}
