using Joker.UnityCli.Editor.ClaudeIntegration;
using UnityEditor;
using UnityEngine;

namespace Joker.UnityCli.Editor.UI
{
    public class JokerServerSettingsWindow : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(320, 320);

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

            // Claude Code 技能区域
            DrawClaudeSkillsSection();

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

        private void DrawClaudeSkillsSection()
        {
            // 分隔线
            var sepRect = GUILayoutUtility.GetRect(0, 1);
            EditorGUI.DrawRect(sepRect, new Color(0.4f, 0.4f, 0.4f, 0.5f));

            GUILayout.Space(8);

            // 标题
            GUILayout.Label("Claude Code 技能", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            });

            GUILayout.Space(4);

            // 状态
            var isInstalled = ClaudeSkillInstaller.IsInstalled;
            var installedVersion = ClaudeSkillInstaller.InstalledVersion;
            var sourceVersion = ClaudeSkillInstaller.SourceVersion;

            var statusText = isInstalled
                ? $"已安装  ·  v{installedVersion}"
                : (sourceVersion != null ? $"可安装  ·  v{sourceVersion}" : "未找到技能文件");
            var statusColor = isInstalled ? new Color(0.18f, 0.75f, 0.35f) : new Color(0.55f, 0.55f, 0.55f);

            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Space(10);

            var prev = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label("●", new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            }, GUILayout.Width(20), GUILayout.Height(20));
            GUI.color = prev;

            GUILayout.Space(4);
            GUILayout.Label(statusText, new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            }, GUILayout.Height(20));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 按钮
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var prevBg = GUI.backgroundColor;

            if (sourceVersion != null)
            {
                if (isInstalled)
                {
                    GUI.backgroundColor = new Color(0.85f, 0.28f, 0.28f);
                    if (GUILayout.Button("卸载技能", GUILayout.Width(100), GUILayout.Height(24)))
                        ClaudeSkillInstaller.Uninstall();
                }
                else
                {
                    GUI.backgroundColor = new Color(0.22f, 0.65f, 0.32f);
                    if (GUILayout.Button("安装技能", GUILayout.Width(100), GUILayout.Height(24)))
                        ClaudeSkillInstaller.Install();
                }

                if (isInstalled && installedVersion != sourceVersion)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.5f, 0.8f);
                    if (GUILayout.Button("更新", GUILayout.Width(60), GUILayout.Height(24)))
                        ClaudeSkillInstaller.Install();
                }
            }

            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
