using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using cfEngine;
using cfEngine.Command;
using Godot;

namespace cfGodotEngine.Debug;

public partial class CommandConsoleOverlay : CanvasLayer
{
    private const float PollIntervalSeconds = 0.5f;
    private const int MaxLogLines = 1000;
    private const float IdleAlpha = 0.5f;
    private const float DragThresholdPx = 8f;

    private PanelContainer _panel;
    private RichTextLabel _output;
    private LineEdit _input;
    private Button _logButton;
    private readonly List<string> _logLines = new();
    private LogFileTailer _tailer;
    private double _pollAccum;
    private bool _pressed;
    private bool _dragging;
    private float _dragDistance;

    private readonly CommandHistory _history = new();
    private IReadOnlyList<string> _allCommands = Array.Empty<string>();
    private List<string> _completions = new();
    private int _completionIndex;

    public override void _Ready()
    {
        Layer = 128;
        BuildPanel();
        BuildLogButton();
        _panel.Visible = false;

        _allCommands = CommandConsoleCore.DiscoverCommands(CommandService.instance);
        _tailer = new LogFileTailer(ProjectSettings.GlobalizePath("user://logs"));
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        // Poll regardless of panel visibility so opening it shows the session history.
        _pollAccum += delta;
        if (_pollAccum < PollIntervalSeconds)
            return;

        _pollAccum = 0;
        foreach (var line in _tailer.Poll())
        {
            AppendLine(line, ColorFor(LogLineClassifier.Classify(line)));
        }
    }

    private void BuildPanel()
    {
        _panel = new PanelContainer();
        _panel.AnchorLeft = 0f;
        _panel.AnchorTop = 0.5f;
        _panel.AnchorRight = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 0f;
        _panel.OffsetTop = 0f;
        _panel.OffsetRight = 0f;
        _panel.OffsetBottom = 0f;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.85f),
            ContentMarginLeft = 16,
            ContentMarginTop = 12,
            ContentMarginRight = 16,
            ContentMarginBottom = 12
        });
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);

        var header = new HBoxContainer();
        header.AddChild(new Label { Text = "LOG" });
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var copy = new Button { Text = "Copy" };
        copy.Pressed += OnCopyPressed;
        header.AddChild(copy);

        var close = new Button { Text = "✕" };
        close.Pressed += () => SetPanelVisible(false);
        header.AddChild(close);
        vbox.AddChild(header);

        _input = new LineEdit { PlaceholderText = "Enter command..." };
        _input.TextSubmitted += OnTextSubmitted;
        _input.GuiInput += OnInputGuiInput;
        vbox.AddChild(_input);

        _output = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var mono = new SystemFont { FontNames = new[] { "monospace" }, AllowSystemFallback = true };
        _output.AddThemeFontOverride("normal_font", mono);
        _output.AddThemeFontOverride("mono_font", mono);
        _output.AddThemeFontSizeOverride("normal_font_size", 28);
        vbox.AddChild(_output);
    }

    private void BuildLogButton()
    {
        _logButton = new Button
        {
            Text = "LOG",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(120, 72),
            Position = new Vector2(900, 160),
            Modulate = ColorForIdle()
        };
        _logButton.GuiInput += OnLogButtonGuiInput;
        AddChild(_logButton);
    }

    private void OnLogButtonGuiInput(InputEvent @event)
    {
        // Mouse-driven; touch arrives through the engine's emulate_mouse_from_touch (default true).
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _pressed = true;
                _dragging = false;
                _dragDistance = 0f;
            }
            else
            {
                _pressed = false;
                if (_dragging)
                    _logButton.Modulate = ColorForIdle();
                else
                    TogglePanel();
                _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _pressed)
        {
            _dragDistance += mm.Relative.Length();
            if (_dragDistance >= DragThresholdPx)
            {
                _dragging = true;
                _logButton.Modulate = new Color(1, 1, 1, 1f);
            }

            if (_dragging)
                MoveButton(_logButton.Position + mm.Relative);
        }
    }

    private void MoveButton(Vector2 pos)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var size = _logButton.Size;
        pos.X = Mathf.Clamp(pos.X, 0f, vp.X - size.X);
        pos.Y = Mathf.Clamp(pos.Y, 0f, vp.Y - size.Y);
        _logButton.Position = pos;
    }

    private void TogglePanel()
    {
        SetPanelVisible(!_panel.Visible);
    }

    private void SetPanelVisible(bool visible)
    {
        _panel.Visible = visible;
        if (visible)
        {
            _logButton.Modulate = new Color(1, 1, 1, 1f);
            _input.GrabFocus();
        }
        else
        {
            _logButton.Modulate = ColorForIdle();
            _input.ReleaseFocus();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F12)
        {
            TogglePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnTextSubmitted(string text)
    {
        ExecuteCommand(text);
    }

    private void OnInputGuiInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            switch (key.Keycode)
            {
                case Key.Up:
                    var prev = _history.Previous();
                    if (prev != null)
                    {
                        _input.Text = prev;
                        _input.CaretColumn = prev.Length;
                    }
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Down:
                    var next = _history.Next();
                    if (next != null)
                    {
                        _input.Text = next;
                        _input.CaretColumn = next.Length;
                    }
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Tab:
                    CycleCompletion();
                    GetViewport().SetInputAsHandled();
                    break;
            }
        }
    }

    private void CycleCompletion()
    {
        var partial = _input.Text.Trim();
        if (string.IsNullOrEmpty(partial))
            return;

        _completions = CommandConsoleCore.GetCompletions(partial, _allCommands).ToList();
        if (_completions.Count == 0)
            return;

        var currentIndex = _completions.IndexOf(partial);
        if (currentIndex < 0)
            _completionIndex = 0;
        else
            _completionIndex = (currentIndex + 1) % _completions.Count;

        _input.Text = _completions[_completionIndex];
        _input.CaretColumn = _input.Text.Length;
    }

    private void ExecuteCommand(string text)
    {
        var command = text.Trim();
        if (string.IsNullOrEmpty(command))
            return;

        AppendLine($"$ {command}", "gray");
        _history.Push(command);
        _input.Clear();

        CommandConsoleCore.ExecuteAndCapture(
            CommandService.instance,
            command,
            onLog: (msg, level) => AppendLine(msg, ColorFor(level)),
            onEvent: evt =>
            {
                if (!evt.Success)
                {
                    var errorText = evt.Exception != null
                        ? $"{evt.Exception.GetType().Name}: {evt.Exception.Message}"
                        : evt.Message ?? "Unknown error";
                    AppendLine(errorText, "red");
                }
            });
    }

    private void OnCopyPressed()
    {
        DisplayServer.ClipboardSet(string.Join("\n", _logLines));
    }

    private void AppendLine(string plain, string color)
    {
        _output.AppendText($"[color={color}]{EscapeBbcode(plain)}[/color]\n");
        _logLines.Add(plain);

        if (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveAt(0);
            _output.RemoveParagraph(0);
        }
    }

    private static string ColorFor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Error => "red",
            LogLevel.Warn => "yellow",
            _ => "white"
        };
    }

    private static Color ColorForIdle()
    {
        return new Color(1, 1, 1, IdleAlpha);
    }

    private static string EscapeBbcode(string text)
    {
        return text
            .Replace("[", "[lb]")
            .Replace("]", "[rb]");
    }
}
