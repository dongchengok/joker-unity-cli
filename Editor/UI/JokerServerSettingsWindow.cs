using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.UI
{
    public class JokerServerSettingsWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(320, 220);

        [MenuItem("Window/Joker/服务设置")]
        public static void ShowWindow()
        {
            var window = GetWindow<JokerServerSettingsWindow>("Joker");
            window.minSize = WindowSize;
            window.maxSize = WindowSize;
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(16);

            // 标题行
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Joker Unity CLI", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(12);

            // 状态卡片
            DrawStatusCard();

            GUILayout.Space(10);

            // 分隔线
            var rect = GUILayoutUtility.GetRect(0, 1);
            EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 0.5f));

            GUILayout.Space(10);

            // 自动启动
            DrawAutoStartToggle();

            GUILayout.Space(12);

            // 控制按钮
            DrawControlButtons();

            GUILayout.FlexibleSpace();

            GUILayout.Space(4);

            // 底部提示
            GUILayout.Label("Toolbar Play 按钮左侧可切换服务",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 });
        }

        private void DrawStatusCard()
        {
            var isRunning = JokerServerController.IsRunning;
            var dotColor = isRunning ? new Color(0.18f, 0.75f, 0.35f) : new Color(0.55f, 0.55f, 0.55f);
            var statusText = isRunning ? $"运行中  ·  端口 {JokerServerController.Port}" : "已停止";

            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Space(10);

            // 彩色圆点
            var prev = GUI.color;
            GUI.color = dotColor;
            GUILayout.Label("●", new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            }, GUILayout.Width(24), GUILayout.Height(24));
            GUI.color = prev;

            GUILayout.Space(6);

            // 状态文字
            GUILayout.Label(statusText, new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            }, GUILayout.Height(24));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawAutoStartToggle()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            var newValue = GUILayout.Toggle(
                JokerServerController.AutoStart,
                "  打开 Editor 时自动启动服务",
                EditorStyles.toggle);
            if (EditorGUI.EndChangeCheck())
            {
                JokerServerController.AutoStart = newValue;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawControlButtons()
        {
            var isRunning = JokerServerController.IsRunning;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isRunning
                ? new Color(0.85f, 0.28f, 0.28f)
                : new Color(0.22f, 0.65f, 0.32f);

            var buttonText = isRunning ? "■  停止服务" : "▶  启动服务";
            if (GUILayout.Button(buttonText, GUILayout.Width(140), GUILayout.Height(30)))
            {
                JokerServerController.Toggle();
            }

            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void Update()
        {
            if (EditorApplication.timeSinceStartup % 1 < 0.05)
                Repaint();
        }
    }
}
