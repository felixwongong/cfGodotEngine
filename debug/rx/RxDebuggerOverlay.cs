using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace cfGodotEngine.Debug
{
    public partial class RxDebuggerOverlay : CanvasLayer, IRxRecorderSink
    {
        private const int MaxVisibleRows = 200;

        private readonly Queue<RxDispatchEvent> _visible = new();
        private bool _sinkAttached;

        private Control _panel;
        private TabContainer _tabs;
        private RichTextLabel _list;
        private Label _summary;
        private Label _profileSummary;
        private RichTextLabel _leaksList;
        private RichTextLabel _graphTree;
        private RichTextLabel _cycleList;
        private RichTextLabel _profileList;

        private Button _pauseButton;
        private Button _markButton;
        private Button _clearButton;
        private Button _exportButton;
        private LineEdit _ownerFilter;
        private LineEdit _methodFilter;
        private CheckBox _exceptionsOnly;
        private LineEdit _minDuration;
        private CheckBox _autoPollLeaks;
        private Timer _leakPollTimer;

        private RxLeakAuditor _leakAuditor;
        private RxGraphBuilder _graphBuilder;

        public override void _Ready()
        {
            Layer = 100;
            Visible = false;
            _leakAuditor = new RxLeakAuditor();
            _graphBuilder = new RxGraphBuilder();
            BuildUi();
            RxRecorder.Instance.AddSink(this);
            _sinkAttached = true;
        }

        public override void _ExitTree()
        {
            if (_sinkAttached)
            {
                RxRecorder.Instance.RemoveSink(this);
                _sinkAttached = false;
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.F8)
                {
                    Visible = !Visible;
                    if (Visible) RenderAll();
                }
                else if (key.Keycode == Key.F9)
                {
                    var label = $"Mark at {Time.GetTicksMsec()}ms";
                    RxRecorder.Instance.Mark(label);
                    GD.Print($"[RxDebugger] {label}");
                }
            }
        }

        public void OnDispatchEvent(RxDispatchEvent evt)
        {
            if (!Visible || _tabs?.CurrentTab != 0) return;
            _visible.Enqueue(evt);
            while (_visible.Count > MaxVisibleRows)
                _visible.Dequeue();
            RenderRow(evt);
            UpdateSummary();
        }

        public void OnListenerSubscribed(string ownerType, string ownerTag, string listenerMethod, string listenerTargetType, long timestampTicks)
        {
        }

        public void OnListenerRemoved(string ownerType, string ownerTag, string listenerMethod, string listenerTargetType, long timestampTicks)
        {
        }

        public void OnCycleDetected(RxCycleEvent evt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[color=ff5555][b]CYCLE DETECTED[/b][/color]");
            for (int i = 0; i < evt.OwnerChain.Count; i++)
            {
                var listener = i < evt.ListenerChain.Count ? evt.ListenerChain[i] : "(re-entry)";
                sb.AppendLine($"[color=ff5555]  {evt.OwnerChain[i]} (listener: {listener})[/color]");
            }
            sb.AppendLine($"[color=ff5555]  -> {evt.OwnerChain[evt.OwnerChain.Count - 1]}  <- CYCLE (re-entered)[/color]");
            sb.AppendLine("");
            _cycleList.AppendText(sb.ToString());
            ShowCycleTab();
        }

        private void ShowCycleTab()
        {
            for (int i = 0; i < _tabs.GetTabCount(); i++)
            {
                if (_tabs.GetTabTitle(i) == "Cycle")
                {
                    _tabs.CurrentTab = i;
                    break;
                }
            }
        }

        private void BuildUi()
        {
            _panel = new Control
            {
                AnchorLeft = 1f,
                AnchorRight = 1f,
                OffsetLeft = -650,
                OffsetRight = -20,
                OffsetTop = 20,
                OffsetBottom = 920,
                GrowHorizontal = Control.GrowDirection.Begin,
                GrowVertical = Control.GrowDirection.End
            };

            var bg = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.85f),
                AnchorRight = 1f,
                AnchorBottom = 1f
            };
            _panel.AddChild(bg);

            var vbox = new VBoxContainer
            {
                AnchorRight = 1f,
                AnchorBottom = 1f,
                OffsetLeft = 10,
                OffsetRight = -10,
                OffsetTop = 10,
                OffsetBottom = -10
            };
            _panel.AddChild(vbox);

            var title = new Label
            {
                Text = "RX DEBUGGER (F8 toggle | F9 mark)",
                ThemeTypeVariation = "HeaderLarge"
            };
            vbox.AddChild(title);

            var toolbar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _pauseButton = new Button { Text = "Pause" };
            _pauseButton.Pressed += () =>
            {
                RxRecorder.Instance.Capturing = !RxRecorder.Instance.Capturing;
                _pauseButton.Text = RxRecorder.Instance.Capturing ? "Pause" : "Resume";
            };
            _markButton = new Button { Text = "Mark" };
            _markButton.Pressed += () =>
            {
                var label = $"Mark at {Time.GetTicksMsec()}ms";
                RxRecorder.Instance.Mark(label);
                GD.Print($"[RxDebugger] {label}");
            };
            _clearButton = new Button { Text = "Clear" };
            _clearButton.Pressed += () =>
            {
                _list.Clear();
                _visible.Clear();
                UpdateSummary();
            };
            _exportButton = new Button { Text = "Export JSON" };
            _exportButton.Pressed += ExportJson;
            toolbar.AddChild(_pauseButton);
            toolbar.AddChild(_markButton);
            toolbar.AddChild(_clearButton);
            toolbar.AddChild(_exportButton);
            vbox.AddChild(toolbar);

            _tabs = new TabContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            vbox.AddChild(_tabs);

            var eventsTab = BuildEventsTab();
            var profileTab = BuildProfileTab();
            var leaksTab = BuildLeaksTab();
            var graphTab = BuildGraphTab();
            var cycleTab = BuildCycleTab();
            _tabs.AddChild(eventsTab);
            _tabs.AddChild(profileTab);
            _tabs.AddChild(leaksTab);
            _tabs.AddChild(graphTab);
            _tabs.AddChild(cycleTab);
            _tabs.SetTabTitle(0, "Events");
            _tabs.SetTabTitle(1, "Profile");
            _tabs.SetTabTitle(2, "Leaks");
            _tabs.SetTabTitle(3, "Graph");
            _tabs.SetTabTitle(4, "Cycle");

            _leakPollTimer = new Timer
            {
                WaitTime = 5,
                OneShot = false,
                Autostart = false
            };
            AddChild(_leakPollTimer);
            _leakPollTimer.Timeout += OnLeakPollTimer;

            AddChild(_panel);
        }

        private Control BuildEventsTab()
        {
            var tab = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var filters = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _ownerFilter = new LineEdit { PlaceholderText = "owner tag filter", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _ownerFilter.TextChanged += _ => ApplyFilters();
            _methodFilter = new LineEdit { PlaceholderText = "method filter", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _methodFilter.TextChanged += _ => ApplyFilters();
            _exceptionsOnly = new CheckBox { Text = "errors only" };
            _exceptionsOnly.Toggled += _ => ApplyFilters();
            _minDuration = new LineEdit { PlaceholderText = "min µs", Text = "0", CustomMinimumSize = new Vector2(80, 0) };
            _minDuration.TextChanged += _ => ApplyFilters();
            filters.AddChild(_ownerFilter);
            filters.AddChild(_methodFilter);
            filters.AddChild(_exceptionsOnly);
            filters.AddChild(_minDuration);
            tab.AddChild(filters);

            _profileSummary = new Label { Text = "profiles: none" };
            tab.AddChild(_profileSummary);

            _list = new RichTextLabel
            {
                BbcodeEnabled = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true
            };
            tab.AddChild(_list);

            _summary = new Label { Text = "events: 0 | live subs: 0" };
            tab.AddChild(_summary);

            return tab;
        }

        private Control BuildProfileTab()
        {
            var tab = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var toolbar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var refreshButton = new Button { Text = "Refresh" };
            refreshButton.Pressed += RenderProfile;
            var resetButton = new Button { Text = "Reset profile" };
            resetButton.Pressed += () =>
            {
                RxRecorder.Instance.Reset();
                RenderProfile();
            };
            toolbar.AddChild(refreshButton);
            toolbar.AddChild(resetButton);
            tab.AddChild(toolbar);

            _profileList = new RichTextLabel
            {
                BbcodeEnabled = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true
            };
            tab.AddChild(_profileList);

            return tab;
        }

        private Control BuildLeaksTab()
        {
            var tab = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var toolbar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var snapshotButton = new Button { Text = "Snapshot load" };
            snapshotButton.Pressed += () =>
            {
                var sceneName = GetTree()?.CurrentScene?.SceneFilePath ?? "unknown";
                _leakAuditor.OnSceneLoaded(sceneName);
                _leaksList.AppendText($"[color=ffff00]Snapshot at {sceneName}[/color]\n");
            };
            var checkButton = new Button { Text = "Check leaks" };
            checkButton.Pressed += RunLeakCheck;
            var clearButton = new Button { Text = "Clear" };
            clearButton.Pressed += () => _leaksList.Clear();
            _autoPollLeaks = new CheckBox { Text = "auto-poll (5s)" };
            _autoPollLeaks.Toggled += pressed =>
            {
                if (pressed)
                    _leakPollTimer.Start();
                else
                    _leakPollTimer.Stop();
            };
            toolbar.AddChild(snapshotButton);
            toolbar.AddChild(checkButton);
            toolbar.AddChild(clearButton);
            toolbar.AddChild(_autoPollLeaks);
            tab.AddChild(toolbar);

            _leaksList = new RichTextLabel
            {
                BbcodeEnabled = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true
            };
            tab.AddChild(_leaksList);

            return tab;
        }

        private Control BuildGraphTab()
        {
            var tab = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var toolbar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var refreshButton = new Button { Text = "Refresh tree" };
            refreshButton.Pressed += RefreshGraphTree;
            var exportButton = new Button { Text = "Export draw.io" };
            exportButton.Pressed += ExportGraph;
            toolbar.AddChild(refreshButton);
            toolbar.AddChild(exportButton);
            tab.AddChild(toolbar);

            _graphTree = new RichTextLabel
            {
                BbcodeEnabled = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true
            };
            tab.AddChild(_graphTree);

            return tab;
        }

        private Control BuildCycleTab()
        {
            var tab = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var toolbar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var clearButton = new Button { Text = "Clear" };
            clearButton.Pressed += () => _cycleList.Clear();
            toolbar.AddChild(clearButton);
            tab.AddChild(toolbar);

            _cycleList = new RichTextLabel
            {
                BbcodeEnabled = true,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true
            };
            tab.AddChild(_cycleList);

            return tab;
        }

        private void ApplyFilters()
        {
            RxRecorder.Instance.OwnerTagFilter = _ownerFilter.Text;
            RxRecorder.Instance.ListenerMethodFilter = _methodFilter.Text;
            RxRecorder.Instance.ExceptionsOnly = _exceptionsOnly.ButtonPressed;
            long.TryParse(_minDuration.Text, out var min);
            RxRecorder.Instance.MinElapsedMicros = min;
        }

        private void RenderAll()
        {
            _list.Clear();
            _visible.Clear();
            foreach (var evt in RxRecorder.Instance.Snapshot())
            {
                _visible.Enqueue(evt);
                if (_visible.Count > MaxVisibleRows)
                    _visible.Dequeue();
            }
            foreach (var evt in _visible)
                RenderRow(evt);
            UpdateSummary();
            RenderProfile();
        }

        private void RenderRow(RxDispatchEvent evt)
        {
            var color = evt.IsException ? "ff5555" : "cccccc";
            if (RxRecorder.Instance.HasBookmark && evt.DispatchOrder > RxRecorder.Instance.BookmarkIndex)
                color = evt.IsException ? "ffaaaa" : "aaffaa";

            var ms = evt.TimestampTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
            var line = $"[color=#{color}]{ms:F1}ms #{evt.DispatchOrder} {evt.OwnerTag}/{evt.ListenerMethod} ({evt.ElapsedMicros}µs){(!string.IsNullOrEmpty(evt.ArgsString) ? $" -> {evt.ArgsString}" : "")}[/color]";
            if (evt.IsException)
                line += $"\n[color=ff5555]  ERR: {evt.Exception?.Message}[/color]";
            _list.AppendText(line + "\n");
        }

        private void UpdateSummary()
        {
            var reg = RxRecorder.Instance.Registry;
            var total = reg.TotalSubscribed;
            var live = total - reg.TotalRemoved;
            var txt = $"events: {RxRecorder.Instance.RingCount} / {RxRecorder.Instance.Capacity} | live subs: {live} / total: {total}";
            txt += $" | batches: {RxRecorder.Instance.DispatchBatches} | invokes: {RxRecorder.Instance.DispatchInvocations} | ex: {RxRecorder.Instance.ExceptionCount}";
            if (RxRecorder.Instance.HasBookmark)
                txt += $" | bookmark: {RxRecorder.Instance.BookmarkLabel}";
            _summary.Text = txt;
        }

        private void RenderProfile()
        {
            var sb = new StringBuilder();
            var rec = RxRecorder.Instance;

            sb.AppendLine($"[b]Totals[/b]: batches={rec.DispatchBatches} invocations={rec.DispatchInvocations} exceptions={rec.ExceptionCount}");

            sb.AppendLine($"[b]Top 20 Owner Tags (by total µs)[/b]");
            var topOwners = rec.ByOwnerTag
                .OrderByDescending(p => p.Value.TotalElapsedMicros)
                .Take(20)
                .ToList();
            foreach (var p in topOwners)
                sb.AppendLine($"  {p.Key}: {p.Value.DispatchCount}x | {p.Value.TotalElapsedMicros}µs total | {p.Value.MaxElapsedMicros}µs max | ex:{p.Value.ExceptionCount} | slowest:{p.Value.SlowestListener}");
            if (topOwners.Count == 0) sb.AppendLine("  (none)");

            sb.AppendLine();
            sb.AppendLine($"[b]Top 20 Listener Methods (by total µs)[/b]");
            var topListeners = rec.ByListenerMethod
                .OrderByDescending(p => p.Value.TotalElapsedMicros)
                .Take(20)
                .ToList();
            foreach (var p in topListeners)
                sb.AppendLine($"  {p.Key}: {p.Value.DispatchCount}x | {p.Value.TotalElapsedMicros}µs total | {p.Value.MaxElapsedMicros}µs max | ex:{p.Value.ExceptionCount}");
            if (topListeners.Count == 0) sb.AppendLine("  (none)");

            sb.AppendLine();
            sb.AppendLine($"[b]Fan-out Histogram[/b]");
            var fanOut = rec.FanOutHistogram
                .OrderBy(p => p.Key)
                .ToList();
            foreach (var p in fanOut)
                sb.AppendLine($"  {p.Key} listeners: {p.Value} batches");
            if (fanOut.Count == 0) sb.AppendLine("  (none)");

            sb.AppendLine();
            sb.AppendLine($"[b]Mark History[/b]");
            var marks = rec.Bookmarks;
            for (int i = 0; i < marks.Count; i++)
                sb.AppendLine($"  #{i}: order={marks[i].DispatchOrder} \"{marks[i].Label}\"");
            if (marks.Count == 0) sb.AppendLine("  (none)");

            _profileList.Text = sb.ToString();
        }

        private void ExportJson()
        {
            var dir = ProjectSettings.GlobalizePath("user://");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"rx-snapshot-{Time.GetTicksMsec()}.json");
            RxSnapshotSerializer.WriteToFile(RxRecorder.Instance, path);
            GD.Print($"[RxDebugger] exported snapshot to {path}");
        }

        private void RunLeakCheck()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[color=ffaa00]--- LEAK CHECK ---[/color]");
            foreach (var r in _leakAuditor.OnSceneUnloaded()) AppendLeak(sb, r);
            foreach (var r in _leakAuditor.PollDeadOwnerLeaks()) AppendLeak(sb, r);
            foreach (var r in _leakAuditor.PollGodotLeaks()) AppendLeak(sb, r);
            _leaksList.AppendText(sb.ToString());
        }

        private void OnLeakPollTimer()
        {
            var sb = new StringBuilder();
            foreach (var r in _leakAuditor.PollDeadOwnerLeaks()) AppendLeak(sb, r);
            foreach (var r in _leakAuditor.PollGodotLeaks()) AppendLeak(sb, r);
            if (sb.Length > 0)
                _leaksList.AppendText(sb.ToString());
        }

        private void AppendLeak(StringBuilder sb, LeakReport r)
        {
            sb.AppendLine($"[color=ff5555]{r.Category}: {r.OwnerTag}/{r.ListenerMethod} -> {r.ListenerTargetType}[/color]");
            sb.AppendLine($"  reason: {r.Reason}");
            if (!string.IsNullOrEmpty(r.StackTrace))
            {
                var lines = r.StackTrace.Split('\n');
                for (int i = 0; i < Math.Min(lines.Length, 8); i++)
                    sb.AppendLine($"  {lines[i]}");
                if (lines.Length > 8) sb.AppendLine("  ...");
            }
            sb.AppendLine("");
        }

        private void RefreshGraphTree()
        {
            var graph = _graphBuilder.Build(RxRecorder.Instance.Registry);
            var sb = new StringBuilder();
            var roots = new List<RxGraphBuilder.GraphNode>();
            var incoming = new HashSet<RxGraphBuilder.GraphNode>();
            foreach (var node in graph)
            {
                foreach (var edge in node.Outgoing)
                    incoming.Add(edge.To);
            }
            foreach (var node in graph)
            {
                if (!incoming.Contains(node)) roots.Add(node);
            }
            if (roots.Count == 0 && graph.Count > 0)
                roots.Add(graph[0]);

            var visited = new HashSet<RxGraphBuilder.GraphNode>();
            foreach (var root in roots)
            {
                RenderNode(sb, root, 0, visited);
                sb.AppendLine("");
            }
            _graphTree.Text = sb.ToString();
        }

        private void RenderNode(StringBuilder sb, RxGraphBuilder.GraphNode node, int depth, HashSet<RxGraphBuilder.GraphNode> visited)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}{node.Label} ({node.OwnerType})");
            if (!visited.Add(node))
            {
                sb.AppendLine($"{indent}  ...");
                return;
            }
            foreach (var edge in node.Outgoing)
            {
                sb.AppendLine($"{indent}  --{edge.Label}--> {edge.To.Label}");
                RenderNode(sb, edge.To, depth + 1, visited);
            }
            visited.Remove(node);
        }

        private void ExportGraph()
        {
            var dir = ProjectSettings.GlobalizePath("user://");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"rx-graph-{Time.GetTicksMsec()}.drawio");
            RxGraphExporter.ExportDrawIo(path);
            GD.Print($"[RxDebugger] exported graph to {path}");
        }
    }
}
