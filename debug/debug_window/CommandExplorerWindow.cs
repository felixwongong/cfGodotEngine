using System;
using System.Collections.Generic;
using System.Reflection;
using cfEngine;
using cfEngine.Command;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Builds the content for the <c>commands</c> debug window ("Command Explorer") and exposes the pure
/// operations exercised by engine-free tests. <see cref="Build"/> returns a fresh, parentless
/// ordinary <see cref="Control"/> subtree; assembling it performs no scene-tree attachment, no
/// worker/async work and no command execution. The host invokes <see cref="Build"/> from its
/// main-thread factory and owns the window chrome (title, close, drag, stacking).
///
/// Content: a built-in <see cref="Tree"/> of collapsible scope branches and command leaves, a detail
/// pane and a last-run output pane. The detail pane is mixed-mode: a command whose handler declares
/// arguments (via <see cref="CommandService.ReadArguments"/>) renders a scrollable row list — one
/// key <see cref="OptionButton"/> plus a type-appropriate value control per argument, an
/// <c>+ add arg</c> button and a wrapped validation/warning display — while a command with zero
/// declared arguments (or no selection) keeps the original raw freeform args field verbatim. The Run
/// button is shared and remains reachable in either mode.
///
/// <see cref="BuildCommandTree"/>, <see cref="AssembleCommand"/>, <see cref="AssembleArgs"/> and
/// <see cref="ValidateArgs"/> never construct Controls, access Godot, resolve game services or
/// reflect hint attributes, so a plain NUnit host can call them. Metadata is read as a per-instance
/// snapshot — never a type-level or global cache. Arg types are advisory: the wire stays
/// <c>-key value</c> strings and handlers keep self-parsing.
/// </summary>
public static class CommandExplorerWindow
{
    private const int ExplorerFontSize = 28;

    // Bounded control widths keep the row list from widening the debug card on a phone-width
    // viewport; long key/choice labels clip and stay reachable through popup + tooltip.
    private const float KeyControlMinWidth = 160f;
    private const float ValueControlMinWidth = 140f;
    private const float RemoveButtonWidth = 48f;

    /// <summary>
    /// Assembles and returns a new parentless Command-Explorer subtree. No scene mutation, no
    /// <c>SceneTree.Root</c> access, no subscriptions to game state.
    /// </summary>
    public static Control Build()
    {
        DebugUiScale.EnsureRegistered();
        var explorer = new Explorer();
        return explorer.Root;
    }

    /// <summary>
    /// Walks the service in current enumeration order (handlers then scopes, matching
    /// <see cref="CommandConsoleCore.DiscoverCommands"/>) and returns root entries carrying recursive
    /// <see cref="CommandTreeEntry.Children"/>. Scope entries are always non-command rows — including
    /// empty scopes — and command entries are always leaves. Handlers are never executed and no
    /// Controls are constructed.
    /// </summary>
    public static IReadOnlyList<CommandTreeEntry> BuildCommandTree(CommandService service)
    {
        var entries = new List<CommandTreeEntry>();
        if (service == null)
            return entries;

        BuildSiblings(service, string.Empty, entries);
        return entries;
    }

    private static void BuildSiblings(CommandService service, string prefix, List<CommandTreeEntry> siblings)
    {
        foreach (var handlerName in service.GetHandlerNames())
        {
            var path = string.IsNullOrEmpty(prefix) ? handlerName : $"{prefix} {handlerName}";
            siblings.Add(CommandTreeEntry.Command(service, handlerName, path));
        }

        foreach (var scopeName in service.GetScopeNames())
        {
            var path = string.IsNullOrEmpty(prefix) ? scopeName : $"{prefix} {scopeName}";
            var entry = CommandTreeEntry.Scope(scopeName, path);
            siblings.Add(entry);

            // A service registered under several prefixes yields a separate branch per prefix, and an
            // unresolvable scope stays a (possibly empty) branch rather than becoming a runnable leaf.
            if (service.TryGetScope(scopeName, out var subService) && subService != null)
                BuildSiblings(subService, path, entry.ChildrenInternal);
        }
    }

    /// <summary>
    /// Joins a selected command path and the raw args text with exactly one separator space. The args
    /// text is never trimmed, quoted, normalized or parsed: an empty args string still leaves the
    /// trailing separator, and repeated/spacing/quote characters are preserved verbatim.
    /// </summary>
    public static string AssembleCommand(string path, string args)
    {
        return $"{path} {args}";
    }

