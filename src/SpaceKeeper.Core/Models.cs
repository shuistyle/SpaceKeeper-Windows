// ======================================================================
// Models.cs — the plain data types the whole app shares
// ======================================================================
// "Models" are simple containers of information. They don't talk to
// Windows; other parts of the app create and read them.
//
//   DesktopInfo  – one Windows virtual desktop as Windows reports it now.
//                  Built by VirtualDesktopService (SpaceKeeper.App/Services).
//   PinAlert     – a warning produced by PinEvaluator when a pinned
//                  desktop is out of order. Shown in the panel.
//   AppSettings  – every switch and choice in the Settings section.
//   SavedState   – everything SpaceKeeper saves to disk (see StateStore).
//
// C# words you'll see:
//   record   a small data type compared by its values (two records with
//            the same values are "equal")
//   enum     a fixed list of choices
//   Guid     a globally unique ID (Windows gives every desktop one)
// ======================================================================

namespace SpaceKeeper.Core;

/// <summary>
/// One virtual desktop. Unlike macOS, Windows 11 stores desktop names
/// itself, so <see cref="Name"/> comes straight from Windows.
/// </summary>
/// <param name="Id">Windows' permanent ID for the desktop (survives restarts).</param>
/// <param name="Name">The name set in Windows; empty if never renamed.</param>
/// <param name="Index">1-based position in Task View (1 = leftmost).</param>
/// <param name="IsCurrent">True for the desktop you're looking at.</param>
public sealed record DesktopInfo(Guid Id, string Name, int Index, bool IsCurrent)
{
    /// <summary>What Windows shows when a desktop has no name: "Desktop 3".</summary>
    public string DefaultName => $"Desktop {Index}";

    /// <summary>The name to show: your name if set, otherwise the default.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;
}

/// <summary>What's wrong with a pinned desktop.</summary>
public enum PinProblem
{
    /// <summary>It's no longer in its pinned order relative to other pinned desktops.</summary>
    OutOfOrder,
    /// <summary>It has been closed.</summary>
    Missing,
}

/// <summary>
/// A warning about one pinned desktop. <see cref="ShouldFollow"/> and
/// <see cref="ShouldPrecede"/> name the pinned desktops it belongs between.
/// </summary>
public sealed record PinAlert(Guid Id, string Name, PinProblem Problem, string? ShouldFollow, string? ShouldPrecede)
{
    public string Title => Problem == PinProblem.Missing
        ? $"“{Name}” is missing"
        : $"“{Name}” is out of order";

    public string Message => (Problem, ShouldFollow, ShouldPrecede) switch
    {
        (PinProblem.Missing, _, _) => "This pinned desktop has been closed.",
        (_, { } after, { } before) => $"It should sit after “{after}” and before “{before}”.",
        (_, { } after, null) => $"It should sit after “{after}”.",
        (_, null, { } before) => $"It should sit before “{before}”.",
        _ => "It's no longer in its pinned order.",
    };
}

/// <summary>Which screen corner the desktop name label sits in.</summary>
public enum LabelCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Accessibility: text size for the on-screen label and switch banner.</summary>
public enum OverlayTextSize { Standard, Large, ExtraLarge }

/// <summary>
/// Accessibility: how big the desktop tiles (and their text) are in the
/// panel. Changed in Settings, with the A−/A+ buttons, or Ctrl+Plus/Minus/0.
/// Works on top of Windows' own Settings › Accessibility › Text size.
/// </summary>
public enum PanelTextSize { Standard, Large, ExtraLarge, Largest }

/// <summary>Helpers for <see cref="PanelTextSize"/> (an "extension" adds methods to the enum).</summary>
public static class PanelTextSizeExtensions
{
    /// <summary>How much bigger than standard: 1.0, 1.3, 1.6 or 2.0 (same as the Mac version).</summary>
    public static double Scale(this PanelTextSize size) => size switch
    {
        PanelTextSize.Large => 1.3,
        PanelTextSize.ExtraLarge => 1.6,
        PanelTextSize.Largest => 2.0,
        _ => 1.0,
    };

    /// <summary>One step bigger (stays at Largest).</summary>
    public static PanelTextSize Bigger(this PanelTextSize size) =>
        size == PanelTextSize.Largest ? size : size + 1;

    /// <summary>One step smaller (stays at Standard).</summary>
    public static PanelTextSize Smaller(this PanelTextSize size) =>
        size == PanelTextSize.Standard ? size : size - 1;
}

/// <summary>
/// Every user setting. New settings get a default value here, so older
/// saved files (which don't mention them) still load correctly.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Show a banner with the desktop's name when you switch.</summary>
    public bool ShowSwitchBanner { get; set; } = true;

    /// <summary>Keep a small name label in a corner of the screen.</summary>
    public bool ShowDesktopLabel { get; set; } = true;

    public LabelCorner LabelCorner { get; set; } = LabelCorner.BottomLeft;

    /// <summary>Label opacity from 0.3 (faint) to 1.0 (solid).</summary>
    public double LabelOpacity { get; set; } = 0.9;

    public OverlayTextSize OverlayTextSize { get; set; } = OverlayTextSize.Standard;

    /// <summary>
    /// Open SpaceKeeper from any app by double-tapping Ctrl, or with Ctrl+Alt+S.
    /// (One switch covers both shortcuts, like the Mac version's fn-S / ⌃⌥S.)
    /// </summary>
    public bool OpenWithHotKey { get; set; } = true;

    /// <summary>Size of the desktop tiles in the panel.</summary>
    public PanelTextSize PanelTextSize { get; set; } = PanelTextSize.Standard;

    /// <summary>Also show a Windows notification when the pinned order changes.</summary>
    public bool NotifyPinMoves { get; set; } = false;

    /// <summary>Point sizes for the overlays at each text size.</summary>
    public (double Banner, double Label) OverlayPoints => OverlayTextSize switch
    {
        OverlayTextSize.Large => (40, 22),
        OverlayTextSize.ExtraLarge => (52, 28),
        _ => (30, 16),
    };
}

/// <summary>Everything SpaceKeeper saves between runs.</summary>
public sealed class SavedState
{
    /// <summary>Pinned desktops → their rank among pinned desktops (0 = leftmost).</summary>
    public Dictionary<Guid, int> Pins { get; set; } = [];

    /// <summary>Your own order for SpaceKeeper's list (desktop IDs, top to bottom).</summary>
    public List<Guid> ListOrder { get; set; } = [];

    /// <summary>Last known names, used to describe a pinned desktop after it's closed.</summary>
    public Dictionary<Guid, string> LastKnownNames { get; set; } = [];

    /// <summary>Each desktop's tile colour, by colour ID (see DesktopColors.cs).</summary>
    public Dictionary<Guid, string> DesktopColors { get; set; } = [];

    public AppSettings Settings { get; set; } = new();
}
