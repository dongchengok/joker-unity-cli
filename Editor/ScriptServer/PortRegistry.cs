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
            var dir = Path.GetDirectoryName(RegistryPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = $"{{\"port\":{port},\"pid\":{Process.GetCurrentProcess().Id}}}";
            File.WriteAllText(RegistryPath, json);
        }

        public static void Delete()
        {
            if (File.Exists(RegistryPath))
                File.Delete(RegistryPath);
        }
    }
}
