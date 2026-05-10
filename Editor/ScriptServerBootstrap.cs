using UnityEditor;
using Joker.UnityCli.Editor.ScriptServer;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class ScriptServerBootstrap
    {
        static ScriptServerBootstrap()
        {
            ScriptServer.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => ScriptServer.Stop();
            EditorApplication.quitting += () => ScriptServer.Stop();
        }
    }
}
