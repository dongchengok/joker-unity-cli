using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public static class PortRegistry
    {
        private static string RegistryPath => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            ".joker-unity", "server.json");

        public static void Write(int port)
        {
            Write(port, "ready");
        }

        public static void Write(int port, string status)
        {
            var dir = Path.GetDirectoryName(RegistryPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = $"{{\"port\":{port},\"pid\":{Process.GetCurrentProcess().Id},\"status\":\"{status}\"}}";
            File.WriteAllText(RegistryPath, json);
        }

        public static void WriteStatus(string status)
        {
            if (!File.Exists(RegistryPath))
                return;
            var content = File.ReadAllText(RegistryPath);
            // Find and replace "status":"<old>" or add status field before closing brace
            var statusKey = "\"status\":";
            var idx = content.IndexOf(statusKey);
            if (idx >= 0)
            {
                // Find the start of the old value (after the quote)
                var valueStart = content.IndexOf('"', idx + statusKey.Length) + 1;
                var valueEnd = content.IndexOf('"', valueStart);
                content = content.Substring(0, valueStart) + status + content.Substring(valueEnd);
            }
            else
            {
                // No status field yet — insert before closing brace
                var closingBrace = content.LastIndexOf('}');
                content = content.Substring(0, closingBrace)
                    + ",\"status\":\"" + status + "\"}";
            }
            File.WriteAllText(RegistryPath, content);
        }

        public static void Delete()
        {
            if (File.Exists(RegistryPath))
                File.Delete(RegistryPath);
        }
    }
}
