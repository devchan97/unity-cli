using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage Unity assets. Actions: find, import, move, delete, deps, labels.")]
    public static class ManageAssets
    {
        public class Parameters
        {
            [ToolParameter("Action to perform: find, import, move, delete, deps, labels", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Asset path (required for import, move, delete, deps, labels)")]
            public string Path { get; set; }

            [ToolParameter("Asset type filter (used with find action)")]
            public string Type { get; set; }

            [ToolParameter("Asset name filter (used with find action)")]
            public string Name { get; set; }

            [ToolParameter("Asset label filter (used with find action)")]
            public string Label { get; set; }

            [ToolParameter("Folder to search in (used with find action)")]
            public string Folder { get; set; }

            [ToolParameter("Source path (required for move action)")]
            public string From { get; set; }

            [ToolParameter("Destination path (required for move action)")]
            public string To { get; set; }

            [ToolParameter("Include recursive dependencies (used with deps action, default true)")]
            public bool Recursive { get; set; }

            [ToolParameter("Labels to set on asset (used with labels action; if omitted, returns current labels)")]
            public string[] Set { get; set; }
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
                case "find":
                    return FindAssets(p);
                case "import":
                    return ImportAsset(p);
                case "move":
                    return MoveAsset(p);
                case "delete":
                    return DeleteAsset(p);
                case "deps":
                    return GetDependencies(p);
                case "labels":
                    return ManageLabels(p);
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: find, import, move, delete, deps, labels.");
            }
        }

        private static object FindAssets(ToolParams p)
        {
            string type = p.Get("type");
            string name = p.Get("name");
            string label = p.Get("label");
            string folder = p.Get("folder");

            var filterParts = new List<string>();
            if (!string.IsNullOrEmpty(name)) filterParts.Add(name);
            if (!string.IsNullOrEmpty(type)) filterParts.Add($"t:{type}");
            if (!string.IsNullOrEmpty(label)) filterParts.Add($"l:{label}");

            string filter = string.Join(" ", filterParts);
            string[] searchFolders = string.IsNullOrEmpty(folder) ? null : new[] { folder };

            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets(filter, searchFolders)
                : AssetDatabase.FindAssets(filter);

            var results = guids.Select(guid =>
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                return new
                {
                    path = assetPath,
                    type = assetType != null ? assetType.Name : "Unknown",
                    guid
                };
            }).ToArray();

            return new SuccessResponse($"Found {results.Length} asset(s).", results);
        }

        private static object ImportAsset(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for import action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            try
            {
                AssetDatabase.ImportAsset(pathResult.Value, ImportAssetOptions.Default);
                return new SuccessResponse($"Imported asset at '{pathResult.Value}'.");
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to import asset: {ex.Message}");
            }
        }

        private static object MoveAsset(ToolParams p)
        {
            var fromResult = p.GetRequired("from", "'from' parameter is required for move action.");
            if (!fromResult.IsSuccess)
                return new ErrorResponse(fromResult.ErrorMessage);

            var toResult = p.GetRequired("to", "'to' parameter is required for move action.");
            if (!toResult.IsSuccess)
                return new ErrorResponse(toResult.ErrorMessage);

            string error = AssetDatabase.MoveAsset(fromResult.Value, toResult.Value);
            if (string.IsNullOrEmpty(error))
                return new SuccessResponse($"Moved asset from '{fromResult.Value}' to '{toResult.Value}'.");

            return new ErrorResponse($"Failed to move asset: {error}");
        }

        private static object DeleteAsset(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for delete action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            bool deleted = AssetDatabase.DeleteAsset(pathResult.Value);
            return deleted
                ? new SuccessResponse($"Deleted asset at '{pathResult.Value}'.")
                : new ErrorResponse($"Failed to delete asset at '{pathResult.Value}'.");
        }

        private static object GetDependencies(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for deps action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            bool recursive = p.GetBool("recursive", true);

            string[] deps = AssetDatabase.GetDependencies(pathResult.Value, recursive);

            var results = deps
                .Where(d => d != pathResult.Value)
                .Select(d =>
                {
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(d);
                    return new
                    {
                        path = d,
                        type = assetType != null ? assetType.Name : "Unknown"
                    };
                })
                .ToArray();

            return new SuccessResponse($"Found {results.Length} dependenc(ies) for '{pathResult.Value}'.", results);
        }

        private static object ManageLabels(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for labels action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            var asset = AssetDatabase.LoadMainAssetAtPath(pathResult.Value);
            if (asset == null)
                return new ErrorResponse($"Asset not found at '{pathResult.Value}'.");

            var setToken = p.GetRaw("set");
            if (setToken != null && setToken.Type != JTokenType.Null)
            {
                string[] labels;
                if (setToken.Type == JTokenType.Array)
                    labels = setToken.ToObject<string[]>();
                else
                    labels = new[] { setToken.ToString() };

                AssetDatabase.SetLabels(asset, labels);
                AssetDatabase.SaveAssets();
                return new SuccessResponse($"Set {labels.Length} label(s) on '{pathResult.Value}'.", labels);
            }

            string[] currentLabels = AssetDatabase.GetLabels(asset);
            return new SuccessResponse($"Asset '{pathResult.Value}' has {currentLabels.Length} label(s).", currentLabels);
        }
    }
}
