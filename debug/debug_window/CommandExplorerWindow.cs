using System;
using System.Collections.Generic;
using System.Reflection;
using cfEngine;
using cfEngine.Command;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Builds the content for the <c>commands</c> debug window ("Command Explorer") and exposes the two
/// pure operations exercised by engine-free tests. <see cref="Build"/> returns a fresh, parentless
/// ordinary <see cref="Control"/> subtree; assembling it performs no scene-tree attachment, no
/// worker/async work and no command execution. The host invokes <see cref="Build"/> from its
/// main-thread factory and owns the window chrome (title, close, drag, stacking).
///
/// Content: a built-in <see cref="Tree"/> of collapsible scope branches and command leaves, a detail
/// pane (read-only full path, optional handler hint, one raw args field) and a last-run output pane.
/// <see cref="BuildCommandTree"/> and <see cref="AssembleCommand"/> never construct Controls, access
/// Godot, resolve game services or reflect hint attributes, so a plain NUnit host can call them.
/// </summary>
public static class CommandExplorerWindow
{
    private const int ExplorerFontSize = 28;

    /// <summary>
    /// Assembles and returns a new parentless Command-Explorer subtree. No scene mutation, no
    /// <c>SceneTree.Root</c> access, no subscriptions to game state.
    /// </summary>
    public static Control Build()
    {
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
        private readonly LineEdit _argsField;
        private readonly Button _runButton;
        private readonly Label _outputStatus;
        private readonly RichTextLabel _output;

        private readonly Dictionary<TreeItem, CommandTreeEntry> _entryByItem = new();

        private CommandTreeEntry _selected;
        private Viewport _viewport;
        private bool _running;

        public Explorer()
        {
            Root = new Control();
            Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            _column = new VBoxContainer();
            _column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _column.AddThemeConstantOverride("separation", 8);
            Root.AddChild(_column);

            // Header: tree summary/status + Refresh.
            var toolbar = new HBoxContainer();
            toolbar.AddThemeConstantOverride("separation", 8);
            _column.AddChild(toolbar);

            _status = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _status.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            toolbar.AddChild(_status);

            _refreshButton = new Button
            {
                Text = "⟳ Refresh",
                FocusMode = Control.FocusModeEnum.None
            };
            _refreshButton.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
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
            _body.AddThemeConstantOverride("separation", 8);
            _column.AddChild(_body);

            _tree = new Tree
            {
                HideRoot = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _tree.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            _tree.ItemSelected += OnItemSelected;
            _tree.NothingSelected += OnNothingSelected;
            _body.AddChild(_tree);

            var detail = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            detail.AddThemeConstantOverride("separation", 6);
            _body.AddChild(detail);

            _pathField = new LineEdit
            {
                Editable = false,
                PlaceholderText = "select a command",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _pathField.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            detail.AddChild(_pathField);

            _hintLabel = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _hintLabel.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            detail.AddChild(_hintLabel);

            var argsRow = new HBoxContainer();
            argsRow.AddThemeConstantOverride("separation", 8);
            detail.AddChild(argsRow);

            _argsField = new LineEdit
            {
                PlaceholderText = "args",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _argsField.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            _argsField.TextSubmitted += OnArgsSubmitted;
            argsRow.AddChild(_argsField);

            _runButton = new Button
            {
                Text = "Run",
                FocusMode = Control.FocusModeEnum.None,
                Disabled = true
            };
            _runButton.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
            _runButton.Pressed += Run;
            argsRow.AddChild(_runButton);

            // Last-run output: a pinned status line and an independent, selectable, scrollable body.
            _outputStatus = new Label
            {
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _outputStatus.AddThemeFontSizeOverride("font_size", ExplorerFontSize);
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
            _output.AddThemeFontSizeOverride("normal_font_size", ExplorerFontSize);
            _output.AddThemeFontSizeOverride("mono_font_size", ExplorerFontSize);
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
            _pathField.Text = string.Empty;
            _hintLabel.Text = string.Empty;
            _hintLabel.Visible = false;
            _argsField.Text = string.Empty;
            _runButton.Disabled = true;
        }

        private void SelectCommand(CommandTreeEntry entry)
        {
            _selected = entry;
            _pathField.Text = entry.Path;
            _argsField.Text = string.Empty;
            _hintLabel.Text = string.Empty;
            _hintLabel.Visible = false;

            if (!entry.Owner.TryGetHandler(entry.Name, out var handler) || handler == null)
            {
                // The registration vanished since the last walk; do not offer a stale runnable leaf.
                _runButton.Disabled = true;
                SetStatus($"'{entry.Path}' is no longer registered. Tap ⟳ Refresh.");
                return;
            }

            var hint = ReadHint(handler);
            if (!string.IsNullOrEmpty(hint))
            {
                _hintLabel.Text = hint;
                _hintLabel.Visible = true;
            }

            _runButton.Disabled = false;
            _argsField.GrabFocus();
            _argsField.CaretColumn = _argsField.Text.Length;
        }

        private static string ReadHint(ICommandHandler handler)
        {
            var attribute = handler.GetType()
                .GetCustomAttribute<ICommandHandler.HintAttribute>(inherit: false);
            return attribute?.description;
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

            var command = AssembleCommand(entry.Path, _argsField.Text ?? string.Empty);

            // Replace the previous run's output; never append runs together.
            _output.Text = string.Empty;
            _outputStatus.Text = string.Empty;

            _running = true;
            _runButton.Disabled = true;
            _refreshButton.Disabled = true;

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
                _runButton.Disabled = _selected == null || !_selected.IsCommand;
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
    }
}
