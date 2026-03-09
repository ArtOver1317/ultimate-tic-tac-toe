using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[assembly: InternalsVisibleTo("Tests.EditMode")]

internal static class BuildExecution
{
    internal static void Execute(BuildRequest request, Func<bool> runEditModeTests)
    {
        if (request is { RunEditModeTests: true, SkipTests: false } && !runEditModeTests())
        {
            BuildFailure.Fail("TestFailed", "EditMode tests failed. Player build cancelled.");
            return;
        }

        switch (request.Target)
        {
            case BuildTargetKind.All:
                BuildPlayerTarget(BuildTargetSpec.Desktop, request.RootBuildPath);
                BuildPlayerTarget(BuildTargetSpec.WebGl, request.RootBuildPath);
                break;
            case BuildTargetKind.Desktop:
                BuildPlayerTarget(BuildTargetSpec.Desktop, request.RootBuildPath);
                break;
            case BuildTargetKind.WebGL:
                BuildPlayerTarget(BuildTargetSpec.WebGl, request.RootBuildPath);
                break;
            case BuildTargetKind.AddressablesOnly:
                BuildAddressables();
                break;
            default:
                BuildFailure.Fail("InvalidBuildTarget", $"Unknown build target '{request.Target}'.");
                break;
        }
    }

    private static void BuildPlayerTarget(BuildTargetSpec targetSpec, string rootBuildPath)
    {
        EnsureActiveBuildTarget(targetSpec.UnityTarget);
        LogScriptingBackend(targetSpec.UnityTarget);
        BuildAddressables();

        var productionScenes = BuildSceneFilter.GetProductionScenePaths(EditorBuildSettings.scenes);
            
        if (productionScenes.Length == 0)
        {
            BuildFailure.Fail("NoProductionScenes", "No enabled production scenes found in Build Settings after filtering test scenes.");
            return;
        }

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = productionScenes,
            target = targetSpec.UnityTarget,
            locationPathName = targetSpec.GetOutputPath(rootBuildPath),
        };

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            
        if (report.summary.result != BuildResult.Succeeded) 
            BuildFailure.Fail("PlayerBuildFailed", report.summary.result + ": " + report.summary.totalErrors + " errors.");
    }

    private static void EnsureActiveBuildTarget(BuildTarget unityBuildTarget)
    {
        if (EditorUserBuildSettings.activeBuildTarget == unityBuildTarget) return;

        var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(unityBuildTarget);
            
        if (buildTargetGroup == BuildTargetGroup.Unknown)
        {
            BuildFailure.Fail("BuildTargetSwitchFailed", $"Build target group for '{unityBuildTarget}' is unknown.");
            return;
        }

        Debug.Log($"[Build] Switching active build target to '{unityBuildTarget}' before Addressables/player build.");
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, unityBuildTarget)) BuildFailure.Fail("BuildTargetSwitchFailed", $"Failed to switch active build target to '{unityBuildTarget}'.");
    }

    private static void LogScriptingBackend(BuildTarget unityBuildTarget)
    {
        var group = BuildPipeline.GetBuildTargetGroup(unityBuildTarget);
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.FromBuildTargetGroup(group));
        Debug.Log($"[Build] Scripting backend for {unityBuildTarget}: {backend}");

        if (unityBuildTarget == BuildTarget.StandaloneWindows64 && backend != ScriptingImplementation.IL2CPP)
        {
            Debug.LogWarning($"[Build] WARNING: Desktop build is using {backend} instead of IL2CPP. " +
                             "NanoSockets P/Invoke may not work correctly with Mono. " +
                             "Set Scripting Backend to IL2CPP in Player Settings or ensure IL2CPP module is installed.");
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
        AddressableAssetSettings.BuildPlayerContent(out var result);
        if (!string.IsNullOrWhiteSpace(result.Error)) BuildFailure.Fail("AddressablesFailed", result.Error);
    }
}

internal readonly struct BuildRequest
{
    internal BuildRequest(BuildTargetKind target, string rootBuildPath, bool runEditModeTests, bool skipTests)
    {
        Target = target;
        RootBuildPath = rootBuildPath;
        RunEditModeTests = runEditModeTests;
        SkipTests = skipTests;
    }

    internal BuildTargetKind Target { get; }

    internal string RootBuildPath { get; }

    internal bool RunEditModeTests { get; }

    internal bool SkipTests { get; }
}

internal enum BuildTargetKind
{
    All,
    Desktop,
    WebGL,
    AddressablesOnly,
}

internal readonly struct BuildTargetSpec
{
    private const string _desktopFolderName = "Desktop";
    private const string _webGlFolderName = "WebGL";
    private const string _windowsExecutableName = "ultimate-tic-tac-toe.exe";

    internal static readonly BuildTargetSpec Desktop = new(BuildTarget.StandaloneWindows64, _desktopFolderName, _windowsExecutableName);
    internal static readonly BuildTargetSpec WebGl = new(BuildTarget.WebGL, _webGlFolderName, string.Empty);

    internal BuildTargetSpec(BuildTarget unityTarget, string outputFolderName, string outputFileName)
    {
        UnityTarget = unityTarget;
        OutputFolderName = outputFolderName;
        OutputFileName = outputFileName;
    }

    internal BuildTarget UnityTarget { get; }

    internal string OutputFolderName { get; }

    internal string OutputFileName { get; }

    internal string GetOutputPath(string rootBuildPath) =>
        string.IsNullOrEmpty(OutputFileName)
            ? $"{rootBuildPath}/{OutputFolderName}"
            : $"{rootBuildPath}/{OutputFolderName}/{OutputFileName}";
}

internal static class BuildSceneFilter
{
    private const string _sceneTestPathPattern = @"(^|/)Tests?(/|$)";
    private static readonly Regex _testSceneRegex = new(_sceneTestPathPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string[] GetProductionScenePaths(EditorBuildSettingsScene[] scenes) => scenes
        .Where(scene => scene.enabled)
        .Select(scene => scene.path)
        .Where(IsProductionScenePath)
        .ToArray();

    private static bool IsProductionScenePath(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath)) 
            return false;

        var normalizedPath = scenePath.Replace('\\', '/');
        return !_testSceneRegex.IsMatch(normalizedPath);
    }
}

internal static class BuildFailure
{
    internal static void Fail(string category, string message)
    {
        Debug.LogError($"[BUILD FAILED: {category}] {message}");

        if (Application.isBatchMode) 
            EditorApplication.Exit(1);

        throw new BuildFailedException(message);
    }
}