using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage Unity scenes. Actions: list, open, save, create, hierarchy, find.")]
    public static class ManageScene
    {
        public class Parameters
        {
            [ToolParameter("Action to perform: list, open, save, create, hierarchy, find", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Scene path (required for open, optional for create)")]
            public string Path { get; set; }

            [ToolParameter("Open mode: Single or Additive (default Single, used with open action)")]
            public string Mode { get; set; }

            [ToolParameter("GameObject name to search for (required for find action)")]
            public string Name { get; set; }

            [ToolParameter("Tag filter for find action")]
            public string Tag { get; set; }

            [ToolParameter("Max hierarchy depth (default 10, used with hierarchy action)")]
            public int Depth { get; set; }
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
                    return ListScenes();
                case "open":
                    return OpenScene(p);
                case "save":
                    return SaveScenes();
                case "create":
                    return CreateScene(p);
                case "hierarchy":
                    return GetHierarchy(p);
                case "find":
                    return FindGameObjects(p);
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: list, open, save, create, hierarchy, find.");
            }
        }

        private static object ListScenes()
        {
            var scenes = EditorBuildSettings.scenes
                .Select((s, i) => new
                {
                    index = i,
                    path = s.path,
                    enabled = s.enabled
                })
                .ToArray();

            return new SuccessResponse($"Found {scenes.Length} scene(s) in build settings.", scenes);
        }

        private static object OpenScene(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter is required for open action.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            string modeStr = p.Get("mode", "Single");
            OpenSceneMode mode;
            if (string.Equals(modeStr, "Additive", StringComparison.OrdinalIgnoreCase))
                mode = OpenSceneMode.Additive;
            else
                mode = OpenSceneMode.Single;

            try
            {
                var scene = EditorSceneManager.OpenScene(pathResult.Value, mode);
                return new SuccessResponse($"Opened scene '{scene.name}' ({mode}).", new
                {
                    name = scene.name,
                    path = scene.path,
                    mode = mode.ToString()
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to open scene: {ex.Message}");
            }
        }

        private static object SaveScenes()
        {
            bool saved = EditorSceneManager.SaveOpenScenes();
            return saved
                ? new SuccessResponse("All open scenes saved.")
                : new ErrorResponse("Failed to save open scenes.");
        }

        private static object CreateScene(ToolParams p)
        {
            string path = p.Get("path");

            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                if (!string.IsNullOrEmpty(path))
                {
                    bool saved = EditorSceneManager.SaveScene(scene, path);
                    if (!saved)
                        return new ErrorResponse($"Created new scene but failed to save to '{path}'.");

                    return new SuccessResponse($"Created and saved new scene at '{path}'.", new
                    {
                        name = scene.name,
                        path = scene.path
                    });
                }

                return new SuccessResponse("Created new untitled scene.", new
                {
                    name = scene.name
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to create scene: {ex.Message}");
            }
        }

        private static object GetHierarchy(ToolParams p)
        {
            int maxDepth = p.GetInt("depth", 10).Value;
            if (maxDepth <= 0) maxDepth = 10;

            var activeScene = SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();

            var hierarchy = new JArray();
            foreach (var go in rootObjects)
            {
                hierarchy.Add(BuildGameObjectNode(go, maxDepth, 0));
            }

            return new SuccessResponse($"Hierarchy of scene '{activeScene.name}' ({rootObjects.Length} root objects).", hierarchy);
        }

        private static JObject BuildGameObjectNode(GameObject go, int maxDepth, int currentDepth)
        {
            var node = new JObject
            {
                ["name"] = go.name,
                ["activeSelf"] = go.activeSelf
            };

            var components = go.GetComponents<Component>();
            var componentNames = new JArray();
            foreach (var comp in components)
            {
                if (comp != null)
                    componentNames.Add(comp.GetType().Name);
            }
            node["components"] = componentNames;

            if (currentDepth < maxDepth && go.transform.childCount > 0)
            {
                var children = new JArray();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i).gameObject;
                    children.Add(BuildGameObjectNode(child, maxDepth, currentDepth + 1));
                }
                node["children"] = children;
            }

            return node;
        }

        private static object FindGameObjects(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for find action.");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            string searchName = nameResult.Value;
            string tag = p.Get("tag");

            var activeScene = SceneManager.GetActiveScene();
            var rootObjects = activeScene.GetRootGameObjects();
            var results = new List<object>();

            foreach (var root in rootObjects)
            {
                SearchGameObject(root, searchName, tag, results);
            }

            return new SuccessResponse($"Found {results.Count} GameObject(s) matching '{searchName}'.", results);
        }

        private static void SearchGameObject(GameObject go, string name, string tag, List<object> results)
        {
            bool nameMatch = go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
            bool tagMatch = string.IsNullOrEmpty(tag) || string.Equals(go.tag, tag, StringComparison.OrdinalIgnoreCase);

            if (nameMatch && tagMatch)
            {
                var components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray();

                results.Add(new
                {
                    name = go.name,
                    path = GetGameObjectPath(go),
                    tag = go.tag,
                    layer = LayerMask.LayerToName(go.layer),
                    activeSelf = go.activeSelf,
                    activeInHierarchy = go.activeInHierarchy,
                    components
                });
            }

            for (int i = 0; i < go.transform.childCount; i++)
            {
                SearchGameObject(go.transform.GetChild(i).gameObject, name, tag, results);
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            var current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
