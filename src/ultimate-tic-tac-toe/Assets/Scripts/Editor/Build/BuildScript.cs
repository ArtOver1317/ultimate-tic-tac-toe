using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class BuildScript
{
    private const string SceneTestPathPattern = @"(^|/)Tests?(/|$)";
    private static readonly Regex TestSceneRegex = new(SceneTestPathPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string SkipTestsFlag = "-skipTests";
    private const string BuildPathFlag = "-buildPath";
    private const string BuildTargetFlag = "-buildTarget";
    private const string PipelineTargetFlag = "-pipelineTarget";
    private const int InputHandlerBoth = 2;
    private static readonly TimeSpan TestRunnerTimeout = TimeSpan.FromMinutes(20);

    public static void BuildAll() => ExecuteBatchBuild(BuildTarget.All);

    public static void BuildDesktop() => ExecuteBatchBuild(BuildTarget.Desktop);

    public static void BuildWebGL() => ExecuteBatchBuild(BuildTarget.WebGL);

    public static void BuildAddressablesOnly() => ExecuteBatchBuild(BuildTarget.AddressablesOnly);

    [MenuItem("Tools/Build/Build All Platforms")]
    private static void MenuBuildAll() => ExecuteMenuBuild(BuildTarget.All, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build Desktop (Windows x64)")]
    private static void MenuBuildDesktop() => ExecuteMenuBuild(BuildTarget.Desktop, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build WebGL")]
    private static void MenuBuildWebGL() => ExecuteMenuBuild(BuildTarget.WebGL, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build Addressables Only")]
    private static void MenuBuildAddressablesOnly() => ExecuteMenuBuild(BuildTarget.AddressablesOnly, requiresPlayModeConfirmation: false);

    private static void ExecuteBatchBuild(BuildTarget defaultTarget)
    {
        try
        {
            AssertSkipTestsIsAllowed();

            var buildTarget = ParseBuildTarget(defaultTarget);
            var buildPath = GetArgValue(BuildPathFlag, "Builds");

            BuildInternal(buildTarget, buildPath);
        }
        catch (BuildFailedException)
        {
            if (!Application.isBatchMode)
                throw;
        }
    }

    private static void ExecuteMenuBuild(BuildTarget menuTarget, bool requiresPlayModeConfirmation)
    {
        try
        {
            if (requiresPlayModeConfirmation && !ConfirmPlayModeTestsExecuted())
                return;

            BuildInternal(menuTarget, "Builds", runEditModeTests: false);
            EditorUtility.DisplayDialog("Build", "Build completed successfully.", "OK");
        }
        catch (BuildFailedException ex)
        {
            EditorUtility.DisplayDialog("Build failed", ex.Message, "OK");
        }
    }

    private static bool ConfirmPlayModeTestsExecuted() => EditorUtility.DisplayDialog(
        "Tests confirmation",
        "Вы запустили тесты через build.ps1 -TestOnly перед этой сборкой?\n\n" +
        "(Menu build не запускает EditMode/PlayMode тесты автоматически.)",
        "Да",
        "Нет");

    private static void BuildInternal(BuildTarget target, string rootBuildPath, bool runEditModeTests = true)
    {
        var skipTests = HasFlag(SkipTestsFlag);
        if (runEditModeTests && !skipTests)
        {
            if (!RunEditModeTestsWithInputHandlerScope())
            {
                FailWith("TestFailed", "EditMode tests failed. Player build cancelled.");
                return;
            }
        }

        switch (target)
        {
            case BuildTarget.All:
                BuildPlayerTarget(UnityEditor.BuildTarget.StandaloneWindows64, rootBuildPath, "Desktop");
                BuildPlayerTarget(UnityEditor.BuildTarget.WebGL, rootBuildPath, "WebGL");
                break;
            case BuildTarget.Desktop:
                BuildPlayerTarget(UnityEditor.BuildTarget.StandaloneWindows64, rootBuildPath, "Desktop");
                break;
            case BuildTarget.WebGL:
                BuildPlayerTarget(UnityEditor.BuildTarget.WebGL, rootBuildPath, "WebGL");
                break;
            case BuildTarget.AddressablesOnly:
                BuildAddressables();
                break;
            default:
                FailWith("InvalidBuildTarget", $"Unknown build target '{target}'.");
                break;
        }
    }

    private static void BuildPlayerTarget(UnityEditor.BuildTarget unityBuildTarget, string rootBuildPath, string folderName)
    {
        EnsureActiveBuildTarget(unityBuildTarget);
        BuildAddressables();

        var productionScenes = GetProductionScenes();
        if (productionScenes.Length == 0)
        {
            FailWith("NoProductionScenes", "No enabled production scenes found in Build Settings after filtering test scenes.");
            return;
        }

        var outputPath = BuildOutputPath.For(unityBuildTarget, rootBuildPath, folderName);
        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = productionScenes,
            target = unityBuildTarget,
            locationPathName = outputPath,
        };

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        if (report.summary.result != BuildResult.Succeeded)
        {
            FailWith("PlayerBuildFailed", report.summary.result + ": " + report.summary.totalErrors + " errors.");
        }
    }

    private static void EnsureActiveBuildTarget(UnityEditor.BuildTarget unityBuildTarget)
    {
        if (EditorUserBuildSettings.activeBuildTarget == unityBuildTarget)
            return;

        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(unityBuildTarget);
        if (buildTargetGroup == UnityEditor.BuildTargetGroup.Unknown)
        {
            FailWith("BuildTargetSwitchFailed", $"Build target group for '{unityBuildTarget}' is unknown.");
            return;
        }

        Debug.Log($"[Build] Switching active build target to '{unityBuildTarget}' before Addressables/player build.");
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, unityBuildTarget))
        {
            FailWith("BuildTargetSwitchFailed", $"Failed to switch active build target to '{unityBuildTarget}'.");
        }
    }

    private static void BuildAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[Build] AddressableAssetSettings not found. Continue without Addressables build.");
            return;
        }

        AddressableAssetSettings.CleanPlayerContent();
        AddressablesPlayerBuildResult result;
        AddressableAssetSettings.BuildPlayerContent(out result);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            FailWith("AddressablesFailed", result.Error);
        }
    }

    private static string[] GetProductionScenes() => EditorBuildSettings.scenes
        .Where(scene => scene.enabled)
        .Select(scene => scene.path)
        .Where(IsProductionScenePath)
        .ToArray();

    private static bool IsProductionScenePath(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return false;

        var normalizedPath = scenePath.Replace('\\', '/');
        return !TestSceneRegex.IsMatch(normalizedPath);
    }

    private static bool RunEditModeTests()
    {
        using var waitHandle = new ManualResetEventSlim(false);

        var callback = new EditModeTestCallbacks(waitHandle);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();

        api.RegisterCallbacks(callback);
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
        }));

        var completed = waitHandle.Wait(TestRunnerTimeout);
        api.UnregisterCallbacks(callback);
        ScriptableObject.DestroyImmediate(api);

        if (!completed)
        {
            FailWith("TestTimeout", $"EditMode test run timed out after {TestRunnerTimeout.TotalMinutes} minutes.");
            return false;
        }

        return callback.Passed;
    }

    private static bool RunEditModeTestsWithInputHandlerScope()
    {
        var hasOriginal = TryGetActiveInputHandler(out var originalValue);
        var switched = false;

        try
        {
            if (hasOriginal && originalValue != InputHandlerBoth)
            {
                switched = TrySetActiveInputHandler(InputHandlerBoth);
                if (switched)
                    Debug.Log($"[Build] Temporarily set activeInputHandler to Both ({InputHandlerBoth}) for test run.");
            }

            return RunEditModeTests();
        }
        finally
        {
            if (switched && hasOriginal)
            {
                if (TrySetActiveInputHandler(originalValue))
                    Debug.Log($"[Build] Restored activeInputHandler to {originalValue}.");
                else
                    Debug.LogWarning("[Build] Failed to restore activeInputHandler after tests. Restore it manually in Player Settings.");
            }
        }
    }

    private static bool TryGetActiveInputHandler(out int value)
    {
        value = default;

        try
        {
            var property = typeof(PlayerSettings).GetProperty("activeInputHandler", BindingFlags.Public | BindingFlags.Static);
            if (property == null || !property.CanRead)
                return false;

            var current = property.GetValue(null);
            if (current == null)
                return false;

            value = Convert.ToInt32(current);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Build] Failed to read activeInputHandler: {ex.Message}");
            return false;
        }
    }

    private static bool TrySetActiveInputHandler(int value)
    {
        try
        {
            var property = typeof(PlayerSettings).GetProperty("activeInputHandler", BindingFlags.Public | BindingFlags.Static);
            if (property == null || !property.CanWrite)
                return false;

            object boxedValue;
            if (property.PropertyType.IsEnum)
                boxedValue = Enum.ToObject(property.PropertyType, value);
            else
                boxedValue = Convert.ChangeType(value, property.PropertyType);

            property.SetValue(null, boxedValue);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Build] Failed to set activeInputHandler to {value}: {ex.Message}");
            return false;
        }
    }

    private static bool HasFlag(string argName) => Environment.GetCommandLineArgs()
        .Any(arg => string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase));

    private static string GetArgValue(string argName, string defaultValue)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], argName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal))
                return args[index + 1];

            break;
        }

        return defaultValue;
    }

    private static void AssertSkipTestsIsAllowed()
    {
        if (!HasFlag(SkipTestsFlag))
            return;

        var allowSkipTests = Environment.GetEnvironmentVariable("ALLOW_SKIP_TESTS");
        if (!string.Equals(allowSkipTests, "true", StringComparison.OrdinalIgnoreCase))
        {
            FailWith("SkipTestsOutsideCI", "Flag -skipTests requires ALLOW_SKIP_TESTS=true.");
        }
    }

    private static BuildTarget ParseBuildTarget(BuildTarget fallback)
    {
        var value = GetArgValue(BuildTargetFlag, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
            value = GetArgValue(PipelineTargetFlag, fallback.ToString());

        if (Enum.TryParse(value, true, out BuildTarget parsed))
            return parsed;

        FailWith("InvalidBuildTarget", $"Unsupported build target value '{value}'. Expected: All, Desktop, WebGL, AddressablesOnly.");
        return fallback;
    }

    private static void FailWith(string category, string message)
    {
        Debug.LogError($"[BUILD FAILED: {category}] {message}");

        if (Application.isBatchMode)
            EditorApplication.Exit(1);

        throw new BuildFailedException(message);
    }

    private enum BuildTarget
    {
        All,
        Desktop,
        WebGL,
        AddressablesOnly,
    }

    private static class BuildOutputPath
    {
        public static string For(UnityEditor.BuildTarget target, string rootBuildPath, string folderName) =>
            target == UnityEditor.BuildTarget.StandaloneWindows64
                ? $"{rootBuildPath}/{folderName}/ultimate-tic-tac-toe.exe"
                : $"{rootBuildPath}/{folderName}";
    }

    private sealed class EditModeTestCallbacks : ICallbacks
    {
        private readonly ManualResetEventSlim _waitHandle;

        public EditModeTestCallbacks(ManualResetEventSlim waitHandle)
        {
            _waitHandle = waitHandle ?? throw new ArgumentNullException(nameof(waitHandle));
            Passed = true;
        }

        public bool Passed { get; private set; }

        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            Passed = result != null && result.FailCount == 0;
            _waitHandle.Set();
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }
    }
}