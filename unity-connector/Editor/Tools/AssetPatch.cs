using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Patch Unity asset files with text replacements, or read raw asset content. Actions: patch, read.")]
    public static class AssetPatch
    {
        public class Parameters
        {
            [ToolParameter("Action: patch, read", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Asset path (e.g. Assets/Scenes/Main.unity)", Required = true)]
            public string Path { get; set; }

            [ToolParameter("Array of replacements: [{old, new}, ...]", Required = false)]
            public object Replacements { get; set; }

            [ToolParameter("Line offset for read action", Required = false)]
            public int Offset { get; set; }

            [ToolParameter("Max lines to return for read action", Required = false)]
            public int Lines { get; set; }
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
                case "patch":
                    return PatchAsset(p);
                case "read":
                    return ReadAsset(p);
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: patch, read.");
            }
        }

        private static object PatchAsset(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for patch action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string assetPath = pathResult.Value;
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", assetPath));

            if (!File.Exists(fullPath))
                return new ErrorResponse($"File not found: '{assetPath}' (resolved to '{fullPath}').");

            var replacementsToken = p.GetRaw("replacements");
            if (replacementsToken == null || replacementsToken.Type == JTokenType.Null)
                return new ErrorResponse("'replacements' parameter is required for patch action.");

            JArray replacementsArray;
            if (replacementsToken.Type == JTokenType.Array)
            {
                replacementsArray = (JArray)replacementsToken;
            }
            else
            {
                return new ErrorResponse("'replacements' must be a JSON array of {old, new} objects.");
            }

            if (replacementsArray.Count == 0)
                return new ErrorResponse("'replacements' array is empty.");

            try
            {
                string content = File.ReadAllText(fullPath);
                int totalReplacements = 0;
                var details = new List<object>();

                foreach (var item in replacementsArray)
                {
                    if (item.Type != JTokenType.Object)
                    {
                        return new ErrorResponse("Each replacement must be an object with 'old' and 'new' fields.");
                    }

                    var obj = (JObject)item;
                    string oldText = obj["old"]?.ToString();
                    string newText = obj["new"]?.ToString();

                    if (string.IsNullOrEmpty(oldText))
                        return new ErrorResponse("Each replacement must have a non-empty 'old' field.");
                    if (newText == null)
                        return new ErrorResponse("Each replacement must have a 'new' field.");

                    int count = CountOccurrences(content, oldText);
                    if (count > 0)
                    {
                        content = content.Replace(oldText, newText);
                        totalReplacements += count;
                    }

                    details.Add(new { old_text = oldText, new_text = newText, occurrences = count });
                }

                File.WriteAllText(fullPath, content);
                AssetDatabase.ImportAsset(assetPath);
                AssetDatabase.ForceReserializeAssets(new List<string> { assetPath });

                return new SuccessResponse(
                    $"Applied {totalReplacements} replacement(s) to '{assetPath}'.",
                    new { path = assetPath, total_replacements = totalReplacements, details });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to patch asset: {ex.Message}");
            }
        }

        private static object ReadAsset(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for read action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string assetPath = pathResult.Value;
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", assetPath));

            if (!File.Exists(fullPath))
                return new ErrorResponse($"File not found: '{assetPath}' (resolved to '{fullPath}').");

            try
            {
                string[] allLines = File.ReadAllLines(fullPath);
                int offset = p.GetInt("offset", 0) ?? 0;
                int maxLines = p.GetInt("lines", 100) ?? 100;

                if (offset < 0) offset = 0;
                if (offset >= allLines.Length)
                {
                    return new SuccessResponse(
                        $"Offset {offset} exceeds total lines ({allLines.Length}) in '{assetPath}'.",
                        new { path = assetPath, total_lines = allLines.Length, offset, lines = new string[0] });
                }

                var lines = allLines.Skip(offset).Take(maxLines).ToArray();

                return new SuccessResponse(
                    $"Read {lines.Length} line(s) from '{assetPath}' (offset={offset}, total={allLines.Length}).",
                    new { path = assetPath, total_lines = allLines.Length, offset, lines });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to read asset: {ex.Message}");
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }
    }
}
