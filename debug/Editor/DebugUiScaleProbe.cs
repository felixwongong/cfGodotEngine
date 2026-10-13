#if TOOLS
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using cfEngine;
using cfEngine.Command;
using cfEngine.Core;
using cfEngine.Info;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Development-only probe for the shared debug-UI scale pass (run <c>debug-ui-mobile</c>, vs-1).
/// Whole-file <c>#if TOOLS</c> gated and never referenced by runtime code or the exported assembly.
///
/// <para>It drives the real production seams on the Godot main thread: the real
/// <see cref="DebugWindowHost"/> registry, the real <c>InfoDebugWindow.Build()</c> and
/// <c>CommandExplorerWindow.Build()</c> entry points, and the real
/// <see cref="CommandConsoleOverlay"/>. It resolves the scale through the same
/// <c>debug/ui_scale_mode</c> project setting production reads (setting-only driving, no direct
/// dependency on the scale type for driving), waits for layout to settle, then asserts:</para>
/// <list type="bullet">
/// <item>×1 desktop absolute values (the documented base constants) — pixel-identity guard;</item>
/// <item>×2 mobile absolute values (fonts, cell widths, action minima, console placement);</item>
/// <item>the cross-mode doubling relation for every recorded font, explicit minimum, container
/// separation and panel content margin;</item>
/// <item>the pure auto/touch/rotation/breakpoint classification contract;</item>
/// <item>attachment and liveness (parented cells, real close→reopen, console hide/show).</item>
/// </list>
///
/// <para>Any failed assertion exits the process nonzero. It writes deterministic JSON snapshots to
/// <c>user://debug-ui-mobile/</c> (timestamps/carets/volatile log text are normalized out). Native
/// auto detection and visible-window screenshots are deliberately not claimed here; the visible
/// rendering backend and physical devices remain a human hold.</para>
/// </summary>
public partial class DebugUiScaleProbe : Node
{
    private const string OutputDir = "user://debug-ui-mobile";
    private const string InfoWindowName = "probe-info";
    private const string CommandsWindowName = "probe-commands";
    private const string ProbeCommand = "probe-scaletest";
    private const int SettleFrames = 8;

    // vs-2 (phone fullscreen debug windows) fixture identifiers. Kept separate from the vs-1
    // window names so the legacy capture's registry state is never reused.
    private const string Vs2InfoWindowName = "probe-vs2-info";
    private const string Vs2CommandsWindowName = "probe-vs2-commands";
    private const int Vs2StableFrames = 12;

    private readonly List<string> _failures = new();
    private readonly StringBuilder _log = new();

    /// <summary>The <see cref="InfoLayer"/> fixture registered into <see cref="Domain.Current"/>.</summary>
    private static InfoLayer? _fixtureLayer;

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var exit = 1;
        try
        {
            var args = ParseArgs(OS.GetCmdlineUserArgs());
            var headless = IsHeadless();
            _log.AppendLine(
                $"probe: scale-mode={args.ScaleMode} viewport={args.Width}x{args.Height} " +
                $"capture={args.Capture} compare={args.Compare} headless={headless}");

            RegisterFixtures();

            Capture? desktop = null;
            Capture? mobile = null;

            if (args.Compare)
            {
                desktop = await CaptureSurfaceAsync(DebugUiScale.ModeDesktop, args.Width, args.Height);
                mobile = await CaptureSurfaceAsync(DebugUiScale.ModeMobile, args.Width, args.Height);
                AssertCrossMode(desktop, mobile);
            }
            else if (args.ScaleMode == DebugUiScale.ModeMobile)
            {
                mobile = await CaptureSurfaceAsync(DebugUiScale.ModeMobile, args.Width, args.Height);
            }
            else if (args.ScaleMode == DebugUiScale.ModeDesktop)
            {
                desktop = await CaptureSurfaceAsync(DebugUiScale.ModeDesktop, args.Width, args.Height);
            }
            else
            {
                await CaptureSurfaceAsync(DebugUiScale.ModeAuto, args.Width, args.Height);
            }

            AssertSynthetics();

            WriteDumps(args, desktop, mobile);

            // vs-2 supplementary fixtures run strictly AFTER the legacy capture has been serialized
            // in memory. They build their own SubViewports (and one Window fallback), so they cannot
            // perturb the legacy snapshot's node order, selection or payload.
            var vs2 = await RunVs2FixturesAsync();
            WriteVs2Dump(args, vs2);

            // Deliberate assertion failure (cleanup still runs): proves a failing run exits nonzero
            // with the same clean teardown as a passing one, rather than only on a crash or a
            // malformed argument.
            if (args.ForceFail)
                Check(false, "vs2 force-fail: deliberate assertion failure (--force-fail)");

            if (headless)
                _log.AppendLine("NOTE  screenshots skipped: headless backend renders no visible framebuffer");
            else
                TryWriteScreenshots(args);

            exit = _failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            _failures.Add($"probe threw: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Release everything the probe built or registered before the tree tears down: detach
            // the process-wide fixtures, free the probe's own child nodes (the SubViewport/scene
            // tree deletes them at the end of the next frame), then let the deferred frees run so
            // no CSharpScript reference survives into engine shutdown.
            DetachFixtures();
            await ReleaseProbeChildrenAsync();

            foreach (var failure in _failures)
                _log.AppendLine($"FAILED: {failure}");

            foreach (var line in _log.ToString().Split('\n'))
            {
                if (line.Length > 0)
                    GD.Print(line);
            }

            if (exit != 0)
                GD.PrintErr($"[DebugUiScaleProbe] FAILED: {_failures.Count} assertion(s)");
            else
                GD.Print("[DebugUiScaleProbe] PASSED");

            GetTree().Quit(exit);
        }
    }

    /// <summary>Detaches and queue-frees every child the probe built, then drains the deferred frees.</summary>
    private async Task ReleaseProbeChildrenAsync()
    {
        var children = GetChildren();
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            RemoveChild(child);
            child.QueueFree();
        }

