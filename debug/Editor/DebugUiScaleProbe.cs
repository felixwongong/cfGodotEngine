#if TOOLS
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
