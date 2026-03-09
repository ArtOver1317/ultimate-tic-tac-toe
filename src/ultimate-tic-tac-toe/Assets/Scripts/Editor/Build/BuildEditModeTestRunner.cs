using System;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class BuildEditModeTestRunner
{
    private const string _activeInputHandlerPropertyName = "activeInputHandler";
    private const int _inputHandlerBoth = 2;
    private static readonly PropertyInfo _activeInputHandlerProperty = typeof(PlayerSettings).GetProperty(_activeInputHandlerPropertyName, BindingFlags.Public | BindingFlags.Static);
    private static readonly TimeSpan _testRunnerTimeout = TimeSpan.FromMinutes(20);

    internal static bool RunWithInputHandlerScope()
    {
        var hasOriginalValue = TryGetActiveInputHandler(out var originalValue);
        var switchedInputHandler = false;

        try
        {
            if (hasOriginalValue && originalValue != _inputHandlerBoth)
            {
                switchedInputHandler = TrySetActiveInputHandler(_inputHandlerBoth);
                
                if (switchedInputHandler) 
                    Debug.Log($"[Build] Temporarily set activeInputHandler to Both ({_inputHandlerBoth}) for test run.");
            }

            return RunEditModeTests();
        }
        finally
        {
            if (switchedInputHandler && hasOriginalValue)
            {
                if (TrySetActiveInputHandler(originalValue))
                    Debug.Log($"[Build] Restored activeInputHandler to {originalValue}.");
                else
                    Debug.LogWarning("[Build] Failed to restore activeInputHandler after tests. Restore it manually in Player Settings.");
            }
        }
    }

    private static bool RunEditModeTests()
    {
        using var waitHandle = new ManualResetEventSlim(false);
        var callback = new EditModeTestCallbacks(waitHandle);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();

        try
        {
            api.RegisterCallbacks(callback);
                
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
            }));

            var completed = waitHandle.Wait(_testRunnerTimeout);
                
            if (!completed)
            {
                BuildFailure.Fail("TestTimeout", $"EditMode test run timed out after {_testRunnerTimeout.TotalMinutes} minutes.");
                return false;
            }

            return callback.Passed;
        }
        finally
        {
            api.UnregisterCallbacks(callback);
            Object.DestroyImmediate(api);
        }
    }

    private static bool TryGetActiveInputHandler(out int value)
    {
        value = 0;

        try
        {
            if (_activeInputHandlerProperty == null || !_activeInputHandlerProperty.CanRead) 
                return false;

            var currentValue = _activeInputHandlerProperty.GetValue(null);
            
            if (currentValue == null) 
                return false;

            value = Convert.ToInt32(currentValue);
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
            if (_activeInputHandlerProperty == null || !_activeInputHandlerProperty.CanWrite) 
                return false;

            var boxedValue = _activeInputHandlerProperty.PropertyType.IsEnum
                ? Enum.ToObject(_activeInputHandlerProperty.PropertyType, value)
                : Convert.ChangeType(value, _activeInputHandlerProperty.PropertyType);

            _activeInputHandlerProperty.SetValue(null, boxedValue);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Build] Failed to set activeInputHandler to {value}: {ex.Message}");
            return false;
        }
    }
}

internal sealed class EditModeTestCallbacks : ICallbacks
{
    private readonly ManualResetEventSlim _waitHandle;

    internal EditModeTestCallbacks(ManualResetEventSlim waitHandle)
    {
        _waitHandle = waitHandle ?? throw new ArgumentNullException(nameof(waitHandle));
        Passed = true;
    }

    internal bool Passed { get; private set; }

    public void RunStarted(ITestAdaptor testsToRun) { }

    public void RunFinished(ITestResultAdaptor result)
    {
        Passed = result is { FailCount: 0 };
        _waitHandle.Set();
    }

    public void TestStarted(ITestAdaptor test) { }

    public void TestFinished(ITestResultAdaptor result) { }
}