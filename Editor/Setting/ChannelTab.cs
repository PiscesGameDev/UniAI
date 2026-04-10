using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 渠道管理 Tab — 双面板布局，左侧渠道列表，右侧渠道配置 + 模型诊断
    /// </summary>
    internal class ChannelTab : ManagerTab
    {
        public override string TabName => "渠道";
        public override string TabIcon => "⚡";
        public override int Order => 0;

        private const float LEFT_PANEL_WIDTH = 140f;
        private const float LABEL_WIDTH = 100f;
        private const float PAD = 10f;
        private const string ICONS_DIR = "Assets/UniAI/Editor/Icons";

        // Colors
        private static readonly Color _greenDot = new(0.3f, 0.85f, 0.4f);
        private static readonly Color _greyDot = new(0.45f, 0.45f, 0.45f);
        private static readonly Color _orangeDot = new(0.92f, 0.65f, 0.1f);
        private static readonly Color _yellowDot = new(0.95f, 0.9f, 0.3f);

        // State
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

        // Fetched available models list (keyed by provider Id)
        private readonly Dictionary<string, List<ModelInfo>> _availableModels = new();
        private readonly Dictionary<string, Vector2> _availableModelsScroll = new();

        // Icons
        private Texture2D _eyeOpenIcon;
        private Texture2D _eyeCloseIcon;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _channelTitleStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _addBtnStyle;
        private GUIStyle _modelNameStyle;
        private bool _stylesReady;

        private class ModelTestResult
        {
            public bool IsTesting;
            public bool? IsSuccess;
            public long LatencyMs;
            public string Error;
        }

        private ChannelEntry SelectedEntry =>
            Config.ChannelEntries.Count > 0 && _selectedIndex < Config.ChannelEntries.Count
                ? Config.ChannelEntries[_selectedIndex]
                : null;

        private string ModelKey(string channelId, string modelId) => $"{channelId}:{modelId}";

        protected override void OnInit()
        {
            _eyeOpenIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ICONS_DIR}/ui-eye-open.png");
            _eyeCloseIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ICONS_DIR}/ui-eye-close.png");
        }

        public override void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _channelTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

            _errorStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _errorStyle.normal.textColor = _orangeDot;

            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
            _modelNameStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        }

        public override void OnGUI(float width, float height)
        {
            // Left panel bg + separator
            EditorGUI.DrawRect(new Rect(0, 0, LEFT_PANEL_WIDTH, height), EditorGUIHelper.LeftPanelBg);
            EditorGUI.DrawRect(new Rect(LEFT_PANEL_WIDTH, 0, 1, height), EditorGUIHelper.SeparatorColor);

            GUILayout.BeginArea(new Rect(0, 0, LEFT_PANEL_WIDTH, height));
            DrawLeftPanel();
            GUILayout.EndArea();

            float rx = LEFT_PANEL_WIDTH + 1;
            GUILayout.BeginArea(new Rect(rx, 0, width - rx, height));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawRightPanel();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Left Panel ──────────────────────────────

        private void DrawLeftPanel()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            if (GUILayout.Button("添加渠道", _addBtnStyle, GUILayout.Height(26)))
                ShowAddProviderMenu();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            for (int i = 0; i < Config.ChannelEntries.Count; i++)
            {
                DrawProviderItem(i);
                GUILayout.Space(2);
            }

            GUILayout.FlexibleSpace();
        }

        private void DrawProviderItem(int index)
        {
            var entry = Config.ChannelEntries[index];
            bool isSelected = _selectedIndex == index;
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(30));

            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isSelected ? EditorGUIHelper.ItemSelectedBg : EditorGUIHelper.ItemBg);

            GUILayout.Space(PAD + 4);

            var oldColor = GUI.contentColor;
            if (!entry.Enabled)
                GUI.contentColor = new Color(1f, 1f, 1f, 0.35f);

            GUILayout.Label(entry.Name ?? entry.Id, EditorStyles.label, GUILayout.Height(30));

            GUI.contentColor = oldColor;

            GUILayout.FlexibleSpace();
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowProviderContextMenu(index);
                else
                    _selectedIndex = index;

                Event.current.Use();
                Window.Repaint();
            }
        }

        private string GetChannelModelStatusText(ChannelEntry entry)
        {
            if (entry.Models == null || entry.Models.Count == 0) return "";

            int total = entry.Models.Count;
            int ok = 0, fail = 0, testing = 0;
            foreach (var modelId in entry.Models)
            {
                var key = ModelKey(entry.Id, modelId);
                if (!_modelResults.TryGetValue(key, out var r)) continue;
                if (r.IsTesting) testing++;
                else if (r.IsSuccess == true) ok++;
                else if (r.IsSuccess == false) fail++;
            }

            if (testing > 0) return "测试中...";
            if (ok == 0 && fail == 0) return "";
            if (fail > 0) return $"{ok}/{total} 在线";
            return $"全部在线 ({ok})";
        }

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("渠道管理", _titleStyle);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16);

            if (SelectedEntry != null)
            {
                DrawSection(DrawChannelConfig);
                GUILayout.Space(12);
                DrawSection(() => DrawModelDiagnostics(SelectedEntry));
            }
            else
            {
                DrawSection(() => GUILayout.Label("未选择渠道，请点击「添加渠道」开始配置。"));
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
        }

        private void DrawSection(Action drawContent)
        {
            EditorGUIHelper.DrawSection(PAD, drawContent);
        }

        private void DrawChannelConfig()
        {
            var entry = SelectedEntry;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{entry.Name} 渠道配置", _channelTitleStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Enabled
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("启用", GUILayout.Width(LABEL_WIDTH));
            var newEnabled = EditorGUILayout.Toggle(entry.Enabled);
            if (newEnabled != entry.Enabled)
            {
                entry.Enabled = newEnabled;
                AIConfigManager.SaveConfig(Config);
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Id", GUILayout.Width(LABEL_WIDTH));
            EditorGUILayout.TextField(entry.Id);
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            
            GUILayout.Space(4);
            // Name
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("名称", GUILayout.Width(LABEL_WIDTH));
            entry.Name = EditorGUILayout.TextField(entry.Name ?? "");
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
            EditorGUILayout.LabelField("Protocol", GUILayout.Width(LABEL_WIDTH));

            var newProtocol = (ProviderProtocol)EditorGUILayout.EnumPopup(entry.Protocol);
            if (newProtocol != entry.Protocol)
            {
                entry.Protocol = newProtocol;
                if (newProtocol == ProviderProtocol.Claude && string.IsNullOrEmpty(entry.ApiVersion))
                    entry.ApiVersion = "2023-06-01";
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── Fields ───

        private void DrawApiKeyRow(ChannelEntry entry)
        {
            bool hasEnv = entry.IsApiKeyFromEnv();
            bool isVisible = _showApiKey.Contains(entry.Id);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key", GUILayout.Width(LABEL_WIDTH));

            if (hasEnv)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PasswordField(
                    Environment.GetEnvironmentVariable(entry.EnvVarName) ?? "",
                    GUILayout.ExpandWidth(true));
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

            if (hasEnv)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(LABEL_WIDTH + 4);
                EditorGUILayout.LabelField($"来自环境变量 ${entry.EnvVarName}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            // Env Var 配置
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("环境变量", GUILayout.Width(LABEL_WIDTH));
            entry.UseEnvVar = EditorGUILayout.Toggle(entry.UseEnvVar, GUILayout.Width(16));
            EditorGUI.BeginDisabledGroup(!entry.UseEnvVar);
            entry.EnvVarName = EditorGUILayout.TextField(entry.EnvVarName ?? "");
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextField(string label, ref string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LABEL_WIDTH));
            value = EditorGUILayout.TextField(value ?? "");
            EditorGUILayout.EndHorizontal();
        }

        // ────────────────────────────── Model Diagnostics ──────────────────────────────

        private void DrawModelDiagnostics(ChannelEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("模型诊断", _channelTitleStyle);
            var statusText = GetChannelModelStatusText(entry);
            if (!string.IsNullOrEmpty(statusText))
            {
                GUILayout.Space(8);
                GUILayout.Label(statusText, EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);

            bool hasApiKey = HasApiKey(entry);

            DrawModelList(entry, hasApiKey);
            GUILayout.Space(4);
            DrawAddModelRow(entry, hasApiKey);
            DrawAvailableModels(entry);

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

        private void DrawModelList(ChannelEntry entry, bool hasApiKey)
        {
            if (entry.Models == null || entry.Models.Count == 0)
            {
                EditorGUILayout.LabelField("暂无模型，请手动添加或获取模型列表。", EditorStyles.miniLabel);
                GUILayout.Space(4);
                return;
            }

            // Header
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("模型 ID", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("状态", EditorStyles.miniLabel, GUILayout.Width(50));
            GUILayout.Label("延迟", EditorStyles.miniLabel, GUILayout.Width(60));
            GUILayout.Space(96);
            EditorGUILayout.EndHorizontal();

            EditorGUIHelper.DrawSeparator();

            for (int i = 0; i < entry.Models.Count; i++)
            {
                var modelId = entry.Models[i];
                var key = ModelKey(entry.Id, modelId);
                _modelResults.TryGetValue(key, out var result);

                EditorGUILayout.BeginHorizontal();

                GUILayout.Label(modelId, _modelNameStyle, GUILayout.ExpandWidth(true));

                DrawModelStatus(result, 50);

                string latencyText = "---";
                if (result is { IsTesting: false, IsSuccess: true })
                    latencyText = $"{result.LatencyMs}ms";
                GUILayout.Label(latencyText, EditorStyles.miniLabel, GUILayout.Width(60));

                bool isTesting = result is { IsTesting: true };
                EditorGUI.BeginDisabledGroup(isTesting || !hasApiKey);
                if (GUILayout.Button(isTesting ? "..." : "测试", EditorStyles.miniButton, GUILayout.Width(44)))
                {
                    TestModel(entry, modelId);
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    entry.Models.RemoveAt(i);
                    _modelResults.Remove(key);
                    AIConfigManager.SaveConfig(Config);
                    GUIUtility.ExitGUI();
                    return;
                }

                EditorGUILayout.EndHorizontal();

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

        private void DrawAddModelRow(ChannelEntry entry, bool hasApiKey)
        {
            _modelInput.TryAdd(entry.Id, "");

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
        }

        private void DrawAvailableModels(ChannelEntry entry)
        {
            if (!_availableModels.TryGetValue(entry.Id, out var available) || available.Count == 0)
                return;

            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("可用模型", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全部添加", EditorStyles.miniButton, GUILayout.Width(56)))
            {
                entry.Models ??= new List<string>();
                foreach (var m in available)
                {
                    if (!entry.Models.Contains(m.Id))
                        entry.Models.Add(m.Id);
                }
                AIConfigManager.SaveConfig(Config);
                _availableModels.Remove(entry.Id);
            }
            if (GUILayout.Button("收起", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                _availableModels.Remove(entry.Id);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);

            if (!_availableModelsScroll.ContainsKey(entry.Id))
                _availableModelsScroll[entry.Id] = Vector2.zero;

            var scroll = _availableModelsScroll[entry.Id];
            float listHeight = Mathf.Min(available.Count * 22f, 160f);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(listHeight));

            var existingModels = new HashSet<string>(entry.Models ?? new List<string>());
            foreach (var model in available)
            {
                bool alreadyAdded = existingModels.Contains(model.Id);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(alreadyAdded);
                if (GUILayout.Button(alreadyAdded ? "✔" : "+", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    entry.Models ??= new List<string>();
                    if (!entry.Models.Contains(model.Id))
                    {
                        entry.Models.Add(model.Id);
                        AIConfigManager.SaveConfig(Config);
                    }
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.Label(model.Label, _modelNameStyle, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            _availableModelsScroll[entry.Id] = scroll;
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

            var oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(width));
            GUI.contentColor = oldColor;
        }

        // ────────────────────────────── Model Testing ──────────────────────────────

        private async void TestModel(ChannelEntry entry, string modelId)
        {
            var key = ModelKey(entry.Id, modelId);
            _modelResults[key] = new ModelTestResult { IsTesting = true };
            Window.Repaint();

            try
            {
                var testEntry = entry.Clone();
                var client = AIClient.Create(testEntry, modelId, Config.General);

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

            Window.Repaint();
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
            _availableModels.Remove(entry.Id);
            Window.Repaint();

            try
            {
                var apiKey = entry.GetEffectiveApiKey();
                var timeout = Config.General?.TimeoutSeconds ?? 30;
                var result = await ModelListService.FetchModelsAsync(entry, apiKey, timeout);

                _fetchedModels[entry.Id] = result;

                if (result.IsSuccess && result.Models.Count > 0)
                    _availableModels[entry.Id] = result.Models;
                else if (result.IsSuccess)
                    _fetchedModels[entry.Id] = ModelListResult.Fail("未获取到任何模型。");
            }
            catch (Exception e)
            {
                _fetchedModels[entry.Id] = ModelListResult.Fail(e.Message);
            }

            _fetchingModels[entry.Id] = false;
            Window.Repaint();
        }

        private void AddModelToEntry(ChannelEntry entry)
        {
            var input = _modelInput[entry.Id]?.Trim();
            if (string.IsNullOrEmpty(input)) return;
            if (entry.Models.Contains(input)) return;

            entry.Models.Add(input);
            _modelInput[entry.Id] = "";
            AIConfigManager.SaveConfig(Config);
            Window.Repaint();
        }

        // ────────────────────────────── Add / Remove Provider ──────────────────────────────

        private void ShowAddProviderMenu()
        {
            var menu = new GenericMenu();
            
            foreach (var channel in ChannelEntry.All)
            {
                var c = channel;
                menu.AddItem(new GUIContent(channel.Name + "(模板)"), false, () =>
                {
                    Config.ChannelEntries.Add(c);
                    _selectedIndex = Config.ChannelEntries.Count - 1;
                    Window.Repaint();
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("自定义 (OpenAI 兼容)"), false, () =>
            {
                Config.ChannelEntries.Add(new ChannelEntry
                {
                    Name = "Custom",
                    Protocol = ProviderProtocol.OpenAI,
                    BaseUrl = "https://",
                    Models = new List<string>()
                });
                _selectedIndex = Config.ChannelEntries.Count - 1;
                Window.Repaint();
            });

            menu.ShowAsContext();
        }

        private void ShowProviderContextMenu(int index)
        {
            var entry = Config.ChannelEntries[index];
            var menu = new GenericMenu();

            var toggleLabel = entry.Enabled ? "禁用" : "启用";
            menu.AddItem(new GUIContent(toggleLabel), false, () =>
            {
                entry.Enabled = !entry.Enabled;
                AIConfigManager.SaveConfig(Config);
                Window.Repaint();
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent($"移除「{entry.Name}」"), false, () =>
            {
                var keysToRemove = _modelResults.Keys.Where(k => k.StartsWith(entry.Id + ":")).ToList();
                foreach (var k in keysToRemove) _modelResults.Remove(k);

                _showApiKey.Remove(entry.Id);
                _modelInput.Remove(entry.Id);
                _fetchingModels.Remove(entry.Id);
                _fetchedModels.Remove(entry.Id);
                _availableModels.Remove(entry.Id);
                _availableModelsScroll.Remove(entry.Id);
                Config.ChannelEntries.RemoveAt(index);
                if (_selectedIndex >= Config.ChannelEntries.Count)
                    _selectedIndex = Mathf.Max(0, Config.ChannelEntries.Count - 1);
                Window.Repaint();
            });
            menu.ShowAsContext();
        }

        // ────────────────────────────── Helpers ──────────────────────────────

        private bool HasApiKey(ChannelEntry entry)
        {
            return !string.IsNullOrEmpty(entry.GetEffectiveApiKey());
        }
    }
}
