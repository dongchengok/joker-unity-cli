using UnityEditor;
using ScriptServerClass = Joker.UnityCli.Editor.ScriptServer.ScriptServer;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class ScriptServerBootstrap
    {
        static ScriptServerBootstrap()
        {
            ScriptServerClass.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => ScriptServerClass.Stop();
            EditorApplication.quitting += () => ScriptServerClass.Stop();
        }
    }
}
