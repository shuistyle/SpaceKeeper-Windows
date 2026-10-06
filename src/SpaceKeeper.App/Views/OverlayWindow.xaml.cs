// ======================================================================
// OverlayWindow.xaml.cs — the corner label and the switch banner
// ======================================================================
// Both are tiny always-on-top windows that:
//   • let mouse clicks pass straight through to whatever is underneath,
//   • never take keyboard focus,
//   • don't appear in Alt+Tab or the taskbar,
//   • appear on EVERY virtual desktop (so the label is always visible).
//
// The LABEL shows the current desktop's name in a chosen corner.
// The BANNER shows the name in the lower middle of the screen for a moment
// after you switch desktop.
//
// Created by App.xaml.cs, which calls ShowLabel / ShowBanner / HideOverlay.
// Accessibility: hidden from screen readers (the panel announces the same
// information); text grows with Settings › Accessibility › Text size;
// solid background (no transparency); no animation.
// ======================================================================

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using SpaceKeeper.App.Services;
using SpaceKeeper.Core;
using Windows.Foundation;
using Windows.Graphics;

namespace SpaceKeeper.App.Views;

public sealed partial class OverlayWindow : Window
{
    private readonly VirtualDesktopService _desktops;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _pinnedToAllDesktops;

    private OverlayWindow(VirtualDesktopService desktops)
    {
        _desktops = desktops;
        InitializeComponent();
        WindowHelpers.MakeBorderless(this, alwaysOnTop: true);
        WindowHelpers.MakeClickThrough(this, opacity: 1.0);
        AutomationProperties.SetAccessibilityView(Box, AccessibilityView.Raw);

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideOverlay();
    }

    public static OverlayWindow CreateLabel(VirtualDesktopService desktops) => new(desktops);
    public static OverlayWindow CreateBanner(VirtualDesktopService desktops) => new(desktops);

    /// <summary>Shows the desktop name in a corner of the main screen.</summary>
    public void ShowLabel(string name, double points, LabelCorner corner, double opacity)
    {
        SetText(name, null, points);
        WindowHelpers.MakeClickThrough(this, opacity);
        var size = MeasureInPixels();
        var area = DisplayArea.Primary.WorkArea;
        var margin = (int)(12 * WindowHelpers.Scale(this));
        var left = corner is LabelCorner.TopLeft or LabelCorner.BottomLeft;
        var top = corner is LabelCorner.TopLeft or LabelCorner.TopRight;
        var x = left ? area.X + margin : area.X + area.Width - size.Width - margin;
        var y = top ? area.Y + margin : area.Y + area.Height - size.Height - margin;
        ShowAt(new RectInt32(x, y, size.Width, size.Height));
    }

    /// <summary>Shows the switch banner for a moment, centred low on the main screen.</summary>
    public void ShowBanner(string title, string? subtitle, double points)
    {
        SetText(title, subtitle, points);
        var size = MeasureInPixels();
        var area = DisplayArea.Primary.WorkArea;
        ShowAt(new RectInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (int)(area.Height * 0.78) - size.Height / 2,
            size.Width,
            size.Height));

        // Stay longer when Windows animations are off (often chosen by people
        // who need more time to read).
        _hideTimer.Interval = AccessibilityInfo.AnimationsEnabled ? TimeSpan.FromSeconds(1.6) : TimeSpan.FromSeconds(2.5);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideOverlay() => AppWindow.Hide();

    private void SetText(string title, string? subtitle, double points)
    {
        TitleText.Text = title;
        TitleText.FontSize = points;
        SubtitleText.Text = subtitle ?? "";
        SubtitleText.FontSize = Math.Max(12, points * 0.45);
        SubtitleText.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        Box.CornerRadius = new CornerRadius(points * 0.4);
    }

    /// <summary>Works out how big the text box needs to be, in physical screen pixels.</summary>
    private SizeInt32 MeasureInPixels()
    {
        Box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = Box.DesiredSize;
        var scale = WindowHelpers.Scale(this);
        // Fallback estimate in case layout hasn't run yet.
        var width = desired.Width > 1 ? desired.Width : TitleText.Text.Length * TitleText.FontSize * 0.6 + 40;
        var height = desired.Height > 1 ? desired.Height : TitleText.FontSize * 1.6 + 20;
        return new SizeInt32((int)Math.Ceiling(width * scale) + 2, (int)Math.Ceiling(height * scale) + 2);
    }

    private void ShowAt(RectInt32 rect)
    {
        AppWindow.MoveAndResize(rect);
        AppWindow.Show(activateWindow: false);
        if (!_pinnedToAllDesktops)
        {
            // Must happen after the window is first shown.
            _desktops.ShowOnAllDesktops(WindowHelpers.Handle(this));
            _pinnedToAllDesktops = true;
        }
    }
}
