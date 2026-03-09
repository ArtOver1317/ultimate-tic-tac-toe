using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class BuildScript
{
    private const string _defaultBuildPath = "Builds";
    private const string _skipTestsFlag = "-skipTests";
    private const string _buildPathFlag = "-buildPath";
    private const string _buildTargetFlag = "-buildTarget";
    private const string _pipelineTargetFlag = "-pipelineTarget";

    public static void BuildAll() => ExecuteBatchBuild(BuildTargetKind.All);

    public static void BuildDesktop() => ExecuteBatchBuild(BuildTargetKind.Desktop);

    public static void BuildWebGL() => ExecuteBatchBuild(BuildTargetKind.WebGL);

    public static void BuildAddressablesOnly() => ExecuteBatchBuild(BuildTargetKind.AddressablesOnly);

    [MenuItem("Tools/Build/Build All Platforms")]
    private static void MenuBuildAll() => ExecuteMenuBuild(BuildTargetKind.All, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build Desktop (Windows x64)")]
    private static void MenuBuildDesktop() => ExecuteMenuBuild(BuildTargetKind.Desktop, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build WebGL")]
    private static void MenuBuildWebGL() => ExecuteMenuBuild(BuildTargetKind.WebGL, requiresPlayModeConfirmation: true);

    [MenuItem("Tools/Build/Build Addressables Only")]
    private static void MenuBuildAddressablesOnly() => ExecuteMenuBuild(BuildTargetKind.AddressablesOnly, requiresPlayModeConfirmation: false);

    private static void ExecuteBatchBuild(BuildTargetKind defaultTarget)
    {
        try
        {
            AssertSkipTestsIsAllowed();
            var buildRequest = CreateBatchRequest(defaultTarget);
            BuildExecution.Execute(buildRequest, BuildEditModeTestRunner.RunWithInputHandlerScope);
        }
        catch (BuildFailedException)
        {
            if (!Application.isBatchMode) 
                throw;
        }
    }

    private static void ExecuteMenuBuild(BuildTargetKind menuTarget, bool requiresPlayModeConfirmation)
    {
        try
        {
            if (requiresPlayModeConfirmation && !ConfirmPlayModeTestsExecuted()) 
                return;

            var buildRequest = CreateMenuRequest(menuTarget);
            BuildExecution.Execute(buildRequest, BuildEditModeTestRunner.RunWithInputHandlerScope);
            EditorUtility.DisplayDialog("Build", "Build completed successfully.", "OK");
        }
        catch (BuildFailedException ex)
        {
            EditorUtility.DisplayDialog("Build failed", ex.Message, "OK");
        }
    }

    private static BuildRequest CreateBatchRequest(BuildTargetKind defaultTarget) =>
        new(ParseBuildTarget(defaultTarget), GetArgValue(_buildPathFlag, _defaultBuildPath), runEditModeTests: true, skipTests: HasFlag(_skipTestsFlag));

    private static BuildRequest CreateMenuRequest(BuildTargetKind target) =>
        new(target, _defaultBuildPath, runEditModeTests: false, skipTests: false);

    private static bool ConfirmPlayModeTestsExecuted() => EditorUtility.DisplayDialog(
        "Tests confirmation",
        "Вы запустили тесты через build.ps1 -TestOnly перед этой сборкой?\n\n" +
        "(Menu build не запускает EditMode/PlayMode тесты автоматически.)",
        "Да",
        "Нет");

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
        if (!HasFlag(_skipTestsFlag)) 
            return;

        var allowSkipTests = Environment.GetEnvironmentVariable("ALLOW_SKIP_TESTS");
        
        if (!string.Equals(allowSkipTests, "true", StringComparison.OrdinalIgnoreCase)) 
            BuildFailure.Fail("SkipTestsOutsideCI", "Flag -skipTests requires ALLOW_SKIP_TESTS=true.");
    }

    private static BuildTargetKind ParseBuildTarget(BuildTargetKind fallback)
    {
        var value = GetArgValue(_buildTargetFlag, string.Empty);
        
        if (string.IsNullOrWhiteSpace(value)) 
            value = GetArgValue(_pipelineTargetFlag, fallback.ToString());

        if (Enum.TryParse(value, true, out BuildTargetKind parsed)) 
            return parsed;

        BuildFailure.Fail("InvalidBuildTarget", $"Unsupported build target value '{value}'. Expected: All, Desktop, WebGL, AddressablesOnly.");
        return fallback;
    }
}