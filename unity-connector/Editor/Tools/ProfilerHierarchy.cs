using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace UnityCliConnector.Tools
{
    /// <summary>
    /// Hierarchical profiler drill-down.
    /// Each call returns one level of children, keeping response size small.
    /// Start with no parentId to see top-level items, then drill into any child by its itemId.
    /// </summary>
    [UnityCliTool(Name = "profiler_hierarchy",
        Description = "[ReadOnly] Hierarchical profiler drill-down. Returns one level of children at a time.")]
    public static class ProfilerHierarchy
    {
        public class Parameters
        {
            [ToolParameter("Frame index to inspect. -1 or omit = last captured frame.")]
            public int Frame { get; set; }

            [ToolParameter("Thread index. 0 = main thread.")]
            public int ThreadIndex { get; set; }

            [ToolParameter("Parent item ID to drill into. Omit for root level.")]
            public int ParentId { get; set; }

            [ToolParameter("Minimum total time (ms) filter.")]
            public float MinTime { get; set; }

            [ToolParameter("Sort column: 'total', 'self', or 'calls'. Default 'total'.")]
            public string SortBy { get; set; }

            [ToolParameter("Max children to return. Default 30.")]
            public int MaxItems { get; set; }
        }

        public static object HandleCommand(JObject parameters)
        {
            if (ProfilerDriver.enabled == false && ProfilerDriver.lastFrameIndex < 0)
                return new ErrorResponse("Profiler has no captured data. Enable profiler and capture frames first.");

            var frameIndex = parameters["frame"]?.Value<int>() ?? -1;
            if (frameIndex < 0) frameIndex = ProfilerDriver.lastFrameIndex;
            if (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex)
                return new ErrorResponse(
                    $"Frame {frameIndex} out of range [{ProfilerDriver.firstFrameIndex}..{ProfilerDriver.lastFrameIndex}]");

            var threadIndex = parameters["thread_index"]?.Value<int>()
                ?? parameters["threadIndex"]?.Value<int>() ?? 0;
            var parentIdToken = parameters["parent_id"] ?? parameters["parentId"];
            var minTime = parameters["min_time"]?.Value<float>()
                ?? parameters["minTime"]?.Value<float>() ?? 0f;
            var sortBy = (parameters["sort_by"]?.Value<string>()
                ?? parameters["sortBy"]?.Value<string>() ?? "total").ToLowerInvariant();
            var maxItems = parameters["max_items"]?.Value<int>()
                ?? parameters["maxItems"]?.Value<int>() ?? 30;
            if (maxItems <= 0) maxItems = 30;

            int sortColumn;
            switch (sortBy)
            {
                case "self": sortColumn = HierarchyFrameDataView.columnSelfTime; break;
                case "calls": sortColumn = HierarchyFrameDataView.columnCalls; break;
                default: sortColumn = HierarchyFrameDataView.columnTotalTime; break;
            }

            using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                frameIndex, threadIndex,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                sortColumn, false);

            if (frameData == null || frameData.valid == false)
                return new ErrorResponse($"No profiler data for frame {frameIndex}, thread {threadIndex}.");

            int parentId;
            if (parentIdToken == null || parentIdToken.Type == JTokenType.Null)
            {
                parentId = frameData.GetRootItemID();
            }
            else
            {
                parentId = parentIdToken.Value<int>();
            }

            var childIds = new List<int>();
            frameData.GetItemChildren(parentId, childIds);

            var items = new JArray();
            int shown = 0;
            foreach (var childId in childIds)
            {
                var totalTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnTotalTime);
                if (totalTime < minTime) continue;

                if (shown >= maxItems) break;
                shown++;

                var selfTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnSelfTime);
                var calls = (int)frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnCalls);

                var childChildIds = new List<int>();
                frameData.GetItemChildren(childId, childChildIds);

                var item = new JObject
                {
                    ["itemId"] = childId,
                    ["name"] = frameData.GetItemName(childId),
                    ["totalMs"] = System.Math.Round(totalTime, 3),
                    ["selfMs"] = System.Math.Round(selfTime, 3),
                    ["calls"] = calls,
                    ["childCount"] = childChildIds.Count,
                };
                items.Add(item);
            }

            var parentName = parentIdToken != null && parentIdToken.Type != JTokenType.Null
                ? frameData.GetItemName(parentId)
                : "(root)";

            var result = new JObject
            {
                ["frame"] = frameIndex,
                ["threadIndex"] = threadIndex,
                ["parentId"] = parentId,
                ["parentName"] = parentName,
                ["childrenCount"] = childIds.Count,
                ["shownCount"] = shown,
                ["minTimeFilter"] = minTime,
                ["children"] = items,
            };

            return new SuccessResponse($"{shown} children of '{parentName}' (frame {frameIndex})", result);
        }
    }
}
