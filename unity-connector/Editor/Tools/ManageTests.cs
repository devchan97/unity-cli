using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage Unity Test Runner. Actions: list, run, results. Requires com.unity.test-framework package.")]
    public static class ManageTests
    {
        private static Type _testRunnerApiType;
        private static Type _filterType;
        private static Type _executionSettingsType;
        private static Type _testModeType;
        private static Type _callbacksType;
        private static Type _testAdaptorType;
        private static Type _testResultAdaptorType;
        private static object _callbacksInstance;
        private static bool _initialized;
        private static string _initError;

        private static readonly List<object> _lastResults = new List<object>();
        private static bool _runInProgress;

        static ManageTests()
        {
            Initialize();
        }

        private static void Initialize()
        {
            _initialized = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (_testRunnerApiType != null) break;
                    _testRunnerApiType = asm.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi");
                }

                if (_testRunnerApiType == null)
                {
                    _initError = "com.unity.test-framework package is not installed. Add it via ManagePackages.";
                    return;
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _filterType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.Filter");
                    _executionSettingsType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.ExecutionSettings");
                    _testModeType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.TestMode");
                    _callbacksType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.ICallbacks");

                    if (_filterType != null && _executionSettingsType != null &&
                        _testModeType != null && _callbacksType != null)
                        break;
                }

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _testAdaptorType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.ITestAdaptor");
                    _testResultAdaptorType ??= asm.GetType("UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor");
                    if (_testAdaptorType != null && _testResultAdaptorType != null) break;
                }
            }
            catch (Exception e)
            {
                _initError = $"Failed to initialize test runner reflection: {e.Message}";
            }
        }

        public class Parameters
        {
            [ToolParameter("Action to perform: list, run, results", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Test mode: EditMode or PlayMode (required for run)")]
            public string Mode { get; set; }

            [ToolParameter("Test name filter (optional for run, filters tests by name)")]
            public string Filter { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case "list":
                    return ListTests(p);

                case "run":
                    return RunTests(p);

                case "results":
                    return GetResults();

                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: list, run, results.");
            }
        }

        private static object ListTests(ToolParams p)
        {
            if (_initError != null)
                return new ErrorResponse(_initError);

            string modeStr = p.Get("mode", "EditMode");
            object testMode = ParseTestMode(modeStr);
            if (testMode == null)
                return new ErrorResponse($"Invalid mode: '{modeStr}'. Use 'EditMode' or 'PlayMode'.");

            return new SuccessResponse("Test framework is available.", new
            {
                frameworkAvailable = true,
                mode = modeStr,
                callbacksRegistered = _callbacksInstance != null,
                hint = "Use 'run' action to execute tests. Results are captured automatically. Use 'results' to view."
            });
        }

        private static object RunTests(ToolParams p)
        {
            if (_initError != null)
                return new ErrorResponse(_initError);

            if (_runInProgress)
                return new ErrorResponse("A test run is already in progress. Check 'results' for status.");

            var modeResult = p.GetRequired("mode", "'mode' parameter is required (EditMode or PlayMode).");
            if (!modeResult.IsSuccess) return new ErrorResponse(modeResult.ErrorMessage);

            string modeStr = modeResult.Value;
            string filter = p.Get("filter");

            try
            {
                var apiInstance = ScriptableObject.CreateInstance(_testRunnerApiType);
                if (apiInstance == null)
                    return new ErrorResponse("Failed to create TestRunnerApi instance.");

                object testMode = ParseTestMode(modeStr);
                if (testMode == null)
                {
                    UnityEngine.Object.DestroyImmediate(apiInstance);
                    return new ErrorResponse($"Invalid mode: '{modeStr}'. Use 'EditMode' or 'PlayMode'.");
                }

                // Register callbacks for results
                RegisterResultCallbacks(apiInstance);

                // Create execution settings
                var settings = Activator.CreateInstance(_executionSettingsType);

                // Create filter
                var filterObj = Activator.CreateInstance(_filterType);

                // Set test mode on filter
                var testModeField = _filterType.GetField("testMode",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (testModeField != null)
                    testModeField.SetValue(filterObj, testMode);

                // Set name filter if provided
                if (!string.IsNullOrEmpty(filter))
                {
                    var testNamesField = _filterType.GetField("testNames",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (testNamesField != null)
                        testNamesField.SetValue(filterObj, new[] { filter });
                }

                // Set filters on execution settings
                var filtersField = _executionSettingsType.GetField("filters",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (filtersField == null)
                {
                    // Try property
                    var filtersProp = _executionSettingsType.GetProperty("filters",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (filtersProp != null)
                    {
                        var filtersArray = Array.CreateInstance(_filterType, 1);
                        filtersArray.SetValue(filterObj, 0);
                        filtersProp.SetValue(settings, filtersArray);
                    }
                }
                else
                {
                    var filtersArray = Array.CreateInstance(_filterType, 1);
                    filtersArray.SetValue(filterObj, 0);
                    filtersField.SetValue(settings, filtersArray);
                }

                // Execute
                var executeMethod = _testRunnerApiType.GetMethod("Execute",
                    BindingFlags.Instance | BindingFlags.Public);
                if (executeMethod == null)
                {
                    UnityEngine.Object.DestroyImmediate(apiInstance);
                    return new ErrorResponse("Could not find Execute method on TestRunnerApi.");
                }

                _lastResults.Clear();
                _runInProgress = true;
                executeMethod.Invoke(apiInstance, new[] { settings });

                return new SuccessResponse($"Test run started in {modeStr} mode." +
                    (string.IsNullOrEmpty(filter) ? "" : $" Filter: '{filter}'.") +
                    " Use 'results' action to check outcomes.");
            }
            catch (Exception e)
            {
                _runInProgress = false;
                return new ErrorResponse($"Error running tests: {e.Message}");
            }
        }

        private static object GetResults()
        {
            if (_initError != null)
                return new ErrorResponse(_initError);

            if (_runInProgress)
            {
                return new SuccessResponse("Test run in progress.", new
                {
                    inProgress = true,
                    completedSoFar = _lastResults.Count
                });
            }

            if (_lastResults.Count == 0)
            {
                return new SuccessResponse("No test results available. Run tests first with the 'run' action.");
            }

            return new SuccessResponse($"Test results: {_lastResults.Count} tests.", _lastResults.ToArray());
        }

        private static object ParseTestMode(string modeStr)
        {
            if (_testModeType == null) return null;
            try
            {
                if (modeStr.Equals("EditMode", StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(_testModeType, "EditMode");
                if (modeStr.Equals("PlayMode", StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(_testModeType, "PlayMode");
            }
            catch { }
            return null;
        }

        private static void RegisterResultCallbacks(object apiInstance)
        {
            try
            {
                var registerMethod = _testRunnerApiType.GetMethod("RegisterCallbacks",
                    BindingFlags.Instance | BindingFlags.Public);
                if (registerMethod == null) return;

                if (_callbacksInstance == null)
                    _callbacksInstance = CreateCallbacksProxy();

                if (_callbacksInstance == null)
                {
                    Debug.LogWarning("[ManageTests] Could not create ICallbacks proxy. Results will not be captured.");
                    return;
                }

                registerMethod.Invoke(apiInstance, new[] { _callbacksInstance });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ManageTests] Could not register callbacks: {e.Message}");
            }
        }

        private static object CreateCallbacksProxy()
        {
            if (_callbacksType == null || _testAdaptorType == null || _testResultAdaptorType == null)
                return null;

            try
            {
                var assemblyName = new AssemblyName("DynamicTestCallbacks");
                var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    assemblyName, AssemblyBuilderAccess.Run);
                var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
                var typeBuilder = moduleBuilder.DefineType(
                    "DynamicCallbacksImpl",
                    TypeAttributes.Public | TypeAttributes.Class,
                    typeof(object),
                    new[] { _callbacksType });

                // Build proxy methods for all 4 interface methods
                BuildProxyMethod(typeBuilder, "RunStarted", new[] { _testAdaptorType },
                    typeof(ManageTests).GetMethod("OnRunStarted", BindingFlags.Public | BindingFlags.Static));
                BuildProxyMethod(typeBuilder, "RunFinished", new[] { _testResultAdaptorType },
                    typeof(ManageTests).GetMethod("OnRunFinished", BindingFlags.Public | BindingFlags.Static));
                BuildProxyMethod(typeBuilder, "TestStarted", new[] { _testAdaptorType },
                    typeof(ManageTests).GetMethod("OnTestStarted", BindingFlags.Public | BindingFlags.Static));
                BuildProxyMethod(typeBuilder, "TestFinished", new[] { _testResultAdaptorType },
                    typeof(ManageTests).GetMethod("OnTestFinished", BindingFlags.Public | BindingFlags.Static));

                var proxyType = typeBuilder.CreateType();
                return Activator.CreateInstance(proxyType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ManageTests] Failed to create ICallbacks proxy: {e.Message}");
                return null;
            }
        }

        private static void BuildProxyMethod(TypeBuilder typeBuilder, string methodName,
            Type[] paramTypes, MethodInfo targetStatic)
        {
            var method = typeBuilder.DefineMethod(methodName,
                MethodAttributes.Public | MethodAttributes.Virtual,
                typeof(void), paramTypes);
            var il = method.GetILGenerator();
            if (targetStatic != null)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, targetStatic);
            }
            il.Emit(OpCodes.Ret);

            var interfaceMethod = _callbacksType.GetMethod(methodName);
            if (interfaceMethod != null)
                typeBuilder.DefineMethodOverride(method, interfaceMethod);
        }

        public static void OnRunStarted(object testsToRun)
        {
            _lastResults.Clear();
            _runInProgress = true;
        }

        public static void OnRunFinished(object testResults)
        {
            _runInProgress = false;
        }

        public static void OnTestStarted(object test)
        {
            // No-op: could track in-progress count if needed
        }

        public static void OnTestFinished(object result)
        {
            try
            {
                _lastResults.Add(ExtractSingleResult(result));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ManageTests] Error extracting test result: {e.Message}");
            }
        }

        private static object ExtractSingleResult(object result)
        {
            if (result == null) return null;
            var type = result.GetType();

            string GetStr(string propName)
            {
                var prop = type.GetProperty(propName);
                return prop?.GetValue(result)?.ToString();
            }

            double GetDouble(string propName)
            {
                var prop = type.GetProperty(propName);
                var val = prop?.GetValue(result);
                if (val is double d) return d;
                if (val is float f) return f;
                if (double.TryParse(val?.ToString(), out var parsed)) return parsed;
                return 0;
            }

            var testObj = type.GetProperty("Test")?.GetValue(result);
            string testName = testObj?.GetType().GetProperty("FullName")?.GetValue(testObj)?.ToString();

            return new
            {
                name = testName ?? GetStr("Name") ?? GetStr("FullName"),
                status = GetStr("TestStatus") ?? GetStr("ResultState"),
                duration = GetDouble("Duration"),
                message = GetStr("Message"),
                stackTrace = GetStr("StackTrace"),
            };
        }
    }
}
