#nullable enable
using System;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Shared debug-UI scale seam. The debug windows, info viewer, command explorer and command
/// console are all code-built with desktop-density pixel constants in the 1080×2560
/// <c>canvas_items</c> design space (1 design px ≈ 1 physical px). This class is the single knob
/// that multiplies those constants on touch/phone-class devices (factor ×2) while keeping the
/// desktop experience byte-identical (factor ×1).
///
/// <para>Resolution is deterministic and pure where it matters: <see cref="GetFactorFor"/> and
/// <see cref="IsPhoneFor"/> perform no Godot calls and are the unit-tested contract, while
/// <see cref="Factor"/> / <see cref="IsPhoneMode"/> add the native reads
/// (<see cref="DisplayServer.IsTouchscreenAvailable"/> / <see cref="DisplayServer.ScreenGetSize"/>)
/// and must only be observed on the Godot main thread (UI construction, never a worker thread or
/// static type initialization).</para>
///
/// <para>Mode is read from the <c>debug/ui_scale_mode</c> project setting on every resolution; the
/// native result is deliberately <em>not</em> cached so a test override is observed between fresh
/// builds. Registration seeds the setting with <c>auto</c> + a string enum hint only when the
/// project does not already define it, and never calls <see cref="ProjectSettings.Save"/> (project
/// configuration files are owner-owned).</para>
/// </summary>
public static class DebugUiScale
{
    /// <summary>Project setting holding the scale mode override.</summary>
    public const string SettingKey = "debug/ui_scale_mode";

    /// <summary>Automatic detection (screen size + touch). The default.</summary>
    public const string ModeAuto = "auto";

    /// <summary>Force the desktop experience: factor ×1, never phone.</summary>
    public const string ModeDesktop = "desktop";

    /// <summary>Force the mobile experience: factor ×2, phone even on a desktop.</summary>
    public const string ModeMobile = "mobile";

    /// <summary>Enum hint string shown in the advanced Project Settings inspector.</summary>
    public const string ModeHint = "auto,desktop,mobile";

    /// <summary>
    /// A touch device whose shorter physical screen edge is at or below this many pixels is a
    /// phone (owner-approved breakpoint: shortest physical edge ≤1200 px AND touch available).
    /// </summary>
    public const int PhoneBreakpointPx = 1200;

    private const int DesktopFactor = 1;
    private const int MobileFactor = 2;

    private static bool _registered;

    /// <summary>Current scale factor: 1 on desktop, 2 on touch/phone-class screens.</summary>
    public static int Factor => GetFactorFor(CurrentMode(), HasTouch());

    /// <summary>
    /// True when the automatic size + touch classification resolves to a phone-class screen.
    /// Forces <c>true</c> under the mobile override and <c>false</c> under the desktop override.
    /// </summary>
    public static bool IsPhoneMode
    {
        get
        {
            var size = ScreenSize();
            return IsPhoneFor(CurrentMode(), HasTouch(), size.X, size.Y);
        }
    }

    /// <summary>Scales an integer pixel dimension by <see cref="Factor"/>.</summary>
    public static int S(int px) => px * Factor;

    /// <summary>Scales a float pixel dimension by <see cref="Factor"/>.</summary>
    public static float S(float px) => px * Factor;

    /// <summary>
    /// Pure factor classification — never touches Godot. Missing/unknown modes fall back to
    /// <c>auto</c>: ×2 only when touch is available.
    /// </summary>
    public static int GetFactorFor(string? mode, bool touch)
    {
        return NormalizeMode(mode) switch
        {
            ModeDesktop => DesktopFactor,
            ModeMobile => MobileFactor,
            _ => touch ? MobileFactor : DesktopFactor
        };
    }

    /// <summary>
    /// Pure phone classification — never touches Godot. Desktop forces false, mobile forces true,
    /// and auto requires touch plus a shortest physical edge ≤ <see cref="PhoneBreakpointPx"/>
    /// (rotation-insensitive). Nonpositive screen dimensions never classify as phone.
    /// </summary>
    public static bool IsPhoneFor(string? mode, bool touch, int width, int height)
    {
        switch (NormalizeMode(mode))
        {
            case ModeDesktop:
                return false;
            case ModeMobile:
                return true;
            default:
                if (width <= 0 || height <= 0)
                    return false;
                if (!touch)
                    return false;
                return Math.Min(width, height) <= PhoneBreakpointPx;
        }
    }

    /// <summary>
    /// Registers <see cref="SettingKey"/> with a default of <c>auto</c> and a
    /// <c>auto,desktop,mobile</c> enum hint when the project has not defined it. Idempotent, uses
    /// <see cref="ProjectSettings.SetSetting"/> only when absent, and never saves the project.
    /// Main-thread only. Internal implementation helper — not part of the public scale API.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
            return;

        if (!ProjectSettings.HasSetting(SettingKey))
        {
            ProjectSettings.SetSetting(SettingKey, ModeAuto);
            ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
            {
                { "name", SettingKey },
                { "type", (int)Variant.Type.String },
                { "hint", (int)PropertyHint.Enum },
                { "hint_string", ModeHint }
            });
        }

        _registered = true;
    }

    /// <summary>
    /// At ×2 only, installs an explicit font-size override derived from the theme value already
    /// resolved for <paramref name="control"/>, so fonts that would otherwise be inherited scale
    /// without guessing a desktop default. At ×1 nothing is installed and inherited resolution is
    /// untouched. Main-thread only.
    /// </summary>
    internal static void ScaleInheritedFont(Control control, string themeFontName = "font_size")
    {
        if (control == null || Factor <= DesktopFactor)
            return;

        var resolved = control.GetThemeFontSize(themeFontName);
        if (resolved <= 0)
            return;

        control.AddThemeFontSizeOverride(themeFontName, resolved * Factor);
    }

    /// <summary>
    /// At ×2 only, installs an explicit constant override (e.g. a container's <c>separation</c>)
    /// derived from the theme value already resolved for <paramref name="control"/>. At ×1 nothing
    /// is installed. Main-thread only.
    /// </summary>
    internal static void ScaleInheritedConstant(Control control, string themeConstantName)
    {
        if (control == null || Factor <= DesktopFactor)
            return;

        var resolved = control.GetThemeConstant(themeConstantName);
        if (resolved <= 0)
            return;

        control.AddThemeConstantOverride(themeConstantName, resolved * Factor);
    }

    private static string CurrentMode()
    {
        EnsureRegistered();
        if (!ProjectSettings.HasSetting(SettingKey))
            return ModeAuto;
        return NormalizeMode(ProjectSettings.GetSetting(SettingKey, ModeAuto).AsString());
    }

    private static bool HasTouch() => DisplayServer.IsTouchscreenAvailable();

    private static Vector2I ScreenSize() => DisplayServer.ScreenGetSize();

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return ModeAuto;

        return mode.Trim().ToLowerInvariant() switch
        {
            ModeDesktop => ModeDesktop,
            ModeMobile => ModeMobile,
            _ => ModeAuto
        };
    }
}
