using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// UniAI 渠道管理窗口 — 双面板布局，左侧渠道列表，右侧渠道配置 + 模型诊断
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
        private static readonly Color _yellowDot = new(0.95f, 0.9f, 0.3f);

        // State
        private AIConfig _config;
        private int _selectedIndex;
        private Vector2 _rightScroll;

        // Per-provider UI state
        private readonly HashSet<string> _showApiKey = new();
        private readonly Dictionary<string, string> _modelInput = new();

        // Model test results: key = "{channelId}:{modelId}"
        private readonly Dictionary<string, ModelTestResult> _modelResults = new();

        // Model fetching state (keyed by provider Id)
        private readonly Dictionary<string, bool> _fetchingModels = new();
        private readonly Dictionary<string, ModelListResult> _fetchedModels = new();

        // Icons
        private Texture2D _eyeOpenIcon;
        private Texture2D _eyeCloseIcon;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _channelTitleStyle;
        private GUIStyle _statusLinkStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _dotStyle;
        private GUIStyle _addBtnStyle;
        private GUIStyle _modelNameStyle;
        private bool _stylesReady;

        private class ModelTestResult
        {
            public bool IsTesting;
            public bool? IsSuccess; // null = 待测
            public long LatencyMs;
            public string Error;
        }

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

        private string ModelKey(string channelId, string modelId) => $"{channelId}:{modelId}";

        // ────────────────────────────── OnGUI ──────────────────────────────

        private void OnGUI()
        {
            if (_config == null) _config = AIConfigManager.LoadConfig();
            EnsureStyles();

            EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), EditorGUIHelper.LeftPanelBg);
            EditorGUI.DrawRect(new Rect(LeftPanelWidth, 0, 1, position.height), EditorGUIHelper.SeparatorColor);

            GUILayout.BeginArea(new Rect(0, 0, LeftPanelWidth, position.height));
            DrawLeftPanel();
            GUILayout.EndArea();

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

            var newEnabled = EditorGUILayout.Toggle(entry.Enabled, GUILayout.Width(16), GUILayout.Height(32));
            if (newEnabled != entry.Enabled)
            {
                entry.Enabled = newEnabled;
                AIConfigManager.SaveConfig(_config);
            }

            GUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(!entry.Enabled);
            GUILayout.Label(entry.Name ?? entry.Id, EditorStyles.label, GUILayout.Height(32));
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            // Aggregated status dot
            _dotStyle.normal.textColor = entry.Enabled ? GetChannelDotColor(entry) : _greyDot;
            GUILayout.Label("●", _dotStyle, GUILayout.Width(16), GUILayout.Height(32));

            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

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

        /// <summary>
        /// 聚合渠道下所有模型的测试状态
        /// </summary>
        private Color GetChannelDotColor(ChannelEntry entry)
        {
            if (entry.Models == null || entry.Models.Count == 0) return _greyDot;

            bool anyTesting = false, anySuccess = false, anyFail = false, allTested = true;
            foreach (var modelId in entry.Models)
            {
                var key = ModelKey(entry.Id, modelId);
                if (!_modelResults.TryGetValue(key, out var r))
                {
                    allTested = false;
                    continue;
                }
                if (r.IsTesting) anyTesting = true;
                else if (r.IsSuccess == true) anySuccess = true;
                else if (r.IsSuccess == false) anyFail = true;
                else allTested = false;
            }

            if (anyTesting) return _yellowDot;
            if (anyFail) return _orangeDot;
            if (anySuccess && allTested) return _greenDot;
            if (anySuccess) return _greenDot; // partial success
            return _greyDot;
        }

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(Pad);

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
            {
                DrawSection(DrawProviderConfig);
                GUILayout.Space(12);
                DrawSection(() => DrawModelDiagnostics(SelectedEntry));
            }
            else
            {
                DrawSection(() => GUILayout.Label("未选择渠道，请点击「添加渠道」开始配置。"));
            }

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
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Enabled
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

            // API Key
            DrawApiKeyRow(entry);

            // Base URL
            DrawTextField("Base URL", ref entry.BaseUrl);

            // Claude-specific
            if (entry.Protocol == ProviderProtocol.Claude)
                DrawTextField("API Version", ref entry.ApiVersion);

            // Protocol
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Protocol", GUILayout.Width(LabelWidth));
            if (ChannelPresets.IsPresetId(entry.Id))
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

            EditorGUILayout.EndHorizontal();

            // Env var hint
            if (hasEnv)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LabelWidth + 4);
                EditorGUILayout.LabelField($"来自环境变量 ${envVarName}", EditorStyles.miniLabel);
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

        // ────────────────────────────── Model Diagnostics ──────────────────────────────

        private void DrawModelDiagnostics(ChannelEntry entry)
        {
            GUILayout.Label("模型诊断", _channelTitleStyle);
            GUILayout.Space(6);

            bool hasApiKey = HasApiKey(entry);

            // Model list table
            if (entry.Models != null && entry.Models.Count > 0)
            {
                // Header
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("模型 ID", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("状态", EditorStyles.miniLabel, GUILayout.Width(50));
                GUILayout.Label("延迟", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Space(96); // test + delete button width
                EditorGUILayout.EndHorizontal();

                EditorGUIHelper.DrawSeparator();

                for (int i = 0; i < entry.Models.Count; i++)
                {
                    var modelId = entry.Models[i];
                    var key = ModelKey(entry.Id, modelId);
                    _modelResults.TryGetValue(key, out var result);

                    // Model row
                    EditorGUILayout.BeginHorizontal();

                    // Model name
                    GUILayout.Label(modelId, _modelNameStyle, GUILayout.ExpandWidth(true));

                    // Status dot + label
                    DrawModelStatus(result, 50);

                    // Latency
                    string latencyText = "---";
                    if (result is { IsTesting: false, IsSuccess: true })
                        latencyText = $"{result.LatencyMs}ms";
                    GUILayout.Label(latencyText, EditorStyles.miniLabel, GUILayout.Width(60));

                    // Test button
                    bool isTesting = result is { IsTesting: true };
                    EditorGUI.BeginDisabledGroup(isTesting || !hasApiKey);
                    if (GUILayout.Button(isTesting ? "..." : "测试", EditorStyles.miniButton, GUILayout.Width(44)))
                    {
                        TestModel(entry, modelId);
                    }
                    EditorGUI.EndDisabledGroup();

                    // Delete button
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        entry.Models.RemoveAt(i);
                        _modelResults.Remove(key);
                        AIConfigManager.SaveConfig(_config);
                        GUIUtility.ExitGUI();
                        return;
                    }

                    EditorGUILayout.EndHorizontal();

                    // Error detail line
                    if (result is { IsSuccess: false, IsTesting: false } && !string.IsNullOrEmpty(result.Error))
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(16);
                        EditorGUILayout.LabelField($"⚠ {result.Error}", _errorStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUIHelper.DrawSeparator();
            }
            else
            {
                EditorGUILayout.LabelField("暂无模型，请手动添加或获取模型列表。", EditorStyles.miniLabel);
                GUILayout.Space(4);
            }

            GUILayout.Space(4);

            // Add model row
            if (!_modelInput.ContainsKey(entry.Id))
                _modelInput[entry.Id] = "";

            EditorGUILayout.BeginHorizontal();
            _modelInput[entry.Id] = EditorGUILayout.TextField(_modelInput[entry.Id], GUILayout.Height(20));
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(20)))
            {
                AddModelToEntry(entry);
            }

            bool isFetching = _fetchingModels.TryGetValue(entry.Id, out var f) && f;
            EditorGUI.BeginDisabledGroup(isFetching || !hasApiKey);
            if (GUILayout.Button(isFetching ? "获取中..." : "获取模型", EditorStyles.miniButton, GUILayout.Width(68), GUILayout.Height(20)))
            {
                FetchModelsForEntry(entry);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // Fetch error
            if (_fetchedModels.TryGetValue(entry.Id, out var fetchResult) && !fetchResult.IsSuccess)
            {
                EditorGUILayout.LabelField($"⚠ {fetchResult.Error}", _errorStyle);
            }

            GUILayout.Space(6);

            // Test all models button
            bool anyTesting = entry.Models != null && entry.Models.Any(m =>
                _modelResults.TryGetValue(ModelKey(entry.Id, m), out var r) && r.IsTesting);

            EditorGUI.BeginDisabledGroup(anyTesting || !hasApiKey || entry.Models == null || entry.Models.Count == 0);
            if (GUILayout.Button(anyTesting ? "测试中..." : "测试所有模型", GUILayout.Height(24)))
            {
                TestAllModels(entry);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawModelStatus(ModelTestResult result, float width)
        {
            string label;
            Color color;

            if (result == null || result.IsSuccess == null && !result.IsTesting)
            {
                label = "○ 待测";
                color = _greyDot;
            }
            else if (result.IsTesting)
            {
                label = "◌ 测试中";
                color = _yellowDot;
            }
            else if (result.IsSuccess == true)
            {
                label = "● 在线";
                color = _greenDot;
            }
            else
            {
                label = "● 异常";
                color = _orangeDot;
            }

            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            GUILayout.Label(label, style, GUILayout.Width(width));
        }

        // ────────────────────────────── Model Testing ──────────────────────────────

        private async void TestModel(ChannelEntry entry, string modelId)
        {
            var key = ModelKey(entry.Id, modelId);
            _modelResults[key] = new ModelTestResult { IsTesting = true };
            Repaint();

            try
            {
                var testEntry = new ChannelEntry
                {
                    Id = entry.Id, Name = entry.Name, Protocol = entry.Protocol,
                    ApiKey = AIConfigManager.GetEffectiveApiKey(entry),
                    BaseUrl = entry.BaseUrl, Models = entry.Models, ApiVersion = entry.ApiVersion
                };

                var client = AIClient.Create(testEntry, modelId, _config.General);

                var sw = Stopwatch.StartNew();
                var response = await client.SendAsync(new AIRequest
                {
                    Messages = { AIMessage.User("Hi, respond with just \"ok\".") },
                    MaxTokens = 16,
                    Temperature = 0f
                });
                sw.Stop();

                _modelResults[key] = new ModelTestResult
                {
                    IsTesting = false,
                    IsSuccess = response.IsSuccess,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = response.IsSuccess ? null : response.Error
                };
            }
            catch (Exception e)
            {
                _modelResults[key] = new ModelTestResult
                {
                    IsTesting = false,
                    IsSuccess = false,
                    Error = e.Message
                };
            }

            Repaint();
        }

        private void TestAllModels(ChannelEntry entry)
        {
            if (entry.Models == null) return;
            foreach (var modelId in entry.Models)
            {
                TestModel(entry, modelId);
            }
        }

        // ────────────────────────────── Model Fetch ──────────────────────────────

        private async void FetchModelsForEntry(ChannelEntry entry)
        {
            _fetchingModels[entry.Id] = true;
            _fetchedModels.Remove(entry.Id);
            Repaint();

            try
            {
                var apiKey = EditorPreferences.GetEffectiveApiKey(entry);
                var timeout = _config.General?.TimeoutSeconds ?? 30;
                var result = await ModelListService.FetchModelsAsync(entry, apiKey, timeout);
                _fetchedModels[entry.Id] = result;

                if (result.IsSuccess && result.Models.Count > 0)
                    ShowModelSelectionMenu(entry, result.Models);
                else if (result.IsSuccess)
                    _fetchedModels[entry.Id] = ModelListResult.Fail("未获取到任何模型。");
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
                    var m = model;
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
                    var p = preset;
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
                // Clean up model results for this channel
                var keysToRemove = _modelResults.Keys.Where(k => k.StartsWith(entry.Id + ":")).ToList();
                foreach (var k in keysToRemove) _modelResults.Remove(k);

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

        // ────────────────────────────── Helpers ──────────────────────────────

        private bool HasApiKey(ChannelEntry entry)
        {
            return !string.IsNullOrEmpty(AIConfigManager.GetEffectiveApiKey(entry));
        }

        private string GetSystemStatusText()
        {
            int totalModels = 0, testedOk = 0, configured = 0;
            foreach (var entry in _config.Providers)
            {
                if (!HasApiKey(entry)) continue;
                configured++;
                if (entry.Models == null) continue;
                foreach (var modelId in entry.Models)
                {
                    totalModels++;
                    var key = ModelKey(entry.Id, modelId);
                    if (_modelResults.TryGetValue(key, out var r) && r.IsSuccess == true)
                        testedOk++;
                }
            }

            if (configured == 0) return "未配置渠道";
            if (totalModels == 0) return "未配置模型";
            if (testedOk == totalModels && testedOk > 0) return $"全部在线 ({testedOk}/{totalModels})";
            if (testedOk > 0) return $"{testedOk}/{totalModels} 模型在线";
            return $"{configured} 渠道已配置";
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _channelTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

            _statusLinkStyle = new GUIStyle(EditorStyles.miniLabel);
            _statusLinkStyle.normal.textColor = _blueLink;

            _errorStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _errorStyle.normal.textColor = _orangeDot;

            _dotStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
            _modelNameStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        }
    }
}
