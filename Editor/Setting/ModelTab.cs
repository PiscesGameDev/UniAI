using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 模型管理 Tab — Master-Detail 布局。
    /// 左侧：紧凑工具栏 + 下拉过滤 + 表格列表（hover 显示操作）。
    /// 右侧：选中模型的详情面板（基本信息 + API 配置 + 快速测试）。
    /// </summary>
    internal class ModelTab : ManagerTab
    {
        public override string TabName => "模型";
        public override string TabIcon => "🧠";
        public override int Order => 1;

        private const float PAD = 10f;
        private const float ROW_HEIGHT = 24f;
        private const float DETAIL_WIDTH = 340f;
        private const float LABEL_WIDTH = 70f;
        private const float TOOLBAR_HEIGHT = 28f;

        // ─── Column widths ───
        private const float COL_TYPE = 20f;     // dot indicator
        private const float COL_VENDOR = 150f;
        private const float COL_CAP = 120f;
        private const float COL_OPS = 52f;  // hover only

        // ─── Colors ───
        private static readonly Color _chatColor = new(0.5f, 0.75f, 1f);
        private static readonly Color _visionInputColor = new(0.95f, 0.55f, 0.35f);
        private static readonly Color _imageGenColor = new(0.4f, 0.9f, 0.5f);
        private static readonly Color _imageEditColor = new(0.3f, 0.8f, 0.7f);
        private static readonly Color _audioGenColor = new(0.95f, 0.75f, 0.3f);
        private static readonly Color _videoGenColor = new(0.85f, 0.5f, 0.9f);
        private static readonly Color _embeddingColor = new(0.45f, 0.85f, 0.95f);
        private static readonly Color _rerankColor = new(0.95f, 0.65f, 0.25f);
        private static readonly Color _builtInColor = new(0.5f, 0.5f, 0.5f);
        private static readonly Color _customColor = new(0.4f, 0.9f, 0.5f);
        private static readonly Color _headerBg = new(0.16f, 0.16f, 0.16f);
        private static readonly Color _rowAltBg = new(0.215f, 0.215f, 0.215f);
        private static readonly Color _rowHoverBg = new(0.27f, 0.27f, 0.30f);
        private static readonly Color _rowSelectedBg = new(0.22f, 0.30f, 0.42f);
        private static readonly Color _detailBg = new(0.19f, 0.19f, 0.19f);
        
        // ─── Filter state ───
        private int _vendorFilterIndex;   // 0 = All
        private int _capFilterIndex;      // 0 = All
        private int _sourceFilterIndex;   // 0 = All, 1 = Built-in, 2 = Custom
        private string _searchText = "";

        private static readonly string[] _capOptions = { "All", "Chat", "VisionInput", "ImageGen", "ImageEdit", "AudioGen", "VideoGen", "Embedding", "Rerank" };
        private static readonly string[] _sourceOptions = { "All", "Built-in", "Custom" };

        // ─── Table state ───
        private Vector2 _tableScroll;
        private Vector2 _detailScroll;
        private List<ModelRow> _rows;
        private bool _rowsDirty = true;
        private int _selectedRowIndex = -1;    // index in _rows

        // ─── Edit state (inline for custom models) ───
        private bool _isEditing;

        // ─── Vendor list cache ───
        private string[] _vendorOptions = { "All" };

        // ─── Styles ───
        private GUIStyle _toolbarTitleStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _cellStyle;
        private GUIStyle _cellBoldStyle;
        private GUIStyle _detailTitleStyle;
        private GUIStyle _detailSectionStyle;
        private GUIStyle _detailValueStyle;
        private GUIStyle _searchFieldStyle;
        private bool _stylesReady;

        private class ModelRow
        {
            public ModelEntry Entry;
            public bool IsBuiltIn;
            public int SourceIndex;
            public List<string> ChannelNames;
        }

        // ────────────────────────────── Lifecycle ──────────────────────────────

        protected override void OnInit() => _rowsDirty = true;

        public override void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _toolbarTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

            _headerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0)
            };
            _headerStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

            _cellStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 2, 0, 0),
                clipping = TextClipping.Clip
            };
            _cellBoldStyle = new GUIStyle(_cellStyle) { fontStyle = FontStyle.Bold };

            _detailTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            _detailSectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _detailSectionStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

            _detailValueStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };

            _searchFieldStyle = new GUIStyle(EditorStyles.toolbarSearchField);
        }

        // ────────────────────────────── Main GUI ──────────────────────────────

        public override void OnGUI(float width, float height)
        {
            if (_rowsDirty) RebuildRows();

            // Detect hover row from mouse position (for hover-only ops)
            
            bool hasDetail = _selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count;
            float detailW = hasDetail ? DETAIL_WIDTH : 0f;
            float tableAreaW = width - detailW;

            // ── Left: Toolbar + Filters + Table ──
            GUILayout.BeginArea(new Rect(0, 0, tableAreaW, height));
            DrawToolbar(tableAreaW);
            DrawFilterRow(tableAreaW);
            DrawTable(tableAreaW, height - TOOLBAR_HEIGHT - 28f);
            GUILayout.EndArea();

            // ── Right: Detail panel ──
            if (hasDetail && _selectedRowIndex >= 0 && _selectedRowIndex < _rows.Count)
            {
                EditorGUI.DrawRect(new Rect(tableAreaW, 0, 1, height), EditorGUIHelper.SeparatorColor);
                var detailRect = new Rect(tableAreaW + 1, 0, detailW - 1, height);
                EditorGUI.DrawRect(detailRect, _detailBg);

                GUILayout.BeginArea(detailRect);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawDetailPanel(_rows[_selectedRowIndex]);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        // ────────────────────────────── Toolbar ──────────────────────────────

        private void DrawToolbar(float width)
        {
            var toolbarRect = new Rect(0, 0, width, TOOLBAR_HEIGHT);
            EditorGUI.DrawRect(toolbarRect, _headerBg);

            GUILayout.BeginArea(toolbarRect);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            // Title
            GUILayout.Label("UniAI Manager", _toolbarTitleStyle, GUILayout.Height(TOOLBAR_HEIGHT));
            GUILayout.Space(8);

            GUILayout.FlexibleSpace();

            // Count
            GUILayout.Label($"Total: {_rows.Count} Models", EditorStyles.miniLabel, GUILayout.Height(TOOLBAR_HEIGHT));
            GUILayout.Space(12);

            // Search
            var newSearch = EditorGUILayout.TextField(_searchText ?? "", _searchFieldStyle,
                GUILayout.Width(150), GUILayout.Height(18));
            if (newSearch != _searchText)
            {
                _searchText = newSearch;
                _rowsDirty = true;
            }

            GUILayout.Space(6);

            // Refresh
            if (GUILayout.Button("↻", EditorGUIHelper.MiniIconBtnStyle))
                _rowsDirty = true;

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Filter Row ──────────────────────────────

        private void DrawFilterRow(float width)
        {
            var filterRect = new Rect(0, TOOLBAR_HEIGHT, width, 26f);
            EditorGUI.DrawRect(filterRect, new Color(0.18f, 0.18f, 0.18f));

            GUILayout.BeginArea(filterRect);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            // Provider dropdown
            GUILayout.Label("Provider:", EditorStyles.miniLabel, GUILayout.Width(52), GUILayout.Height(22));
            int newVendor = EditorGUILayout.Popup(_vendorFilterIndex, _vendorOptions,
                EditorStyles.toolbarPopup, GUILayout.Width(80), GUILayout.Height(18));
            if (newVendor != _vendorFilterIndex) { _vendorFilterIndex = newVendor; }

            GUILayout.Space(10);

            // Capability dropdown
            GUILayout.Label("Capability:", EditorStyles.miniLabel, GUILayout.Width(62), GUILayout.Height(22));
            int newCap = EditorGUILayout.Popup(_capFilterIndex, _capOptions,
                EditorStyles.toolbarPopup, GUILayout.Width(80), GUILayout.Height(18));
            if (newCap != _capFilterIndex) { _capFilterIndex = newCap; }

            GUILayout.Space(10);

            // Source dropdown
            GUILayout.Label("Source:", EditorStyles.miniLabel, GUILayout.Width(44), GUILayout.Height(22));
            int newSource = EditorGUILayout.Popup(_sourceFilterIndex, _sourceOptions,
                EditorStyles.toolbarPopup, GUILayout.Width(80), GUILayout.Height(18));
            if (newSource != _sourceFilterIndex) { _sourceFilterIndex = newSource; }

            GUILayout.FlexibleSpace();

            // + Add custom model button
            if (GUILayout.Button("+", EditorGUIHelper.MiniIconBtnStyle))
                AddCustomModel();

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Table ──────────────────────────────

        private void DrawTable(float tableWidth, float availableHeight)
        {
            float y = TOOLBAR_HEIGHT + 26f;
            float colIdWidth = tableWidth - COL_TYPE - COL_VENDOR - COL_CAP - COL_OPS - PAD * 2;
            if (colIdWidth < 80f) colIdWidth = 80f;

            // Header row
            var headerRect = new Rect(0, y, tableWidth, 20f);
            EditorGUI.DrawRect(headerRect, _headerBg);

            GUILayout.BeginArea(new Rect(0, y, tableWidth, 20f));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("", _headerStyle, GUILayout.Width(COL_TYPE), GUILayout.Height(20));
            GUILayout.Label("Model ID", _headerStyle, GUILayout.Width(colIdWidth), GUILayout.Height(20));
            GUILayout.Label("Manufacturer", _headerStyle, GUILayout.Width(COL_VENDOR), GUILayout.Height(20));
            GUILayout.Label("Capability", _headerStyle, GUILayout.Width(COL_CAP), GUILayout.Height(20));
            GUILayout.Label("", _headerStyle, GUILayout.Width(COL_OPS), GUILayout.Height(20));
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            y += 20f;
            float scrollHeight = availableHeight - 20f;
            if (scrollHeight < 50f) scrollHeight = 50f;

            // Scrollable rows
            var scrollAreaRect = new Rect(0, y, tableWidth, scrollHeight);

            var filteredRows = GetFilteredRows();
            float contentHeight = filteredRows.Count * ROW_HEIGHT;

            _tableScroll = GUI.BeginScrollView(scrollAreaRect, _tableScroll,
                new Rect(0, 0, tableWidth - 16, contentHeight));

            var mousePos = Event.current.mousePosition;

            for (int i = 0; i < filteredRows.Count; i++)
            {
                var rowRect = new Rect(0, i * ROW_HEIGHT, tableWidth - 16, ROW_HEIGHT);
                var row = filteredRows[i];
                int globalIndex = _rows.IndexOf(row);
                bool isSelected = globalIndex == _selectedRowIndex;
                bool isHover = rowRect.Contains(mousePos);
                
                // Row background
                Color bgColor;
                if (isSelected) bgColor = _rowSelectedBg;
                else if (isHover) bgColor = _rowHoverBg;
                else bgColor = i % 2 == 1 ? _rowAltBg : EditorGUIHelper.CardBg;
                EditorGUI.DrawRect(rowRect, bgColor);

                // Cells
                float cx = PAD;

                // Type dot indicator (circle)
                float dotRadius = 4f;
                float dotCenterX = cx + COL_TYPE * 0.5f;
                float dotCenterY = rowRect.y + ROW_HEIGHT * 0.5f;
                var dotColor = row.IsBuiltIn ? _builtInColor : _customColor;
                Handles.color = dotColor;
                Handles.DrawSolidDisc(new Vector3(dotCenterX, dotCenterY, 0), Vector3.forward, dotRadius);
                cx += COL_TYPE;

                // Model ID
                GUI.Label(new Rect(cx, rowRect.y, colIdWidth, ROW_HEIGHT),
                    row.Entry.Id ?? "-", _cellBoldStyle);
                cx += colIdWidth;

                // Vendor
                GUI.Label(new Rect(cx, rowRect.y, COL_VENDOR, ROW_HEIGHT),
                    row.Entry.Vendor ?? "-", _cellStyle);
                cx += COL_VENDOR;

                // Capability badges (support multi-capability)
                DrawCapabilityBadgesAt(cx + 2, rowRect.y + 4, COL_CAP - 4, row.Entry.Capabilities);
                cx += COL_CAP;

                // Operations (hover only, custom only)
                if (isHover && !row.IsBuiltIn)
                {
                    float opX = cx + 2;
                    float opY = rowRect.y + 3;
                    if (GUI.Button(new Rect(opX, opY, 22, 18), "✎", EditorGUIHelper.MiniIconBtnStyle))
                    {
                        _selectedRowIndex = globalIndex;
                        _isEditing = true;
                        Window.Repaint();
                    }
                    if (GUI.Button(new Rect(opX + 24, opY, 22, 18), "✕", EditorGUIHelper.MiniIconBtnStyle))
                    {
                        DeleteCustomModel(row);
                        return; // list changed, bail
                    }
                }

                // Row click → select
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    if (_selectedRowIndex == globalIndex)
                    {
                        _selectedRowIndex = -1; // deselect
                        _isEditing = false;
                    }
                    else
                    {
                        _selectedRowIndex = globalIndex;
                        _isEditing = false;
                    }
                    Event.current.Use();
                    Window.Repaint();
                }
            }

            GUI.EndScrollView();

            // Empty state
            if (filteredRows.Count == 0)
            {
                var emptyRect = new Rect(0, y + 30, tableWidth, 40);
                var capName = _capFilterIndex > 0 ? _capOptions[_capFilterIndex] : "";
                var emptyText = string.IsNullOrEmpty(capName) || capName == "All"
                    ? "No matching models"
                    : $"No {capName} models found. Click '+' in the filter row to add one.";
                GUI.Label(emptyRect, emptyText, new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
            }

            // Repaint for hover effects
            if (Event.current.type == EventType.Repaint)
                Window.Repaint();
        }

        // ────────────────────────────── Detail Panel ──────────────────────────────

        private void DrawDetailPanel(ModelRow row)
        {
            var entry = row.Entry;

            GUILayout.Space(PAD);

            // Header: Icon + Title + Close
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            // Model icon
            if (entry.Icon != null)
            {
                var iconRect = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                GUI.DrawTexture(iconRect, entry.Icon, ScaleMode.ScaleToFit);
                GUILayout.Space(8);
            }

            EditorGUILayout.BeginVertical();
            GUILayout.Label("Model Details: " + (entry.Id ?? ""), EditorStyles.miniLabel);
            GUILayout.Label(entry.Id ?? "", _detailTitleStyle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EditorGUIHelper.MiniIconBtnStyle))
            {
                _selectedRowIndex = -1;
                _isEditing = false;
                Window.Repaint();
            }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawDetailSeparator();

            // ── Basic Info ──
            GUILayout.Space(6);

            if (_isEditing && !row.IsBuiltIn)
                DrawEditableInfo(entry);
            else
                DrawReadOnlyInfo(row);

            GUILayout.Space(4);
            DrawDetailSeparator();

            // ── Channels ──
            GUILayout.Space(6);
            DrawDetailSection("Channels", () =>
            {
                if (row.ChannelNames.Count == 0)
                {
                    GUILayout.Label("Not configured in any channel", EditorGUIHelper.DetailLabelStyle);
                }
                else
                {
                    foreach (var ch in row.ChannelNames)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(4);
                        GUILayout.Label("•", GUILayout.Width(10));
                        GUILayout.Label(ch, _detailValueStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            });

            // ── Edit/Delete (for custom) ──
            if (!row.IsBuiltIn)
            {
                GUILayout.Space(8);
                DrawDetailSeparator();
                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(PAD);

                if (!_isEditing)
                {
                    if (GUILayout.Button("Edit", GUILayout.Height(22), GUILayout.Width(60)))
                    {
                        _isEditing = true;
                        Window.Repaint();
                    }
                }
                else
                {
                    if (GUILayout.Button("Done", GUILayout.Height(22), GUILayout.Width(60)))
                    {
                        _isEditing = false;
                        _rowsDirty = true;
                        Window.Repaint();
                    }
                }

                GUILayout.Space(8);

                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("Delete", GUILayout.Height(22), GUILayout.Width(60)))
                    DeleteCustomModel(row);
                GUI.backgroundColor = oldBg;

                GUILayout.FlexibleSpace();
                GUILayout.Space(PAD);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(PAD);
        }

        private void DrawReadOnlyInfo(ModelRow row)
        {
            var entry = row.Entry;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            DrawDetailRow("Type", null);
            EditorGUIHelper.DrawBadgeInline(row.IsBuiltIn ? "Built-in" : "Custom",
                row.IsBuiltIn ? _builtInColor : _customColor);
            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            DrawDetailKV("Provider", entry.Vendor ?? "Unknown");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            DrawDetailRow("Capability", null);
            DrawCapabilityBadgesInline(entry.Capabilities);
            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(entry.Description))
                DrawDetailKV("Description", entry.Description);

            DrawDetailKV("Endpoint", entry.Endpoint.ToString());
            if (!string.IsNullOrEmpty(entry.AdapterId))
                DrawDetailKV("Adapter", entry.AdapterId);
            if (entry.Behavior != ModelBehavior.None)
                DrawDetailKV("Behavior", entry.Behavior.ToString());
            if (entry.BehaviorTags is { Count: > 0 })
                DrawDetailKV("Tags", string.Join(", ", entry.BehaviorTags));
            if (entry.BehaviorOptions is { Count: > 0 })
                DrawDetailKV("Options", FormatBehaviorOptions(entry.BehaviorOptions));
            DrawDetailKV("Context Window", FormatContextWindow(ModelRegistry.GetContextWindow(entry.Id)));
        }

        private void DrawEditableInfo(ModelEntry entry)
        {
            DrawEditField("Model ID", ref entry.Id);
            DrawEditField("Vendor", ref entry.Vendor);
            DrawEditField("Description", ref entry.Description);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Capabilities", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newCap = (ModelCapability)EditorGUILayout.EnumFlagsField(entry.Capabilities, GUILayout.Width(120));
            if (newCap != entry.Capabilities) { entry.Capabilities = newCap; MarkDirty(); _rowsDirty = true; }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Endpoint", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newEndpoint = (ModelEndpoint)EditorGUILayout.EnumPopup(entry.Endpoint, GUILayout.Width(120));
            if (newEndpoint != entry.Endpoint) { entry.Endpoint = newEndpoint; MarkDirty(); }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            DrawAdapterSelector(entry);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Behavior", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newBehavior = (ModelBehavior)EditorGUILayout.EnumFlagsField(entry.Behavior, GUILayout.Width(160));
            if (newBehavior != entry.Behavior) { entry.Behavior = newBehavior; MarkDirty(); _rowsDirty = true; }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            DrawBehaviorTagsEditor(entry);
            DrawBehaviorOptionsEditor(entry);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Context Window", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newContextWindow = EditorGUILayout.IntField(entry.ContextWindow, GUILayout.Width(120));
            if (newContextWindow < 0) newContextWindow = 0;
            if (newContextWindow != entry.ContextWindow) { entry.ContextWindow = newContextWindow; MarkDirty(); }
            GUILayout.Label("tokens (0 = auto)", EditorGUIHelper.DetailLabelStyle);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Icon", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newIcon = (Texture2D)EditorGUILayout.ObjectField(entry.Icon, typeof(Texture2D), false,
                GUILayout.Width(40), GUILayout.Height(40));
            if (newIcon != entry.Icon) { entry.Icon = newIcon; MarkDirty(); }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAdapterSelector(ModelEntry entry)
        {
            var adapters = GetAdapterDescriptorsFor(entry);
            var labels = new List<string> { "(None)" };
            labels.AddRange(adapters.Select(FormatAdapterOption));
            labels.Add("Custom / manual");

            var current = entry.AdapterId ?? "";
            var matchedIndex = adapters.FindIndex(a =>
                string.Equals(a.Id, current, StringComparison.OrdinalIgnoreCase));

            var selectedIndex = 0;
            if (!string.IsNullOrEmpty(current))
                selectedIndex = matchedIndex >= 0 ? matchedIndex + 1 : labels.Count - 1;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Adapter", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var nextIndex = EditorGUILayout.Popup(selectedIndex, labels.ToArray());
            if (nextIndex != selectedIndex)
            {
                if (nextIndex == 0)
                    entry.AdapterId = "";
                else if (nextIndex <= adapters.Count)
                    entry.AdapterId = adapters[nextIndex - 1].Id;

                MarkDirty();
                _rowsDirty = true;
            }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            DrawEditField("Adapter ID", ref entry.AdapterId);

            var selectedAdapter = adapters.FirstOrDefault(a =>
                string.Equals(a.Id, entry.AdapterId, StringComparison.OrdinalIgnoreCase));
            if (selectedAdapter != null && !string.IsNullOrEmpty(selectedAdapter.Description))
                DrawDetailKV("Adapter Info", selectedAdapter.Description);
        }

        private List<AdapterDescriptor> GetAdapterDescriptorsFor(ModelEntry entry)
        {
            if (entry == null)
                return new List<AdapterDescriptor>();

            var channels = GetChannelsForModel(entry.Id);
            return AdapterCatalog.GetAdaptersFor(entry, channels).ToList();
        }

        private List<ChannelEntry> GetChannelsForModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId) || Config?.ChannelEntries == null)
                return null;

            return Config.ChannelEntries
                .Where(channel => channel?.Models != null && channel.Models.Contains(modelId))
                .ToList();
        }

        private static string FormatAdapterOption(AdapterDescriptor adapter)
        {
            return $"{adapter.DisplayName} [{adapter.Target}] ({adapter.Id})";
        }

        private void DrawEditField(string label, ref string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label(label, EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var newVal = EditorGUILayout.TextField(value ?? "");
            if (newVal != value) { value = newVal; MarkDirty(); _rowsDirty = true; }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawBehaviorTagsEditor(ModelEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Tags", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var current = entry.BehaviorTags != null ? string.Join("\n", entry.BehaviorTags) : "";
            var next = EditorGUILayout.TextArea(current, GUILayout.MinHeight(42));
            if (next != current)
            {
                entry.BehaviorTags = ParseBehaviorTags(next);
                MarkDirty();
                _rowsDirty = true;
            }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawBehaviorOptionsEditor(ModelEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("Options", EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            var current = FormatBehaviorOptionsMultiline(entry.BehaviorOptions);
            var next = EditorGUILayout.TextArea(current, GUILayout.MinHeight(52));
            if (next != current)
            {
                entry.BehaviorOptions = ParseBehaviorOptions(next);
                MarkDirty();
                _rowsDirty = true;
            }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // ────────────────────────────── Detail Helpers ──────────────────────────────

        private void DrawDetailSection(string title, Action drawContent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label(title, _detailSectionStyle);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            EditorGUILayout.BeginVertical();
            drawContent();
            EditorGUILayout.EndVertical();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDetailKV(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label(label, EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            GUILayout.Label(value ?? "-", _detailValueStyle);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private static string FormatContextWindow(int tokens)
        {
            if (tokens <= 0) return "-";
            if (tokens >= 1_000_000) return $"{tokens / 1000_000f:0.#}M tokens";
            if (tokens >= 1_000) return $"{tokens / 1000f:0.#}K tokens";
            return $"{tokens} tokens";
        }

        private void DrawDetailRow(string label, string value)
        {
            GUILayout.Label(label, EditorGUIHelper.DetailLabelStyle, GUILayout.Width(LABEL_WIDTH));
            if (value != null)
                GUILayout.Label(value, _detailValueStyle);
        }

        private void DrawDetailSeparator()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, EditorGUIHelper.SeparatorColor);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
        }

        // ────────────────────────────── Badge Drawing (delegates to EditorGUIHelper) ──────────────────────────────

        /// <summary>列表行：在指定位置绘制所有能力标签（固定宽度）</summary>
        private void DrawCapabilityBadgesAt(float x, float y, float width, ModelCapability caps)
        {
            const float badgeWidth = 38f;
            const float badgeHeight = 16f;
            const float gap = 2f;
            float cx = x;
            float maxX = x + width;

            foreach (ModelCapability flag in Enum.GetValues(typeof(ModelCapability)))
            {
                if (flag == ModelCapability.None) continue;
                if ((caps & flag) == 0) continue;
                if (cx + badgeWidth > maxX) break;

                EditorGUIHelper.DrawBadge(new Rect(cx, y, badgeWidth, badgeHeight),
                    GetCapabilityShortName(flag), GetCapabilityColor(flag));
                cx += badgeWidth + gap;
            }
        }

        /// <summary>详情面板：内联绘制所有能力标签</summary>
        private void DrawCapabilityBadgesInline(ModelCapability caps)
        {
            foreach (ModelCapability flag in Enum.GetValues(typeof(ModelCapability)))
            {
                if (flag == ModelCapability.None) continue;
                if ((caps & flag) == 0) continue;
                EditorGUIHelper.DrawBadgeInline(flag.ToString(), GetCapabilityColor(flag));
                GUILayout.Space(2);
            }
        }

        private void RebuildRows()
        {
            _rowsDirty = false;
            _rows = new List<ModelRow>();

            var channelMap = BuildChannelMap();

            // Built-in
            foreach (var kvp in ModelRegistry.BuiltInModels)
            {
                channelMap.TryGetValue(kvp.Key, out var channels);
                _rows.Add(new ModelRow
                {
                    Entry = kvp.Value,
                    IsBuiltIn = true,
                    SourceIndex = -1,
                    ChannelNames = channels ?? new List<string>()
                });
            }

            // Custom
            var customModels = GetCustomModels();
            for (int i = 0; i < customModels.Count; i++)
            {
                var entry = customModels[i];
                channelMap.TryGetValue(entry.Id ?? "", out var channels);
                _rows.Add(new ModelRow
                {
                    Entry = entry,
                    IsBuiltIn = false,
                    SourceIndex = i,
                    ChannelNames = channels ?? new List<string>()
                });
            }

            // Sort: custom first, then vendor, then id
            _rows.Sort((a, b) =>
            {
                int c = a.IsBuiltIn.CompareTo(b.IsBuiltIn);
                if (c != 0) return c;
                c = string.Compare(a.Entry.Vendor, b.Entry.Vendor, StringComparison.Ordinal);
                if (c != 0) return c;
                return string.Compare(a.Entry.Id, b.Entry.Id, StringComparison.Ordinal);
            });

            // Rebuild vendor options
            var vendors = _rows
                .Where(r => !string.IsNullOrEmpty(r.Entry.Vendor))
                .Select(r => r.Entry.Vendor)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            var opts = new List<string> { "All" };
            opts.AddRange(vendors);
            _vendorOptions = opts.ToArray();

            // Clamp vendor filter
            if (_vendorFilterIndex >= _vendorOptions.Length)
                _vendorFilterIndex = 0;

            // Clamp selection
            if (_selectedRowIndex >= _rows.Count)
                _selectedRowIndex = -1;
        }

        private Dictionary<string, List<string>> BuildChannelMap()
        {
            var map = new Dictionary<string, List<string>>();
            if (Config?.ChannelEntries == null) return map;

            foreach (var ch in Config.ChannelEntries)
            {
                if (!ch.Enabled || ch.Models == null) continue;
                foreach (var modelId in ch.Models)
                {
                    if (!map.ContainsKey(modelId))
                        map[modelId] = new List<string>();
                    map[modelId].Add(ch.Name);
                }
            }
            return map;
        }

        private List<ModelRow> GetFilteredRows()
        {
            var result = _rows.AsEnumerable();

            // Vendor
            if (_vendorFilterIndex > 0 && _vendorFilterIndex < _vendorOptions.Length)
            {
                var vendor = _vendorOptions[_vendorFilterIndex];
                result = result.Where(r => r.Entry.Vendor == vendor);
            }

            // Capability (flags filter — match if any flag overlaps)
            if (_capFilterIndex > 0)
            {
                var cap = (ModelCapability)(1 << (_capFilterIndex - 1));
                result = result.Where(r => r.Entry.HasCapability(cap));
            }

            // Source
            if (_sourceFilterIndex == 1) result = result.Where(r => r.IsBuiltIn);
            else if (_sourceFilterIndex == 2) result = result.Where(r => !r.IsBuiltIn);

            // Search
            if (!string.IsNullOrEmpty(_searchText))
            {
                var lower = _searchText.ToLowerInvariant();
                result = result.Where(r => MatchesFilter(r.Entry, lower));
            }

            return result.ToList();
        }

        // ────────────────────────────── Actions ──────────────────────────────

        private void AddCustomModel()
        {
            var settings = UniAISettings.Instance;
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Error", "UniAISettings not found. Create one at Resources/UniAI/.", "OK");
                return;
            }

            settings.CustomModels.Add(new ModelEntry
            {
                Id = "custom-model",
                Vendor = "Custom",
                Capabilities = ModelCapability.Chat,
                Endpoint = ModelEndpoint.ChatCompletions
            });

            MarkDirty();
            _rowsDirty = true;
            RebuildRows();

            _selectedRowIndex = _rows.FindIndex(r => !r.IsBuiltIn && r.SourceIndex == settings.CustomModels.Count - 1);
            _isEditing = true;
            Window.Repaint();
        }

        private void DeleteCustomModel(ModelRow row)
        {
            if (!EditorUtility.DisplayDialog("Delete Model",
                $"Delete custom model '{row.Entry.Id}'?", "Delete", "Cancel"))
                return;

            var customModels = GetCustomModels();
            if (row.SourceIndex >= 0 && row.SourceIndex < customModels.Count)
            {
                customModels.RemoveAt(row.SourceIndex);
                _selectedRowIndex = -1;
                _isEditing = false;
                MarkDirty();
                _rowsDirty = true;
                Window.Repaint();
            }
        }

        // ────────────────────────────── Helpers ──────────────────────────────

        private List<ModelEntry> GetCustomModels()
        {
            var settings = UniAISettings.Instance;
            return settings != null ? settings.CustomModels : new List<ModelEntry>();
        }

        private bool MatchesFilter(ModelEntry entry, string filter)
        {
            return (entry.Id != null && entry.Id.ToLowerInvariant().Contains(filter))
                || (entry.Vendor != null && entry.Vendor.ToLowerInvariant().Contains(filter))
                || (entry.AdapterId != null && entry.AdapterId.ToLowerInvariant().Contains(filter))
                || (entry.BehaviorTags != null && entry.BehaviorTags.Any(t => t != null && t.ToLowerInvariant().Contains(filter)))
                || entry.Capabilities.ToString().ToLowerInvariant().Contains(filter);
        }

        /// <summary>获取单个能力的颜色</summary>
        private static string FormatBehaviorOptions(IReadOnlyList<ModelBehaviorOption> options)
        {
            if (options == null || options.Count == 0)
                return "";

            return string.Join(", ", options
                .Where(o => o != null && !string.IsNullOrEmpty(o.Key))
                .Select(o => $"{o.Key}={o.Value}"));
        }

        private static string FormatBehaviorOptionsMultiline(IReadOnlyList<ModelBehaviorOption> options)
        {
            if (options == null || options.Count == 0)
                return "";

            return string.Join("\n", options
                .Where(o => o != null && !string.IsNullOrEmpty(o.Key))
                .Select(o => $"{o.Key}={o.Value}"));
        }

        private static List<string> ParseBehaviorTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            return text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();
        }

        private static List<ModelBehaviorOption> ParseBehaviorOptions(string text)
        {
            var result = new List<ModelBehaviorOption>();
            if (string.IsNullOrEmpty(text))
                return result;

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var sep = line.IndexOf('=');
                if (sep <= 0)
                    continue;

                var key = line.Substring(0, sep).Trim();
                var value = line.Substring(sep + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                    result.Add(new ModelBehaviorOption(key, value));
            }

            return result;
        }

        /// <summary>获取单个能力的颜色</summary>
        private static Color GetCapabilityColor(ModelCapability cap) => cap switch
        {
            ModelCapability.Chat => _chatColor,
            ModelCapability.VisionInput => _visionInputColor,
            ModelCapability.ImageGen => _imageGenColor,
            ModelCapability.ImageEdit => _imageEditColor,
            ModelCapability.AudioGen => _audioGenColor,
            ModelCapability.VideoGen => _videoGenColor,
            ModelCapability.Embedding => _embeddingColor,
            ModelCapability.Rerank => _rerankColor,
            _ => _chatColor
        };

        /// <summary>能力缩写映射，用于列表行紧凑显示</summary>
        private static string GetCapabilityShortName(ModelCapability cap) => cap switch
        {
            ModelCapability.Chat => "Chat",
            ModelCapability.VisionInput => "Vision",
            ModelCapability.ImageGen => "ImgGen",
            ModelCapability.ImageEdit => "ImgEdit",
            ModelCapability.AudioGen => "Audio",
            ModelCapability.VideoGen => "Video",
            ModelCapability.Embedding => "Embed",
            ModelCapability.Rerank => "Rerank",
            _ => cap.ToString()
        };

        private void MarkDirty()
        {
            var settings = UniAISettings.Instance;
            if (settings != null)
                EditorUtility.SetDirty(settings);
        }
    }
}
