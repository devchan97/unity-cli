using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage GameObjects. Actions: create, find, inspect, add_component, set_property, destroy.")]
    public static class ManageGameObject
    {
        private const int MaxPropertyDepth = 3;

        public class Parameters
        {
            [ToolParameter("Action: create, find, inspect, add_component, set_property, destroy", Required = true)]
            public string Action { get; set; }

            [ToolParameter("GameObject name (required for all actions)")]
            public string Name { get; set; }

            [ToolParameter("Parent GameObject name (optional for create)")]
            public string Parent { get; set; }

            [ToolParameter("Component type name (required for add_component, set_property)")]
            public string Component { get; set; }

            [ToolParameter("Property name (required for set_property)")]
            public string Property { get; set; }

            [ToolParameter("Property value (required for set_property)")]
            public string Value { get; set; }
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
                case "create":
                    return CreateGameObject(p);
                case "find":
                    return FindGameObjects(p);
                case "inspect":
                    return InspectGameObject(p);
                case "add_component":
                    return AddComponent(p);
                case "set_property":
                    return SetProperty(p);
                case "destroy":
                    return DestroyGameObject(p);
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: create, find, inspect, add_component, set_property, destroy.");
            }
        }

        private static object CreateGameObject(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for create action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            string parentName = p.Get("parent");
            Transform parentTransform = null;

            if (!string.IsNullOrEmpty(parentName))
            {
                var parentGo = FindFirstGameObject(parentName);
                if (parentGo == null)
                    return new ErrorResponse($"Parent GameObject '{parentName}' not found.");
                parentTransform = parentGo.transform;
            }

            var go = new GameObject(nameResult.Value);
            if (parentTransform != null)
                go.transform.SetParent(parentTransform, false);

            Undo.RegisterCreatedObjectUndo(go, $"Create {nameResult.Value}");

            return new SuccessResponse($"GameObject '{nameResult.Value}' created.", new
            {
                name = go.name,
                path = GetHierarchyPath(go.transform)
            });
        }

        private static object FindGameObjects(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for find action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var results = new List<object>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    SearchHierarchy(root.transform, nameResult.Value, results);
                }
            }

            if (results.Count == 0)
                return new SuccessResponse($"No GameObjects found matching '{nameResult.Value}'.", results);

            return new SuccessResponse($"Found {results.Count} GameObjects matching '{nameResult.Value}'.", results);
        }

        private static void SearchHierarchy(Transform transform, string searchName, List<object> results)
        {
            if (transform.name.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var go = transform.gameObject;
                results.Add(new
                {
                    name = go.name,
                    path = GetHierarchyPath(transform),
                    activeSelf = go.activeSelf,
                    tag = go.tag,
                    layer = go.layer,
                    components = go.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToArray()
                });
            }

            for (int i = 0; i < transform.childCount; i++)
                SearchHierarchy(transform.GetChild(i), searchName, results);
        }

        private static object InspectGameObject(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for inspect action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var go = FindFirstGameObject(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject '{nameResult.Value}' not found.");

            var components = new List<object>();
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;

                var properties = new List<object>();
                try
                {
                    var so = new SerializedObject(component);
                    var prop = so.GetIterator();
                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            properties.Add(new
                            {
                                name = prop.name,
                                type = prop.propertyType.ToString(),
                                value = GetSerializedPropertyValue(prop)
                            });
                        } while (prop.NextVisible(false) && prop.depth < MaxPropertyDepth);
                    }
                    so.Dispose();
                }
                catch (Exception)
                {
                    // Some components may not support serialization
                }

                components.Add(new
                {
                    type = component.GetType().Name,
                    properties
                });
            }

            return new SuccessResponse($"Inspected '{go.name}'.", new
            {
                name = go.name,
                path = GetHierarchyPath(go.transform),
                tag = go.tag,
                layer = go.layer,
                active = go.activeSelf,
                components
            });
        }

        private static object AddComponent(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for add_component action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var componentResult = p.GetRequired("component", "'component' parameter is required for add_component action.");
            if (!componentResult.IsSuccess) return new ErrorResponse(componentResult.ErrorMessage);

            var go = FindFirstGameObject(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject '{nameResult.Value}' not found.");

            Type componentType = ResolveComponentType(componentResult.Value);
            if (componentType == null)
                return new ErrorResponse($"Component type '{componentResult.Value}' not found.");

            var component = Undo.AddComponent(go, componentType);
            if (component == null)
                return new ErrorResponse($"Failed to add component '{componentResult.Value}' to '{go.name}'.");

            return new SuccessResponse($"Added '{componentType.Name}' to '{go.name}'.", new
            {
                gameObject = go.name,
                component = componentType.Name
            });
        }

        private static object SetProperty(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for set_property action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var componentResult = p.GetRequired("component", "'component' parameter is required for set_property action.");
            if (!componentResult.IsSuccess) return new ErrorResponse(componentResult.ErrorMessage);

            var propertyResult = p.GetRequired("property", "'property' parameter is required for set_property action.");
            if (!propertyResult.IsSuccess) return new ErrorResponse(propertyResult.ErrorMessage);

            string valueStr = p.Get("value");
            if (valueStr == null)
                return new ErrorResponse("'value' parameter is required for set_property action.");

            var go = FindFirstGameObject(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject '{nameResult.Value}' not found.");

            Type componentType = ResolveComponentType(componentResult.Value);
            if (componentType == null)
                return new ErrorResponse($"Component type '{componentResult.Value}' not found.");

            var component = go.GetComponent(componentType);
            if (component == null)
                return new ErrorResponse($"Component '{componentResult.Value}' not found on '{go.name}'.");

            var so = new SerializedObject(component);
            var prop = so.FindProperty(propertyResult.Value);
            if (prop == null)
            {
                so.Dispose();
                return new ErrorResponse($"Property '{propertyResult.Value}' not found on '{componentResult.Value}'.");
            }

            bool success = SetSerializedPropertyValue(prop, valueStr);
            if (!success)
            {
                so.Dispose();
                return new ErrorResponse($"Failed to set property '{propertyResult.Value}' (type: {prop.propertyType}) to '{valueStr}'.");
            }

            so.ApplyModifiedProperties();
            so.Dispose();

            return new SuccessResponse($"Set '{propertyResult.Value}' on '{componentResult.Value}' to '{valueStr}'.", new
            {
                gameObject = go.name,
                component = componentResult.Value,
                property = propertyResult.Value,
                value = valueStr
            });
        }

        private static object DestroyGameObject(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for destroy action.");
            if (!nameResult.IsSuccess) return new ErrorResponse(nameResult.ErrorMessage);

            var go = FindFirstGameObject(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject '{nameResult.Value}' not found.");

            string goName = go.name;
            Undo.DestroyObjectImmediate(go);

            return new SuccessResponse($"GameObject '{goName}' destroyed.");
        }

        private static GameObject FindFirstGameObject(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInHierarchy(root.transform, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static GameObject FindInHierarchy(Transform transform, string name)
        {
            if (transform.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return transform.gameObject;

            for (int i = 0; i < transform.childCount; i++)
            {
                var found = FindInHierarchy(transform.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        private static Type ResolveComponentType(string typeName)
        {
            // Try common UnityEngine types first
            Type type = typeof(Component).Assembly.GetType($"UnityEngine.{typeName}");
            if (type != null) return type;

            // Try UnityEngine.UI
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType($"UnityEngine.UI.{typeName}");
                if (type != null) return type;
            }

            // Try exact name across all assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;
            }

            // Try searching by short name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                            typeof(Component).IsAssignableFrom(t))
                            return t;
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException) { }
            }

            return null;
        }

        private static string GetSerializedPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return prop.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return prop.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Color:
                    return prop.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "null";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return prop.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return prop.rectValue.ToString();
                case SerializedPropertyType.Bounds:
                    return prop.boundsValue.ToString();
                case SerializedPropertyType.Quaternion:
                    return prop.quaternionValue.eulerAngles.ToString();
                case SerializedPropertyType.Vector2Int:
                    return prop.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int:
                    return prop.vector3IntValue.ToString();
                case SerializedPropertyType.RectInt:
                    return prop.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt:
                    return prop.boundsIntValue.ToString();
                case SerializedPropertyType.LayerMask:
                    return prop.intValue.ToString();
                default:
                    return $"({prop.propertyType})";
            }
        }

        private static bool SetSerializedPropertyValue(SerializedProperty prop, string value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (int.TryParse(value, out int intVal)) { prop.intValue = intVal; return true; }
                        return false;
                    case SerializedPropertyType.Boolean:
                        if (bool.TryParse(value, out bool boolVal)) { prop.boolValue = boolVal; return true; }
                        if (value == "1") { prop.boolValue = true; return true; }
                        if (value == "0") { prop.boolValue = false; return true; }
                        return false;
                    case SerializedPropertyType.Float:
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                        { prop.floatValue = floatVal; return true; }
                        return false;
                    case SerializedPropertyType.String:
                        prop.stringValue = value;
                        return true;
                    case SerializedPropertyType.Enum:
                        if (int.TryParse(value, out int enumIdx)) { prop.enumValueIndex = enumIdx; return true; }
                        if (prop.enumDisplayNames != null)
                        {
                            for (int i = 0; i < prop.enumDisplayNames.Length; i++)
                            {
                                if (prop.enumDisplayNames[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                                { prop.enumValueIndex = i; return true; }
                            }
                        }
                        return false;
                    case SerializedPropertyType.Color:
                        if (ColorUtility.TryParseHtmlString(value, out Color color))
                        { prop.colorValue = color; return true; }
                        return false;
                    case SerializedPropertyType.Vector3:
                        var v3 = ParseVector3(value);
                        if (v3.HasValue) { prop.vector3Value = v3.Value; return true; }
                        return false;
                    case SerializedPropertyType.Vector2:
                        var v2 = ParseVector2(value);
                        if (v2.HasValue) { prop.vector2Value = v2.Value; return true; }
                        return false;
                    case SerializedPropertyType.LayerMask:
                        if (int.TryParse(value, out int maskVal)) { prop.intValue = maskVal; return true; }
                        return false;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static Vector3? ParseVector3(string value)
        {
            var cleaned = value.Trim('(', ')', ' ');
            var parts = cleaned.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z))
                return new Vector3(x, y, z);
            return null;
        }

        private static Vector2? ParseVector2(string value)
        {
            var cleaned = value.Trim('(', ')', ' ');
            var parts = cleaned.Split(',');
            if (parts.Length != 2) return null;
            if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            return null;
        }
    }
}
