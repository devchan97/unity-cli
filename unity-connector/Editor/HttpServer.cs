using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector
{
    /// <summary>
    /// Lightweight HTTP server on localhost. Receives CLI commands as POST /command,
    /// dispatches via CommandRouter, returns JSON responses.
    /// Uses ConcurrentQueue + EditorApplication.update for main-thread marshaling
    /// so commands execute even when Unity is unfocused.
    /// Survives domain reloads via InitializeOnLoad.
    /// </summary>
    [InitializeOnLoad]
    public static class HttpServer
    {
        const int DEFAULT_PORT = 8090;
        const int MAX_PORT_ATTEMPTS = 10;

        static HttpListener s_Listener;
        static CancellationTokenSource s_Cts;
        static int s_Port;

        static readonly ConcurrentQueue<WorkItem> s_Queue = new();

        struct WorkItem
        {
            public string Command;
            public JObject Parameters;
            public TaskCompletionSource<object> Tcs;
        }

        static HttpServer()
        {
            Start();
            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += StopListener;
            AssemblyReloadEvents.afterAssemblyReload += Start;
            EditorApplication.projectChanged += Restart;
            EditorApplication.update += ProcessQueue;
        }

        static void Restart()
        {
            StopListener();
            Start();
        }

        public static int Port => s_Port;

        static void Start()
        {
            if (s_Listener != null) return;

            for (var attempt = 0; attempt < MAX_PORT_ATTEMPTS; attempt++)
            {
                var port = DEFAULT_PORT + attempt;
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();

                    s_Listener = listener;
                    s_Port = port;
                    s_Cts = new CancellationTokenSource();

                    _ = ListenLoop(s_Cts.Token);

                    InstanceRegistry.Register(port);
                    Debug.Log($"[UnityCliConnector] HTTP server started on port {port}");
                    return;
                }
                catch (HttpListenerException)
                {
                    // Port in use, try next
                }
            }

            Debug.LogError("[UnityCliConnector] Failed to start HTTP server — no available port");
        }

        static void StopListener()
        {
            if (s_Listener == null) return;

            s_Cts?.Cancel();
            s_Cts?.Dispose();
            s_Cts = null;

            try
            {
                s_Listener.Stop();
                s_Listener.Close();
            }
            catch
            {
            }

            s_Listener = null;
        }

        static void Stop()
        {
            var port = s_Port;
            StopListener();
            InstanceRegistry.Unregister();
            Debug.Log($"[UnityCliConnector] HTTP server stopped (was port {port})");
        }

        static void ForceEditorUpdate()
        {
            try { UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
            catch { }
        }

        static void ProcessQueue()
        {
            while (s_Queue.TryDequeue(out var item))
                ProcessItem(item);
        }

        static async void ProcessItem(WorkItem item)
        {
            try
            {
                var r = await CommandRouter.Dispatch(item.Command, item.Parameters);
                item.Tcs.SetResult(r);
            }
            catch (Exception ex)
            {
                item.Tcs.SetResult(new ErrorResponse(ex.Message));
            }
        }

        static async Task ListenLoop(CancellationToken ct)
        {
            while (ct.IsCancellationRequested == false && s_Listener?.IsListening == true)
            {
                try
                {
                    var context = await s_Listener.GetContextAsync();
                    _ = HandleRequest(context);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
            }
        }

        static async Task<object> HandleSingleCommand(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var json = JObject.Parse(body);

            var command = json["command"]?.ToString();
            var parameters = json["params"] as JObject;

            if (string.IsNullOrEmpty(command))
                return new ErrorResponse("Missing 'command' field");

            var tcs = new TaskCompletionSource<object>();
            s_Queue.Enqueue(new WorkItem
            {
                Command = command,
                Parameters = parameters,
                Tcs = tcs,
            });
            ForceEditorUpdate();
            return await tcs.Task;
        }

        static async Task<object> HandleBatchCommand(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var json = JObject.Parse(body);

            var commands = json["commands"] as JArray;
            if (commands == null || commands.Count == 0)
                return new ErrorResponse("Missing or empty 'commands' array");

            if (commands.Count > 20)
                return new ErrorResponse("Batch limit exceeded: maximum 20 commands per request");

            // Enqueue ALL commands at once, then await all results.
            // This allows ProcessQueue to drain them in a single editor tick
            // instead of requiring N separate ticks.
            var tasks = new System.Collections.Generic.List<Task<object>>();

            foreach (var cmd in commands)
            {
                var command = cmd["command"]?.ToString();
                var parameters = cmd["params"] as JObject;

                if (string.IsNullOrEmpty(command))
                {
                    tasks.Add(Task.FromResult<object>(new ErrorResponse("Missing 'command' field")));
                    continue;
                }

                var tcs = new TaskCompletionSource<object>();
                s_Queue.Enqueue(new WorkItem
                {
                    Command = command,
                    Parameters = parameters,
                    Tcs = tcs,
                });
                tasks.Add(tcs.Task);
            }

            ForceEditorUpdate();

            var results = new System.Collections.Generic.List<object>();
            foreach (var task in tasks)
            {
                try
                {
                    results.Add(await task);
                }
                catch (Exception ex)
                {
                    results.Add(new ErrorResponse($"Command failed: {ex.Message}"));
                }
            }

            return new SuccessResponse($"Batch completed: {results.Count} commands", results);
        }

        static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.ContentType = "application/json";
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            object result;

            try
            {
                if (request.HttpMethod != "POST")
                {
                    result = new ErrorResponse($"Expected POST, got {request.HttpMethod}");
                    response.StatusCode = 400;
                }
                else if (request.Url.AbsolutePath == "/command")
                {
                    result = await HandleSingleCommand(request);
                }
                else if (request.Url.AbsolutePath == "/batch")
                {
                    result = await HandleBatchCommand(request);
                }
                else
                {
                    result = new ErrorResponse($"Unknown endpoint: {request.Url.AbsolutePath}");
                    response.StatusCode = 400;
                }
            }
            catch (Exception ex)
            {
                result = new ErrorResponse($"Request error: {ex.Message}");
                response.StatusCode = 500;
            }

            var responseJson = JsonConvert.SerializeObject(result);
            var buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
    }
}
