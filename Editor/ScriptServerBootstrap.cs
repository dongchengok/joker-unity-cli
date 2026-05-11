using UnityEditor;
using HttpServerClass = Joker.UnityCli.Editor.ScriptServer.HttpServer;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class ScriptServerBootstrap
    {
        static ScriptServerBootstrap()
        {
            HttpServerClass.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => HttpServerClass.Stop();
            EditorApplication.quitting += () => HttpServerClass.Stop();
        }
    }
}