    /// <summary>
    /// Serializes declared-argument rows into the exact <c>-key value …</c> wire text the parser
    /// already consumes. This is serialization only: rows are joined in user order with one
    /// separating space, each row emits its declared <see cref="CommandArg.Name"/> verbatim
    /// (including any leading hyphen(s)) followed by one space and its supplied value, except a
    /// <see cref="ArgType.Bool"/> row which emits only its name and ignores the tuple value. Values
    /// are never trimmed, quoted, escaped, sorted, deduplicated or skipped; a null tuple value
    /// coalesces to the empty string. An empty row list yields the empty string, so it composes with
    /// the byte-unchanged <see cref="AssembleCommand"/> (path plus trailing separator). A null
    /// list or a row with a null spec is an invalid programming input and throws.
    /// </summary>
    public static string AssembleArgs(IReadOnlyList<(CommandArg, string)> rows)
    {
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));

        var parts = new List<string>(rows.Count * 2);
        foreach (var row in rows)
        {
            var spec = row.Item1 ?? throw new ArgumentException("A row has a null argument spec.", nameof(rows));
            parts.Add(spec.Name);
            if (spec.Type != ArgType.Bool)
                parts.Add(row.Item2 ?? string.Empty);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// UI validation policy for declared-argument rows — deliberately separate from the parser and
    /// the handler contract. Returns the first blocking message, or the empty string when the rows
    /// may run; <paramref name="warning"/> is advisory (e.g. a flag that is not the last argument)
    /// and never blocks. The current declared list is the membership authority: a selected spec must
    /// belong to it by reference, names must be hyphen-prefixed and whitespace-free, and a name may
    /// appear in at most one row (ordinal comparison). Each included non-<see cref="ArgType.Bool"/>
    /// row needs a single non-whitespace token — the parser cannot quote space-containing values —
    /// typed per its <see cref="ArgType"/> using the current culture (matching the
    /// <c>Parameters</c> getters): Int via <c>int.TryParse</c>, Float via <c>float.TryParse</c> plus
    /// a finite check, and Choice must exactly match one of the declared choices. Every Required
    /// non-Bool declaration must be present — a Required Bool does not gate presence, because a flag
    /// carries no value. Rows and specs are never mutated.
    /// </summary>
    public static string ValidateArgs(
        IReadOnlyList<CommandArg> declared,
        IReadOnlyList<(CommandArg, string)> rows,
        out string warning)
    {
        if (declared == null)
            throw new ArgumentNullException(nameof(declared));
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));

        warning = string.Empty;
        var lastIndex = rows.Count - 1;

        for (var i = 0; i < rows.Count; i++)
        {
            var spec = rows[i].Item1 ?? throw new ArgumentException("A row has a null argument spec.", nameof(rows));
            var value = rows[i].Item2 ?? string.Empty;

            if (!IsDeclared(declared, spec))
                return $"Argument '{spec.Name}' is not declared for this command.";
            if (!IsValidKeyToken(spec.Name))
                return $"Argument name '{spec.Name}' must start with '-' and contain no whitespace.";
            if (!Enum.IsDefined(typeof(ArgType), spec.Type))
                return $"Argument '{spec.Name}' has an unsupported type.";

            for (var j = 0; j < i; j++)
            {
                var other = rows[j].Item1;
                if (other != null && string.Equals(other.Name, spec.Name, StringComparison.Ordinal))
                    return $"Argument '{spec.Name}' is selected in more than one row.";
            }

            if (spec.Type != ArgType.Bool)
            {
                if (value.Length == 0)
                    return $"Argument '{spec.Name}' needs a value, or remove the row.";
                if (ContainsWhitespace(value))
                    return $"Argument '{spec.Name}' value cannot contain spaces; the parser cannot quote space-containing values.";

                var typeError = ValidateValueType(spec, value);
                if (typeError != null)
                    return typeError;
            }
            else if (i < lastIndex && warning.Length == 0)
            {
                warning = $"'{spec.Name}' is a flag and must be the last argument.";
            }
        }

        for (var i = 0; i < declared.Count; i++)
        {
            var spec = declared[i];
            if (spec == null || !spec.Required || spec.Type == ArgType.Bool)
                continue;

            var present = false;
            for (var j = 0; j < rows.Count; j++)
            {
                if (ReferenceEquals(rows[j].Item1, spec))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
                return $"Required argument '{spec.Name}' is missing.";
        }

        return string.Empty;
    }

    private static bool IsDeclared(IReadOnlyList<CommandArg> declared, CommandArg spec)
    {
        for (var i = 0; i < declared.Count; i++)
        {
            if (ReferenceEquals(declared[i], spec))
                return true;
        }

        return false;
    }

    private static bool IsValidKeyToken(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '-')
            return false;

        return !ContainsWhitespace(name);
    }

    private static bool ContainsWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return true;
        }

        return false;
    }

    private static string ValidateValueType(CommandArg spec, string value)
    {
        switch (spec.Type)
        {
            case ArgType.Int:
                return int.TryParse(value, out _)
                    ? null
                    : $"Argument '{spec.Name}' must be a whole number.";

            case ArgType.Float:
                if (!float.TryParse(value, out var number))
                    return $"Argument '{spec.Name}' must be a number.";
                if (float.IsNaN(number) || float.IsInfinity(number))
                    return $"Argument '{spec.Name}' must be a finite number.";
                return null;

            case ArgType.Choice:
                if (spec.Choices == null || spec.Choices.Count == 0)
                    return $"Argument '{spec.Name}' has no choices to select.";
                for (var i = 0; i < spec.Choices.Count; i++)
                {
                    if (string.Equals(spec.Choices[i], value, StringComparison.Ordinal))
                        return null;
                }

                return $"Argument '{spec.Name}' must be one of: {string.Join(", ", spec.Choices)}.";

            default:
                return null;
        }
    }

    /// <summary>
    /// One node of the command tree: a command leaf (<see cref="IsCommand"/> true) or a scope branch.
    /// The public surface is read-only; construction and owner-service/name bookkeeping are nonpublic.
    /// </summary>
    public sealed class CommandTreeEntry
    {
        private readonly List<CommandTreeEntry> _children = new();

        private CommandTreeEntry(CommandService owner, string name, string path, bool isCommand)
        {
            Owner = owner;
            Name = name;
            Path = path;
            IsCommand = isCommand;
        }

        /// <summary>Local name: the scope token or the command's registered handler name.</summary>
        public string Name { get; }

        /// <summary>Full space-joined path from the service root.</summary>
        public string Path { get; }

        /// <summary>True for a runnable command leaf; false for a scope branch (including an empty one).</summary>
        public bool IsCommand { get; }

        /// <summary>Child entries in discovery order; always empty for a command leaf.</summary>
        public IReadOnlyList<CommandTreeEntry> Children => _children;

        /// <summary>The service this command's handler is registered on; only meaningful for leaves.</summary>
        internal CommandService Owner { get; }

        internal List<CommandTreeEntry> ChildrenInternal => _children;

        internal static CommandTreeEntry Command(CommandService owner, string name, string path)
            => new(owner, name, path, isCommand: true);

        internal static CommandTreeEntry Scope(string name, string path)
            => new(null, name, path, isCommand: false);
    }

    /// <summary>Per-window state and behaviour. One instance per built window; no static state.</summary>
    private sealed class Explorer
    {
        private readonly VBoxContainer _column;
        private readonly Label _status;
        private readonly Button _refreshButton;
        private readonly BoxContainer _body;
        private readonly Tree _tree;
        private readonly LineEdit _pathField;
        private readonly Label _hintLabel;
        private readonly HBoxContainer _rawArea;
        private readonly LineEdit _argsField;
        private readonly VBoxContainer _declaredArea;
        private readonly ScrollContainer _rowsScroll;
        private readonly VBoxContainer _rowsBox;
        private readonly Button _addButton;
        private readonly Label _errorLabel;
        private readonly Label _warningLabel;
        private readonly Button _runButton;
        private readonly Label _outputStatus;
        private readonly RichTextLabel _output;

        private readonly Dictionary<TreeItem, CommandTreeEntry> _entryByItem = new();
        private readonly List<RowState> _rows = new();

        private CommandTreeEntry _selected;
        private ICommandHandler _selectedHandler;
        private IReadOnlyList<CommandArg> _declaredArgs = Array.Empty<CommandArg>();
        private Viewport _viewport;
        private bool _running;
        private bool _declaredMode;
        private bool _editingLocked;
        private bool _rebuilding;

        public Explorer()
        {
            Root = new Control();
            Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _column = new VBoxContainer();
            _column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _column.AddThemeConstantOverride("separation", DebugUiScale.S(8));
            Root.AddChild(_column);

            // Header: tree summary/status + Refresh.
            var toolbar = new HBoxContainer();
            toolbar.AddThemeConstantOverride("separation", DebugUiScale.S(8));
            _column.AddChild(toolbar);

            _status = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _status.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            toolbar.AddChild(_status);

            _refreshButton = new Button
            {
                Text = "⟳ Refresh",
                FocusMode = Control.FocusModeEnum.None
            };
            _refreshButton.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _refreshButton.Pressed += Refresh;
            toolbar.AddChild(_refreshButton);

            // Body: tree and detail, one orientation-switchable container. Vertical in portrait
            // (tree above detail); horizontal otherwise (side-by-side).
            _body = new BoxContainer
            {
                Vertical = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _body.AddThemeConstantOverride("separation", DebugUiScale.S(8));
            _column.AddChild(_body);

            _tree = new Tree
            {
                HideRoot = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _tree.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _tree.ItemSelected += OnItemSelected;
            _tree.NothingSelected += OnNothingSelected;
            _body.AddChild(_tree);

            var detail = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            detail.AddThemeConstantOverride("separation", DebugUiScale.S(6));
            _body.AddChild(detail);

            _pathField = new LineEdit
            {
                Editable = false,
                PlaceholderText = "select a command",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _pathField.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            detail.AddChild(_pathField);

            _hintLabel = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _hintLabel.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            detail.AddChild(_hintLabel);

            // Raw mode: the original freeform args field, kept verbatim. Its whole area is hidden in
            // declared mode; Run deliberately lives outside it so it stays reachable either way.
            _rawArea = new HBoxContainer();
            _rawArea.AddThemeConstantOverride("separation", DebugUiScale.S(8));
            detail.AddChild(_rawArea);

            _argsField = new LineEdit
            {
                PlaceholderText = "args",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _argsField.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _argsField.TextSubmitted += OnArgsSubmitted;
            _rawArea.AddChild(_argsField);

            // Declared mode: a bounded scroll of key rows, an add button and a wrapped
            // validation/warning display. Hidden until a command with declared args is selected.
            _declaredArea = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                Visible = false
            };
            _declaredArea.AddThemeConstantOverride("separation", DebugUiScale.S(6));
            detail.AddChild(_declaredArea);

            _rowsScroll = new ScrollContainer
            {
                ClipContents = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _declaredArea.AddChild(_rowsScroll);

            _rowsBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.Fill };
            _rowsBox.AddThemeConstantOverride("separation", DebugUiScale.S(6));
            _rowsScroll.AddChild(_rowsBox);

            _addButton = new Button
            {
                Text = "+ add arg",
                FocusMode = Control.FocusModeEnum.None,
                Disabled = true
            };
            _addButton.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _addButton.Pressed += OnAddPressed;
            _declaredArea.AddChild(_addButton);

            _errorLabel = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _errorLabel.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _declaredArea.AddChild(_errorLabel);

            _warningLabel = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _warningLabel.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _declaredArea.AddChild(_warningLabel);

            // Run is shared by both modes and is never a descendant of a hidden raw-only container.
            _runButton = new Button
            {
                Text = "Run",
                FocusMode = Control.FocusModeEnum.None,
                Disabled = true
            };
            _runButton.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _runButton.Pressed += Run;
            detail.AddChild(_runButton);

            // Last-run output: a pinned status line and an independent, selectable, scrollable body.
            _outputStatus = new Label
            {
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _outputStatus.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            _column.AddChild(_outputStatus);

            _output = new RichTextLabel
            {
                BbcodeEnabled = false,
                SelectionEnabled = true,
                ScrollActive = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            var mono = new SystemFont { FontNames = new[] { "monospace" }, AllowSystemFallback = true };
            _output.AddThemeFontOverride("normal_font", mono);
            _output.AddThemeFontOverride("mono_font", mono);
            _output.AddThemeFontSizeOverride("normal_font_size", DebugUiScale.S(ExplorerFontSize));
            _output.AddThemeFontSizeOverride("mono_font_size", DebugUiScale.S(ExplorerFontSize));
            _column.AddChild(_output);

            Root.TreeEntered += OnTreeEntered;
            Root.TreeExiting += OnTreeExiting;

            Refresh();
            ApplyResponsive(Vector2.Zero);
        }

        public Control Root { get; }

        // ---- lifecycle (Godot main thread) -----------------------------------

        private void OnTreeEntered()
        {
            _viewport = Root.GetViewport();
            if (_viewport != null)
                _viewport.SizeChanged += OnViewportSizeChanged;
            Root.Resized += OnRootResized;
            ApplyResponsive(CurrentViewportSize());
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
            ApplyResponsive(CurrentViewportSize());
        }

        private void OnRootResized()
        {
            ApplyResponsive(CurrentViewportSize());
        }

        private Vector2 CurrentViewportSize()
        {
            var viewport = _viewport ?? Root.GetViewport();
            return viewport?.GetVisibleRect().Size ?? Vector2.Zero;
        }

        // ---- refresh ---------------------------------------------------------

        private void Refresh()
        {
            // Rebuild replaces every old item and its identity mapping; nothing from the previous
            // snapshot is retained. No handler is executed and no pipeline/network call is made.
            _tree.Clear();
            _entryByItem.Clear();
            ClearSelection();

            // Tree.Clear() drops the root item. Recreate it explicitly so top-level entries are its
            // children (HideRoot shows them as the top-level rows); otherwise the first entry would
            // itself become the hidden root and vanish from the tree.
            var root = _tree.CreateItem();

            var entries = BuildCommandTree(CommandService.instance);
            var commandCount = 0;
            foreach (var entry in entries)
                commandCount += AddEntry(root, entry);

            if (commandCount == 0)
                SetStatus("No commands registered. Tap ⟳ Refresh.");
            else
                SetStatus(commandCount == 1 ? "1 command" : $"{commandCount} commands");

            ApplyResponsive(CurrentViewportSize());
        }

        private int AddEntry(TreeItem parent, CommandTreeEntry entry)
        {
            var item = _tree.CreateItem(parent);
            item.SetText(0, entry.Name);
            item.SetSelectable(0, true);
            _entryByItem[item] = entry;

            if (entry.IsCommand)
                return 1;

            item.Collapsed = false;
            var count = 0;
            foreach (var child in entry.Children)
                count += AddEntry(item, child);
            return count;
        }

        // ---- selection -------------------------------------------------------

        private void OnItemSelected()
        {
            var item = _tree.GetSelected();
            if (item == null || !_entryByItem.TryGetValue(item, out var entry) || !entry.IsCommand)
            {
                ClearSelection();
                return;
            }

            SelectCommand(entry);
        }

        private void OnNothingSelected()
        {
            ClearSelection();
        }

        private void ClearSelection()
        {
            _selected = null;
            _selectedHandler = null;
            _declaredArgs = Array.Empty<CommandArg>();
            _pathField.Text = string.Empty;
            _hintLabel.Text = string.Empty;
            _hintLabel.Visible = false;
            _argsField.Text = string.Empty;
            EnterRawMode(grabFocus: false);
            _runButton.Disabled = true;
        }

        private void SelectCommand(CommandTreeEntry entry)
        {
            _selected = entry;
            _pathField.Text = entry.Path;
            _argsField.Text = string.Empty;
            _hintLabel.Text = string.Empty;
            _hintLabel.Visible = false;
            _selectedHandler = null;
            _declaredArgs = Array.Empty<CommandArg>();

            if (!entry.Owner.TryGetHandler(entry.Name, out var handler) || handler == null)
            {
                // The registration vanished since the last walk; do not offer a stale runnable leaf.
                EnterRawMode(grabFocus: false);
                _runButton.Disabled = true;
                SetStatus($"'{entry.Path}' is no longer registered. Tap ⟳ Refresh.");
                return;
            }

            _selectedHandler = handler;

            var hint = ReadHint(handler);
            if (!string.IsNullOrEmpty(hint))
            {
                _hintLabel.Text = hint;
                _hintLabel.Visible = true;
            }

            // One per-instance metadata read per selection; never a type-level or global cache.
            _declaredArgs = CommandService.ReadArguments(handler) ?? Array.Empty<CommandArg>();

            if (_declaredArgs.Count == 0)
                EnterRawMode(grabFocus: true);
            else
                EnterDeclaredMode();

            UpdateEligibility();
        }

        private static string ReadHint(ICommandHandler handler)
        {
            var attribute = handler.GetType()
                .GetCustomAttribute<ICommandHandler.HintAttribute>(inherit: false);
            return attribute?.description;
        }

        // ---- declared-arg mode ----------------------------------------------

        private void EnterRawMode(bool grabFocus)
        {
            _declaredMode = false;
            DisposeRows();
            _rawArea.Visible = true;
            _declaredArea.Visible = false;
            _addButton.Disabled = true;
            _errorLabel.Text = string.Empty;
            _errorLabel.Visible = false;
            _warningLabel.Text = string.Empty;
            _warningLabel.Visible = false;

            if (grabFocus)
            {
                _argsField.GrabFocus();
                _argsField.CaretColumn = _argsField.Text.Length;
            }
        }

        private void EnterDeclaredMode()
        {
            _declaredMode = true;
            _rawArea.Visible = false;
            _declaredArea.Visible = true;
            RebuildInitialRows();
            FocusFirstRow();
        }

        private void RebuildInitialRows()
        {
            _rebuilding = true;
            try
            {
                DisposeRows();
                // Required non-Bool declarations start as rows in declaration order; optional and
                // Required-Bool declarations are added explicitly through "+ add arg".
                for (var i = 0; i < _declaredArgs.Count; i++)
                {
                    var spec = _declaredArgs[i];
                    if (spec == null || !spec.Required || spec.Type == ArgType.Bool)
                        continue;

                    AddRow(spec);
                }
            }
            finally
            {
                _rebuilding = false;
            }

            RefreshRowAvailability();
            RecomputeValidation();
        }

        private void FocusFirstRow()
        {
            if (_rows.Count == 0)
                return;

            var row = _rows[0];
            if (row.ValueControl is LineEdit line)
            {
                line.GrabFocus();
                line.CaretColumn = line.Text.Length;
            }
            else
            {
                row.KeyButton.GrabFocus();
            }
        }

        private RowState AddRow(CommandArg spec)
        {
            var row = new RowState { Spec = spec };

            var rowBox = new HBoxContainer();
            rowBox.AddThemeConstantOverride("separation", DebugUiScale.S(8));
            rowBox.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            _rowsBox.AddChild(rowBox);
            row.Row = rowBox;

            var keyButton = new OptionButton
            {
                // Cap the drop-down's minimum width so long labels cannot widen the card; the full
                // label stays reachable through the popup and the tooltip.
                ClipText = true,
                CustomMinimumSize = new Vector2(DebugUiScale.S(KeyControlMinWidth), 0)
            };
            keyButton.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            for (var i = 0; i < _declaredArgs.Count; i++)
            {
                var candidate = _declaredArgs[i];
                keyButton.AddItem(candidate.Name);
                if (!string.IsNullOrEmpty(candidate.Description))
                    keyButton.SetItemTooltip(i, candidate.Description);
            }

            keyButton.Selected = IndexOfSpec(spec);
            keyButton.TooltipText = SpecTooltip(spec);
            keyButton.ItemSelected += index => OnRowKeyChanged(row, (int)index);
            rowBox.AddChild(keyButton);
            row.KeyButton = keyButton;

            rowBox.AddChild(BuildValueControl(row, spec));

            var removeButton = new Button
            {
                Text = "✕",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(DebugUiScale.S(RemoveButtonWidth), 0)
            };
            removeButton.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
            removeButton.Pressed += () => OnRowRemoved(row);
            rowBox.AddChild(removeButton);
            row.RemoveButton = removeButton;

            _rows.Add(row);
            return row;
        }

        private Control BuildValueControl(RowState row, CommandArg spec)
        {
            switch (spec.Type)
            {
                case ArgType.Bool:
                    // A flag carries no value at all — presence is the value.
                    row.Value = string.Empty;
                    row.ValueControl = null;
                    return null;

                case ArgType.Choice:
                {
                    var choice = new OptionButton
                    {
                        ClipText = true,
                        CustomMinimumSize = new Vector2(DebugUiScale.S(ValueControlMinWidth), 0)
                    };
                    choice.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
                    for (var i = 0; i < spec.Choices.Count; i++)
                    {
                        choice.AddItem(spec.Choices[i]);
                        choice.SetItemTooltip(i, spec.Choices[i]);
                    }

                    var initial = spec.Choices.Count > 0 ? spec.Choices[0] : string.Empty;
                    if (spec.Choices.Count > 0)
                        choice.Selected = 0;
                    row.Value = initial;
                    choice.TooltipText = initial;
                    choice.ItemSelected += index => OnRowValueSelected(row, (int)index);
                    row.ValueControl = choice;
                    return choice;
                }

                default:
                {
                    var line = new LineEdit
                    {
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        CustomMinimumSize = new Vector2(DebugUiScale.S(ValueControlMinWidth), 0)
                    };
                    if (spec.Type == ArgType.Int || spec.Type == ArgType.Float)
                        line.VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.NumberDecimal;
                    line.AddThemeFontSizeOverride("font_size", DebugUiScale.S(ExplorerFontSize));
                    row.Value = string.Empty;
                    line.TextChanged += text => OnRowValueChanged(row, text);
                    line.TextSubmitted += _ => Run();
                    row.ValueControl = line;
                    return line;
                }
            }
        }

        private int IndexOfSpec(CommandArg spec)
        {
            for (var i = 0; i < _declaredArgs.Count; i++)
            {
                if (ReferenceEquals(_declaredArgs[i], spec))
                    return i;
            }

            return -1;
        }

        private static string SpecTooltip(CommandArg spec)
        {
            var label = string.IsNullOrEmpty(spec.Description) ? spec.Name : $"{spec.Name} — {spec.Description}";
            return spec.Required ? $"{label} (required)" : label;
        }

        private void OnRowKeyChanged(RowState row, int index)
        {
            if (_rebuilding || index < 0 || index >= _declaredArgs.Count)
                return;

            var spec = _declaredArgs[index];
            if (ReferenceEquals(row.Spec, spec))
                return;

            _rebuilding = true;
            try
            {
                row.Spec = spec;
                // Changing type discards the previous value control entirely and resets its value.
                if (row.ValueControl != null)
                {
                    row.Row.RemoveChild(row.ValueControl);
                    row.ValueControl.QueueFree();
                    row.ValueControl = null;
                }

                var replacement = BuildValueControl(row, spec);
                if (replacement != null)
                {
                    row.Row.AddChild(replacement);
                    // Keep the value control between the key dropdown and the remove button.
                    row.Row.MoveChild(replacement, 1);
                }

                row.KeyButton.TooltipText = SpecTooltip(spec);
            }
            finally
            {
                _rebuilding = false;
            }

            RefreshRowAvailability();
            RecomputeValidation();
            UpdateEligibility();
        }

        private void OnRowValueChanged(RowState row, string text)
        {
            if (_rebuilding)
                return;

            row.Value = text ?? string.Empty;
            RecomputeValidation();
            UpdateEligibility();
        }

        private void OnRowValueSelected(RowState row, int index)
        {
            if (_rebuilding || row.Spec == null)
                return;

            var choices = row.Spec.Choices;
            if (index < 0 || index >= choices.Count)
                return;

            row.Value = choices[index];
            if (row.ValueControl is OptionButton button)
                button.TooltipText = row.Value;
            RecomputeValidation();
            UpdateEligibility();
        }

        private void OnRowRemoved(RowState row)
        {
            if (_editingLocked || _rebuilding)
                return;

            RemoveRow(row);
            RefreshRowAvailability();
            RecomputeValidation();
            UpdateEligibility();
        }

        private void RemoveRow(RowState row)
        {
            if (!_rows.Remove(row))
                return;

            if (row.Row.GetParent() != null)
            {
                _rowsBox.RemoveChild(row.Row);
                row.Row.QueueFree();
            }
        }

        private void OnAddPressed()
        {
            if (_editingLocked || _rebuilding)
                return;

            var spec = FirstUnusedSpec();
            if (spec == null)
                return;

            var row = AddRow(spec);
            RefreshRowAvailability();
            RecomputeValidation();
            UpdateEligibility();

            if (row.ValueControl is LineEdit line)
                line.GrabFocus();
            else
                row.KeyButton.GrabFocus();
        }

        private CommandArg FirstUnusedSpec()
        {
            for (var i = 0; i < _declaredArgs.Count; i++)
            {
                if (!IsSpecUsed(_declaredArgs[i]))
                    return _declaredArgs[i];
            }

            return null;
        }

        private bool IsSpecUsed(CommandArg spec)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (ReferenceEquals(_rows[i].Spec, spec))
                    return true;
            }

            return false;
        }

        private void DisposeRows()
        {
            // Mirror InfoDebugWindow.ClearChildren: detach then queue-free so no stale callback can
            // mutate a removed row.
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Row.GetParent() != null)
                {
                    _rowsBox.RemoveChild(row.Row);
                    row.Row.QueueFree();
                }
            }

            _rows.Clear();

            foreach (var child in _rowsBox.GetChildren())
            {
                _rowsBox.RemoveChild(child);
                child.QueueFree();
            }
        }

        private void RefreshRowAvailability()
        {
            // A key chosen by one row is disabled in every other row's dropdown; each row keeps its
            // own key selectable so the popup mis-selection that produced the duplicate is reversible.
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                for (var item = 0; item < _declaredArgs.Count; item++)
                {
                    var spec = _declaredArgs[item];
                    var usedByOther = false;
                    for (var other = 0; other < _rows.Count; other++)
                    {
                        if (other == i)
                            continue;
                        if (_rows[other].Spec != null && ReferenceEquals(_rows[other].Spec, spec))
                        {
                            usedByOther = true;
                            break;
                        }
                    }

                    row.KeyButton.SetItemDisabled(item, usedByOther);
                }
            }

            _addButton.Disabled = _editingLocked || FirstUnusedSpec() == null;
        }

        private void RecomputeValidation()
        {
            if (!_declaredMode)
            {
                _errorLabel.Text = string.Empty;
                _errorLabel.Visible = false;
                _warningLabel.Text = string.Empty;
                _warningLabel.Visible = false;
                return;
            }

            var tuples = CurrentTuples();
            var error = ValidateArgs(_declaredArgs, tuples, out var warning);

            _errorLabel.Text = error;
            _errorLabel.Visible = !string.IsNullOrEmpty(error);
            _warningLabel.Text = warning;
            _warningLabel.Visible = !string.IsNullOrEmpty(warning);
        }

        private List<(CommandArg, string)> CurrentTuples()
        {
            var tuples = new List<(CommandArg, string)>(_rows.Count);
            for (var i = 0; i < _rows.Count; i++)
                tuples.Add((_rows[i].Spec, _rows[i].Value ?? string.Empty));
            return tuples;
        }

        private void SetEditingLocked(bool locked)
        {
            _editingLocked = locked;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.KeyButton.Disabled = locked;
                row.RemoveButton.Disabled = locked;
                if (row.ValueControl is LineEdit line)
                    line.Editable = !locked;
                else if (row.ValueControl is OptionButton choice)
                    choice.Disabled = locked;
            }

            RefreshRowAvailability();
        }

        private void UpdateEligibility()
        {
            _runButton.Disabled = !IsEligible();
        }

        private bool IsEligible()
        {
            if (_running || _selected == null || !_selected.IsCommand)
                return false;

            if (!_declaredMode)
                return true;

            return string.IsNullOrEmpty(ValidateArgs(_declaredArgs, CurrentTuples(), out _));
        }

        // ---- run -------------------------------------------------------------

        private void OnArgsSubmitted(string text)
        {
            Run();
        }

        private void Run()
        {
            if (_running)
                return;

            var entry = _selected;
            if (entry == null || !entry.IsCommand)
                return;

            if (!entry.Owner.TryGetHandler(entry.Name, out var handler) || handler == null)
            {
                _runButton.Disabled = true;
                SetStatus($"'{entry.Path}' is no longer registered. Tap ⟳ Refresh.");
                return;
            }

            string command;
            if (_declaredMode)
            {
                // The instance's declarations are the source of truth. If the registered instance
                // changed since selection, the rows describe a stale command — invalidate, refresh
                // and refuse rather than executing against the old declarations.
                if (!ReferenceEquals(handler, _selectedHandler))
                {
                    DisposeRows();
                    Refresh();
                    return;
                }

                // Independently validate: Enter and programmatic calls must not bypass Required,
                // duplicate or type checks that might otherwise leave Run enabled by mistake.
                var tuples = CurrentTuples();
                var error = ValidateArgs(_declaredArgs, tuples, out var warning);
                _errorLabel.Text = error;
                _errorLabel.Visible = !string.IsNullOrEmpty(error);
                _warningLabel.Text = warning;
                _warningLabel.Visible = !string.IsNullOrEmpty(warning);
                if (!string.IsNullOrEmpty(error))
                {
                    UpdateEligibility();
                    return;
                }

                command = AssembleCommand(entry.Path, AssembleArgs(tuples));
            }
            else
            {
                // Raw legacy mode keeps the byte-unchanged assembly and registered-handler checks.
                command = AssembleCommand(entry.Path, _argsField.Text ?? string.Empty);
            }

            // Replace the previous run's output; never append runs together.
            _output.Text = string.Empty;
            _outputStatus.Text = string.Empty;

            _running = true;
            _runButton.Disabled = true;
            _refreshButton.Disabled = true;
            SetEditingLocked(true);

            // Captured callbacks only append managed text here; no Godot object is touched off the
            // Run routine, which does all Control mutation after ExecuteAndCapture returns.
            var logs = new List<string>();
            var success = false;
            CommandExecutionEvent completion = null;
            Exception failure = null;

            try
            {
                success = CommandConsoleCore.ExecuteAndCapture(
                    CommandService.instance,
                    command,
                    onLog: (message, level) => logs.Add(FormatLog(message, level)),
                    onEvent: evt => completion = evt);
            }
            catch (Exception ex)
            {
                // ExecuteAndCapture restores the logger and drops its subscription before rethrowing.
                failure = ex;
            }
            finally
            {
                RenderOutput(logs, success, completion, failure);
                _running = false;
                _refreshButton.Disabled = false;
                SetEditingLocked(false);
                // Failed execution must not unlock invalid rows: recompute from live state rather
                // than the old unconditional leaf-only re-enable.
                RecomputeValidation();
                UpdateEligibility();
            }
        }

        private void RenderOutput(List<string> logs, bool success, CommandExecutionEvent completion, Exception failure)
        {
            var lines = new List<string>(logs);

            if (failure != null)
            {
                lines.Add($"[error] {failure.GetType().Name}: {failure.Message}");
                _outputStatus.Text = "✗ failed";
            }
            else if (success)
            {
                _outputStatus.Text = "✓ success";
            }
            else
            {
                var message = completion?.Message;
                lines.Add(string.IsNullOrEmpty(message) ? "[error] command failed" : $"[error] {message}");
                _outputStatus.Text = "✗ failed";
            }

            _output.Text = string.Join("\n", lines);
        }

        private static string FormatLog(string message, LogLevel level)
        {
            return $"[{LevelLabel(level)}] {message}";
        }

        private static string LevelLabel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    return "debug";
                case LogLevel.Info:
                    return "info";
                case LogLevel.Warn:
                    return "warn";
                case LogLevel.Error:
                    return "error";
                default:
                    return "log";
            }
        }

        // ---- responsive ------------------------------------------------------

        private void ApplyResponsive(Vector2 size)
        {
            // Aspect decides the axis: a portrait viewport (height > width, equality is landscape)
            // stacks tree above detail; landscape keeps them side-by-side. The output pane stays below.
            _body.Vertical = size.Y > size.X;
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        /// <summary>
        /// Private per-row managed state projecting onto real Controls: the selected declaration, the
        /// current wire value and the three controls. Kept private so no new public type is exported.
        /// </summary>
        private sealed class RowState
        {
            public CommandArg Spec { get; set; }
            public HBoxContainer Row { get; set; }
            public OptionButton KeyButton { get; set; }
            public Control ValueControl { get; set; }
            public Button RemoveButton { get; set; }
            public string Value { get; set; } = string.Empty;
        }
    }
}
