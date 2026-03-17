using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Description = "Returns connector and Unity version information.")]
    public static class ManageVersion
    {
        public class Parameters
        {
            // No required parameters
        }

        public static object HandleCommand(JObject @params)
        {
            // Read connector version from package.json
            string connectorVersion = "unknown";
            string packageJsonPath = Path.Combine("Packages", "com.devchan97.unity-cli-connector", "package.json");
            if (File.Exists(packageJsonPath))
            {
                var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                connectorVersion = json["version"]?.ToString() ?? "unknown";
            }

            return new SuccessResponse("Version info", new
            {
                connector_version = connectorVersion,
                unity_version = Application.unityVersion,
                api_version = "1",
                platform = Application.platform.ToString()
            });
        }
    }
}
