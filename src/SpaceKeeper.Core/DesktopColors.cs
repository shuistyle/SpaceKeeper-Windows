// ======================================================================
// DesktopColors.cs — the colours you can give a desktop's tile
// ======================================================================
// The same ten colours as the Mac version, chosen for people with low
// vision and colour blindness:
//   • Each colour's text (black or white) has a contrast ratio of at least
//     7:1 — the strictest WCAG level (AAA) — so names stay easy to read.
//     (Checked by the ContrastIsAtLeast7To1 test.)
//   • Five light and five dark colours, based on the Okabe–Ito palette that
//     stays distinguishable with the common kinds of colour blindness.
//   • Each colour also has its own SYMBOL (circle, square, triangle…) shown on
//     the tile, and a NAME read by screen readers — never colour alone.
//
// Saved by ID (e.g. "navy") in SavedState.DesktopColors, so a colour this
// version doesn't know is simply ignored.
// Used by the tiles in SpaceKeeper.App (DesktopTileViewModel, Views/Ui.cs).
// ======================================================================

namespace SpaceKeeper.Core;

/// <param name="Id">Saved in the settings file, e.g. "navy".</param>
/// <param name="Name">What people see and screen readers say, e.g. "Navy".</param>
/// <param name="Hex">The fill colour, e.g. "#005A8C".</param>
/// <param name="UsesDarkText">True: black text (light colours). False: white text.</param>
/// <param name="Symbol">A shape character shown on the tile, e.g. "★".</param>
public sealed record DesktopColor(string Id, string Name, string Hex, bool UsesDarkText, string Symbol)
{
    public byte R => Convert.ToByte(Hex.Substring(1, 2), 16);
    public byte G => Convert.ToByte(Hex.Substring(3, 2), 16);
    public byte B => Convert.ToByte(Hex.Substring(5, 2), 16);
}

public static class DesktopColors
{
    public static IReadOnlyList<DesktopColor> All { get; } =
    [
        new("yellow",  "Yellow",   "#F0E442", true,  "●"), // ● circle      contrast 15.9
        new("orange",  "Orange",   "#E69F00", true,  "■"), // ■ square      9.3
        new("skyBlue", "Sky blue", "#56B4E9", true,  "▲"), // ▲ triangle    9.1
        new("mint",    "Mint",     "#4CC9A0", true,  "◆"), // ◆ diamond     10.2
        new("pink",    "Pink",     "#E08FBE", true,  "♥"), // ♥ heart       8.8
        new("navy",    "Navy",     "#005A8C", false, "★"), // ★ star        7.4
        new("plum",    "Plum",     "#8A396B", false, "⬢"), // ⬢ hexagon     7.3
        new("forest",  "Forest",   "#005E45", false, "⬟"), // ⬟ pentagon    7.8
        new("brick",   "Brick",    "#8F3600", false, "♣"), // ♣ club        7.8
        new("slate",   "Slate",    "#3F4A55", false, "✚"), // ✚ cross       9.0
    ];

    /// <summary>The colour with this ID, or null (none chosen, or unknown).</summary>
    public static DesktopColor? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(c => c.Id == id);

    /// <summary>WCAG contrast ratio between two colours (1 to 21).</summary>
    public static double Contrast(DesktopColor color, bool againstBlack)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        return againstBlack ? (luminance + 0.05) / 0.05 : 1.05 / (luminance + 0.05);
    }
}
