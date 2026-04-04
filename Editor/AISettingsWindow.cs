using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// UniAI 渠道管理窗口 — 双面板布局，左侧渠道列表，右侧渠道配置详情
    /// </summary>
    public class AIChannelWindow : EditorWindow
    {
        private const float LeftPanelWidth = 200f;
        private const float LabelWidth = 100f;
        private const float Pad = 10f;
        private const string IconsDir = "Assets/UniAI/Editor/Icons";

        // Colors
        private static readonly Color _greenDot = new(0.3f, 0.85f, 0.4f);
        private static readonly Color _greyDot = new(0.45f, 0.45f, 0.45f);
        private static readonly Color _orangeDot = new(0.92f, 0.65f, 0.1f);
        private static readonly Color _blueLink = new(0.4f, 0.72f, 1f);

        // State
        private AIConfig _config;
        private int _selectedIndex;
        private Vector2 _rightScroll;

        // Per-provider state (keyed by provider Id)
        private readonly HashSet<string> _showApiKey = new();
        private readonly Dictionary<string, ConnStatus> _connStatus = new();
        private readonly Dictionary<string, string> _connError = new();
        private readonly Dictionary<string, string> _modelInput = new(); // 模型输入框状态

        // Icons cache
        private Texture2D _eyeOpenIcon;
        private Texture2D _eyeCloseIcon;

        private enum ConnStatus { Testing, Connected, Error }

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _channelTitleStyle;
        private GUIStyle _providerLabelStyle;
        private GUIStyle _statusLinkStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _dotStyle;
        private GUIStyle _addBtnStyle;
        private bool _stylesReady;

        // Model fetching state (keyed by provider Id)
        private readonly Dictionary<string, bool> _fetchingModels = new();
        private readonly Dictionary<string, ModelListResult> _fetchedModels = new();

        [MenuItem("Window/UniAI/渠道管理")]
        [MenuItem("Tools/UniAI/渠道管理")]
        public static void Open()
        {
            var w = GetWindow<AIChannelWindow>("渠道管理");
            w.minSize = new Vector2(720, 460);
        }

        private void OnEnable()
        {
            _config = AIConfigManager.LoadConfig();
            _eyeOpenIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/ui-eye-open.png");
            _eyeCloseIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/ui-eye-close.png");
        }

        private ChannelEntry SelectedEntry =>
            _config.Providers.Count > 0 && _selectedIndex < _config.Providers.Count
                ? _config.Providers[_selectedIndex]
                : null;

        // ────────────────────────────── OnGUI ──────────────────────────────

        private void OnGUI()
        {
            if (_config == null) _config = AIConfigManager.LoadConfig();
            EnsureStyles();

            // Left panel bg + separator
            EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), EditorGUIHelper.LeftPanelBg);
            EditorGUI.DrawRect(new Rect(LeftPanelWidth, 0, 1, position.height), EditorGUIHelper.SeparatorColor);

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
            if (GUILayout.Button("添加渠道", _addBtnStyle, GUILayout.Height(26)))
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
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(32));

            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isSelected ? EditorGUIHelper.ItemSelectedBg : EditorGUIHelper.ItemBg);

            GUILayout.Space(Pad);

            // Enabled toggle
            var newEnabled = EditorGUILayout.Toggle(entry.Enabled, GUILayout.Width(16), GUILayout.Height(32));
            if (newEnabled != entry.Enabled)
            {
                entry.Enabled = newEnabled;
                AIConfigManager.SaveConfig(_config);
            }

            GUILayout.Space(4);

            // Name
            EditorGUI.BeginDisabledGroup(!entry.Enabled);
            GUILayout.Label(entry.Name ?? entry.Id, EditorStyles.label, GUILayout.Height(32));
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            // Status dot
            _dotStyle.normal.textColor = entry.Enabled ? GetDotColor(entry.Id) : _greyDot;
            GUILayout.Label("●", _dotStyle, GUILayout.Width(16), GUILayout.Height(32));

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

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(Pad);

            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label("渠道管理", _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("系统状态: ", EditorStyles.miniLabel);
            GUILayout.Label(GetSystemStatusText(), _statusLinkStyle);
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16);

            if (SelectedEntry != null)
                DrawSection(DrawProviderConfig);
            else
                DrawSection(() => GUILayout.Label("未选择渠道，请点击「添加渠道」开始配置。"));

            GUILayout.FlexibleSpace();

            DrawBottomBar();

            GUILayout.Space(Pad);
        }

        private void DrawSection(Action drawContent)
        {
            EditorGUIHelper.DrawSection(Pad, drawContent);
        }

        private void DrawProviderConfig()
        {
            var entry = SelectedEntry;

            // Title row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{entry.Name} 渠道配置", _channelTitleStyle);
            GUILayout.FlexibleSpace();

            if (_connStatus.TryGetValue(entry.Id, out var status) && status == ConnStatus.Connected)
            {
                var s = new GUIStyle(EditorStyles.miniLabel);
                s.normal.textColor = _greenDot;
                GUILayout.Label("✔ 已连接", s);
                GUILayout.Space(4);
            }

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(24))) { }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Enabled toggle
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("启用", GUILayout.Width(LabelWidth));
            var newEnabled = EditorGUILayout.Toggle(entry.Enabled);
            if (newEnabled != entry.Enabled)
            {
                entry.Enabled = newEnabled;
                AIConfigManager.SaveConfig(_config);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Name
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("名称", GUILayout.Width(LabelWidth));
            if (ChannelPresets.IsPresetId(entry.Id))
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(entry.Name);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                entry.Name = EditorGUILayout.TextField(entry.Name ?? "");
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Common fields
            DrawApiKeyRow(entry);
            DrawTextField("Base URL", ref entry.BaseUrl);
            DrawModelTags(entry);

            // Claude-specific
            if (entry.Protocol == ProviderProtocol.Claude)
                DrawTextField("API Version", ref entry.ApiVersion);

            // Protocol
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Protocol", GUILayout.Width(LabelWidth));
            bool isPreset = ChannelPresets.IsPresetId(entry.Id);
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

        private void DrawApiKeyRow(ChannelEntry entry)
        {
            var envVarName = EditorPreferences.GetEnvVarName(entry.Id);
            string envVal = null;
            if (!string.IsNullOrEmpty(envVarName))
                envVal = Environment.GetEnvironmentVariable(envVarName);
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
            string btnText = isTesting ? "测试中..." : $"测试 ({entry.Name})";
            if (GUILayout.Button(btnText, GUILayout.Width(110)))
                TestProvider(entry);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Env var hint
            if (hasEnv)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth + 4);
                EditorGUILayout.LabelField($"来自环境变量 ${envVarName}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

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

        private void DrawModelTags(ChannelEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Models", GUILayout.Width(LabelWidth));
            EditorGUILayout.BeginVertical();

            // 已添加的模型标签
            if (entry.Models != null && entry.Models.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                float lineWidth = 0;
                float maxWidth = EditorGUIUtility.currentViewWidth - LeftPanelWidth - LabelWidth - 60;

                for (int i = entry.Models.Count - 1; i >= 0; i--)
                {
                    var model = entry.Models[i];
                    var content = new GUIContent($"  {model}  ✕");
                    float tagWidth = EditorStyles.miniButton.CalcSize(content).x + 4;

                    if (lineWidth + tagWidth > maxWidth && lineWidth > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        lineWidth = 0;
                    }

                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Height(20)))
                    {
                        entry.Models.RemoveAt(i);
                        AIConfigManager.SaveConfig(_config);
                    }

                    lineWidth += tagWidth;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            // 输入框 + 添加按钮 + 获取模型按钮
            if (!_modelInput.ContainsKey(entry.Id))
                _modelInput[entry.Id] = "";

            EditorGUILayout.BeginHorizontal();
            _modelInput[entry.Id] = EditorGUILayout.TextField(_modelInput[entry.Id], GUILayout.Height(20));
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(20)))
            {
                AddModelToEntry(entry);
            }

            // 获取模型列表按钮
            bool isFetching = _fetchingModels.TryGetValue(entry.Id, out var f) && f;
            EditorGUI.BeginDisabledGroup(isFetching || !HasApiKey(entry));
            string fetchBtnText = isFetching ? "获取中..." : "获取模型";
            if (GUILayout.Button(fetchBtnText, EditorStyles.miniButton, GUILayout.Width(68), GUILayout.Height(20)))
            {
                FetchModelsForEntry(entry);
            }
            EditorGUI.EndDisabledGroup();

            // 回车添加
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == $"model_input_{entry.Id}")
            {
                AddModelToEntry(entry);
                Event.current.Use();
            }

            EditorGUILayout.EndHorizontal();

            // 获取模型错误提示
            if (_fetchedModels.TryGetValue(entry.Id, out var fetchResult) && !fetchResult.IsSuccess)
            {
                EditorGUILayout.LabelField($"⚠ {fetchResult.Error}", _errorStyle);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private async void FetchModelsForEntry(ChannelEntry entry)
        {
            _fetchingModels[entry.Id] = true;
            _fetchedModels.Remove(entry.Id);
            Repaint();

            try
            {
                var result = await ModelListService.FetchModelsAsync(entry, _config.General);
                _fetchedModels[entry.Id] = result;

                if (result.IsSuccess && result.Models.Count > 0)
                {
                    ShowModelSelectionMenu(entry, result.Models);
                }
                else if (result.IsSuccess)
                {
                    _fetchedModels[entry.Id] = ModelListResult.Fail("未获取到任何模型。");
                }
            }
            catch (Exception e)
            {
                _fetchedModels[entry.Id] = ModelListResult.Fail(e.Message);
            }

            _fetchingModels[entry.Id] = false;
            Repaint();
        }

        private void ShowModelSelectionMenu(ChannelEntry entry, List<ModelInfo> models)
        {
            var existingModels = new HashSet<string>(entry.Models ?? new List<string>());
            var menu = new GenericMenu();

            foreach (var model in models)
            {
                bool alreadyAdded = existingModels.Contains(model.Id);
                if (alreadyAdded)
                {
                    menu.AddDisabledItem(new GUIContent($"✔ {model.Label}"));
                }
                else
                {
                    var m = model; // capture
                    menu.AddItem(new GUIContent(m.Label), false, () =>
                    {
                        entry.Models ??= new List<string>();
                        if (!entry.Models.Contains(m.Id))
                        {
                            entry.Models.Add(m.Id);
                            AIConfigManager.SaveConfig(_config);
                            Repaint();
                        }
                    });
                }
            }

            menu.ShowAsContext();
        }

        private void AddModelToEntry(ChannelEntry entry)
        {
            var input = _modelInput[entry.Id]?.Trim();
            if (string.IsNullOrEmpty(input)) return;
            if (entry.Models.Contains(input)) return;

            entry.Models.Add(input);
            _modelInput[entry.Id] = "";
            AIConfigManager.SaveConfig(_config);
            Repaint();
        }

        // ─── Bottom Bar ───

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);

            if (GUILayout.Button("测试所有连接", GUILayout.Height(28), GUILayout.Width(160)))
                TestAllProviders();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存", GUILayout.Height(28), GUILayout.Width(80)))
            {
                AIConfigManager.SaveConfig(_config);
                ShowNotification(new GUIContent("配置已保存"));
            }

            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();
        }

        // ────────────────────────────── Add / Remove Provider ──────────────────────────────

        private void ShowAddProviderMenu()
        {
            var menu = new GenericMenu();
            var existingIds = new HashSet<string>(_config.Providers.Select(p => p.Id));

            foreach (var preset in ChannelPresets.All)
            {
                if (existingIds.Contains(preset.Id))
                {
                    menu.AddDisabledItem(new GUIContent($"{preset.Name} (已添加)"));
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
            menu.AddItem(new GUIContent("自定义 (OpenAI 兼容)"), false, () =>
            {
                var id = $"custom_{DateTime.Now.Ticks}";
                _config.Providers.Add(new ChannelEntry
                {
                    Id = id,
                    Name = "Custom",
                    Protocol = ProviderProtocol.OpenAI,
                    BaseUrl = "https://",
                    Models = new List<string>()
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
            menu.AddItem(new GUIContent($"移除「{entry.Name}」"), false, () =>
            {
                _connStatus.Remove(entry.Id);
                _connError.Remove(entry.Id);
                _showApiKey.Remove(entry.Id);
                _modelInput.Remove(entry.Id);
                _fetchingModels.Remove(entry.Id);
                _fetchedModels.Remove(entry.Id);
                _config.Providers.RemoveAt(index);
                if (_selectedIndex >= _config.Providers.Count)
                    _selectedIndex = Mathf.Max(0, _config.Providers.Count - 1);
                Repaint();
            });
            menu.ShowAsContext();
        }

        // ────────────────────────────── Connection Test ──────────────────────────────

        private async void TestProvider(ChannelEntry entry)
        {
            _connStatus[entry.Id] = ConnStatus.Testing;
            _connError.Remove(entry.Id);
            Repaint();

            try
            {
                var testEntry = new ChannelEntry
                {
                    Id = entry.Id, Name = entry.Name, Protocol = entry.Protocol,
                    ApiKey = AIConfigManager.GetEffectiveApiKey(entry), BaseUrl = entry.BaseUrl,
                    Models = entry.Models, ApiVersion = entry.ApiVersion
                };

                var client = AIClient.Create(testEntry, testEntry.DefaultModel, _config.General);
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

        private bool HasApiKey(ChannelEntry entry)
        {
            return !string.IsNullOrEmpty(AIConfigManager.GetEffectiveApiKey(entry));
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

            if (configured == 0) return "未配置渠道";
            if (connected == configured && connected > 0) return "所有渠道已连接";
            return $"{connected}/{configured} 已连接";
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _channelTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
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
