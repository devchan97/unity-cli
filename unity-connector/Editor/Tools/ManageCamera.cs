using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Manage cameras. Actions: screenshot, info, list, set_position, set_rotation.")]
    public static class ManageCamera
    {
        public class Parameters
        {
            [ToolParameter("Action: screenshot, info, list, set_position, set_rotation", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Camera name (defaults to Main Camera)")]
            public string Name { get; set; }

            [ToolParameter("File path for screenshot output")]
            public string Path { get; set; }

            [ToolParameter("X coordinate")]
            public float X { get; set; }

            [ToolParameter("Y coordinate")]
            public float Y { get; set; }

            [ToolParameter("Z coordinate")]
            public float Z { get; set; }

            [ToolParameter("Screenshot width (0 = current resolution)")]
            public int Width { get; set; }

            [ToolParameter("Screenshot height (0 = current resolution)")]
            public int Height { get; set; }
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
                case "screenshot":
                    return TakeScreenshot(p);
                case "info":
                    return GetCameraInfo(p);
                case "list":
                    return ListCameras();
                case "set_position":
                    return SetPosition(p);
                case "set_rotation":
                    return SetRotation(p);
                default:
                    return new ErrorResponse($"Unknown action: '{action}'. Valid: screenshot, info, list, set_position, set_rotation.");
            }
        }

        private static object TakeScreenshot(ToolParams p)
        {
            string path = p.Get("path");
            if (string.IsNullOrEmpty(path))
                path = System.IO.Path.Combine(Application.temporaryCachePath, "screenshot.png");

            int width = p.GetInt("width", 0).Value;
            int height = p.GetInt("height", 0).Value;

            try
            {
                Camera cam = FindCamera(p.Get("name"));
                if (cam == null)
                    return new ErrorResponse("No camera found.");

                if (width <= 0) width = Screen.width > 0 ? Screen.width : 1920;
                if (height <= 0) height = Screen.height > 0 ? Screen.height : 1080;

                var rt = new RenderTexture(width, height, 24);
                var prevTarget = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                cam.targetTexture = prevTarget;
                RenderTexture.active = null;

                byte[] pngData = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(rt);

                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, pngData);

                string absolutePath = System.IO.Path.GetFullPath(path);
                return new SuccessResponse($"Screenshot saved to '{absolutePath}'.", new
                {
                    path = absolutePath,
                    width,
                    height,
                    camera = cam.name
                });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to capture screenshot: {ex.Message}");
            }
        }

        private static object GetCameraInfo(ToolParams p)
        {
            Camera cam = FindCamera(p.Get("name"));
            if (cam == null)
                return new ErrorResponse("No camera found.");

            var t = cam.transform;
            return new SuccessResponse($"Camera info for '{cam.name}'.", new
            {
                name = cam.name,
                position = new { x = t.position.x, y = t.position.y, z = t.position.z },
                rotation = new { x = t.eulerAngles.x, y = t.eulerAngles.y, z = t.eulerAngles.z },
                fieldOfView = cam.fieldOfView,
                nearClipPlane = cam.nearClipPlane,
                farClipPlane = cam.farClipPlane,
                orthographic = cam.orthographic,
                orthographicSize = cam.orthographicSize
            });
        }

        private static object ListCameras()
        {
            var cameras = Camera.allCameras;
            if (cameras.Length == 0)
                return new SuccessResponse("No cameras found in scene.", new object[0]);

            var result = cameras.Select(cam =>
            {
                var t = cam.transform;
                return new
                {
                    name = cam.name,
                    tag = cam.tag,
                    position = new { x = t.position.x, y = t.position.y, z = t.position.z },
                    rotation = new { x = t.eulerAngles.x, y = t.eulerAngles.y, z = t.eulerAngles.z },
                    fieldOfView = cam.fieldOfView,
                    depth = cam.depth,
                    cullingMask = cam.cullingMask,
                    enabled = cam.enabled
                };
            }).ToArray();

            return new SuccessResponse($"Found {result.Length} camera(s).", result);
        }

        private static object SetPosition(ToolParams p)
        {
            Camera cam = FindCamera(p.Get("name"));
            if (cam == null)
                return new ErrorResponse("No camera found.");

            float? x = p.GetFloat("x");
            float? y = p.GetFloat("y");
            float? z = p.GetFloat("z");

            if (!x.HasValue || !y.HasValue || !z.HasValue)
                return new ErrorResponse("'x', 'y', and 'z' parameters are required for set_position.");

            Undo.RecordObject(cam.transform, $"Set Camera Position ({cam.name})");
            cam.transform.position = new Vector3(x.Value, y.Value, z.Value);

            var pos = cam.transform.position;
            return new SuccessResponse($"Camera '{cam.name}' position set to ({pos.x}, {pos.y}, {pos.z}).", new
            {
                name = cam.name,
                position = new { x = pos.x, y = pos.y, z = pos.z }
            });
        }

        private static object SetRotation(ToolParams p)
        {
            Camera cam = FindCamera(p.Get("name"));
            if (cam == null)
                return new ErrorResponse("No camera found.");

            float? x = p.GetFloat("x");
            float? y = p.GetFloat("y");
            float? z = p.GetFloat("z");

            if (!x.HasValue || !y.HasValue || !z.HasValue)
                return new ErrorResponse("'x', 'y', and 'z' parameters are required for set_rotation.");

            Undo.RecordObject(cam.transform, $"Set Camera Rotation ({cam.name})");
            cam.transform.eulerAngles = new Vector3(x.Value, y.Value, z.Value);

            var rot = cam.transform.eulerAngles;
            return new SuccessResponse($"Camera '{cam.name}' rotation set to ({rot.x}, {rot.y}, {rot.z}).", new
            {
                name = cam.name,
                rotation = new { x = rot.x, y = rot.y, z = rot.z }
            });
        }

        private static Camera FindCamera(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                if (Camera.main != null)
                    return Camera.main;
                return Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
            }

            foreach (var cam in Camera.allCameras)
            {
                if (cam.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return cam;
            }

            // Also search inactive cameras via scene hierarchy
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = root.GetComponentsInChildren<Camera>(true)
                        .FirstOrDefault(c => c.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                }
            }

            return null;
        }
    }
}
