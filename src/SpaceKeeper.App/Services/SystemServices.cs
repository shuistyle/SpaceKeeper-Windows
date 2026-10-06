// ======================================================================
// SystemServices.cs — small helpers for Windows features
// ======================================================================
//   StartupService      – "Launch at sign-in" (a registry entry)
//   NotificationService – Windows notifications (toasts)
//   AccessibilityInfo   – the user's Windows accessibility settings
//   WindowHelpers       – sizing, rounded corners, click-through windows, bring to front
// Used by MainViewModel, PanelWindow and OverlayWindow.
// ======================================================================

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.UI.ViewManagement;

namespace SpaceKeeper.App.Services;

/// <summary>
/// "Launch at sign-in": Windows starts every program listed under this
/// registry key when you sign in. Only affects the current user.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SpaceKeeper";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>Windows notifications ("toasts") via the Windows App SDK.</summary>
public static class NotificationService
{
    private static bool _registered;

    public static void Show(string title, string message)
    {
        try
        {
            if (!_registered)
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            var notification = new AppNotificationBuilder().AddText(title).AddText(message).BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception)
        {
            // Notifications are optional; never let them crash the app.
        }
    }

    public static void Unregister()
    {
        if (_registered) AppNotificationManager.Default.Unregister();
    }
}

/// <summary>
/// Reads the user's choices in Settings › Accessibility. WinUI already
/// follows most of these automatically (high-contrast themes, text size,
/// animations in standard controls); we use them for our custom overlays.
/// </summary>
public static class AccessibilityInfo
{
    private static readonly UISettings Ui = new();
    private static readonly AccessibilitySettings A11y = new();

    /// <summary>Settings › Accessibility › Visual effects › Animation effects.</summary>
    public static bool AnimationsEnabled => Ui.AnimationsEnabled;

    /// <summary>Settings › Accessibility › Visual effects › Transparency effects.</summary>
    public static bool TransparencyEnabled => Ui.AdvancedEffectsEnabled;

    /// <summary>A contrast theme is on (Settings › Accessibility › Contrast themes).</summary>
    public static bool HighContrast => A11y.HighContrast;

    /// <summary>Settings › Accessibility › Text size (1.0 = 100 %).</summary>
    public static double TextScale => Ui.TextScaleFactor;

    /// <summary>How long notifications should stay (Settings › Accessibility › Visual effects).</summary>
    public static TimeSpan MessageDuration => TimeSpan.FromSeconds(Math.Max(1.5, Ui.MessageDuration));
}

/// <summary>Helpers for window size, position and style.</summary>
public static class WindowHelpers
{
    public static IntPtr Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    /// <summary>Screen pixels per layout unit (1.0 at 100 % scaling, 1.5 at 150 %…).</summary>
    public static double Scale(Window window) => Win32.GetDpiForWindow(Handle(window)) / 96.0;

    /// <summary>
    /// Brings a window to the front and gives it keyboard focus.
    /// Windows normally only lets the app you're using do this (so programs
    /// can't steal your typing). When the panel is opened by double-tapping
    /// Ctrl, the app you're in is still "in charge", so we briefly join its
    /// keyboard queue ("AttachThreadInput"), which Windows allows, then
    /// bring the panel forward and let go again.
    /// </summary>
    public static void BringToFront(Window window)
    {
        var hwnd = Handle(window);
        var foreground = Win32.GetForegroundWindow();
        var ours = Win32.GetCurrentThreadId();
        var theirs = foreground == IntPtr.Zero ? 0 : Win32.GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var attached = theirs != 0 && theirs != ours && Win32.AttachThreadInput(theirs, ours, true);
        try
        {
            Win32.BringWindowToTop(hwnd);
            Win32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(theirs, ours, false);
        }
    }

    /// <summary>A window with no title bar, not resizable, not in Alt+Tab.</summary>
    public static AppWindow MakeBorderless(Window window, bool alwaysOnTop)
    {
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = alwaysOnTop;
        }
        appWindow.IsShownInSwitchers = false;

        var corner = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(Handle(window), Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        return appWindow;
    }

    /// <summary>
    /// Turns a window into a see-through-to-clicks overlay that never takes
    /// focus, with the given opacity (0–1).
    /// </summary>
    public static void MakeClickThrough(Window window, double opacity)
    {
        var hwnd = Handle(window);
        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        style |= Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)style);
        Win32.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Clamp(opacity * 255, 40, 255), Win32.LWA_ALPHA);
    }
}