        await SettleAsync();
    }

    // ---- fixtures --------------------------------------------------------

    private static void RegisterFixtures()
    {
        var layer = new InfoLayer();
        layer.RegisterInfo(new ProbeInfoManager(
            new ProbeRow { id = "a", count = 1, flag = true },
            new ProbeRow { id = "b", count = 2, flag = false },
            new ProbeRow { id = "c", count = 3, flag = true }));
        Domain.Current.With(layer);
        _fixtureLayer = layer;

        CommandService.instance.RegisterHandler(ProbeCommand, new ProbeHandler());
    }

    /// <summary>Removes the fixtures this probe registered into process-wide holders.</summary>
    private static void DetachFixtures()
    {
        if (_fixtureLayer != null)
        {
            Domain.Current.Unregister<InfoLayer>(_fixtureLayer);
            _fixtureLayer = null;
        }

        CommandService.instance.UnregisterHandler(ProbeCommand);
    }

    // ---- capture ---------------------------------------------------------

    private async Task<Capture> CaptureSurfaceAsync(string mode, int width, int height)
    {
        DebugUiScale.EnsureRegistered();
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, mode);

        var viewport = new SubViewport { Size = new Vector2I(width, height), Disable3D = true };
        AddChild(viewport);

        var host = new DebugWindowHost();
        viewport.AddChild(host);

        var console = new CommandConsoleOverlay();
        viewport.AddChild(console);

        DebugWindowRegistry.Register(InfoWindowName, "info · scale probe", InfoDebugWindow.Build);
        DebugWindowRegistry.Register(CommandsWindowName, "commands · scale probe", CommandExplorerWindow.Build);
        DebugWindowRegistry.Open(InfoWindowName);
        DebugWindowRegistry.Open(CommandsWindowName);

        await SettleAsync();

        var capture = new Capture
        {
            Mode = mode,
            Width = width,
            Height = height,
            Factor = DebugUiScale.Factor,
            IsPhone = DebugUiScale.IsPhoneMode,
            Viewport = viewport,
            Host = host,
            Console = console,
        };

        CaptureTree(capture);

        // Programmatic liveness: select the probe command so the explorer's declared-arg rows
        // exist, then run a real close → reopen cycle on the info card and a console hide/show.
        capture.CommandSelected = SelectExplorerCommand(capture, ProbeCommand);
        await SettleAsync();
        Recapture(capture);

        capture.CloseReopen = await CloseAndReopenAsync(capture);
        capture.ConsoleToggle = ToggleConsolePanel(capture);

        // Fresh live references: the close/reopen cycle freed the old card nodes.
        Recapture(capture);

        // Mode-specific absolute assertions run while the real controls are still alive.
        if (mode == DebugUiScale.ModeMobile)
            AssertMobile(capture);
        else if (mode == DebugUiScale.ModeDesktop)
            AssertDesktop(capture);
        else
            AssertAuto(capture);

        FreeSurface(viewport);
        await SettleAsync();
        capture.Flat.Clear();
        // Drop the (already freed) node references so nothing pins a CSharpScript into shutdown.
        capture.Viewport = null;
        capture.Host = null;
        capture.Console = null;

        return capture;
    }

    private void Recapture(Capture capture)
    {
        capture.Flat.Clear();
        capture.Records.Clear();
        CaptureTree(capture);
    }

    private static void FreeSurface(Node viewport)
    {
        if (viewport.GetParent() != null)
            viewport.GetParent().RemoveChild(viewport);
        viewport.QueueFree();
    }

    private void CaptureTree(Capture capture)
    {
        capture.Flat.Clear();
        capture.Records.Clear();
        Walk(capture.Viewport!, string.Empty, capture.Flat, capture.Records);
    }

    private void Walk(Node node, string path, List<Node> flat, List<NodeRecord> records)
    {
        flat.Add(node);
        records.Add(Record(node, path));

        var children = node.GetChildren();
        for (var i = 0; i < children.Count; i++)
            Walk(children[i], path + "/" + i, flat, records);
    }

    private static NodeRecord Record(Node node, string path)
    {
        var record = new NodeRecord
        {
            path = path,
            cls = node.GetClass(),
            visible = node is CanvasItem canvasItem ? canvasItem.IsVisibleInTree() : true,
        };

        // Node.GetScript() returns a Variant that owns a reference to the (RefCounted) CSharpScript;
        // an undisposed Variant keeps that script referenced past teardown ("N ObjectDB instances
        // were leaked at exit" + "resources still in use" + a teardown SIGSEGV). Dispose it in scope.
        using var scriptVariant = node.GetScript();
        var script = scriptVariant.As<Script>();
        if (script != null && !string.IsNullOrEmpty(script.ResourcePath))
            record.script = script.ResourcePath.GetFile();

        if (node is not Control control)
            return record;

        var size = control.Size;
        var pos = control.Position;
        record.x = Round(pos.X);
        record.y = Round(pos.Y);
        record.w = Round(size.X);
        record.h = Round(size.Y);
        record.minX = Round(control.CustomMinimumSize.X);
        record.minY = Round(control.CustomMinimumSize.Y);

        var fontName = FontNameFor(control);
        if (fontName != null)
            record.font = control.GetThemeFontSize(fontName);

        if (control.HasThemeConstantOverride("separation"))
            record.sep = control.GetThemeConstant("separation");
        else if (control is GridContainer && control.HasThemeConstantOverride("h_separation"))
            record.sep = control.GetThemeConstant("h_separation");

        if (control.HasThemeStyleboxOverride("panel") && control.GetThemeStylebox("panel") is StyleBoxFlat flat)
        {
            record.mLeft = Round(flat.ContentMarginLeft);
            record.mTop = Round(flat.ContentMarginTop);
            record.mRight = Round(flat.ContentMarginRight);
            record.mBottom = Round(flat.ContentMarginBottom);
        }

        record.text = TextOf(control);

        return record;
    }

    private static float Round(float value) => (float)Math.Round(value, 2);

    private static string? FontNameFor(Control control) => control switch
    {
        RichTextLabel => "normal_font_size",
        Label or Button or LineEdit or TextEdit or Tree or TabBar => "font_size",
        _ => null,
    };

    private static string TextOf(Control control)
    {
        var raw = control switch
        {
            CheckBox checkBox => checkBox.Text,
            Label label => label.Text,
            Button button => button.Text,
            LineEdit lineEdit => lineEdit.Text,
            RichTextLabel rich => rich.Text,
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var normalized = Regex.Replace(raw, "\\d{2}:\\d{2}:\\d{2}", "<ts>").Trim();
        return normalized.Length > 60 ? normalized[..60] : normalized;
    }

    private async Task SettleAsync()
    {
        for (var i = 0; i < SettleFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // ---- liveness --------------------------------------------------------

    private bool SelectExplorerCommand(Capture capture, string name)
    {
        var tree = capture.Flat.OfType<Tree>().FirstOrDefault();
        if (tree == null)
            return false;

        var root = tree.GetRoot();
        for (var item = root?.GetFirstChild(); item != null; item = item.GetNext())
        {
            if (item.GetText(0) == name)
            {
                item.Select(0);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> CloseAndReopenAsync(Capture capture)
    {
        var card = FindInfoCard(capture.Host!);
        if (card == null)
            return false;

        var close = FindHeaderButton(card);
        if (close == null)
            return false;

        var before = CountCards(capture.Host!);
        close.EmitSignal(BaseButton.SignalName.Pressed);
        await SettleAsync();
        var afterClose = CountCards(capture.Host!);

        DebugWindowRegistry.Open(InfoWindowName);
        await SettleAsync();
        var afterReopen = CountCards(capture.Host!);
        var reopened = FindInfoCard(capture.Host!) != null;

        return afterClose == before - 1 && afterReopen == before && reopened;
    }

    private static bool ToggleConsolePanel(Capture capture)
    {
        var panel = capture.Console!.GetChildren().OfType<PanelContainer>().FirstOrDefault();
        if (panel == null)
            return false;

        var content = panel.GetChildren().OfType<Control>().FirstOrDefault();
        if (content == null)
            return false;

        var hiddenInTree = !content.IsVisibleInTree();
        panel.Visible = true;
        var shownInTree = content.IsVisibleInTree();
        panel.Visible = false;

        return hiddenInTree && shownInTree;
    }

    private static PanelContainer? FindInfoCard(Node host)
    {
        foreach (var child in host.GetChildren())
        {
            if (child is PanelContainer panel && HasDescendant<TabContainer>(panel))
                return panel;
        }

        return null;
    }

    private static bool HasDescendant<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T || HasDescendant<T>(child))
                return true;
        }

        return false;
    }

    private static int CountCards(Node host) => host.GetChildren().OfType<PanelContainer>().Count();

    private static Button? FindHeaderButton(PanelContainer card)
    {
        foreach (var child in card.GetChildren())
        {
            if (child is not VBoxContainer layout)
                continue;
            foreach (var inner in layout.GetChildren())
            {
                if (inner is not HBoxContainer header)
                    continue;
                foreach (var headerChild in header.GetChildren())
                {
                    if (headerChild is Button button && button.Text == "✕")
                        return button;
                }
            }
        }

        return null;
    }

    // ---- assertions: desktop (×1) ---------------------------------------

    private void AssertDesktop(Capture capture)
    {
        Check(capture.Factor == 1, $"desktop factor == 1 (got {capture.Factor})");
        Check(!capture.IsPhone, "desktop isPhone == false");
        Check(capture.CommandSelected, "desktop explorer probe command selected");

        var consoleButton = FindButtonByText(capture, "DEBUG");
        Check(consoleButton != null, "desktop console DEBUG button present");
        if (consoleButton != null)
        {
            Check(consoleButton.CustomMinimumSize == new Vector2(120, 72),
                $"desktop console button min == 120x72 (got {consoleButton.CustomMinimumSize})");
            Check(consoleButton.Position == new Vector2(900, 160),
                $"desktop console button position == (900,160) (got {consoleButton.Position})");
        }

        var hostClose = FindHostCloseButton(capture);
        Check(hostClose != null && hostClose.CustomMinimumSize == Vector2.Zero,
            "desktop host close has no explicit minimum");

        Check(HasLabelMinX(capture, 240), "desktop info cell label min width 240");
        Check(HasButtonMinX(capture, 48), "desktop info row action min width 48");
        Check(HasControlMinX(capture, 160), "desktop explorer key control min width 160");
        Check(HasControlMinX(capture, 140), "desktop explorer value control min width 140");

        Check(ResolvedFont(FindHostTitleLabel(capture)) == 28,
            "desktop host title font == 28");
        Check(ResolvedFont(capture.Flat.OfType<Tree>().FirstOrDefault()) == 28,
            "desktop explorer tree font == 28");
    }

    // ---- assertions: mobile (×2) ----------------------------------------

    private void AssertMobile(Capture capture)
    {
        Check(capture.Factor == 2, $"mobile factor == 2 (got {capture.Factor})");
        Check(capture.IsPhone, "mobile isPhone == true");
        Check(capture.CommandSelected, "mobile explorer probe command selected");

        var consoleButton = FindButtonByText(capture, "DEBUG");
        Check(consoleButton != null, "mobile console DEBUG button present");
        if (consoleButton != null)
        {
            Check(consoleButton.CustomMinimumSize == new Vector2(240, 144),
                $"mobile console button min == 240x144 (got {consoleButton.CustomMinimumSize})");
            var size = consoleButton.Size;
            var right = consoleButton.Position.X + size.X;
            var bottom = consoleButton.Position.Y + size.Y;
            Check(right <= capture.Width + 0.5f && bottom <= capture.Height + 0.5f,
                $"mobile console entry within viewport (right={right} bottom={bottom})");
            Check(consoleButton.Position.X <= capture.Width - size.X + 0.5f,
                $"mobile console entry clamped (x={consoleButton.Position.X} size={size.X})");
        }

        var hostClose = FindHostCloseButton(capture);
        Check(hostClose != null, "mobile host close present");
        if (hostClose != null)
        {
            Check(hostClose.CustomMinimumSize.X >= 120f && hostClose.CustomMinimumSize.Y >= 120f,
                $"mobile host close >= 120x120 (got {hostClose.CustomMinimumSize})");
        }

        Check(HasLabelMinX(capture, 480), "mobile info cell label min width 480");
        Check(HasButtonMinX(capture, 96), "mobile info row action min width 96");
        Check(HasControlMinX(capture, 320), "mobile explorer key control min width 320");
        Check(HasControlMinX(capture, 280), "mobile explorer value control min width 280");

        Check(ResolvedFont(FindHostTitleLabel(capture)) == 56,
            "mobile host title font == 56");
        Check(ResolvedFont(capture.Flat.OfType<Tree>().FirstOrDefault()) == 56,
            "mobile explorer tree font == 56");

        var grid = capture.Flat.OfType<GridContainer>().FirstOrDefault();
        Check(grid != null && grid.Columns == 4, $"mobile info grid columns == 4 (got {grid?.Columns})");
        Check(grid != null && grid.GetChildCount() == 16,
            $"mobile info grid cells == 16 (got {grid?.GetChildCount()})");

        Check(capture.CloseReopen, "mobile close → reopen info cycle");
        Check(capture.ConsoleToggle, "mobile console hide → show visibility");
    }

    // ---- assertions: cross-mode -----------------------------------------

    private void AssertCrossMode(Capture desktop, Capture mobile)
    {
        var fontsCompared = 0;
        var minsCompared = 0;
        var sepsCompared = 0;
        var marginsCompared = 0;

        foreach (var d in desktop.Records)
        {
            var m = mobile.Records.FirstOrDefault(r => r.path == d.path);
            if (m == null)
            {
                if (d.cls != "SubViewport")
                    Check(false, $"cross: path {d.path} ({d.cls}) missing in mobile");
                continue;
            }

            if (d.font.HasValue && m.font.HasValue)
            {
                fontsCompared++;
                Check(m.font == d.font * 2,
                    $"cross font ×2 at {d.cls}{d.path}: {d.font} → {m.font}");
            }

            if (d.minX > 0f || d.minY > 0f)
            {
                minsCompared++;
                Check(Close(m.minX, d.minX * 2f) && Close(m.minY, d.minY * 2f),
                    $"cross min ×2 at {d.cls}{d.path}: ({d.minX},{d.minY}) → ({m.minX},{m.minY})");
            }

            if (d.sep.HasValue && m.sep.HasValue)
            {
                sepsCompared++;
                Check(m.sep == d.sep * 2,
                    $"cross separation ×2 at {d.cls}{d.path}: {d.sep} → {m.sep}");
            }

            if (d.mLeft.HasValue && m.mLeft.HasValue)
            {
                marginsCompared++;
                Check(Close(m.mLeft.Value, d.mLeft.Value * 2f)
                      && Close(m.mTop!.Value, d.mTop!.Value * 2f)
                      && Close(m.mRight!.Value, d.mRight!.Value * 2f)
                      && Close(m.mBottom!.Value, d.mBottom!.Value * 2f),
                    $"cross panel margins ×2 at {d.path}");
            }
        }

        Check(fontsCompared >= 10, $"cross compared >= 10 fonts (got {fontsCompared})");
        Check(minsCompared >= 5, $"cross compared >= 5 explicit minima (got {minsCompared})");
        Check(sepsCompared >= 5, $"cross compared >= 5 separations (got {sepsCompared})");
        Check(marginsCompared >= 2, $"cross compared >= 2 panel margin sets (got {marginsCompared})");
    }

    // ---- assertions: auto + synthetics ----------------------------------

    private void AssertAuto(Capture capture)
    {
        // Auto classification reads the physical screen, not the probe's SubViewport, so compare
        // against the same native inputs the production path used.
        var touch = DisplayServer.IsTouchscreenAvailable();
        var screen = DisplayServer.ScreenGetSize();
        _log.AppendLine($"NOTE  auto native: touch={touch} screen={screen.X}x{screen.Y}");

        var expectedFactor = DebugUiScale.GetFactorFor(DebugUiScale.ModeAuto, touch);
        Check(capture.Factor == expectedFactor,
            $"auto factor consistent (got {capture.Factor}, expected {expectedFactor})");

        var expectedPhone = DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, touch, screen.X, screen.Y);
        Check(capture.IsPhone == expectedPhone,
            $"auto isPhone consistent (got {capture.IsPhone}, expected {expectedPhone})");
    }

    private void AssertSynthetics()
    {
        var edge = DebugUiScale.PhoneBreakpointPx;

        Check(DebugUiScale.GetFactorFor(DebugUiScale.ModeAuto, true) == 2, "auto+touch factor 2");
        Check(DebugUiScale.GetFactorFor(DebugUiScale.ModeAuto, false) == 1, "auto+no-touch factor 1");
        Check(DebugUiScale.GetFactorFor(DebugUiScale.ModeDesktop, true) == 1, "desktop+touch factor 1");
        Check(DebugUiScale.GetFactorFor(DebugUiScale.ModeMobile, false) == 2, "mobile+no-touch factor 2");
        Check(DebugUiScale.GetFactorFor("banana", true) == 2, "invalid mode falls back to auto");
        Check(DebugUiScale.GetFactorFor(null, true) == 2, "null mode falls back to auto");

        Check(DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, 1080, 2560), "auto phone portrait");
        Check(DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, 2560, 1080), "auto phone rotated");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, edge + 1, 2560), "auto edge+1 not phone");
        Check(DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, edge, 2560), "auto edge exactly phone");
        Check(DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, edge - 1, 2560), "auto edge-1 phone");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, 1600, 2560), "auto tablet not phone (scale only)");
        Check(DebugUiScale.GetFactorFor(DebugUiScale.ModeAuto, true) == 2, "auto tablet still ×2");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, false, 800, 600), "auto no-touch not phone");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeDesktop, true, 600, 800), "desktop override never phone");
        Check(DebugUiScale.IsPhoneFor(DebugUiScale.ModeMobile, false, 4000, 3000), "mobile override always phone");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, 0, 0), "zero dimensions not phone");
        Check(!DebugUiScale.IsPhoneFor(DebugUiScale.ModeAuto, true, -1, 100), "negative dimensions not phone");
        Check(DebugUiScale.IsPhoneFor("banana", true, 1080, 2400), "invalid mode auto-classifies");
    }

    // ---- output ----------------------------------------------------------

    private void WriteDumps(ArgSet args, Capture? desktop, Capture? mobile)
    {
        var dir = ProjectSettings.GlobalizePath(OutputDir);
        DirAccess.MakeDirRecursiveAbsolute(dir);

        if (desktop != null)
            WriteJson($"{dir}/{args.Capture}-desktop.json", desktop);
        if (mobile != null)
            WriteJson($"{dir}/{args.Capture}-mobile.json", mobile);
    }

    private void WriteJson(string path, Capture capture)
    {
        var payload = new
        {
            capture = capture.Mode,
            mode = capture.Mode,
            viewport = new[] { capture.Width, capture.Height },
            factor = capture.Factor,
            isPhone = capture.IsPhone,
            nodes = capture.Records,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Check(false, $"could not write {path}");
            return;
        }

        file.StoreString(json);
        _log.AppendLine($"wrote {path} ({capture.Records.Count} nodes)");
    }

    /// <summary>
    /// Writes the vs-2 diagnostics to their OWN file. The legacy desktop snapshot keeps its schema
    /// and payload untouched; these richer vs-2 measurements must never appear in it.
    /// </summary>
    private void WriteVs2Dump(ArgSet args, Vs2Report report)
    {
        var dir = ProjectSettings.GlobalizePath(OutputDir);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var path = $"{dir}/{args.Capture}-vs2.json";

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Check(false, $"vs2 could not write {path}");
            return;
        }

        file.StoreString(json);
        _log.AppendLine($"wrote {path} ({report.facts.Count} facts, {report.shapes.Count} shapes, {report.filters.Count} filter chains)");
    }

    private void TryWriteScreenshots(ArgSet args)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image == null)
        {
            _log.AppendLine("NOTE  screenshot unavailable from the current backend");
            return;
        }

        var dir = ProjectSettings.GlobalizePath(OutputDir);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        image.SavePng($"{dir}/{args.Capture}-visible.png");
        _log.AppendLine($"wrote {dir}/{args.Capture}-visible.png");
    }

    // ---- helpers ---------------------------------------------------------

    private void Check(bool condition, string message)
    {
        if (condition)
        {
            _log.AppendLine($"PASS  {message}");
        }
        else
        {
            _failures.Add(message);
            _log.AppendLine($"FAIL  {message}");
        }
    }

    private static bool Close(float a, float b) => Math.Abs(a - b) < 0.01f;

    private static bool IsHeadless() => DisplayServer.GetName() == "headless";

    private static Button? FindButtonByText(Capture capture, string text)
        => capture.Flat.OfType<Button>().FirstOrDefault(b => b.Text == text);

    private static Label? FindHostTitleLabel(Capture capture)
    {
        var card = FindInfoCard(capture.Host!);
        if (card == null)
            return null;

        foreach (var child in card.GetChildren())
        {
            if (child is not VBoxContainer layout)
                continue;
            foreach (var inner in layout.GetChildren())
            {
                if (inner is not HBoxContainer header)
                    continue;
                foreach (var headerChild in header.GetChildren())
                {
                    if (headerChild is Label label)
                        return label;
                }
            }
        }

        return null;
    }

    private static int? ResolvedFont(Control? control)
        => control == null ? null : control.GetThemeFontSize(FontNameFor(control) ?? "font_size");

    private static Button? FindHostCloseButton(Capture capture)
    {
        var card = FindInfoCard(capture.Host!);
        return card == null ? null : FindHeaderButton(card);
    }

    private static bool HasLabelMinX(Capture capture, float minX)
        => capture.Flat.OfType<Label>().Any(l => Close(l.CustomMinimumSize.X, minX));

    private static bool HasButtonMinX(Capture capture, float minX)
        => capture.Flat.OfType<Button>().Any(b => Close(b.CustomMinimumSize.X, minX));

    private static bool HasControlMinX(Capture capture, float minX)
        => capture.Flat.OfType<Control>().Any(c => Close(c.CustomMinimumSize.X, minX));

    private static ArgSet ParseArgs(string[] argv)
    {
        var args = new ArgSet();
        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--scale-mode" when i + 1 < argv.Length:
                    args.ScaleMode = argv[++i];
                    break;
                case "--viewport" when i + 1 < argv.Length:
                {
                    var parts = argv[++i].Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                    {
                        args.Width = width;
                        args.Height = height;
                    }

                    break;
                }
                case "--capture" when i + 1 < argv.Length:
                    args.Capture = argv[++i];
                    break;
                case "--compare":
                    args.Compare = true;
                    break;
                case "--force-fail":
                    args.ForceFail = true;
                    break;
            }
        }

        return args;
    }

    private sealed class ArgSet
    {
        public string ScaleMode { get; set; } = DebugUiScale.ModeAuto;
        public int Width { get; set; } = 1080;
        public int Height { get; set; } = 2560;
        public string Capture { get; set; } = "probe";
        public bool Compare { get; set; }
        public bool ForceFail { get; set; }
    }

    private sealed class Capture
    {
        public string Mode { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Factor { get; set; }
        public bool IsPhone { get; set; }
        public bool CommandSelected { get; set; }
        public bool CloseReopen { get; set; }
        public bool ConsoleToggle { get; set; }
        public SubViewport? Viewport { get; set; }
        public DebugWindowHost? Host { get; set; }
        public CommandConsoleOverlay? Console { get; set; }
        public List<Node> Flat { get; } = new();
        public List<NodeRecord> Records { get; } = new();
    }

    private sealed class NodeRecord
    {
        public string path { get; set; } = string.Empty;
        public string cls { get; set; } = string.Empty;
        public string script { get; set; } = string.Empty;
        public bool visible { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float w { get; set; }
        public float h { get; set; }
        public float minX { get; set; }
        public float minY { get; set; }
        public int? font { get; set; }
        public int? sep { get; set; }
        public float? mLeft { get; set; }
        public float? mTop { get; set; }
        public float? mRight { get; set; }
        public float? mBottom { get; set; }
        public string text { get; set; } = string.Empty;
    }

    // =============================================================================================
    // vs-2 supplements: phone fullscreen placement, safe-area conversion, stacking, console
    // topology, real chrome/filter audit and width arithmetic. Everything drives the production
    // seams only (the real host, the real mapper, the real Info/Explorer builders, the real
    // console). A synthetic SafeAreaReader bypasses the native display read; it never bypasses the
    // conversion or the placement. A synthetic nonzero inset therefore still has to travel through
    // MapDisplaySafeAreaToHost and the host's placement path to be observed on a card.
    // =============================================================================================

    private async Task<Vs2Report> RunVs2FixturesAsync()
    {
        var report = new Vs2Report();

        // The app's `CommandConsole` autoload owns its own real DebugWindowHost, so it is already
        // subscribed to the process-wide registry when the probe starts. Every synthetic Open in a
        // vs-2 fixture would otherwise also build a card inside that ambient host, corrupting any
        // factory-invocation count. Record it, then detach it for the duration of the fixtures and
        // restore it afterwards (the probe quits; the ambient list is put back as found).
        report.facts.Add(new Vs2Fact
        {
            name = "registry.ambient-subscribers",
            value = DescribeRegistrySubscribers()
        });

        var ambientHandlers = DetachRegistryHandlers();
        try
        {
            AssertMapperMath(report);
            await RunFixtureAsync("phone-placement", () => AssertPhonePlacementAsync(report));
            await RunFixtureAsync("phone-fallback", () => AssertPhoneFallbackAsync(report));
            await RunFixtureAsync("stretch", () => AssertStretchedFixtureAsync(report));
            await RunFixtureAsync("stack", () => AssertPhoneStackAsync(report));
            await RunFixtureAsync("console", () => AssertConsoleTopologyAsync(report));
            await RunFixtureAsync("width", () => AssertWidthArithmeticAsync(report));
            await RunFixtureAsync("resize", () => AssertPhoneResizeAsync(report));
            await RunFixtureAsync("tablet", () => AssertTabletFloatingAsync(report));
        }
        catch (Exception ex)
        {
            _failures.Add($"vs2 fixture threw: {ex.GetType().Name}: {ex.Message}");
            _log.AppendLine($"FAIL  vs2 fixture threw: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            RestoreRegistryHandlers(ambientHandlers);
            // Restore the shared setting so a fixture failure cannot leak a mode into the next one.
            ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeAuto);
        }

        return report;
    }

    private async Task RunFixtureAsync(string name, Func<Task> fixture)
    {
        _log.AppendLine($"NOTE  vs2 fixture '{name}' start, registry subscribers={CountRegistrySubscribers()}");
        await fixture();
        _log.AppendLine($"NOTE  vs2 fixture '{name}' end, registry subscribers={CountRegistrySubscribers()} probeChildren={GetChildCount()}");
    }

    // ---- vs-2: registry isolation (probe-only) ----------------------------

    private static FieldInfo? RegistryEventField() => typeof(DebugWindowRegistry).GetField(
        nameof(DebugWindowRegistry.WindowOpenRequested),
        BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>Test-only read of the registry's subscriber count (field-like event backing field).</summary>
    private static int CountRegistrySubscribers()
    {
        var field = RegistryEventField();
        if (field == null)
            return -1;
        return field.GetValue(null) is Delegate handler ? handler.GetInvocationList().Length : 0;
    }

    private static string DescribeRegistrySubscribers()
    {
        if (RegistryEventField()?.GetValue(null) is not Delegate handler)
            return "(none)";

        var parts = new List<string>();
        foreach (var entry in handler.GetInvocationList())
        {
            if (entry.Target is Node node && GodotObject.IsInstanceValid(node))
                parts.Add($"{node.GetType().Name}#{node.GetInstanceId()} inTree={node.IsInsideTree()} children={node.GetChildCount()}");
            else if (entry.Target != null)
                parts.Add($"{entry.Target.GetType().Name}(freed)");
            else
                parts.Add("(static)");
        }

        return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
    }

    /// <summary>Test-only: detaches every ambient registry handler, returning the previous list.</summary>
    private static Delegate? DetachRegistryHandlers()
    {
        var field = RegistryEventField();
        var original = field?.GetValue(null) as Delegate;
        field?.SetValue(null, null);
        return original;
    }

    private static void RestoreRegistryHandlers(Delegate? handlers) => RegistryEventField()?.SetValue(null, handlers);

    // ---- vs-2: mapper math (pure) ----------------------------------------

    private void AssertMapperMath(Vs2Report report)
    {
        var portrait = new Rect2(0f, 0f, 1080f, 2560f);
        var landscape = new Rect2(0f, 0f, 2560f, 1080f);
        var identity = Transform2D.Identity;
        var half = new Transform2D(new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), Vector2.Zero);
        var originScale = new Transform2D(new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(200f, 300f));
        var letterbox = new Transform2D(new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(0f, 100f));
        var singular = new Transform2D(Vector2.Zero, Vector2.Zero, Vector2.Zero);

        ExpectMap(report, "identity-portrait-top-inset", new Rect2(0f, 100f, 1080f, 2400f), portrait, identity, new Rect2(0f, 100f, 1080f, 2400f));
        ExpectMap(report, "half-scale-top-inset", new Rect2(0f, 50f, 540f, 1200f), portrait, half, new Rect2(0f, 100f, 1080f, 2400f));
        ExpectMap(report, "window-origin-half-scale", new Rect2(200f, 350f, 540f, 1200f), portrait, originScale, new Rect2(0f, 100f, 1080f, 2400f));
        ExpectMap(report, "letterbox-translation", new Rect2(0f, 150f, 540f, 1200f), portrait, letterbox, new Rect2(0f, 100f, 1080f, 2400f));
        ExpectMap(report, "full-display-identity", new Rect2(0f, 0f, 1080f, 2560f), portrait, identity, portrait);
        ExpectMap(report, "landscape-side-inset", new Rect2(100f, 0f, 2360f, 1080f), landscape, identity, new Rect2(100f, 0f, 2360f, 1080f));
        ExpectMap(report, "portrait-bottom-inset", new Rect2(0f, 0f, 1080f, 2440f), portrait, identity, new Rect2(0f, 0f, 1080f, 2440f));
        ExpectMap(report, "landscape-both-sides", new Rect2(80f, 0f, 2400f, 1080f), landscape, identity, new Rect2(80f, 0f, 2400f, 1080f));
        ExpectMap(report, "inset-clips-to-visible", new Rect2(-50f, -50f, 2000f, 3000f), portrait, identity, portrait);
        ExpectMap(report, "fallback-zero-safe", new Rect2(0f, 0f, 0f, 0f), portrait, identity, portrait);
        ExpectMap(report, "fallback-negative-safe", new Rect2(10f, 10f, -5f, -5f), portrait, identity, portrait);
        ExpectMap(report, "fallback-empty-visible", new Rect2(0f, 100f, 1080f, 2400f), new Rect2(0f, 0f, 0f, 0f), identity, new Rect2(0f, 0f, 0f, 0f));
        ExpectMap(report, "fallback-singular-transform", new Rect2(0f, 100f, 1080f, 2400f), portrait, singular, portrait);
        ExpectMap(report, "fallback-nonfinite-safe", new Rect2(0f, 0f, float.NaN, 100f), portrait, identity, portrait);
        ExpectMap(report, "fallback-empty-intersection", new Rect2(5000f, 5000f, 100f, 100f), portrait, identity, portrait);
    }

    private void ExpectMap(Vs2Report report, string name, Rect2 safe, Rect2 visible, Transform2D hostToScreen, Rect2 expected)
    {
        var actual = DebugWindowHost.MapDisplaySafeAreaToHost(safe, visible, hostToScreen);
        report.facts.Add(new Vs2Fact
        {
            name = $"mapper.{name}",
            value = $"safe={Fmt(safe)} visible={Fmt(visible)} hostToScreen={Fmt(hostToScreen)} expected={Fmt(expected)} actual={Fmt(actual)}"
        });

        Check(RectClose(actual, expected), $"vs2 mapper {name}: {Fmt(actual)} == {Fmt(expected)}");
    }

    // ---- vs-2: production placement (injected reader) --------------------

    private async Task AssertPhonePlacementAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        var safe = new Rect2(0f, 100f, 1080f, 2400f);
        host.SafeAreaReader = () => safe;
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var panel = FirstCard(host);
        Check(panel != null, "vs2 phone placement: info card attached");
        if (panel == null)
        {
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        var visible = viewport.GetVisibleRect();
        var hostToScreen = panel.GetScreenTransform() * panel.GetTransform().AffineInverse();
        var expected = DebugWindowHost.MapDisplaySafeAreaToHost(safe, visible, hostToScreen);
        var actual = RectOf(panel);

        report.facts.Add(new Vs2Fact
        {
            name = "phone-placement.raw",
            value = $"visible={Fmt(visible)} safe={Fmt(safe)} panelTransform={Fmt(panel.GetTransform())} hostToScreen={Fmt(hostToScreen)} expected={Fmt(expected)} actual={Fmt(actual)} combinedMin={Fmt(panel.GetCombinedMinimumSize())}"
        });
        RecordShape(report, "phone-card", panel);
        RecordShape(report, "phone-card-scroll", FindDirectScroll(panel));

        Check(panel.GetParent() == host, "vs2 phone placement: panel parent is the host CanvasLayer");
        Check(panel.IsInsideTree(), "vs2 phone placement: panel IsInsideTree");
        Check(panel.Visible, "vs2 phone placement: top card visible");
        Check(RectClose(actual, expected),
            $"vs2 phone placement: card {Fmt(actual)} == mapped safe rect {Fmt(expected)} (tolerance 0.5)");

        var combinedMin = panel.GetCombinedMinimumSize();
        Check(combinedMin.X <= panel.Size.X + 0.5f && combinedMin.Y <= panel.Size.Y + 0.5f,
            $"vs2 phone placement: chrome minimum does not expand past the assigned size (min {Fmt(combinedMin)} <= {Fmt(panel.Size)})");

        var close = FindHeaderButton(panel);
        Check(close != null && close.CustomMinimumSize.X >= 120f && close.CustomMinimumSize.Y >= 120f,
            $"vs2 phone placement: close >= 120x120 (got {close?.CustomMinimumSize})");

        var title = FindHeaderTitle(panel);
        Check(ResolvedFont(title) == 56, $"vs2 phone placement: title font 56 (got {ResolvedFont(title)})");

        var margin = PanelMargin(panel);
        Check(Close(margin.X, 24f) && Close(margin.Y, 16f),
            $"vs2 phone placement: panel content margins 24/16 scaled (got {margin})");

        // The phone card must equal the safe rect exactly, i.e. no cascade offset was written.
        Check(actual.Position == safe.Position && actual.Size == safe.Size,
            $"vs2 phone placement: exactly the safe rect, no cascade offset (got {Fmt(actual)})");

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    private async Task AssertPhoneFallbackAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var native = DisplayServer.GetDisplaySafeArea();
        report.facts.Add(new Vs2Fact
        {
            name = "phone-fallback.native-safe-area",
            value = $"backend={DisplayServer.GetName()} safe={native.Position.X},{native.Position.Y},{native.Size.X},{native.Size.Y}"
        });

        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        host.SafeAreaReader = static () => new Rect2();
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var panel = FirstCard(host);
        var visible = viewport.GetVisibleRect();
        Check(panel != null, "vs2 phone fallback: card attached");
        if (panel != null)
        {
            var actual = RectOf(panel);
            report.facts.Add(new Vs2Fact
            {
                name = "phone-fallback.empty-reader",
                value = $"visible={Fmt(visible)} actual={Fmt(actual)}"
            });
            Check(RectClose(actual, visible),
                $"vs2 phone fallback: empty/zero safe rect yields the full visible host rect {Fmt(visible)} (got {Fmt(actual)})");
        }

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: stretched logical/physical mapping ------------------------

    private async Task AssertStretchedFixtureAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        // Controlled stretched SubViewport: physical 540x1280, logical 1080x2560. The injected safe
        // rect is in the fixture's PHYSICAL screen space, so a correct conversion must produce the
        // logical top 100 (physical 50 / 0.5 stretch == 100 logical).
        var viewport = new SubViewport
        {
            Size = new Vector2I(540, 1280),
            Size2DOverride = new Vector2I(1080, 2560),
            Size2DOverrideStretch = true,
            Disable3D = true
        };
        AddChild(viewport);

        var host = new DebugWindowHost();
        viewport.AddChild(host);
        var safe = new Rect2(0f, 50f, 540f, 1200f);
        host.SafeAreaReader = () => safe;
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var panel = FirstCard(host);
        var visible = viewport.GetVisibleRect();
        var finalTransform = viewport.GetFinalTransform();
        // Same derivation the production host uses: the panel's own translation is cancelled.
        var hostToScreen = panel == null
            ? Transform2D.Identity
            : panel.GetScreenTransform() * panel.GetTransform().AffineInverse();
        var expected = DebugWindowHost.MapDisplaySafeAreaToHost(safe, visible, hostToScreen);
        var actual = panel == null ? new Rect2() : RectOf(panel);

        report.facts.Add(new Vs2Fact
        {
            name = "stretch.subviewport",
            value = $"size={viewport.Size} override={viewport.Size2DOverride} stretch={viewport.Size2DOverrideStretch} finalTransform={Fmt(finalTransform)} visible={Fmt(visible)} hostToScreen={Fmt(hostToScreen)} expected={Fmt(expected)} actual={Fmt(actual)}"
        });

        var hasStretch = Close(visible.Size.X, 1080f) && Close(visible.Size.Y, 2560f) && Close(hostToScreen.X.X, 0.5f);
        if (panel != null && hasStretch)
        {
            Check(Close(expected.Position.Y, 100f),
                $"vs2 stretch: physical inset 50 maps to logical top 100 (got {Round(expected.Position.Y)})");
            Check(RectClose(actual, expected),
                $"vs2 stretch: card {Fmt(actual)} == mapped logical rect {Fmt(expected)}");
        }
        else
        {
            Check(false,
                $"vs2 stretch: SubViewport screen mapping lacks the intended stretch (visible {Fmt(visible)}, hostToScreen {Fmt(hostToScreen)}) — a standalone Window stretch fixture is required");
        }

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: stacking / liveness ---------------------------------------

    private async Task AssertPhoneStackAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var infoBuilds = 0;
        var commandBuilds = 0;

        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        var safe = new Rect2(0f, 100f, 1080f, 2400f);
        host.SafeAreaReader = () => safe;
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", () => { infoBuilds++; return InfoDebugWindow.Build(); });
        DebugWindowRegistry.Register(Vs2CommandsWindowName, "vs2 commands", () => { commandBuilds++; return CommandExplorerWindow.Build(); });

        // ---- A opens -------------------------------------------------------
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));
        var cardA = FindInfoCard(host);
        Check(cardA != null, "vs2 stack: A attached after open");
        if (cardA == null)
        {
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        var idA = cardA.GetInstanceId();
        var buildsAfterA = infoBuilds;
        Check(infoBuilds == 1, $"vs2 stack: A factory invoked once (got {infoBuilds})");
        Check(cardA.GetParent() == host && cardA.IsInsideTree(), "vs2 stack: A parented + inside tree");
        Check(Cards(host).Count == 1, $"vs2 stack: one card after A (got {Cards(host).Count})");
        Check(cardA.Visible, "vs2 stack: A visible");
        Check(RectClose(RectOf(cardA), safe), $"vs2 stack: A fills the safe rect {Fmt(safe)} (got {Fmt(RectOf(cardA))})");
        RecordShape(report, "stack.a-open", cardA);

        // Interact with A: select a real info row so the detail pane carries state.
        var interactedA = SelectInfoRow(cardA);
        await SettleStableAsync(() => SafeMeasure(host));
        var detailTextA = FindDescendant<RichTextLabel>(cardA);
        var detailPaneA = detailTextA?.GetParent() as Control;
        Check(interactedA, "vs2 stack: A programmatic row selection reached the viewer");
        Check(detailPaneA != null && detailPaneA.Visible && detailTextA != null && detailTextA.IsVisibleInTree()
              && !string.IsNullOrWhiteSpace(detailTextA.Text),
            "vs2 stack: A detail pane locally visible, in tree and carrying JSON after the programmatic selection");
        RecordShape(report, "stack.a-interacted", detailTextA);

        // ---- B opens: A stays in tree, hidden ---------------------------------
        DebugWindowRegistry.Open(Vs2CommandsWindowName);
        await SettleStableAsync(() => SafeMeasure(host));
        var cardB = FindCommandsCard(host);
        Check(cardB != null, "vs2 stack: B attached after open");
        if (cardB == null)
        {
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        var idB = cardB.GetInstanceId();
        Check(Cards(host).Count == 2, $"vs2 stack: two cards after B (got {Cards(host).Count})");
        Check(cardB.Visible && !cardA.Visible, "vs2 stack: B visible, A hidden");
        Check(ReferenceEquals(FindInfoCard(host), cardA), "vs2 stack: A is the same hidden instance");
        Check(CountVisibleCards(host) == 1, $"vs2 stack: exactly one visible phone card (got {CountVisibleCards(host)})");
        Check(cardA.GetParent() == host && cardA.IsInsideTree(), "vs2 stack: hidden A stays attached and in tree");
        Check(detailPaneA != null && detailPaneA.Visible && !string.IsNullOrWhiteSpace(detailTextA?.Text),
            "vs2 stack: hidden A retained its selected detail state (local flag + text)");
        Check(RectClose(RectOf(cardA), safe), $"vs2 stack: hidden A keeps the safe rect (got {Fmt(RectOf(cardA))})");
        RecordShape(report, "stack.b-open.top", cardB);
        RecordShape(report, "stack.b-open.hidden", cardA);

        // ---- reopen A: existing card brought to front, no rebuild -------------
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));
        Check(infoBuilds == buildsAfterA, $"vs2 stack: reopen A did not rebuild content (builds {infoBuilds})");
        Check(ReferenceEquals(FindInfoCard(host), cardA), "vs2 stack: reopen A kept the same instance");
        Check(FindInfoCard(host)!.GetInstanceId() == idA, "vs2 stack: reopen A kept the same instance id");
        Check(cardA.Visible && !cardB.Visible, "vs2 stack: A visible, B hidden after reopen");
        Check(cardA.GetIndex() == Cards(host).Count - 1, "vs2 stack: reopened A is the last sibling (stack top)");
        Check(CountVisibleCards(host) == 1, "vs2 stack: exactly one visible card after reopen");

        // ---- drag suppression on the top phone card ---------------------------
        var headerA = FindHeader(cardA);
        var dragBefore = cardA.Position;
        if (headerA != null)
            EmitHeaderDrag(headerA, new Vector2(160f, 90f));
        await SettleAsync();
        Check(cardA.Position == dragBefore && cardA.Size == safe.Size,
            $"vs2 stack: phone header drag/motion suppressed (before {Fmt(dragBefore)} after {Fmt(cardA.Position)})");

        // ---- close A (top) with a real close press ----------------------------
        var closeA = FindHeaderButton(cardA);
        Check(closeA != null, "vs2 stack: A close button present");
        closeA?.EmitSignal(BaseButton.SignalName.Pressed);
        await SettleStableAsync(() => SafeMeasure(host));
        Check(!Cards(host).Contains(cardA), "vs2 stack: closed A detached from the host");
        Check(Cards(host).Count == 1, $"vs2 stack: one card after closing A (got {Cards(host).Count})");
        Check(cardB.Visible && cardB.IsInsideTree(), "vs2 stack: B revealed after closing the top card");
        Check(FindCommandsCard(host)!.GetInstanceId() == idB, "vs2 stack: revealed B is the same instance");
        Check(commandBuilds == 1, $"vs2 stack: closing A did not rebuild B (builds {commandBuilds})");
        Check(CountVisibleCards(host) == 1, "vs2 stack: exactly one visible card after closing the top");

        // ---- reopen A after a real close: fresh content, new instance ---------
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));
        var cardA2 = FindInfoCard(host);
        Check(cardA2 != null, "vs2 stack: A re-attached after a fresh open");
        if (cardA2 == null)
        {
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        Check(infoBuilds == buildsAfterA + 1, $"vs2 stack: post-close reopen rebuilt content (builds {infoBuilds})");
        Check(cardA2.GetInstanceId() != idA, "vs2 stack: post-close reopen created a new instance");
        Check(cardA2.Visible && !cardB.Visible, "vs2 stack: fresh A on top, B hidden");

        // ---- close a NON-top card: the top must not move ----------------------
        var idA2 = cardA2.GetInstanceId();
        var rectA2 = RectOf(cardA2);
        var closeB = FindHeaderButton(cardB);
        Check(closeB != null, "vs2 stack: B close button present");
        closeB?.EmitSignal(BaseButton.SignalName.Pressed);
        await SettleStableAsync(() => SafeMeasure(host));
        Check(Cards(host).Count == 1, $"vs2 stack: one card after closing non-top B (got {Cards(host).Count})");
        Check(ReferenceEquals(FindInfoCard(host), cardA2), "vs2 stack: closing non-top B left the top card untouched");
        Check(cardA2.GetInstanceId() == idA2 && RectClose(RectOf(cardA2), rectA2),
            "vs2 stack: closing non-top B did not move or rebuild the top card");
        Check(cardA2.Visible, "vs2 stack: the top card is still the visible one");

        // ---- close the LAST card: zero live cards, no stranded surface --------
        FindHeaderButton(cardA2)?.EmitSignal(BaseButton.SignalName.Pressed);
        await SettleStableAsync(() => SafeMeasure(host));
        Check(Cards(host).Count == 0, $"vs2 stack: zero live cards after closing the last (got {Cards(host).Count})");
        Check(host.GetChildren().OfType<Control>().All(static c => !c.Visible),
            "vs2 stack: no invisible stranded STOP surface after the last close");

        report.facts.Add(new Vs2Fact
        {
            name = "stack.counts",
            value = $"infoBuilds={infoBuilds} commandBuilds={commandBuilds} (expected info=2 command=1)"
        });

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: console topology + full filter audit -----------------------

    private async Task AssertConsoleTopologyAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        var console = new CommandConsoleOverlay();
        viewport.AddChild(console);
        host.SafeAreaReader = () => new Rect2(0f, 100f, 1080f, 2400f);
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        Check(host.Layer == 126, $"vs2 console: host layer is 126 (got {host.Layer})");
        Check(console.Layer == 128, $"vs2 console: console layer is 128 (got {console.Layer})");

        var card = FindInfoCard(host);
        var idCard = card?.GetInstanceId() ?? 0;

        var debugButton = FindButton(console, "DEBUG");
        Check(debugButton != null, "vs2 console: DEBUG button present");
        if (debugButton != null)
        {
            Check(debugButton.GetParent() != null && debugButton.IsInsideTree(), "vs2 console: DEBUG attached + inside tree");
            Check(debugButton.IsVisibleInTree(), "vs2 console: DEBUG visible");
            var viewportRect = new Rect2(Vector2.Zero, viewport.GetVisibleRect().Size);
            Check(viewportRect.Intersects(debugButton.GetGlobalRect()), "vs2 console: DEBUG inside the viewport");
        }

        var panel = console.GetChildren().OfType<PanelContainer>().FirstOrDefault();
        var content = panel?.GetChildren().OfType<Control>().FirstOrDefault();
        Check(panel != null && content != null, "vs2 console: panel + content present");
        if (panel != null && content != null)
        {
            var hiddenInitially = !content.IsVisibleInTree();
            panel.Visible = true;
            await SettleAsync();
            var shown = content.IsVisibleInTree();
            panel.Visible = false;
            await SettleAsync();
            var hiddenAgain = !content.IsVisibleInTree();
            Check(hiddenInitially && shown && hiddenAgain,
                $"vs2 console: content hidden -> shown -> hidden (hidden={hiddenInitially} shown={shown} hiddenAgain={hiddenAgain})");
        }

        var cardAfter = FindInfoCard(host);
        Check(cardAfter != null && cardAfter.GetInstanceId() == idCard,
            "vs2 console: the top host card identity survived console show/hide");
        Check(cardAfter != null && cardAfter.Visible, "vs2 console: the top host card is still visible");

        // Full instantiated ancestry/filter audit — every Control on the path, not a leaf-only grep.
        if (card != null)
        {
            AuditFilterChain(report, "host-card-close-button", FindHeaderButton(card));
            var grid = FindDescendant<GridContainer>(card);
            AuditFilterChain(report, "host-card-grid-cell", grid?.GetChild(0) as Control ?? grid);
            AuditFilterChain(report, "host-card-content-root", FindDirectScroll(card)?.GetChild(0) as Control);
        }
        AuditFilterChain(report, "console-input", FindDescendant<LineEdit>(console));

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: width arithmetic (honest overflow) -------------------------

    private async Task AssertWidthArithmeticAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        host.SafeAreaReader = static () => new Rect2();   // zero insets -> panel width == viewport width
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var card = FindInfoCard(host);
        if (card == null)
        {
            Check(false, "vs2 width: info card missing");
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        var margin = PanelMargin(card);
        var panelWidth = card.Size.X;
        var available = panelWidth - margin.X * 2f;
        var hostScroll = FindDirectScroll(card);
        var vbar = hostScroll?.GetVScrollBar();
        var vbarVisible = vbar != null && vbar.IsVisibleInTree();
        var vbarWidth = vbarVisible && vbar != null ? vbar.Size.X : 0f;
        var usable = available - vbarWidth;

        var contentRoot = hostScroll != null && hostScroll.GetChildCount() > 0 ? hostScroll.GetChild(0) as Control : null;
        var rootMin = contentRoot?.GetCombinedMinimumSize() ?? Vector2.Zero;
        var rootSize = contentRoot?.Size ?? Vector2.Zero;
        // The viewer's own VBox is the intrinsic content: its combined minimum is the width the
        // action rows / body actually need, independent of the slimmer outer Control wrapper.
        var column = contentRoot != null && contentRoot.GetChildCount() > 0 ? contentRoot.GetChild(0) as Control : null;
        var columnMin = column?.GetCombinedMinimumSize() ?? Vector2.Zero;
        var columnSize = column?.Size ?? Vector2.Zero;

        var grid = contentRoot == null ? null : FindDescendant<GridContainer>(contentRoot);
        var gridMin = grid?.GetCombinedMinimumSize() ?? Vector2.Zero;
        var gridSize = grid?.Size ?? Vector2.Zero;
        var tabContainer = contentRoot == null ? null : FindDescendant<TabContainer>(contentRoot);
        var tabCount = tabContainer?.GetTabCount() ?? 0;
        var tabTitles = new List<string>();
        for (var i = 0; i < tabCount; i++)
            tabTitles.Add(tabContainer!.GetTabTitle(i));

        // The table's own ScrollContainer is the surface that actually scrolls horizontally.
        var tabScroll = grid?.GetParent() as ScrollContainer;
        var tabHBar = tabScroll?.GetHScrollBar();
        var tabMax = (float)(tabHBar?.MaxValue ?? 0.0);
        var tabPage = (float)(tabHBar?.Page ?? 0.0);
        var tabMaxOffset = Mathf.Max(0f, tabMax - tabPage);

        // Action row 2 carries the widest real labels: statusRow=0, actionRow1=1, actionRow2=2.
        var actionRow2 = column != null && column.GetChildCount() > 2 ? column.GetChild(2) as HBoxContainer : null;
        var rowParts = new List<string>();
        var rowTotal = 0f;
        var rowSep = 0;
        if (actionRow2 != null)
        {
            rowSep = actionRow2.GetThemeConstant("separation");
            for (var i = 0; i < actionRow2.GetChildCount(); i++)
            {
                if (actionRow2.GetChild(i) is not Control child)
                    continue;

                var text = child switch
                {
                    CheckBox check => check.Text,
                    Button button => button.Text,
                    _ => child.GetClass(),
                };
                rowParts.Add($"{text}={Round(child.Size.X)}px");
                rowTotal += child.Size.X;
            }

            rowTotal += rowSep * Mathf.Max(0, actionRow2.GetChildCount() - 1);
        }

        var labels = new List<string>();
        foreach (var control in WalkControls(contentRoot ?? card))
        {
            var text = control switch
            {
                CheckBox check when !string.IsNullOrEmpty(check.Text) => check.Text,
                Button button when !string.IsNullOrEmpty(button.Text) => button.Text,
                _ => string.Empty,
            };
            if (text.Length > 0)
                labels.Add($"{text}={Round(control.Size.X)}px");
        }

        report.facts.Add(new Vs2Fact
        {
            name = "width.panel",
            value = $"panelWidth={Round(panelWidth)} marginX={Round(margin.X)} available={Round(available)} vbarVisible={vbarVisible} vbarWidth={Round(vbarWidth)} usable={Round(usable)}"
        });
        report.facts.Add(new Vs2Fact
        {
            name = "width.content",
            value = $"hostScrollSize={Fmt(hostScroll?.Size ?? Vector2.Zero)} contentRootSize={Fmt(rootSize)} contentRootMin={Fmt(rootMin)} columnSize={Fmt(columnSize)} columnMin={Fmt(columnMin)}"
        });
        report.facts.Add(new Vs2Fact
        {
            name = "width.grid",
            value = $"gridSize={Fmt(gridSize)} gridMin={Fmt(gridMin)} gridColumns={grid?.Columns} gridCells={grid?.GetChildCount()} tabs={tabCount} tabTitles=[{string.Join(", ", tabTitles)}]"
        });
        report.facts.Add(new Vs2Fact
        {
            name = "width.action-row2",
            value = $"sep={rowSep} parts=[{string.Join(", ", rowParts)}] total={Round(rowTotal)} minusUsable={Round(rowTotal - usable)}"
        });
        report.facts.Add(new Vs2Fact
        {
            name = "width.labels",
            value = string.Join(" | ", labels)
        });
        report.facts.Add(new Vs2Fact
        {
            name = "width.tab-scroll",
            value = $"tabScrollSize={Fmt(tabScroll?.Size ?? Vector2.Zero)} tabMax={Round(tabMax)} tabPage={Round(tabPage)} tabMaxOffset={Round(tabMaxOffset)}"
        });

        Check(Mathf.Abs(panelWidth - 1080f) < 0.5f, $"vs2 width: zero-inset phone panel width is 1080 (got {Round(panelWidth)})");
        Check(Close(margin.X, 24f), $"vs2 width: panel content margin 24 at ×2 (got {Round(margin.X)})");
        Check(Close(available, 1032f), $"vs2 width: available content width 1032 (got {Round(available)})");
        Check(columnMin.X > usable && rowTotal > usable,
            $"vs2 width: intrinsic content {Round(columnMin.X)} / action row {Round(rowTotal)} exceeds usable {Round(usable)} -> the horizontal overflow is real, not a usability pass");
        Check(gridMin.X > usable, $"vs2 width: grid minimum {Round(gridMin.X)} exceeds usable {Round(usable)}");
        Check(tabMaxOffset > 0f, $"vs2 width: the table's horizontal scroll range is positive (got {Round(tabMaxOffset)})");

        // Programmatic horizontal reachability at settled layout (the table's own scroll surface).
        if (tabScroll != null && tabMaxOffset > 0f)
        {
            tabScroll.ScrollHorizontal = (int)Mathf.Ceil(tabMaxOffset);
            await SettleAsync();
            var scrollRect = tabScroll.GetGlobalRect();
            var columns = Mathf.Max(1, grid?.Columns ?? 1);
            var lastHeaderCell = grid != null && grid.GetChildCount() >= columns ? grid.GetChild(columns - 1) as Control : null;
            var lastRect = lastHeaderCell?.GetGlobalRect() ?? new Rect2();
            var gridRect = grid?.GetGlobalRect() ?? new Rect2();

            report.facts.Add(new Vs2Fact
            {
                name = "width.reach",
                value = $"scrolledTo={tabScroll.ScrollHorizontal} scrollRect={Fmt(scrollRect)} gridRect={Fmt(gridRect)} lastHeaderCellRect={Fmt(lastRect)} intersects={scrollRect.Intersects(lastRect)}"
            });

            Check(lastHeaderCell != null && scrollRect.Intersects(lastRect),
                "vs2 width: scrolling to max reaches the last header cell");
            Check(Mathf.Abs(gridRect.End.X - scrollRect.End.X) < 1.5f,
                $"vs2 width: scrolled to max reveals the table right edge ({Round(gridRect.End.X)} vs {Round(scrollRect.End.X)})");
            tabScroll.ScrollHorizontal = 0;
        }

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: phone resize + safe-area-only change -----------------------

    private async Task AssertPhoneResizeAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var infoBuilds = 0;
        var viewport = NewSurface(1080, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);
        var portrait = new Rect2(0f, 100f, 1080f, 2400f);
        host.SafeAreaReader = () => portrait;
        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", () => { infoBuilds++; return InfoDebugWindow.Build(); });
        DebugWindowRegistry.Register(Vs2CommandsWindowName, "vs2 commands", CommandExplorerWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleAsync();
        DebugWindowRegistry.Open(Vs2CommandsWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var cardTop = FindCommandsCard(host);
        var cardHidden = FindInfoCard(host);
        if (cardTop == null || cardHidden == null)
        {
            Check(false, "vs2 resize: two cards required for the resize fixture");
            TeardownSurface(viewport, host);
            await SettleAsync();
            return;
        }

        var idTop = cardTop.GetInstanceId();
        var idHidden = cardHidden.GetInstanceId();
        Check(CountVisibleCards(host) == 1 && cardTop.Visible, "vs2 resize: one visible card in portrait");
        Check(RectClose(RectOf(cardHidden), portrait), $"vs2 resize: hidden card portrait rect {Fmt(portrait)} (got {Fmt(RectOf(cardHidden))})");

        // Viewport resize portrait -> landscape with a changed injected safe rect.
        var landscape = new Rect2(100f, 0f, 2360f, 1080f);
        viewport.Size = new Vector2I(2560, 1080);
        host.SafeAreaReader = () => landscape;
        await SettleStableAsync(() => SafeMeasure(host));

        report.facts.Add(new Vs2Fact
        {
            name = "resize.landscape",
            value = $"visible={Fmt(viewport.GetVisibleRect())} top={Fmt(RectOf(cardTop))} hidden={Fmt(RectOf(cardHidden))} expected={Fmt(landscape)}"
        });

        Check(RectClose(RectOf(cardTop), landscape), $"vs2 resize: top card follows the landscape safe rect (got {Fmt(RectOf(cardTop))})");
        Check(RectClose(RectOf(cardHidden), landscape), $"vs2 resize: hidden card follows the landscape safe rect (got {Fmt(RectOf(cardHidden))})");
        Check(cardTop.GetInstanceId() == idTop && cardHidden.GetInstanceId() == idHidden, "vs2 resize: no card was rebuilt by the resize");
        Check(infoBuilds == 1, $"vs2 resize: info factory still invoked once (got {infoBuilds})");
        Check(CountVisibleCards(host) == 1 && FindCommandsCard(host)!.Visible, "vs2 resize: exactly one visible card after the resize");

        // Safe-area-only change, no viewport resize at all.
        var changed = new Rect2(100f, 120f, 2360f, 800f);
        host.SafeAreaReader = () => changed;
        await SettleStableAsync(() => SafeMeasure(host));

        report.facts.Add(new Vs2Fact
        {
            name = "resize.safearea-only",
            value = $"top={Fmt(RectOf(cardTop))} hidden={Fmt(RectOf(cardHidden))} expected={Fmt(changed)}"
        });

        Check(RectClose(RectOf(cardTop), changed), $"vs2 resize: safe-area-only change repositioned the top card (got {Fmt(RectOf(cardTop))})");
        Check(RectClose(RectOf(cardHidden), changed), $"vs2 resize: safe-area-only change repositioned the hidden card (got {Fmt(RectOf(cardHidden))})");
        Check(infoBuilds == 1, "vs2 resize: safe-area-only change rebuilt no content");

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2: floating ×2 host branch (branch injection, not tablet) -----

    private async Task AssertTabletFloatingAsync(Vs2Report report)
    {
        ProjectSettings.SetSetting(DebugUiScale.SettingKey, DebugUiScale.ModeMobile);

        var viewport = NewSurface(1600, 2560);
        var host = new DebugWindowHost();
        viewport.AddChild(host);

        // Branch injection: mobile density is selected, then the private phone field is forced false
        // BEFORE any window opens, so the floating ×2 host branch is exercised in isolation. This is
        // NOT native tablet detection, and real native tablet behaviour stays a human hold (H-TABLET).
        var field = typeof(DebugWindowHost).GetField("_isPhone", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, "vs2 tablet: private _isPhone field reachable for branch injection");
        field?.SetValue(host, false);
        report.facts.Add(new Vs2Fact
        {
            name = "tablet.branch-injection",
            value = $"debug/ui_scale_mode=mobile, _isPhone forced false via reflection; factor={DebugUiScale.Factor}"
        });

        DebugWindowRegistry.Register(Vs2InfoWindowName, "vs2 info", InfoDebugWindow.Build);
        DebugWindowRegistry.Register(Vs2CommandsWindowName, "vs2 commands", CommandExplorerWindow.Build);
        DebugWindowRegistry.Open(Vs2InfoWindowName);
        await SettleAsync();
        DebugWindowRegistry.Open(Vs2CommandsWindowName);
        await SettleStableAsync(() => SafeMeasure(host));

        var cards = Cards(host);
        Check(cards.Count == 2, $"vs2 tablet: two floating cards (got {cards.Count})");
        Check(DebugUiScale.Factor == 2, $"vs2 tablet: mobile density factor 2 (got {DebugUiScale.Factor})");
        if (cards.Count == 2)
        {
            var first = cards[0];
            var second = cards[1];
            var desired = new Vector2(
                Mathf.Min(1600f * 0.92f, DebugUiScale.S(1600f)),
                Mathf.Min(2560f * 0.65f, DebugUiScale.S(2000f)));
            var delta = second.Position - first.Position;

            report.facts.Add(new Vs2Fact
            {
                name = "tablet.float",
                value = $"card0={Fmt(RectOf(first))} card1={Fmt(RectOf(second))} desired={Fmt(desired)} cascadeDelta={Fmt(delta)} expectedCascade={DebugUiScale.S(32f)}"
            });
            RecordShape(report, "tablet.card0", first);
            RecordShape(report, "tablet.card1", second);

            Check(VecClose(first.Size, desired) && VecClose(second.Size, desired),
                $"vs2 tablet: floating desired size preserved (got {Fmt(first.Size)} / {Fmt(second.Size)} vs {Fmt(desired)})");
            Check(delta != Vector2.Zero, "vs2 tablet: cards are not stacked at one full-rect position");
            Check(Close(delta.X, DebugUiScale.S(32f)) && Close(delta.Y, DebugUiScale.S(32f)),
                $"vs2 tablet: cascade offset S(32) applied (delta {Fmt(delta)})");

            var header = FindHeader(second);
            var before = second.Position;
            if (header != null)
                EmitHeaderDrag(header, new Vector2(-120f, -80f));
            await SettleAsync();
            Check(second.Position != before,
                $"vs2 tablet: legacy header drag still moves a floating card (before {Fmt(before)} after {Fmt(second.Position)})");
            Check(VecClose(second.Size, desired), "vs2 tablet: dragging did not resize the card");

            // The injected non-phone branch applies no explicit filters, so these chains are the
            // host's untouched default (evidence for "absent filter" semantics vs. the phone Stop).
            AuditFilterChain(report, "tablet-default-card-close", FindHeaderButton(second));
            AuditFilterChain(report, "tablet-default-card-grid-cell",
                FindDescendant<GridContainer>(first)?.GetChild(0) as Control);
        }

        TeardownSurface(viewport, host);
        await SettleAsync();
    }

    // ---- vs-2 helpers -----------------------------------------------------

    private SubViewport NewSurface(int width, int height)
    {
        var viewport = new SubViewport { Size = new Vector2I(width, height), Disable3D = true };
        AddChild(viewport);
        return viewport;
    }

    private void TeardownSurface(Node viewport, DebugWindowHost? host)
    {
        // Release the synthetic reader closure before freeing the host (no owned reference survives
        // the fixture), then detach + free the whole surface.
        if (host != null)
            host.SafeAreaReader = null;
        FreeSurface(viewport);
    }

    private async Task SettleStableAsync(Func<Vector2> measure)
    {
        await SettleAsync();
        for (var i = 0; i < Vs2StableFrames; i++)
        {
            var before = measure();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var after = measure();
            if (before == after)
            {
                Check(true, $"vs2 layout stable at {Fmt(before)}");
                return;
            }
        }

        Check(false, $"vs2 layout did not stabilise in {Vs2StableFrames} frames (last {Fmt(measure())})");
    }

    private void EmitHeaderDrag(HBoxContainer header, Vector2 relative)
    {
        header.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = Vector2.Zero
        });
        header.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = relative });
    }

    private static void AuditFilterChain(Vs2Report report, string label, Control? leaf)
    {
        var chain = new Vs2FilterChain { label = label };
        var node = leaf;
        var depth = 0;
        while (node != null && depth < 64)
        {
            depth++;
            chain.entries.Add(new Vs2FilterEntry
            {
                depth = depth,
                cls = node.GetClass(),
                name = node.Name,
                mouseFilter = node.MouseFilter.ToString(),
                visible = node.IsVisibleInTree()
            });
            node = node.GetParent() as Control;
        }

        var parent = leaf?.GetParent();
        while (parent != null && parent is not CanvasLayer)
            parent = parent.GetParent();
        chain.containerLayer = parent?.GetClass() ?? "(none)";

        report.filters.Add(chain);
    }

    private static void RecordShape(Vs2Report report, string label, Control? control)
    {
        if (control == null)
            return;

        var min = control.GetCombinedMinimumSize();
        report.shapes.Add(new Vs2Shape
        {
            label = label,
            cls = control.GetClass(),
            x = Round(control.Position.X),
            y = Round(control.Position.Y),
            w = Round(control.Size.X),
            h = Round(control.Size.Y),
            minW = Round(min.X),
            minH = Round(min.Y),
            visible = control.IsVisibleInTree(),
            insideTree = control.IsInsideTree(),
            text = control switch
            {
                Button button => button.Text,
                Label textLabel => textLabel.Text,
                _ => string.Empty,
            }
        });
    }

    private static Vector2 SafeMeasure(Node host)
    {
        var card = FirstCard(host);
        return card == null ? Vector2.Zero : card.Size;
    }

    private static PanelContainer? FirstCard(Node host)
    {
        foreach (var child in host.GetChildren())
        {
            if (child is PanelContainer panel)
                return panel;
        }

        return null;
    }

    private static List<PanelContainer> Cards(Node host) => host.GetChildren().OfType<PanelContainer>().ToList();

    private static int CountVisibleCards(Node host) => Cards(host).Count(panel => panel.Visible);

    private static Rect2 RectOf(Control control) => new(control.Position, control.Size);

    private static HBoxContainer? FindHeader(PanelContainer card)
    {
        foreach (var child in card.GetChildren())
        {
            if (child is not VBoxContainer layout)
                continue;
            foreach (var inner in layout.GetChildren())
            {
                if (inner is HBoxContainer header)
                    return header;
            }
        }

        return null;
    }

    private static Label? FindHeaderTitle(PanelContainer card)
    {
        var header = FindHeader(card);
        if (header == null)
            return null;

        foreach (var child in header.GetChildren())
        {
            if (child is Label label)
                return label;
        }

        return null;
    }

    private static ScrollContainer? FindDirectScroll(PanelContainer card)
    {
        foreach (var child in card.GetChildren())
        {
            if (child is not VBoxContainer layout)
                continue;
            foreach (var inner in layout.GetChildren())
            {
                if (inner is ScrollContainer scroll)
                    return scroll;
            }
        }

        return null;
    }

    private static Vector2 PanelMargin(PanelContainer panel)
        => panel.GetThemeStylebox("panel") is StyleBoxFlat flat
            ? new Vector2(flat.ContentMarginLeft, flat.ContentMarginTop)
            : Vector2.Zero;

    private static PanelContainer? FindCommandsCard(Node host)
    {
        foreach (var child in host.GetChildren())
        {
            if (child is PanelContainer panel && HasDescendant<Tree>(panel))
                return panel;
        }

        return null;
    }

    private static bool SelectInfoRow(PanelContainer card)
    {
        var grid = FindDescendant<GridContainer>(card);
        if (grid == null || grid.Columns <= 0 || grid.GetChildCount() <= grid.Columns)
            return false;

        if (grid.GetChild(grid.Columns) is not Control selectCell)
            return false;

        var button = selectCell switch
        {
            Button direct => direct,
            HBoxContainer row => row.GetChildren().OfType<Button>().FirstOrDefault(),
            _ => null,
        };

        if (button == null)
            return false;

        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private static T? FindDescendant<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T typed)
                return typed;

            var found = FindDescendant<T>(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Button? FindButton(Node root, string text)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button && button.Text == text)
                return button;

            var found = FindButton(child, text);
            if (found != null)
                return found;
        }

        return null;
    }

    private static IEnumerable<Control> WalkControls(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control)
                yield return control;

            foreach (var descendant in WalkControls(child))
                yield return descendant;
        }
    }

    private static string Fmt(Vector2 value) => $"({Round(value.X)},{Round(value.Y)})";

    private static string Fmt(Rect2 value) => $"({Round(value.Position.X)},{Round(value.Position.Y)} {Round(value.Size.X)}x{Round(value.Size.Y)})";

    private static string Fmt(Transform2D value) => $"{{o={Fmt(value.Origin)},x={Fmt(value.X)},y={Fmt(value.Y)}}}";

    private static bool RectClose(Rect2 left, Rect2 right)
        => Mathf.Abs(left.Position.X - right.Position.X) <= 0.5f
           && Mathf.Abs(left.Position.Y - right.Position.Y) <= 0.5f
           && Mathf.Abs(left.Size.X - right.Size.X) <= 0.5f
           && Mathf.Abs(left.Size.Y - right.Size.Y) <= 0.5f;

    private static bool VecClose(Vector2 left, Vector2 right)
        => Mathf.Abs(left.X - right.X) <= 0.5f && Mathf.Abs(left.Y - right.Y) <= 0.5f;

    private sealed class Vs2Report
    {
        public string probe { get; set; } = "debug-ui-mobile vs-2";
        public List<Vs2Fact> facts { get; } = new();
        public List<Vs2Shape> shapes { get; } = new();
        public List<Vs2FilterChain> filters { get; } = new();
    }

    private sealed class Vs2Fact
    {
        public string name { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
    }

    private sealed class Vs2Shape
    {
        public string label { get; set; } = string.Empty;
        public string cls { get; set; } = string.Empty;
        public float x { get; set; }
        public float y { get; set; }
        public float w { get; set; }
        public float h { get; set; }
        public float minW { get; set; }
        public float minH { get; set; }
        public bool visible { get; set; }
        public bool insideTree { get; set; }
        public string text { get; set; } = string.Empty;
    }

    private sealed class Vs2FilterChain
    {
        public string label { get; set; } = string.Empty;
        public string containerLayer { get; set; } = string.Empty;
        public List<Vs2FilterEntry> entries { get; } = new();
    }

    private sealed class Vs2FilterEntry
    {
        public int depth { get; set; }
        public string cls { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string mouseFilter { get; set; } = string.Empty;
        public bool visible { get; set; }
    }

    // ---- fixtures: info --------------------------------------------------

    private sealed class ProbeRow
    {
        [InfoKey] public string id { get; set; } = string.Empty;
        public int count { get; set; }
        public bool flag { get; set; }
    }

    private sealed class ProbeInfoManager : IInfoManager
    {
        private readonly List<object> _rows;

        public ProbeInfoManager(params object[] rows) => _rows = new List<object>(rows);

        public Type infoType => typeof(ProbeRow);

        public IEnumerable<object> GetAllValue() => _rows;

        public void LoadInfo()
        {
        }

        public Task LoadInfoAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool Reload() => false;

        public bool Revert() => false;

        public event Action? Reloaded
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    // ---- fixtures: commands ----------------------------------------------

    private sealed class ProbeHandler : ICommandHandler
    {
        public bool Execute(Parameters param) => true;

        public IReadOnlyList<CommandArg> Arguments { get; } = new[]
        {
            new CommandArg("-id", ArgType.String, "identity", required: true),
            new CommandArg("-count", ArgType.Int, "count"),
        };
    }
}
#endif
