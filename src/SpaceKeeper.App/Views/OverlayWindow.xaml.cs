// ======================================================================
// OverlayWindow.xaml.cs — the switch banner
// ======================================================================
// A tiny always-on-top window that shows the desktop's name in the lower
// middle of the screen for a moment after you switch desktop, then hides
// itself. While it's showing it:
//   • lets mouse clicks pass straight through to whatever is underneath,
//   • never takes keyboard focus,
//   • doesn't appear in Alt+Tab or the taskbar,
//   • appears on EVERY virtual desktop.
//
// (Earlier versions also kept a permanent name label in a corner of the
// screen; it was removed because it could cover controls in other apps.)
//
// Created by App.xaml.cs, which calls ShowBanner.
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

    public static OverlayWindow CreateBanner(VirtualDesktopService desktops) => new(desktops);

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
