using System;
using Joker.UnityCli.Editor.ScriptServer;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor
{
    [InitializeOnLoad]
    public static class JokerServerController
    {
        private const string AutoStartPrefKey = "Joker.UnityCli.AutoStartServer";

        public static bool IsRunning => HttpServer.IsRunning;
        public static int Port => HttpServer.Port;

        public static bool AutoStart
        {
            get => EditorPrefs.GetBool(AutoStartPrefKey, true);
            set => EditorPrefs.SetBool(AutoStartPrefKey, value);
        }

        static JokerServerController()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;

            if (AutoStart)
                HttpServer.Start();
        }

        public static void Start()
        {
            HttpServer.Start();
        }

        public static void Stop()
        {
            HttpServer.Stop();
        }

        public static void Toggle()
        {
            if (IsRunning)
                Stop();
            else
                Start();
        }

        private static void OnBeforeAssemblyReload()
        {
            HttpExecHandler.IsCompiling = true;
            PortRegistry.WriteStatus("compiling");
            HttpExecHandler.WaitForScriptTasks(TimeSpan.FromSeconds(3));
            Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void OnEditorQuitting()
        {
            PortRegistry.WriteStatus("stopped");
            Stop();
            PortRegistry.Delete();
        }
    }
}
