// ======================================================================
// App.xaml.cs — START HERE: the app's entry point and "wiring"
// ======================================================================
// Windows runs OnLaunched() when SpaceKeeper starts. It creates every
// part of the app and connects them:
//
//   VirtualDesktopService  talks to Windows' virtual desktops   (Services/)
//          │
//   MainViewModel          the "brain": data + actions          (ViewModels/)
//          │
//   PanelWindow            the panel you click on                (Views/)
//   OverlayWindow ×2       the corner label + the switch banner  (Views/)
//   TaskbarIcon            the icon in the notification area ("tray")
//   DoubleTapCtrlService   double-tap Ctrl from any app (one hand) (Services/)
//   HotKeyService          Ctrl+Alt+S from any app (backup)       (Services/)
//
// Map of the project:
//   SpaceKeeper.Core/   plain logic (pins, list order, saving) — no Windows code,
//                       covered by automatic tests in tests/
//   SpaceKeeper.App/    this Windows app (WinUI 3)
//
// Glossary:
//   Virtual desktop  one of the desktops in Task View (Win+Tab)
//   Tray             the notification area at the right of the taskbar
//   XAML             the markup language that describes each window's layout
//   Binding          a live link between something on screen and a property
//   Dispatcher queue the UI thread's to-do list; UI changes must go through it
// ======================================================================

using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SpaceKeeper.App.Services;
using SpaceKeeper.App.ViewModels;
using SpaceKeeper.App.Views;

namespace SpaceKeeper.App;

public partial class App : Application
{
    // Only one copy of SpaceKeeper may run at a time.
    private static Mutex? _singleInstance;

    private VirtualDesktopService? _desktops;
    private MainViewModel? _viewModel;
    private PanelWindow? _panel;
    private OverlayWindow? _label;
    private OverlayWindow? _banner;
    private TaskbarIcon? _tray;
    private HotKeyService? _hotKey;
    private DoubleTapCtrlService? _doubleTap;

