using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityCliConnector
{
    /// <summary>
    /// Manages ~/.unity-cli/instances.json so the CLI can discover running Unity instances.
    /// Each Unity Editor registers on startup and unregisters on quit.
    /// </summary>
    public static class InstanceRegistry
    {
        static readonly string s_Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-cli");
        static readonly string s_Path = Path.Combine(s_Dir, "instances.json");
        static int s_RegisteredPort;

        /// <summary>
        /// Returns the port this project was last registered on (from instances.json),
        /// so HttpServer can reuse it across domain reloads instead of climbing to a new port.
        /// </summary>
        public static int LastPort
        {
            get
            {
                var projectPath = Application.dataPath.Replace("/Assets", "");
                foreach (var inst in Load())
                    if (inst.projectPath == projectPath)
                        return inst.port;
                return 0;
            }
        }

        [Serializable]
        class InstanceEntry
        {
            public string projectPath;
            public int port;
            public int pid;
            public string unityVersion;
            public string registeredAt;
        }

        public static void Register(int port)
        {
            var instances = Load();

            // Remove stale entry for this project
            var projectPath = Application.dataPath.Replace("/Assets", "");
            instances.RemoveAll(i => i.projectPath == projectPath);

            instances.Add(new InstanceEntry
            {
                projectPath = projectPath,
                port = port,
                pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                unityVersion = Application.unityVersion,
                registeredAt = DateTime.UtcNow.ToString("o"),
            });

            Save(instances);
            s_RegisteredPort = port;
        }

        public static void Unregister()
        {
            if (s_RegisteredPort == 0) return;

            var instances = Load();
            instances.RemoveAll(i => i.port == s_RegisteredPort);
            Save(instances);
            s_RegisteredPort = 0;
        }

        static List<InstanceEntry> Load()
        {
            try
            {
                if (File.Exists(s_Path))
                {
                    var json = File.ReadAllText(s_Path);
                    return JsonConvert.DeserializeObject<List<InstanceEntry>>(json) ?? new List<InstanceEntry>();
                }
            }
            catch
            {
                // Corrupted file, start fresh
            }
            return new List<InstanceEntry>();
        }

        static void Save(List<InstanceEntry> instances)
        {
            Directory.CreateDirectory(s_Dir);
            File.WriteAllText(s_Path, JsonConvert.SerializeObject(instances, Formatting.Indented));
        }
    }
}
