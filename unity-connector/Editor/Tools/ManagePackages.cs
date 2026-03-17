using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage Unity packages. Actions: list, add, remove.")]
    public static class ManagePackages
    {
        private const int DEFAULT_TIMEOUT_SEC = 60;

        public class Parameters
        {
            [ToolParameter("Action to perform: list, add, remove", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Package name (required for add/remove, e.g. 'com.unity.inputsystem' or 'com.unity.inputsystem@1.0.0')")]
            public string Name { get; set; }

            [ToolParameter("Timeout in seconds (default: 60)")]
            public int Timeout { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();
            int timeout = p.GetInt("timeout", DEFAULT_TIMEOUT_SEC) ?? DEFAULT_TIMEOUT_SEC;

            switch (action)
            {
                case "list":
                    return await ListPackages(timeout);

                case "add":
                {
                    var nameResult = p.GetRequired("name", "'name' parameter is required for add action.");
                    if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);
                    return await AddPackage(nameResult.Value, timeout);
                }

                case "remove":
                {
                    var nameResult = p.GetRequired("name", "'name' parameter is required for remove action.");
                    if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);
                    return await RemovePackage(nameResult.Value, timeout);
                }

                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: list, add, remove.");
            }
        }

        private static async Task WaitForRequest(Request request, int timeoutSec)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (!request.IsCompleted)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Package operation timed out after {timeoutSec} seconds.");
                await Task.Delay(100);
            }
        }

        private static async Task<object> ListPackages(int timeout)
        {
            var request = Client.List();
            try
            {
                await WaitForRequest(request, timeout);
            }
            catch (TimeoutException ex)
            {
                return new ErrorResponse(ex.Message);
            }

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse(request.Error?.message ?? "Failed to list packages.");

            var packages = request.Result.Select(pkg => new
            {
                name = pkg.name,
                version = pkg.version,
                displayName = pkg.displayName,
                source = pkg.source.ToString()
            }).ToArray();

            return new SuccessResponse($"Found {packages.Length} packages.", packages);
        }

        private static async Task<object> AddPackage(string name, int timeout)
        {
            var request = Client.Add(name);
            try
            {
                await WaitForRequest(request, timeout);
            }
            catch (TimeoutException ex)
            {
                return new ErrorResponse(ex.Message);
            }

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse(request.Error?.message ?? $"Failed to add package '{name}'.");

            var pkg = request.Result;
            return new SuccessResponse($"Package '{pkg.displayName}' added.", new
            {
                name = pkg.name,
                version = pkg.version,
                displayName = pkg.displayName,
                source = pkg.source.ToString()
            });
        }

        private static async Task<object> RemovePackage(string name, int timeout)
        {
            var request = Client.Remove(name);
            try
            {
                await WaitForRequest(request, timeout);
            }
            catch (TimeoutException ex)
            {
                return new ErrorResponse(ex.Message);
            }

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse(request.Error?.message ?? $"Failed to remove package '{name}'.");

            return new SuccessResponse($"Package '{name}' removed.");
        }
    }
}
