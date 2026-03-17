using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage Unity builds. Actions: build, report, list_targets.")]
    public static class ManageBuild
    {
        public class Parameters
        {
            [ToolParameter("Action to perform: build, report, list_targets", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Build target (e.g. Android, iOS, StandaloneWindows64; required for build action)")]
            public string Target { get; set; }

            [ToolParameter("Output path (required for build action)")]
            public string Output { get; set; }

            [ToolParameter("Scene paths to include (optional for build; defaults to EditorBuildSettings scenes)")]
            public string[] Scenes { get; set; }

            [ToolParameter("Enable development build (optional for build action, default false)")]
            public bool Development { get; set; }
        }

        private static BuildReport _lastBuildReport;

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
                case "build":
                    return BuildPlayer(p);
                case "report":
                    return GetBuildReport();
                case "list_targets":
                    return ListTargets();
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: build, report, list_targets.");
            }
        }

        private static object BuildPlayer(ToolParams p)
        {
            var targetResult = p.GetRequired("target", "'target' parameter is required for build action.");
            if (!targetResult.IsSuccess)
                return new ErrorResponse(targetResult.ErrorMessage);

            var outputResult = p.GetRequired("output", "'output' parameter is required for build action.");
            if (!outputResult.IsSuccess)
                return new ErrorResponse(outputResult.ErrorMessage);

            if (!Enum.TryParse<BuildTarget>(targetResult.Value, true, out var buildTarget))
                return new ErrorResponse($"Unknown build target: '{targetResult.Value}'. Use list_targets to see available targets.");

            var scenesToken = p.GetRaw("scenes");
            string[] scenes;
            if (scenesToken != null && scenesToken.Type == JTokenType.Array)
            {
                scenes = scenesToken.ToObject<string[]>();
            }
            else
            {
                scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();
            }

            if (scenes.Length == 0)
                return new ErrorResponse("No scenes to build. Add scenes to build settings or provide a 'scenes' array.");

            bool development = p.GetBool("development", false);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputResult.Value,
                target = buildTarget,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            try
            {
                var report = BuildPipeline.BuildPlayer(options);
                _lastBuildReport = report;

                var summary = report.summary;
                return new SuccessResponse($"Build {summary.result} for {buildTarget}.", new
                {
                    result = summary.result.ToString(),
                    target = buildTarget.ToString(),
                    outputPath = summary.outputPath,
                    totalSize = summary.totalSize,
                    totalTime = summary.totalTime.ToString(),
                    totalErrors = summary.totalErrors,
                    totalWarnings = summary.totalWarnings
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Build failed: {ex.Message}");
            }
        }

        private static object GetBuildReport()
        {
            if (_lastBuildReport == null)
                return new ErrorResponse("No build report available. Run a build first.");

            var summary = _lastBuildReport.summary;

            var steps = _lastBuildReport.steps.Select(step => new
            {
                name = step.name,
                duration = step.duration.ToString()
            }).ToArray();

            return new SuccessResponse($"Last build: {summary.result} for {summary.platform}.", new
            {
                result = summary.result.ToString(),
                platform = summary.platform.ToString(),
                outputPath = summary.outputPath,
                totalSize = summary.totalSize,
                totalTime = summary.totalTime.ToString(),
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                steps
            });
        }

        private static object ListTargets()
        {
            var targets = Enum.GetValues(typeof(BuildTarget))
                .Cast<BuildTarget>()
                .Where(t =>
                {
                    try
                    {
                        return BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(t), t);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Select(t => new
                {
                    name = t.ToString(),
                    group = BuildPipeline.GetBuildTargetGroup(t).ToString()
                })
                .ToArray();

            return new SuccessResponse($"Found {targets.Length} supported build target(s).", targets);
        }
    }
}
