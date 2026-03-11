using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliConnector
{
    /// <summary>
    /// Routes incoming command requests to the appropriate tool handler.
    /// Handles both sync and async handlers, with main-thread marshaling.
    /// State-changing commands are serialized via exclusive lock to prevent
    /// race conditions when multiple CLI agents access the same Unity instance.
    /// </summary>
    public static class CommandRouter
    {
        static readonly SemaphoreSlim s_WriteLock = new(1, 1);

        static readonly HashSet<string> s_ReadOnlyCommands = new()
        {
            "list_tools",
            "tool_help",
            "read_console",
            "execute_csharp",
            "mcp_query_entities",
            "mcp_inspect_entity",
            "mcp_query_component_values",
            "mcp_query_singleton",
            "mcp_list_systems",
            "mcp_player_status",
            "mcp_game_overview",
            "mcp_entity_hierarchy",
            "mcp_compare_server_client",
            "mcp_inspect_vehicle",
            "mcp_profiler_hierarchy",
            "manage_profiler",
        };

        static bool IsReadOnly(string command)
        {
            if (s_ReadOnlyCommands.Contains(command)) return true;
            if (ToolDiscovery.Tools.TryGetValue(command, out var tool) == false) return true;
            return tool.Description != null && tool.Description.StartsWith("[ReadOnly]");
        }

        public static async Task<object> Dispatch(string command, JObject parameters)
        {
            if (command == "list_tools")
            {
                return new SuccessResponse("Available tools", ToolDiscovery.GetToolSchemas());
            }

            if (command == "tool_help")
            {
                var name = parameters?["name"]?.ToString();
                if (name == null) return new ErrorResponse("Missing 'name' parameter");
                if (ToolDiscovery.Tools.TryGetValue(name, out var info) == false)
                    return new ErrorResponse($"Unknown tool: {name}");
                return new SuccessResponse(info.Description, new
                {
                    name = info.Name,
                    description = info.Description,
                    group = info.Group,
                    parameters = ToolDiscovery.GetParameterSchema(info.ParametersType),
                });
            }

            if (ToolDiscovery.Tools.TryGetValue(command, out var tool) == false)
            {
                return new
                {
                    success = false,
                    error = $"Unknown command: {command}",
                    registered_tools = ToolDiscovery.Tools.Keys.ToArray(),
                    tool_count = ToolDiscovery.Tools.Count,
                };
            }

            if (IsReadOnly(command))
            {
                return await InvokeHandler(command, tool, parameters);
            }

            await s_WriteLock.WaitAsync();
            try
            {
                return await InvokeHandler(command, tool, parameters);
            }
            finally
            {
                s_WriteLock.Release();
            }
        }

        static async Task<object> InvokeHandler(string command, ToolDiscovery.ToolInfo tool, JObject parameters)
        {
            try
            {
                var result = tool.Handler.Invoke(null, new object[] { parameters ?? new JObject() });

                if (result is Task<object> asyncTask)
                {
                    return await asyncTask;
                }

                if (result is Task task)
                {
                    await task;
                    return new SuccessResponse($"{command} completed");
                }

                return result ?? new SuccessResponse($"{command} completed");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Debug.LogException(inner);
                return new ErrorResponse($"{command} failed: {inner.Message}");
            }
        }
    }
}