    public App()
    {
        // If anything goes wrong that nothing else catches, show it rather
        // than letting the app vanish silently (see Services/CrashReport.cs).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashReport.Show("and had to close", e.ExceptionObject as Exception);
        UnhandledException += (_, e) =>
        {
            CrashReport.Show("and had to close", e.Exception);
        };

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            CrashReport.Show("while starting", ex);
            Exit();
        }
    }

    /// <summary>Creates and connects every part of the app (called once, from OnLaunched).</summary>
    private void Start()
    {
        _singleInstance = new Mutex(true, @"Local\SpaceKeeper.SingleInstance", out var isFirstCopy);
        if (!isFirstCopy)
        {
            // Say so, rather than appearing to do nothing.
            CrashReport.Inform("SpaceKeeper is already running.\n\nClick its icon in the notification area (bottom-right of the taskbar; it may be under the ^ arrow), double-tap Ctrl, or press Ctrl+Alt+S.");
            Exit();
            return;
        }

        var ui = DispatcherQueue.GetForCurrentThread();
        _desktops = new VirtualDesktopService(ui);
        _viewModel = new MainViewModel(_desktops, ui);

        // The panel (hidden until you open it).
        _panel = new PanelWindow(_viewModel);

        // The shortcuts that open the panel from any app:
        // double-tap Ctrl (one hand) and Ctrl+Alt+S (backup).
        _doubleTap = new DoubleTapCtrlService(ui);
        _doubleTap.Pressed += (_, _) => _panel.Toggle();
        _hotKey = new HotKeyService(WindowHelpers.Handle(_panel));
        _hotKey.Pressed += (_, _) => _panel.Toggle();
        ApplyHotKeySetting();
        _viewModel.HotKeySettingChanged += (_, _) => ApplyHotKeySetting();
        _viewModel.DoubleTapStatusSource = DoubleTapStatus;

        // On-screen label and switch banner.
        _label = OverlayWindow.CreateLabel(_desktops);
        _banner = OverlayWindow.CreateBanner(_desktops);
        UpdateLabel();
        _viewModel.OverlaySettingsChanged += (_, _) => UpdateLabel();
        _viewModel.SwitchedDesktop += (_, desktop) =>
        {
            UpdateLabel();
            if (_viewModel.Settings.ShowSwitchBanner)
            {
                var subtitle = desktop.Name.Length > 0 ? desktop.DefaultName : null;
                _banner.ShowBanner(desktop.DisplayName, subtitle, _viewModel.Settings.OverlayPoints.Banner);
            }
            _panel.Announce($"{desktop.DisplayName}, {desktop.DefaultName}");
        };

        // Screen-reader announcements come out of the panel window.
        _viewModel.Announce += (_, text) => _panel.Announce(text);

        // Keep the tray tooltip and the label up to date (e.g. after a rename).
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TrayToolTip) && _tray is not null)
            {
                _tray.ToolTipText = _viewModel.TrayToolTip;
            }
            if (e.PropertyName == nameof(MainViewModel.CurrentName)) UpdateLabel();
        };

        try
        {
            CreateTrayIcon();
        }
        catch (Exception ex)
        {
            // The app still works from double-tap Ctrl / Ctrl+Alt+S without the icon.
            CrashReport.Show("while creating its notification-area icon (SpaceKeeper will keep running)", ex);
        }

        // Show the panel on first launch so people can see the app started.
        _panel.ShowPanel();
    }

    /// <summary>The icon in the notification area: left-click opens the panel, right-click shows a menu.</summary>
    private void CreateTrayIcon()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open SpaceKeeper", Command = new RelayCommand(() => _panel?.ShowPanel()) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Quit SpaceKeeper", Command = new RelayCommand(Quit) });

        _tray = new TaskbarIcon
        {
            ToolTipText = _viewModel?.TrayToolTip ?? "SpaceKeeper",
            // Loaded straight from the file next to SpaceKeeper.exe. (An "ms-appx:" address
            // only works for apps installed as a package, which SpaceKeeper isn't.)
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "SpaceKeeper.ico")),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => _panel?.Toggle()),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
        };
        _tray.ForceCreate(false); // false = don't put SpaceKeeper into Windows "efficiency mode"
    }

    /// <summary>Turns both shortcuts on or off to match the "Open with double-tap Ctrl" setting.</summary>
    private void ApplyHotKeySetting()
    {
        if (_hotKey is null || _doubleTap is null || _viewModel is null) return;
        var on = _viewModel.Settings.OpenWithHotKey;
        _doubleTap.SetEnabled(on);
        _hotKey.SetEnabled(on);
        _viewModel.HotKeyStatus = _hotKey.Status;
        _viewModel.DoubleTapStatus = DoubleTapStatus();
    }

    /// <summary>Text for the Diagnostics report, e.g. "listening (3 double taps)".</summary>
    public string DoubleTapStatus() =>
        _doubleTap is null ? "off" :
        _doubleTap.Status == "listening" ? $"listening ({_doubleTap.PressCount} double taps so far)" : _doubleTap.Status;

    /// <summary>Shows, hides or updates the corner label to match the settings and current desktop.</summary>
    private void UpdateLabel()
    {
        if (_label is null || _viewModel is null) return;
        var settings = _viewModel.Settings;
        if (!settings.ShowDesktopLabel || _viewModel.Current is null)
        {
            _label.HideOverlay();
            return;
        }
        _label.ShowLabel(_viewModel.CurrentName, settings.OverlayPoints.Label, settings.LabelCorner, settings.LabelOpacity);
    }

    /// <summary>Closes everything cleanly. Called from the tray menu and the panel's Quit button.</summary>
    public void Quit()
    {
        _doubleTap?.Dispose();
        _hotKey?.Dispose();
        _tray?.Dispose();
        NotificationService.Unregister();
        _label?.Close();
        _banner?.Close();
        _panel?.CloseForReal();
        _singleInstance?.ReleaseMutex();
        Exit();
    }
}
