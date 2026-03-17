using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityCliConnector
{
    /// <summary>
    /// Finds [UnityCliTool] handlers via reflection with caching.
    /// Assembly scan runs once and results are cached in static fields.
    /// Domain reload (assembly recompilation) resets all statics automatically.
    /// </summary>
    public static class ToolDiscovery
    {
        private static Dictionary<string, MethodInfo> s_HandlerCache;
        private static List<object> s_SchemaCache;
        private static bool s_Scanned;

        private static void EnsureScanned()
        {
            if (s_Scanned) return;

            s_HandlerCache = new Dictionary<string, MethodInfo>();
            s_SchemaCache = new List<object>();
            var nameToType = new Dictionary<string, Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }

                foreach (var type in types)
                {
                    if (type.IsClass == false) continue;
                    var attr = type.GetCustomAttribute<UnityCliToolAttribute>();
                    if (attr == null) continue;

                    var name = attr.Name ?? StringCaseUtility.ToSnakeCase(type.Name);

                    if (nameToType.TryGetValue(name, out var existing))
                    {
                        UnityEngine.Debug.LogError(
                            $"[UnityCliConnector] Duplicate tool name '{name}': " +
                            $"{existing.FullName} and {type.FullName}. " +
                            $"Rename one or remove the duplicate.");
                        continue;
                    }
                    nameToType[name] = type;

                    var method = type.GetMethod("HandleCommand",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(JObject) }, null);

                    if (method != null)
                    {
                        s_HandlerCache[name] = method;
                    }

                    var paramsType = type.GetNestedType("Parameters");
                    s_SchemaCache.Add(new
                    {
                        name,
                        description = attr.Description ?? "",
                        group = attr.Group ?? "",
                        parameters = GetParameterSchema(paramsType),
                    });
                }
            }

            s_Scanned = true;
        }

        public static MethodInfo FindHandler(string command)
        {
            EnsureScanned();
            return s_HandlerCache.TryGetValue(command, out var method) ? method : null;
        }

        public static List<object> GetToolSchemas()
        {
            EnsureScanned();
            return s_SchemaCache;
        }

        public static List<object> GetParameterSchema(Type paramsType)
        {
            if (paramsType == null) return new List<object>();

            return paramsType.GetProperties()
                .Select(p =>
                {
                    var attr = p.GetCustomAttribute<ToolParameterAttribute>();
                    return new
                    {
                        name = StringCaseUtility.ToSnakeCase(p.Name),
                        type = p.PropertyType.Name,
                        description = attr?.Description ?? "",
                        required = attr?.Required ?? false,
                    };
                })
                .Cast<object>()
                .ToList();
        }
    }
}
