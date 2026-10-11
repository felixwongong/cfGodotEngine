using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using cfEngine;
using cfEngine.Core;
using cfEngine.Info;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Builds the content for the <c>info</c> debug window ("Info Viewer"). The returned subtree is a
/// fresh, parentless ordinary <see cref="Control"/>; assembling it performs no scene-tree attachment
/// and no worker/async work. The host invokes <see cref="Build"/> from its main-thread factory,
/// attaches the result and owns the window chrome (title, close, drag, stacking).
///
/// Content: one tab per <see cref="InfoLayer.InfoMap"/> entry (name derived from the manager type),
/// a reflection table of the manager's rows (public readable instance properties, ordinal name order,
/// capped at <see cref="RowCap"/>), and a read-only JSON detail pane for the tapped row. Layout is
/// responsive against the current viewport visible-rect width. The InfoLayer is re-resolved on every
/// build and refresh and reads currently-loaded runtime objects only (no pipeline/network calls).
/// </summary>
public static class InfoDebugWindow
{
    private const int WideBreakpointWidth = 1000;
    private const int ViewerFontSize = 28;
    private const int RowCap = 500;
    private const float CellWidth = 240f;
    private const float TableStretchRatio = 0.55f;
    private const float DetailStretchRatio = 0.45f;

    /// <summary>
    /// Assembles and returns a new parentless Info-viewer subtree. No scene mutation, no
    /// <c>SceneTree.Root</c> access, no subscriptions to game state.
    /// </summary>
    public static Control Build()
    {
        var viewer = new Viewer();
        return viewer.Root;
    }

    /// <summary>
    /// Freshly resolves the current <see cref="InfoLayer"/> through <see cref="Domain.Current"/>.
    /// Handles a thrown lookup, an error result and null explicitly; always returns null on failure.
    /// </summary>
    private static InfoLayer ResolveInfoLayer()
    {
        try
        {
            var result = Domain.Current.Get<InfoLayer>();
            if (result.HasError(out var error))
            {
                Log.LogException(error, "InfoDebugWindow: InfoLayer lookup returned an error.");
                return null;
            }

            return result.value;
        }
        catch (Exception ex)
        {
            Log.LogException(ex, "InfoDebugWindow: InfoLayer lookup threw.");
            return null;
        }
    }

    private static string DisplayNameFor(Type managerType)
    {
        var name = managerType.Name;
        const string infoSuffix = "InfoManager";
        if (name.EndsWith(infoSuffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - infoSuffix.Length);

        const string managerSuffix = "Manager";
        if (name.EndsWith(managerSuffix, StringComparison.Ordinal))
            return name.Substring(0, name.Length - managerSuffix.Length);

        return name;
    }

    private static string FormatCellValue(object value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return text;
            case bool flag:
                return flag ? "true" : "false";
            case IEnumerable enumerable:
                return $"{value.GetType().Name}[{CountOf(enumerable)}]";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return $"{{{value.GetType().Name}}}";
        }
    }

    private static int CountOf(IEnumerable enumerable)
    {
        if (enumerable is ICollection collection)
            return collection.Count;

        var count = 0;
        foreach (var _ in enumerable)
            count++;
        return count;
    }

    /// <summary>
    /// Public instance properties with a public getter and no index parameters, including inherited
    /// readable properties, ordered by ordinal property name for deterministic column alignment.
    /// </summary>
    private static List<PropertyInfo> ReadableColumns(Type infoType)
    {
        var columns = new List<PropertyInfo>();
        foreach (var property in infoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            var getter = property.GetGetMethod();
            if (getter == null || !getter.IsPublic || getter.IsStatic)
                continue;

            columns.Add(property);
        }

        columns.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return columns;
    }

