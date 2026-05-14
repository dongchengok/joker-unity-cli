using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.UI
{
    [InitializeOnLoad]
    public static class JokerToolbarButton
    {
        private static ScriptableObject _currentToolbar;
        private static bool _wasRunning;
        private static int _injectAttempts;

        private static Type _toolbarType;
        private static PropertyInfo _visualTreeProp;
        private static FieldInfo _onGUIHandlerField;
        private static bool _initFailed;

        static JokerToolbarButton()
        {
            EditorApplication.update += OnUpdate;
        }

        private static void EnsureInitialized()
        {
            if (_initFailed || _visualTreeProp != null)
                return;

            try
            {
                _toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
                if (_toolbarType == null)
                {
                    Debug.LogWarning("[Joker] 找不到 UnityEditor.Toolbar 类型");
                    _initFailed = true;
                    return;
                }

                var guiViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GUIView");
                if (guiViewType == null)
                {
                    Debug.LogWarning("[Joker] 找不到 UnityEditor.GUIView 类型");
                    _initFailed = true;
                    return;
                }

#if UNITY_2020_1_OR_NEWER
                var iWindowBackendType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.IWindowBackend");
                _visualTreeProp = iWindowBackendType?.GetProperty("visualTree",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#else
                _visualTreeProp = guiViewType.GetProperty("visualTree",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#endif

                if (_visualTreeProp == null)
                {
                    Debug.LogWarning("[Joker] 找不到 visualTree 属性");
                    _initFailed = true;
                    return;
                }

                var imguiContainerType = typeof(UnityEngine.UIElements.IMGUIContainer);
                _onGUIHandlerField = imguiContainerType.GetField("m_OnGUIHandler",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (_onGUIHandlerField == null)
                {
                    Debug.LogWarning("[Joker] 找不到 IMGUIContainer.m_OnGUIHandler 字段");
                    _initFailed = true;
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Joker] Toolbar 初始化异常: {e.Message}");
                _initFailed = true;
            }
        }

        private static void OnUpdate()
        {
            if (_initFailed)
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            if (_currentToolbar == null)
            {
                TryInjectToolbarButton();
                return;
            }

            bool isRunning = JokerServerController.IsRunning;
            if (isRunning == _wasRunning)
                return;

            _wasRunning = isRunning;
            RepaintToolbar();
        }

        private static void TryInjectToolbarButton()
        {
            EnsureInitialized();
            if (_initFailed)
            {
                EditorApplication.update -= OnUpdate;
                Debug.LogWarning("[Joker] Toolbar 注入初始化失败，已放弃。");
                return;
            }

            if (++_injectAttempts > 300)
            {
                EditorApplication.update -= OnUpdate;
                Debug.LogWarning("[Joker] Toolbar 注入重试超过 300 次，已放弃。");
                return;
            }

            var toolbars = Resources.FindObjectsOfTypeAll(_toolbarType);
            if (toolbars.Length == 0)
                return;

            var toolbar = (ScriptableObject)toolbars[0];
            _currentToolbar = toolbar;

            object visualTree;
#if UNITY_2020_1_OR_NEWER
            var windowBackendProp = toolbar.GetType().BaseType.GetProperty("windowBackend",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var windowBackend = windowBackendProp.GetValue(toolbar);
            visualTree = _visualTreeProp.GetValue(windowBackend);
#else
            visualTree = _visualTreeProp.GetValue(toolbar);
#endif

            if (visualTree == null)
            {
                _currentToolbar = null;
                return;
            }

            var visualElementType = visualTree.GetType();
            var indexer = visualElementType.GetProperty("Item", new[] { typeof(int) });
            if (indexer == null)
            {
                Debug.LogWarning($"[Joker] visualTree 类型 {visualElementType.Name} 没有 int 索引器");
                _currentToolbar = null;
                return;
            }

            var container = indexer.GetValue(visualTree, new object[] { 0 });
            if (container == null)
            {
                Debug.LogWarning("[Joker] visualTree[0] 为 null");
                _currentToolbar = null;
                return;
            }

            var handler = (Action)_onGUIHandlerField.GetValue(container);
            handler = (Action)Delegate.Remove(handler, (Action)OnToolbarGUI);
            handler = (Action)Delegate.Combine(handler, (Action)OnToolbarGUI);
            _onGUIHandlerField.SetValue(container, handler);

            Debug.Log("[Joker] Toolbar 按钮注入成功");
        }

        private static void RepaintToolbar()
        {
            if (_toolbarType == null || _currentToolbar == null)
                return;

            var repaintMethod = _toolbarType.GetMethod("Repaint",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            repaintMethod?.Invoke(_currentToolbar, null);
        }

        private static void OnToolbarGUI()
        {
            var isRunning = JokerServerController.IsRunning;
            var toolbarWidth = Screen.width;
            var toolbarHeight = Screen.height;

            // Play 按钮左侧
            var buttonW = 100;
            var buttonH = 20;
            var x = toolbarWidth / 2 - 70 - buttonW;
            var y = (toolbarHeight - buttonH) / 2;
            var buttonRect = new Rect(x, y, buttonW, buttonH);

            var dot = isRunning ? "●" : "○";
            var label = isRunning ? "Cli启动中" : "Cli未启动";
            var tooltip = isRunning
                ? $"Joker 运行中 · 端口 {JokerServerController.Port}\n点击停止"
                : "Joker 已停止\n点击启动";

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isRunning
                ? new Color(0.30f, 0.68f, 0.35f)   // 深绿底
                : new Color(0.65f, 0.22f, 0.22f);   // 深红底

            var prevColor = GUI.color;
            GUI.color = isRunning
                ? new Color(0.50f, 1.00f, 0.55f)   // 亮绿字
                : new Color(1.00f, 0.45f, 0.40f);   // 亮红字

            if (GUI.Button(buttonRect, new GUIContent($"{dot} {label}", tooltip), EditorStyles.miniButton))
                JokerServerController.Toggle();

            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }
    }
}
