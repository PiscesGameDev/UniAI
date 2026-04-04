using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// UniAI 配置窗口 — 双面板布局，左侧动态 Provider 列表，右侧配置详情
    /// </summary>
    public class AISettingsWindow : EditorWindow
    {
        private const float LeftPanelWidth = 200f;
        private const float LabelWidth = 100f;
        private const float Pad = 10f;
        private const float IconSize = 22f;
        private const float IconSizeLarge = 28f;
        private const string IconsDir = "Assets/UniAI/Editor/Icons";

        // Colors
        private static readonly Color _leftPanelBg = new(0.18f, 0.18f, 0.18f);
        private static readonly Color _itemBg = new(0.24f, 0.24f, 0.24f);
        private static readonly Color _itemSelectedBg = new(0.30f, 0.30f, 0.33f);
        private static readonly Color _separatorColor = new(0.12f, 0.12f, 0.12f);
        private static readonly Color _greenDot = new(0.3f, 0.85f, 0.4f);
        private static readonly Color _greyDot = new(0.45f, 0.45f, 0.45f);
        private static readonly Color _orangeDot = new(0.92f, 0.65f, 0.1f);
        private static readonly Color _blueLink = new(0.4f, 0.72f, 1f);
        private static readonly Color _sectionBg = new(0.21f, 0.21f, 0.21f);

        // State
        private AIConfig _config;
        private int _selectedIndex;
        private Vector2 _rightScroll;

        // Per-provider state (keyed by provider Id)
        private readonly HashSet<string> _showApiKey = new();
        private readonly Dictionary<string, ConnStatus> _connStatus = new();
        private readonly Dictionary<string, string> _connError = new();

        // Icons cache
        private readonly Dictionary<string, Texture2D> _iconCache = new();
        private Texture2D _eyeOpenIcon;
        private Texture2D _eyeCloseIcon;

        private enum ConnStatus { Testing, Connected, Error }

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _providerLabelStyle;
        private GUIStyle _statusLinkStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _dotStyle;
        private GUIStyle _addBtnStyle;
        private bool _stylesReady;

        [MenuItem("Window/UniAI/Configuration")]
        [MenuItem("Tools/UniAI/Configuration")]
        public static void Open()
        {
            var w = GetWindow<AISettingsWindow>("Configuration");
            w.minSize = new Vector2(720, 460);
        }

        private void OnEnable()
        {
            _config = AIConfigManager.LoadConfig();
            _eyeOpenIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/ui-eye-open.png");
            _eyeCloseIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/ui-eye-close.png");
        }

        private Texture2D GetProviderIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (_iconCache.TryGetValue(iconName, out var cached)) return cached;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/{iconName}.png");
            _iconCache[iconName] = tex;
            return tex;
        }

        private ProviderEntry SelectedEntry =>
            _config.Providers.Count > 0 && _selectedIndex < _config.Providers.Count
                ? _config.Providers[_selectedIndex]
                : null;

        // ────────────────────────────── OnGUI ──────────────────────────────

        private void OnGUI()
        {
            if (_config == null) _config = AIConfigManager.LoadConfig();
            EnsureStyles();

            // Left panel bg + separator
            EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), _leftPanelBg);
            EditorGUI.DrawRect(new Rect(LeftPanelWidth, 0, 1, position.height), _separatorColor);

            // Left panel
            GUILayout.BeginArea(new Rect(0, 0, LeftPanelWidth, position.height));
            DrawLeftPanel();
            GUILayout.EndArea();

            // Right panel
            float rx = LeftPanelWidth + 1;
            GUILayout.BeginArea(new Rect(rx, 0, position.width - rx, position.height));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawRightPanel();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Left Panel ──────────────────────────────

        private void DrawLeftPanel()
        {
            GUILayout.Space(Pad);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label("UniAI", _titleStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            if (GUILayout.Button("Add Provider", _addBtnStyle, GUILayout.Height(26)))
                ShowAddProviderMenu();
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            for (int i = 0; i < _config.Providers.Count; i++)
            {
                DrawProviderItem(i);
                GUILayout.Space(2);
            }

            GUILayout.FlexibleSpace();
        }

        private void DrawProviderItem(int index)
        {
            var entry = _config.Providers[index];
            bool isSelected = _selectedIndex == index;
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(36));

            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isSelected ? _itemSelectedBg : _itemBg);

            GUILayout.Space(Pad);

            // Icon
            DrawIcon(entry.IconName, IconSize, 36);
            GUILayout.Space(6);

            // Name
            GUILayout.Label(entry.Name ?? entry.Id, EditorStyles.label, GUILayout.Height(36));

            GUILayout.FlexibleSpace();

            // Status dot
            _dotStyle.normal.textColor = GetDotColor(entry.Id);
            GUILayout.Label("●", _dotStyle, GUILayout.Width(16), GUILayout.Height(36));

            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            // Left click = select
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowProviderContextMenu(index);
                else
                    _selectedIndex = index;

                Event.current.Use();
                Repaint();
            }
        }

        private void DrawIcon(string iconName, float size, float height)
        {
            var icon = GetProviderIcon(iconName);
            if (icon != null)
            {
                var r = GUILayoutUtility.GetRect(size, height, GUILayout.Width(size), GUILayout.Height(height));
                float y = r.y + (r.height - size) * 0.5f;
                GUI.DrawTexture(new Rect(r.x, y, size, size), icon, ScaleMode.ScaleToFit);
            }
            else
            {
                var s = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
                string fallback = string.IsNullOrEmpty(iconName) ? "?" : iconName[0].ToString().ToUpper();
                GUILayout.Label(fallback, s, GUILayout.Width(size), GUILayout.Height(height));
            }
        }

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(Pad);

            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label("UniAI Configuration", _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("System Status: ", EditorStyles.miniLabel);
            GUILayout.Label(GetSystemStatusText(), _statusLinkStyle);
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16);

            if (SelectedEntry != null)
                DrawSection(DrawProviderConfig);
            else
                DrawSection(() => GUILayout.Label("No provider selected. Click 'Add Provider' to get started."));

            GUILayout.Space(12);

            DrawSection(DrawGeneralSettings);

            GUILayout.FlexibleSpace();

            DrawBottomBar();

            GUILayout.Space(Pad);
        }

        private void DrawSection(Action drawContent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            var r = EditorGUILayout.BeginVertical();
            if (r.width > 1)
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), _sectionBg);
            GUILayout.Space(8);
            drawContent();
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProviderConfig()
        {
            var entry = SelectedEntry;

            // Title row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{entry.Name} Configuration", _sectionTitleStyle);
            GUILayout.FlexibleSpace();

            if (_connStatus.TryGetValue(entry.Id, out var status) && status == ConnStatus.Connected)
            {
                var s = new GUIStyle(EditorStyles.miniLabel);
                s.normal.textColor = _greenDot;
                GUILayout.Label("✔ Connected", s);
                GUILayout.Space(4);
            }

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(24))) { }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Icon + name
            EditorGUILayout.BeginHorizontal();
            DrawIcon(entry.IconName, IconSizeLarge, IconSizeLarge);
            GUILayout.Space(6);
            if (ProviderPresets.IsPresetId(entry.Id))
            {
                GUILayout.Label(entry.Name, _providerLabelStyle, GUILayout.Height(IconSizeLarge));
            }
            else
            {
                entry.Name = EditorGUILayout.TextField(entry.Name ?? "", _providerLabelStyle, GUILayout.Height(IconSizeLarge));
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Common fields
            DrawApiKeyRow(entry);
            DrawTextField("Base URL", ref entry.BaseUrl);
            DrawTextField("Model", ref entry.Model);

            // Claude-specific
            if (entry.Protocol == ProviderProtocol.Claude)
                DrawTextField("API Version", ref entry.ApiVersion);

            // Protocol
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Protocol", GUILayout.Width(LabelWidth));
            bool isPreset = ProviderPresets.IsPresetId(entry.Id);
            if (isPreset)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.EnumPopup(entry.Protocol);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                var newProtocol = (ProviderProtocol)EditorGUILayout.EnumPopup(entry.Protocol);
                if (newProtocol != entry.Protocol)
                {
                    entry.Protocol = newProtocol;
                    if (newProtocol == ProviderProtocol.Claude && string.IsNullOrEmpty(entry.ApiVersion))
                        entry.ApiVersion = "2023-06-01";
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Fields ───

        private void DrawApiKeyRow(ProviderEntry entry)
        {
            string envVal = null;
            if (!string.IsNullOrEmpty(entry.EnvVarName))
                envVal = Environment.GetEnvironmentVariable(entry.EnvVarName);
            bool hasEnv = !string.IsNullOrEmpty(envVal);
            bool isVisible = _showApiKey.Contains(entry.Id);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key", GUILayout.Width(LabelWidth));

            if (hasEnv)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PasswordField(envVal, GUILayout.ExpandWidth(true));
                EditorGUI.EndDisabledGroup();
            }
            else if (isVisible)
            {
                entry.ApiKey = EditorGUILayout.TextField(entry.ApiKey ?? "");
            }
            else
            {
                entry.ApiKey = EditorGUILayout.PasswordField(entry.ApiKey ?? "");
            }

            // Eye toggle
            if (!hasEnv)
            {
                var eyeIcon = isVisible ? _eyeOpenIcon : _eyeCloseIcon;
                bool clicked = eyeIcon != null
                    ? GUILayout.Button(new GUIContent(eyeIcon), GUILayout.Width(26), GUILayout.Height(18))
                    : GUILayout.Button(isVisible ? "◉" : "◎", GUILayout.Width(26));

                if (clicked)
                {
                    if (isVisible) _showApiKey.Remove(entry.Id);
                    else _showApiKey.Add(entry.Id);
                }
            }

            // Test button
            bool isTesting = _connStatus.TryGetValue(entry.Id, out var st) && st == ConnStatus.Testing;
            EditorGUI.BeginDisabledGroup(isTesting);
            string btnText = isTesting ? "Testing..." : $"Test ({entry.Name})";
            if (GUILayout.Button(btnText, GUILayout.Width(110)))
                TestProvider(entry);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Error line
            if (_connStatus.TryGetValue(entry.Id, out var s2) && s2 == ConnStatus.Error
                && _connError.TryGetValue(entry.Id, out var err) && !string.IsNullOrEmpty(err))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth + 4);
                EditorGUILayout.LabelField($"⚠ {err}", _errorStyle);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTextField(string label, ref string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelWidth));
            value = EditorGUILayout.TextField(value ?? "");
            EditorGUILayout.EndHorizontal();
        }

        // ─── General Settings ───

        private void DrawGeneralSettings()
        {
            GUILayout.Label("General settings", _sectionTitleStyle);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Timeout (s)", GUILayout.Width(LabelWidth));
            _config.General.TimeoutSeconds = EditorGUILayout.IntSlider(_config.General.TimeoutSeconds, 10, 300);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Max Retries", GUILayout.Width(LabelWidth));
            _config.General.MaxRetries = EditorGUILayout.IntSlider(_config.General.MaxRetries, 0, 5);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Log Level", GUILayout.Width(LabelWidth));
            _config.General.LogLevel = (AILogLevel)EditorGUILayout.EnumPopup(_config.General.LogLevel);
            EditorGUILayout.EndHorizontal();
        }

        // ─── Bottom Bar ───

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);

            if (GUILayout.Button("Test All Connections", GUILayout.Height(28), GUILayout.Width(160)))
                TestAllProviders();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Save", GUILayout.Height(28), GUILayout.Width(80)))
            {
                AIConfigManager.SaveConfig(_config);
                ShowNotification(new GUIContent("Configuration saved"));
            }

            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();
        }

        // ────────────────────────────── Add / Remove Provider ──────────────────────────────

        private void ShowAddProviderMenu()
        {
            var menu = new GenericMenu();
            var existingIds = new HashSet<string>(_config.Providers.Select(p => p.Id));

            foreach (var preset in ProviderPresets.All)
            {
                if (existingIds.Contains(preset.Id))
                {
                    menu.AddDisabledItem(new GUIContent($"{preset.Name} (already added)"));
                }
                else
                {
                    var p = preset; // capture
                    menu.AddItem(new GUIContent(preset.Name), false, () =>
                    {
                        _config.Providers.Add(p);
                        _selectedIndex = _config.Providers.Count - 1;
                        Repaint();
                    });
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Custom (OpenAI Compatible)"), false, () =>
            {
                var id = $"custom_{DateTime.Now.Ticks}";
                _config.Providers.Add(new ProviderEntry
                {
                    Id = id,
                    Name = "Custom",
                    Protocol = ProviderProtocol.OpenAI,
                    BaseUrl = "https://",
                    Model = ""
                });
                _selectedIndex = _config.Providers.Count - 1;
                Repaint();
            });

            menu.ShowAsContext();
        }

        private void ShowProviderContextMenu(int index)
        {
            var entry = _config.Providers[index];
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"Remove \"{entry.Name}\""), false, () =>
            {
                _connStatus.Remove(entry.Id);
                _connError.Remove(entry.Id);
                _showApiKey.Remove(entry.Id);
                _config.Providers.RemoveAt(index);
                if (_selectedIndex >= _config.Providers.Count)
                    _selectedIndex = Mathf.Max(0, _config.Providers.Count - 1);
                Repaint();
            });
            menu.ShowAsContext();
        }

        // ────────────────────────────── Connection Test ──────────────────────────────

        private async void TestProvider(ProviderEntry entry)
        {
            _connStatus[entry.Id] = ConnStatus.Testing;
            _connError.Remove(entry.Id);
            Repaint();

            try
            {
                // 环境变量覆盖
                var apiKey = entry.ApiKey;
                if (!string.IsNullOrEmpty(entry.EnvVarName))
                {
                    var envKey = Environment.GetEnvironmentVariable(entry.EnvVarName);
                    if (!string.IsNullOrEmpty(envKey)) apiKey = envKey;
                }

                var testEntry = new ProviderEntry
                {
                    Id = entry.Id, Name = entry.Name, Protocol = entry.Protocol,
                    ApiKey = apiKey, BaseUrl = entry.BaseUrl, Model = entry.Model,
                    ApiVersion = entry.ApiVersion
                };

                var client = AIClient.Create(testEntry, _config.General);
                var response = await client.SendAsync(new AIRequest
                {
                    Messages = { AIMessage.User("Hi, respond with just \"ok\".") },
                    MaxTokens = 16,
                    Temperature = 0f
                });

                _connStatus[entry.Id] = response.IsSuccess ? ConnStatus.Connected : ConnStatus.Error;
                if (!response.IsSuccess) _connError[entry.Id] = response.Error;
            }
            catch (Exception e)
            {
                _connStatus[entry.Id] = ConnStatus.Error;
                _connError[entry.Id] = e.Message;
            }

            Repaint();
        }

        private void TestAllProviders()
        {
            foreach (var entry in _config.Providers)
            {
                if (HasApiKey(entry))
                    TestProvider(entry);
            }
        }

        // ────────────────────────────── Helpers ──────────────────────────────

        private bool HasApiKey(ProviderEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.ApiKey)) return true;
            if (!string.IsNullOrEmpty(entry.EnvVarName))
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(entry.EnvVarName));
            return false;
        }

        private Color GetDotColor(string providerId)
        {
            if (!_connStatus.TryGetValue(providerId, out var s)) return _greyDot;
            return s switch
            {
                ConnStatus.Connected => _greenDot,
                ConnStatus.Error => _orangeDot,
                ConnStatus.Testing => Color.yellow,
                _ => _greyDot
            };
        }

        private string GetSystemStatusText()
        {
            int connected = 0, configured = 0;
            foreach (var entry in _config.Providers)
            {
                if (HasApiKey(entry)) configured++;
                if (_connStatus.TryGetValue(entry.Id, out var s) && s == ConnStatus.Connected) connected++;
            }

            if (configured == 0) return "No providers configured";
            if (connected == configured && connected > 0) return "All Providers Connected";
            return $"{connected}/{configured} Connected";
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _providerLabelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft };

            _statusLinkStyle = new GUIStyle(EditorStyles.miniLabel);
            _statusLinkStyle.normal.textColor = _blueLink;

            _errorStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _errorStyle.normal.textColor = _orangeDot;

            _dotStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
