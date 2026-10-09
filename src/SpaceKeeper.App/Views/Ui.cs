// ======================================================================
// Ui.cs — small helper functions used inside the XAML
// ======================================================================
// {x:Bind} can call static functions, e.g.
//     Visibility="{x:Bind views:Ui.CollapsedIf(IsCurrent), Mode=OneWay}"
// These turn true/false values into things the screen needs (visibility,
// colours, borders). Used by PanelWindow.xaml.
// ======================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SpaceKeeper.App.Services;
using SpaceKeeper.Core;

namespace SpaceKeeper.App.Views;

public static class Ui
{
    public static bool Not(bool value) => !value;

    /// <summary>Hidden when true, shown when false.</summary>
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Pin icon colour. The icon's SHAPE also changes (pin / filled pin /
    /// crossed-out pin), so the meaning never depends on colour alone.
    /// On a coloured tile it uses the tile's text colour, to keep 7:1 contrast.
    /// </summary>
    public static Brush PinBrush(bool pinned, bool outOfOrder, string? colorId) =>
        Effective(colorId) is { } color ? Plain(color.UsesDarkText)
        : Theme(outOfOrder ? "SystemFillColorCautionBrush" : pinned ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

    /// <summary>Number colour inside the badge: white on the accent circle, the tile's text colour otherwise.</summary>
    public static Brush BadgeText(bool isCurrent, string? colorId) =>
        isCurrent ? Theme("TextOnAccentFillColorPrimaryBrush")
        : Effective(colorId) is { } color ? Plain(color.UsesDarkText) : Theme("TextFillColorPrimaryBrush");

    // ----- Desktop colours (SpaceKeeper.Core/DesktopColors.cs) -----

    /// <summary>
    /// The colour actually used for a tile. With a Windows CONTRAST THEME on,
    /// tiles ignore their colours and use the theme's own colours instead —
    /// the person has chosen those colours for a reason.
    /// </summary>
    private static DesktopColor? Effective(string? colorId) =>
        AccessibilityInfo.HighContrast ? null : DesktopColors.Find(colorId);

    /// <summary>Tile background: its colour, or the normal card background.</summary>
    public static Brush TileFill(string? colorId) =>
        Effective(colorId) is { } color
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, color.R, color.G, color.B))
            : Theme("CardBackgroundFillColorDefaultBrush");

    /// <summary>
    /// Text on a tile: black or white on a coloured tile (always at least 7:1
    /// contrast), otherwise the normal text colour (secondary for the subtitle).
    /// </summary>
    public static Brush TileText(string? colorId, bool secondary) =>
        Effective(colorId) is { } color ? Plain(color.UsesDarkText)
        : Theme(secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");

    /// <summary>Is this the tile's colour? (Ticks the right item in the Colour menu.)</summary>
    public static bool IsColor(string? colorId, string id) => colorId == id;

    /// <summary>True when the tile has no colour (the "None" item in the Colour menu).</summary>
    public static bool HasNoColor(string? colorId) => DesktopColors.Find(colorId) is null;

    private static Brush Plain(bool black) =>
        new SolidColorBrush(black ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);

    /// <summary>Tile border: the accent colour for the current desktop, a faint line otherwise.</summary>
    public static Brush TileBorder(bool isCurrent) =>
        Theme(isCurrent ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");

    /// <summary>
    /// Tile border thickness: 3 for the current desktop, 1 otherwise — so the
    /// current tile stands out by shape too, not just colour.
    /// </summary>
    public static Thickness TileBorderThickness(bool isCurrent) => new(isCurrent ? 3 : 1);

    /// <summary>Looks up a Windows 11 theme colour (these adapt to light, dark and contrast themes).</summary>
    private static Brush Theme(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