    /// <summary>
    /// Writes a <see cref="Vector2I"/> as numeric <c>{"X":..,"Y":..}</c> so nested coordinate arrays
    /// (e.g. floor grids) are useful instead of an empty object or a recursive derived-property loop.
    /// </summary>
    private sealed partial class Vector2IConverter : JsonConverter<Vector2I>
    {
        public override Vector2I Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Vector2I reading is not supported.");
        }

        public override void Write(Utf8JsonWriter writer, Vector2I value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteEndObject();
        }
    }

    /// <summary>Per-window state and behaviour. One instance per built window; no static state.</summary>
    private sealed class Viewer
    {
        private readonly JsonSerializerOptions _jsonOptions;

        private readonly VBoxContainer _column;
        private readonly Label _status;
        private readonly Button _refreshButton;
        private readonly TabContainer _tabContainer;
        private readonly VBoxContainer _detailPane;
        private readonly Button _backButton;
        private readonly RichTextLabel _detailText;

        private List<TabState> _tabStates;
        private string _placeholderTitle = "InfoLayer unavailable";
        private int _selectedTab = -1;
        private int _selectedRow = -1;
        private bool _narrowDetailOpen;
        private bool _suppressTabChanged;

        private Viewport _viewport;

        public Viewer()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                MaxDepth = 64
            };
            _jsonOptions.Converters.Add(new Vector2IConverter());

            Root = new Control();
            Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _column = new VBoxContainer();
            _column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _column.AddThemeConstantOverride("separation", 8);
            Root.AddChild(_column);

            // Header: status line for the selected tab + Refresh.
            var toolbar = new HBoxContainer();
            toolbar.AddThemeConstantOverride("separation", 8);
            _column.AddChild(toolbar);

            _status = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Text = _placeholderTitle
            };
            _status.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            toolbar.AddChild(_status);

            _refreshButton = new Button
            {
                Text = "⟳ Refresh",
                FocusMode = Control.FocusModeEnum.None
            };
            _refreshButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _refreshButton.Pressed += Refresh;
            toolbar.AddChild(_refreshButton);

            // Body: table tabs and the JSON detail pane, split side-by-side when wide.
            var body = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            body.AddThemeConstantOverride("separation", 8);
            _column.AddChild(body);

            _tabContainer = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = TableStretchRatio
            };
            _tabContainer.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            var tabBar = _tabContainer.GetTabBar();
            tabBar.ClipTabs = true;
            tabBar.MaxTabWidth = 150;
            _tabContainer.TabChanged += OnTabChanged;
            body.AddChild(_tabContainer);

            _detailPane = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = DetailStretchRatio,
                Visible = false
            };
            _detailPane.AddThemeConstantOverride("separation", 6);
            body.AddChild(_detailPane);

            _backButton = new Button
            {
                Text = "‹ back",
                FocusMode = Control.FocusModeEnum.None,
                Visible = false
            };
            _backButton.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            _backButton.Pressed += OnBack;
            _detailPane.AddChild(_backButton);

            var mono = new SystemFont { FontNames = new[] { "monospace" }, AllowSystemFallback = true };
            _detailText = new RichTextLabel
            {
                BbcodeEnabled = false,
                SelectionEnabled = true,
                ScrollActive = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _detailText.AddThemeFontOverride("normal_font", mono);
            _detailText.AddThemeFontOverride("mono_font", mono);
            _detailText.AddThemeFontSizeOverride("normal_font_size", ViewerFontSize);
            _detailText.AddThemeFontSizeOverride("mono_font_size", ViewerFontSize);
            _detailPane.AddChild(_detailText);

            Root.TreeEntered += OnTreeEntered;
            Root.TreeExiting += OnTreeExiting;

            Refresh();
            ApplyResponsive(0f);
        }

        public Control Root { get; }

        // ---- lifecycle -------------------------------------------------------

        private void OnTreeEntered()
        {
            _viewport = Root.GetViewport();
            if (_viewport != null)
                _viewport.SizeChanged += OnViewportSizeChanged;
            Root.Resized += OnRootResized;
            ApplyResponsive(CurrentWidth());
        }

        private void OnTreeExiting()
        {
            if (_viewport != null)
            {
                _viewport.SizeChanged -= OnViewportSizeChanged;
                _viewport = null;
            }

            Root.Resized -= OnRootResized;
        }

        private void OnViewportSizeChanged()
        {
            ApplyResponsive(CurrentWidth());
        }

        private void OnRootResized()
        {
            ApplyResponsive(CurrentWidth());
        }

        private float CurrentWidth()
        {
            var viewport = _viewport ?? Root.GetViewport();
            return viewport?.GetVisibleRect().Size.X ?? 0f;
        }

        // ---- refresh ---------------------------------------------------------

        private void Refresh()
        {
            var previousType = SelectedType();
            var infoLayer = ResolveInfoLayer();
            if (infoLayer == null)
            {
                ShowMessage("InfoLayer unavailable", "InfoLayer unavailable. Tap ⟳ Refresh to retry.");
                return;
            }

            IReadOnlyDictionary<Type, IInfoManager> map;
            try
            {
                map = infoLayer.InfoMap;
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: InfoMap enumeration failed.");
                ShowMessage("InfoLayer unavailable", "Info managers could not be enumerated. Tap ⟳ Refresh to retry.");
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var tabs = new List<TabState>();
            try
            {
                foreach (var entry in map)
                    tabs.Add(BuildTab(entry.Key, entry.Value, timestamp));
            }
            catch (Exception ex)
            {
                Log.LogException(ex, "InfoDebugWindow: manager enumeration failed.");
                ShowMessage("InfoLayer unavailable", "Info managers could not be enumerated. Tap ⟳ Refresh to retry.");
                return;
            }

            if (tabs.Count == 0)
            {
                ShowMessage("no managers", "No Info managers registered. Tap ⟳ Refresh to retry.");
                return;
            }

            _tabStates = tabs;
            _placeholderTitle = "InfoLayer unavailable";
            BuildTabUi();

            _suppressTabChanged = true;
            _selectedTab = IndexOfType(previousType);
            _tabContainer.CurrentTab = _selectedTab;
            _suppressTabChanged = false;

            _selectedRow = -1;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;

            UpdateStatus();
            ApplyResponsive(CurrentWidth());
        }

        private static TabState BuildTab(Type managerType, IInfoManager manager, string timestamp)
        {
            var tab = new TabState(managerType, DisplayNameFor(managerType), timestamp);

            try
            {
                var infoType = manager.infoType ?? typeof(object);
                tab.Columns = ReadableColumns(infoType);
            }
            catch (Exception ex)
            {
                tab.Error = $"infoType failed: {ex.GetType().Name}";
                return tab;
            }

            try
            {
                var rows = new List<object>();
                var total = 0;
                foreach (var value in manager.GetAllValue())
                {
                    if (total < RowCap)
                        rows.Add(value);
                    total++;
                }

                tab.Rows = rows;
                tab.TotalRows = total;
            }
            catch (Exception ex)
            {
                tab.Error = $"GetAllValue failed: {ex.GetType().Name}";
            }

            return tab;
        }

        private void ShowMessage(string tabTitle, string bodyText)
        {
            _tabStates = null;
            _placeholderTitle = tabTitle;
            _selectedTab = -1;
            _selectedRow = -1;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;

            ClearChildren(_tabContainer);
            var content = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            var label = new Label { Text = bodyText, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            content.AddChild(label);
            _tabContainer.AddChild(content);
            _tabContainer.SetTabTitle(0, tabTitle);

            _suppressTabChanged = true;
            _tabContainer.CurrentTab = 0;
            _suppressTabChanged = false;

            UpdateStatus();
            ApplyResponsive(CurrentWidth());
        }

        // ---- tab UI ----------------------------------------------------------

        private void BuildTabUi()
        {
            ClearChildren(_tabContainer);

            foreach (var tab in _tabStates)
            {
                var content = new VBoxContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill
                };
                content.AddThemeConstantOverride("separation", 6);

                tab.Note = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                tab.Note.AddThemeFontSizeOverride("font_size", ViewerFontSize);
                content.AddChild(tab.Note);

                var scroll = new ScrollContainer
                {
                    ClipContents = true,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                    VerticalScrollMode = ScrollContainer.ScrollMode.Auto
                };
                tab.Grid = new GridContainer { Columns = 1 };
                scroll.AddChild(tab.Grid);
                content.AddChild(scroll);

                _tabContainer.AddChild(content);
                BuildTable(tab);
            }

            for (var i = 0; i < _tabStates.Count; i++)
                _tabContainer.SetTabTitle(i, TabTitle(_tabStates[i]));
        }

        private void BuildTable(TabState tab)
        {
            ClearChildren(tab.Grid);
            tab.RowButtons.Clear();

            if (tab.Error != null)
            {
                tab.Grid.Columns = 1;
                SetNote(tab, $"Error: {tab.Error}");
                return;
            }

            var columns = tab.Columns;
            var notes = new List<string>();
            if (columns.Count == 0)
                notes.Add("No readable properties.");
            if (tab.TotalRows > RowCap)
                notes.Add($"Showing {RowCap} of {tab.TotalRows} rows (truncated)");
            SetNote(tab, string.Join("\n", notes));

            if (columns.Count == 0)
            {
                tab.Grid.Columns = 1;
                AddHeaderCell(tab.Grid, "#");
                for (var i = 0; i < tab.Rows.Count; i++)
                {
                    var rowButtons = new List<Button>
                    {
                        AddRowCell(tab, i, i.ToString(CultureInfo.InvariantCulture))
                    };
                    tab.RowButtons.Add(rowButtons);
                }

                return;
            }

            tab.Grid.Columns = columns.Count;
            foreach (var column in columns)
                AddHeaderCell(tab.Grid, column.Name);

            for (var r = 0; r < tab.Rows.Count; r++)
            {
                var row = tab.Rows[r];
                var rowButtons = new List<Button>(columns.Count);
                foreach (var column in columns)
                {
                    string text;
                    try
                    {
                        text = FormatCellValue(column.GetValue(row));
                    }
                    catch (Exception ex)
                    {
                        text = $"<error: {ex.GetType().Name}>";
                    }

                    rowButtons.Add(AddRowCell(tab, r, text));
                }

                tab.RowButtons.Add(rowButtons);
            }

            HighlightSelectedRow();
        }

        private static void SetNote(TabState tab, string text)
        {
            tab.Note.Text = text;
            tab.Note.Visible = !string.IsNullOrEmpty(text);
        }

        private static void AddHeaderCell(GridContainer grid, string text)
        {
            var label = new Label
            {
                Text = text,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(CellWidth, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            grid.AddChild(label);
        }

        private Button AddRowCell(TabState tab, int rowIndex, string text)
        {
            var button = new Button
            {
                Text = text,
                Flat = true,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(CellWidth, 0)
            };
            button.AddThemeFontSizeOverride("font_size", ViewerFontSize);
            button.Pressed += () => SelectRow(tab, rowIndex);
            tab.Grid.AddChild(button);
            return button;
        }

        private static string TabTitle(TabState tab)
        {
            // Short, stable tab labels: the tab strip must stay reachable on a phone-width card.
            // Counts, timestamps and error state belong on the status line (see UpdateStatus).
            return tab.DisplayName;
        }

        // ---- selection / detail ---------------------------------------------

        private void SelectRow(TabState tab, int rowIndex)
        {
            _selectedRow = rowIndex;
            _narrowDetailOpen = true;

            if (rowIndex >= 0 && rowIndex < tab.Rows.Count)
                _detailText.Text = SerializeDetail(tab.Rows[rowIndex]);

            HighlightSelectedRow();
            ApplyResponsive(CurrentWidth());
        }

        private void OnBack()
        {
            _narrowDetailOpen = false;
            ApplyResponsive(CurrentWidth());
        }

        private void HighlightSelectedRow()
        {
            var tab = SelectedTabState();
            if (tab == null)
                return;

            for (var r = 0; r < tab.RowButtons.Count; r++)
            {
                var selected = r == _selectedRow;
                var color = selected ? new Color(1f, 1f, 0.4f, 1f) : new Color(1f, 1f, 1f, 1f);
                foreach (var button in tab.RowButtons[r])
                    button.Modulate = color;
            }
        }

        private string SerializeDetail(object value)
        {
            if (value == null)
                return "null";

            try
            {
                return JsonSerializer.Serialize(value, value.GetType(), _jsonOptions);
            }
            catch (Exception ex)
            {
                return $"<detail serialization failed: {ex.GetType().Name}: {ex.Message}>";
            }
        }

        // ---- tabs ------------------------------------------------------------

        private void OnTabChanged(long tab)
        {
            if (_suppressTabChanged)
                return;

            var index = (int)tab;
            if (_tabStates == null || index < 0 || index >= _tabStates.Count)
                return;

            _selectedTab = index;
            _selectedRow = -1;
            _narrowDetailOpen = false;
            _detailText.Text = string.Empty;
            HighlightSelectedRow();
            UpdateStatus();
            ApplyResponsive(CurrentWidth());
        }

        private TabState SelectedTabState()
        {
            if (_tabStates == null || _selectedTab < 0 || _selectedTab >= _tabStates.Count)
                return null;
            return _tabStates[_selectedTab];
        }

        private Type SelectedType()
        {
            return SelectedTabState()?.ManagerType;
        }

        private int IndexOfType(Type managerType)
        {
            if (managerType != null && _tabStates != null)
            {
                for (var i = 0; i < _tabStates.Count; i++)
                {
                    if (_tabStates[i].ManagerType == managerType)
                        return i;
                }
            }

            return 0;
        }

        private void UpdateStatus()
        {
            var tab = SelectedTabState();
            if (tab == null)
            {
                _status.Text = _placeholderTitle;
                return;
            }

            if (tab.Error != null)
            {
                _status.Text = $"{tab.DisplayName} · error · refreshed {tab.Timestamp}";
                return;
            }

            _status.Text = $"{tab.DisplayName} · {tab.TotalRows} rows · refreshed {tab.Timestamp}";
        }

        // ---- responsive ------------------------------------------------------

        private void ApplyResponsive(float width)
        {
            var hasSelection = SelectedTabState() != null && _selectedRow >= 0;
            var wide = width >= WideBreakpointWidth;

            if (!hasSelection)
            {
                _tabContainer.Visible = true;
                _detailPane.Visible = false;
                _backButton.Visible = false;
                return;
            }

            if (wide)
            {
                _tabContainer.Visible = true;
                _detailPane.Visible = true;
                _backButton.Visible = false;
                return;
            }

            _tabContainer.Visible = !_narrowDetailOpen;
            _detailPane.Visible = _narrowDetailOpen;
            _backButton.Visible = _narrowDetailOpen;
        }

        private static void ClearChildren(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

    /// <summary>Snapshot of one manager tab: derived name, reflected columns, capped rows, status.</summary>
    private sealed class TabState
    {
        public TabState(Type managerType, string displayName, string timestamp)
        {
            ManagerType = managerType;
            DisplayName = displayName;
            Timestamp = timestamp;
        }

        public Type ManagerType { get; }

        public string DisplayName { get; }

        public string Timestamp { get; }

        public List<object> Rows { get; set; } = new();

        public int TotalRows { get; set; }

        public List<PropertyInfo> Columns { get; set; } = new();

        public string Error { get; set; }

        public Label Note { get; set; }

        public GridContainer Grid { get; set; }

        public List<List<Button>> RowButtons { get; } = new();
    }
}
