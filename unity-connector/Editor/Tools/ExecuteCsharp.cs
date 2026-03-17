using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CSharp;
using Newtonsoft.Json.Linq;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Execute arbitrary C# code at runtime. Full access to Unity and all loaded assemblies.")]
    public static class ExecuteCsharp
    {
        private static readonly string[] DefaultUsings =
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Reflection",
            "UnityEngine",
            "UnityEditor",
        };

        // LRU compilation cache: source hash → compiled MethodInfo
        private const int MAX_CACHE_SIZE = 50;
        private static readonly Dictionary<string, CacheEntry> s_Cache = new();
        private static readonly LinkedList<string> s_LruOrder = new();

        private struct CacheEntry
        {
            public MethodInfo Method;
            public LinkedListNode<string> LruNode;
        }

        public class Parameters
        {
            [ToolParameter("C# code to execute. Single expressions auto-return their result", Required = true)]
            public string Code { get; set; }

            [ToolParameter("Additional using directives (e.g. Unity.Entities, Unity.Mathematics)")]
            public string[] Usings { get; set; }

            [ToolParameter("Execution timeout in seconds (default: 30, max: 300)")]
            public int Timeout { get; set; }
        }

        public static async Task<object> HandleCommand(JObject parameters)
        {
            var code = parameters["code"]?.Value<string>();
            if (string.IsNullOrEmpty(code))
                return new ErrorResponse("'code' required");

            var extraUsings = parameters["usings"]?.ToObject<string[]>();

            var timeoutSec = parameters["timeout"]?.Value<int>() ?? 30;
            timeoutSec = Math.Max(1, Math.Min(timeoutSec, 300));

            if (Regex.IsMatch(code, @"\breturn[\s;]") == false)
            {
                var trimmed = code.TrimEnd().TrimEnd(';');
                code = $"return (object)({trimmed});";
            }

            var source = BuildSource(code, extraUsings);
            return await CompileAndExecuteWithTimeout(source, timeoutSec);
        }

        private static string BuildSource(string code, string[] extraUsings)
        {
            var sb = new StringBuilder();
            foreach (var u in DefaultUsings)
                sb.AppendLine($"using {u};");
            if (extraUsings != null)
                foreach (var u in extraUsings)
                    sb.AppendLine($"using {u};");

            sb.AppendLine();
            sb.AppendLine("public static class __CliDynamic {");
            sb.AppendLine("  public static object Execute() {");
            sb.AppendLine(code);
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ComputeSourceHash(string source)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        private static MethodInfo TryGetCachedMethod(string hash)
        {
            if (!s_Cache.TryGetValue(hash, out var entry))
                return null;
            s_LruOrder.Remove(entry.LruNode);
            s_LruOrder.AddFirst(entry.LruNode);
            return entry.Method;
        }

        private static void StoreInCache(string hash, MethodInfo method)
        {
            while (s_Cache.Count >= MAX_CACHE_SIZE && s_LruOrder.Count > 0)
            {
                var oldest = s_LruOrder.Last.Value;
                s_LruOrder.RemoveLast();
                s_Cache.Remove(oldest);
            }
            var node = s_LruOrder.AddFirst(hash);
            s_Cache[hash] = new CacheEntry { Method = method, LruNode = node };
        }

        private static async Task<object> CompileAndExecuteWithTimeout(string source, int timeoutSec)
        {
            var hash = ComputeSourceHash(source);
            var method = TryGetCachedMethod(hash);

            if (method == null)
            {
                // Cache miss: full compilation
                var provider = new CSharpCodeProvider();
                var cp = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false
                };

                var refs = new List<string>();
                var added = new HashSet<string>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                        var name = asm.GetName().Name;
                        if (!added.Add(name)) continue;
                        if (name == "mscorlib") continue;
                        if (IsBclFacade(asm)) continue;
                        refs.Add(asm.Location);
                    }
                    catch { }
                }

                var rspPath = Path.Combine(Path.GetTempPath(), $"unity-cli-exec-{Guid.NewGuid():N}.rsp");
                try
                {
                    var rspContent = new StringBuilder();
                    foreach (var r in refs)
                        rspContent.AppendLine($"-r:\"{r}\"");
                    File.WriteAllText(rspPath, rspContent.ToString());
                    cp.CompilerOptions = $"@\"{rspPath}\"";

                    var result = provider.CompileAssemblyFromSource(cp, source);
                    if (result.Errors.HasErrors)
                    {
                        var errors = new List<string>();
                        foreach (CompilerError err in result.Errors)
                            if (!err.IsWarning) errors.Add($"L{err.Line}: {err.ErrorText}");
                        return new ErrorResponse($"Compile error:\n{string.Join("\n", errors)}");
                    }

                    method = result.CompiledAssembly.GetType("__CliDynamic")?.GetMethod("Execute");
                    if (method == null)
                        return new ErrorResponse("Internal error: compiled type or method not found.");

                    StoreInCache(hash, method);
                }
                finally
                {
                    try { File.Delete(rspPath); } catch { }
                }
            }

            // Execute (cache hit and miss paths converge here)
            var execTask = Task.Run(() => method.Invoke(null, null));
            var timeoutTask = Task.Delay(timeoutSec * 1000);
            var completed = await Task.WhenAny(execTask, timeoutTask);

            if (completed == timeoutTask)
            {
                return new ErrorResponse(
                    $"Execution timed out after {timeoutSec}s. " +
                    "Increase with 'timeout' parameter (max 300s). " +
                    "Note: exec runs on a background thread; use dedicated tools for Unity API calls.");
            }

            if (execTask.IsFaulted)
            {
                var ex = execTask.Exception?.InnerException ?? execTask.Exception;
                return new ErrorResponse($"Execution error: {ex?.Message}");
            }

            var output = execTask.Result;
            return new SuccessResponse("OK", Serialize(output, 0));
        }

        private static bool IsBclFacade(Assembly asm)
        {
            var name = asm.GetName().Name;
            if (!name.StartsWith("System.")) return false;
            if (name.StartsWith("System.Private.")) return false;
            try
            {
                foreach (var attr in asm.GetCustomAttributesData())
                    if (attr.AttributeType.Name == "TypeForwardedToAttribute") return true;
            }
            catch { }
            return false;
        }

        private static object Serialize(object obj, int depth)
        {
            if (obj == null) return null;
            if (depth > 4) return obj.ToString();
            var type = obj.GetType();
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return obj;
            if (type.IsEnum) return obj.ToString();
            if (type.Name.StartsWith("FixedString")) return obj.ToString();
            if (obj is IDictionary dict)
            {
                var r = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dict)
                    r[e.Key.ToString()] = Serialize(e.Value, depth + 1);
                return r;
            }
            if (obj is IEnumerable enumerable)
            {
                var list = new List<object>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 100) { list.Add("... (truncated at 100)"); break; }
                    list.Add(Serialize(item, depth + 1));
                }
                return list;
            }
            if (type.IsValueType || type.IsClass)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (fields.Length > 0)
                {
                    var r = new Dictionary<string, object>();
                    foreach (var f in fields)
                    {
                        try { r[f.Name] = Serialize(f.GetValue(obj), depth + 1); }
                        catch { r[f.Name] = "<error>"; }
                    }
                    return r;
                }
            }
            return obj.ToString();
        }
    }
}
