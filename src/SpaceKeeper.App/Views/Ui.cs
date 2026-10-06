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

namespace SpaceKeeper.App.Views;

public static class Ui
{
    public static bool Not(bool value) => !value;

    /// <summary>Hidden when true, shown when false.</summary>
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Pin icon colour. The icon's SHAPE also changes (pin / filled pin /
    /// crossed-out pin), so the meaning never depends on colour alone.
    /// </summary>
    public static Brush PinBrush(bool pinned, bool outOfOrder) =>
        Theme(outOfOrder ? "SystemFillColorCautionBrush" : pinned ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

    /// <summary>Number colour inside the badge: white on the accent circle, normal text otherwise.</summary>
    public static Brush BadgeText(bool isCurrent) =>
        Theme(isCurrent ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");

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
