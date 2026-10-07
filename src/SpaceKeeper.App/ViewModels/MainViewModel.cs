// ======================================================================
// MainViewModel.cs — the "brain" of SpaceKeeper for Windows
// ======================================================================
// This is the Windows twin of AppModel.swift in the Mac version. There is
// ONE MainViewModel, created in App.xaml.cs. It:
//   1. HOLDS what the panel shows: the grid of desktop tiles, warnings,
//      status messages, settings and the tile size.
//   2. DOES things when the panel asks: switch, rename, pin, add, remove,
//      reorder the grid, change settings.
//   3. REFRESHES every 1.5 seconds and whenever you switch desktop, because
//      Windows doesn't announce renames or reordering in Task View.
//
// "MVVM" (Model–View–ViewModel) is the modern pattern for Windows apps:
//   Model     = plain data (SpaceKeeper.Core)
//   View      = the XAML screens (Views/)
//   ViewModel = this class: turns data into things the screen can show
//               and turns button presses into actions.
// The screen is connected to these properties with "data binding"
// ({x:Bind ...} in the XAML), so when a property changes the screen
// updates by itself.
//
// CommunityToolkit.Mvvm writes the repetitive code for us:
//   [ObservableProperty] – the screen is told whenever this value changes
//   [RelayCommand]       – turns a method into a Command a button can call
//                          (e.g. AddDesktop() → AddDesktopCommand)
// ======================================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SpaceKeeper.App.Services;
using SpaceKeeper.Core;

namespace SpaceKeeper.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly VirtualDesktopService _desktops;
    private readonly StateStore _store = new();
    private readonly SavedState _state;
    private readonly DispatcherQueueTimer _timer;
    private IReadOnlyList<DesktopInfo> _live = [];
    private Guid? _lastCurrentId;
    private HashSet<Guid> _notifiedAlerts = [];

    /// <summary>Raised when you change desktop: (new desktop, its display name).</summary>
    public event EventHandler<DesktopInfo>? SwitchedDesktop;

    /// <summary>Raised when a setting affecting the overlays changes.</summary>
    public event EventHandler? OverlaySettingsChanged;

    /// <summary>Raised when the shortcut setting changes (App.xaml.cs applies it).</summary>
    public event EventHandler? HotKeySettingChanged;

    /// <summary>Raised when the tiles change size, so PanelWindow can resize itself.</summary>
    public event EventHandler? PanelLayoutChanged;

    /// <summary>Raised just before switching desktop: the panel hides (it would vanish mid-switch anyway).</summary>
    public event EventHandler? HideRequested;

    /// <summary>Raised when a tile shows its Remove / Cancel confirmation (PanelWindow focuses Remove).</summary>
    public event EventHandler<DesktopTileViewModel>? RemoveConfirmRequested;

    /// <summary>Raised when a tile should show its name box (PanelWindow focuses it).</summary>
    public event EventHandler<DesktopTileViewModel>? RenameRequested;

    /// <summary>Raised with text that screen readers should announce.</summary>
    public event EventHandler<string>? Announce;

    public MainViewModel(VirtualDesktopService desktops, DispatcherQueue ui)
    {
        _desktops = desktops;
        _state = _store.Load();
        _launchAtSignIn = StartupService.IsEnabled;
        // (Partial properties can't have "= value" defaults, so they're set here.)
        CurrentName = "";
        CurrentSubtitle = "";
        PinAlertsText = "";
        DesktopCountText = "";
        UpdateTileMetrics();

        _desktops.CurrentChanged += (_, _) => Refresh();

        // Poll every 1.5 s: Windows doesn't send events for renames or for
        // reordering in Task View, so we check regularly (it's very cheap).
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.5);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        StatusMessage = _desktops.StartupError;
        Refresh();
    }

    // ------------------------------------------------------------------
    // STATE shown by the panel
    // ------------------------------------------------------------------

    /// <summary>One tile per desktop, in your own order. The GridView shows these.</summary>
    public ObservableCollection<DesktopTileViewModel> Tiles { get; } = [];

    /// <summary>Warnings about pinned desktops (shown in the orange InfoBar).</summary>
    public ObservableCollection<PinAlert> PinAlerts { get; } = [];

    [ObservableProperty] public partial string CurrentName { get; set; }
    [ObservableProperty] public partial string CurrentSubtitle { get; set; }
    [ObservableProperty] public partial bool HasPinAlerts { get; set; }
    [ObservableProperty] public partial string PinAlertsText { get; set; }
    [ObservableProperty] public partial bool HasCustomOrder { get; set; }
    [ObservableProperty] public partial bool IsChangingDesktops { get; set; }

    /// <summary>"5 desktops" under the Desktops heading.</summary>
    [ObservableProperty] public partial string DesktopCountText { get; set; }

    /// <summary>Settings section open? Closed every time the app starts (like the Mac version).</summary>
    [ObservableProperty] public partial bool IsSettingsExpanded { get; set; }

    /// <summary>The grey status line. Also announced to screen readers.</summary>
    [ObservableProperty] public partial string? StatusMessage { get; set; }

    // While a desktop is being added/removed, the tiles' remove buttons are disabled.
    // (Add Desktop is never greyed out for a limit: unlike macOS, which stops
    // at 16, Windows has no fixed maximum number of desktops.)
    partial void OnIsChangingDesktopsChanged(bool value) => RefreshAllTiles();

    partial void OnStatusMessageChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value)) Announce?.Invoke(this, value);
    }

    public DesktopInfo? Current => _live.FirstOrDefault(d => d.IsCurrent);
    public AppSettings Settings => _state.Settings;

    /// <summary>Text for the tray icon's tooltip (Windows has no menu bar text).</summary>
    public string TrayToolTip => HasPinAlerts
        ? $"SpaceKeeper – {CurrentName} (pinned order changed)"
        : $"SpaceKeeper – {CurrentName}";

    // ------------------------------------------------------------------
    // REFRESH: read Windows, then update everything that depends on it
    // ------------------------------------------------------------------

    public void Refresh()
    {
        var fresh = _desktops.GetDesktops();
        if (fresh.Count == 0 && _desktops.IsAvailable) return; // Explorer restarting

        var changed = !fresh.SequenceEqual(_live);
        _live = fresh;

        // Remember names so a closed pinned desktop can still be described.
        foreach (var d in fresh) _state.LastKnownNames[d.Id] = d.DisplayName;

        if (changed) SyncTiles();
        EvaluatePins();
        DesktopCountText = _live.Count == 1 ? "1 desktop" : $"{_live.Count} desktops";

        var current = Current;
        CurrentName = current?.DisplayName ?? "Unknown";
        CurrentSubtitle = current is null ? "" :
            current.Name.Length > 0 ? $"{current.DefaultName} of {_live.Count}" : $"of {_live.Count} desktops";
        OnPropertyChanged(nameof(TrayToolTip));

        if (current is not null && current.Id != _lastCurrentId)
        {
            if (_lastCurrentId is not null) SwitchedDesktop?.Invoke(this, current);
            _lastCurrentId = current.Id;
        }
    }

    /// <summary>
    /// Updates the tiles IN PLACE (rather than rebuilding the grid) so that a
    /// name you're typing, keyboard focus and screen-reader position survive
    /// each refresh.
    /// </summary>
    private void SyncTiles()
    {
        var ordered = ListOrder.Apply(_live, _state.ListOrder);
        HasCustomOrder = ListOrder.IsCustom(_live, _state.ListOrder);

        // Remove tiles for desktops that have gone.
        for (var i = Tiles.Count - 1; i >= 0; i--)
            if (ordered.All(d => d.Id != Tiles[i].Id)) Tiles.RemoveAt(i);

        // Add, update and reposition the rest.
        for (var i = 0; i < ordered.Count; i++)
        {
            var info = ordered[i];
            var existingIndex = Tiles.ToList().FindIndex(t => t.Id == info.Id);
            if (existingIndex < 0)
            {
                Tiles.Insert(i, new DesktopTileViewModel(this, info));
            }
            else
            {
                if (existingIndex != i) Tiles.Move(existingIndex, i);
                Tiles[i].Update(info);
            }
        }

        RefreshAllTiles();
    }

    private void RefreshAllTiles()
    {
        foreach (var tile in Tiles) tile.RefreshState();
    }

    // ------------------------------------------------------------------
    // TILE ACTIONS (called by DesktopTileViewModel)
    // ------------------------------------------------------------------

    public bool IsPinned(Guid id) => _state.Pins.ContainsKey(id);
    public bool IsOutOfOrder(Guid id) => PinAlerts.Any(a => a.Id == id);
    public bool CanRemove => _live.Count > 1 && !IsChangingDesktops;
    public bool CanMove(Guid id, int offset) => ListOrder.CanMove(ListOrder.Apply(_live, _state.ListOrder), id, offset);

    public void SwitchTo(Guid id)
    {
        HideRequested?.Invoke(this, EventArgs.Empty);
        if (!_desktops.Switch(id)) StatusMessage = "Windows didn't switch desktop. Try again.";
        else StatusMessage = null;
    }

    /// <summary>Asks the panel to show a tile's name box (double-click, F2, right-click › Rename).</summary>
    public void RequestRename(DesktopTileViewModel tile)
    {
        foreach (var other in Tiles.Where(t => t != tile && t.IsRenaming)) other.CancelRename();
        tile.StartRename();
        RenameRequested?.Invoke(this, tile);
    }

    /// <summary>Shows a tile's Remove / Cancel confirmation.</summary>
    public void RequestRemove(DesktopTileViewModel tile)
    {
        tile.IsConfirmingRemove = true;
        RemoveConfirmRequested?.Invoke(this, tile);
    }

    public void Rename(Guid id, string name)
    {
        if (_desktops.Rename(id, name)) Refresh();
        else StatusMessage = "Windows didn't accept that name.";
    }

    public void TogglePin(Guid id)
    {
        _state.Pins = IsPinned(id)
            ? PinEvaluator.RemovePin(_state.Pins, id)
            : PinEvaluator.AddPin(_live, _state.Pins, id);
        Save();
        EvaluatePins();
    }

    public async Task RemoveAsync(Guid id)
    {
        if (!CanRemove) return;
        var name = _live.FirstOrDefault(d => d.Id == id)?.DisplayName ?? "Desktop";
        IsChangingDesktops = true;
        var ok = _desktops.Remove(id);
        await Task.Delay(300); // give Windows a moment to finish
        if (ok)
        {
            _state.Pins = PinEvaluator.RemovePin(_state.Pins, id);
            _state.ListOrder.Remove(id);
            _state.LastKnownNames.Remove(id);
            Save();
            StatusMessage = $"Removed “{name}”. Its windows moved to a neighbouring desktop.";
        }
        else
        {
            StatusMessage = $"Windows didn't remove “{name}”.";
        }
        IsChangingDesktops = false;
        Refresh();
    }

    public void Move(Guid id, int offset)
    {
        var ordered = ListOrder.Apply(_live, _state.ListOrder);
        if (ListOrder.Move(ordered, id, offset) is not { } newOrder) return;
        _state.ListOrder = newOrder;
        Save();
        SyncTiles();
        var name = _live.FirstOrDefault(d => d.Id == id)?.DisplayName;
        var position = Tiles.ToList().FindIndex(t => t.Id == id) + 1;
        Announce?.Invoke(this, $"Moved {name} {(offset < 0 ? "earlier" : "later")}, now {position} of {Tiles.Count}");
    }

    /// <summary>Called after a drag-and-drop in the grid: save the tiles' new order.</summary>
    public void SaveOrderFromTiles()
    {
        _state.ListOrder = Tiles.Select(t => t.Id).ToList();
        HasCustomOrder = ListOrder.IsCustom(_live, _state.ListOrder);
        Save();
        RefreshAllTiles();
    }

    // ------------------------------------------------------------------
    // PANEL COMMANDS (buttons outside the rows)
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task AddDesktop()
    {
        if (IsChangingDesktops) return;
        IsChangingDesktops = true;
        var id = _desktops.Create();
        await Task.Delay(200);
        IsChangingDesktops = false;
        Refresh();
        StatusMessage = id is null ? "Windows didn't add a desktop." : "Added a desktop. Double-click its tile (or press F2) to name it.";
    }

    [RelayCommand]
    private void AcceptNewOrder()
    {
        _state.Pins = PinEvaluator.AcceptCurrentOrder(_live, _state.Pins);
        Save();
        EvaluatePins();
        StatusMessage = "Saved the current order of your pinned desktops.";
    }

    [RelayCommand]
    private void UnpinMissing()
    {
        foreach (var alert in PinAlerts.Where(a => a.Problem == PinProblem.Missing).ToList())
            _state.Pins = PinEvaluator.RemovePin(_state.Pins, alert.Id);
        Save();
        EvaluatePins();
    }

    [RelayCommand]
    private void ResetOrder()
    {
        _state.ListOrder = [];
        Save();
        SyncTiles();
        StatusMessage = "Grid order reset to match Task View.";
    }

    // ------------------------------------------------------------------
    // PINS: compare live order with saved pins (logic in SpaceKeeper.Core)
    // ------------------------------------------------------------------

    private void EvaluatePins()
    {
        var alerts = PinEvaluator.Evaluate(_live, _state.Pins, _state.LastKnownNames);

        var fresh = alerts.Where(a => !_notifiedAlerts.Contains(a.Id)).ToList();
        foreach (var alert in fresh)
        {
            Announce?.Invoke(this, $"{alert.Title}. {alert.Message}");
            // PRIVACY: Windows can show notifications on the lock screen, so they
            // don't include desktop names; the details are in the panel.
            if (Settings.NotifyPinMoves)
                NotificationService.Show("A pinned desktop has moved",
                    alert.Problem == PinProblem.Missing
                        ? "A pinned desktop was closed. Open SpaceKeeper for details."
                        : "A pinned desktop is out of order. Open SpaceKeeper for details.");
        }
        _notifiedAlerts = alerts.Select(a => a.Id).ToHashSet();

        if (!alerts.SequenceEqual(PinAlerts))
        {
            PinAlerts.Clear();
            foreach (var alert in alerts) PinAlerts.Add(alert);
        }
        HasPinAlerts = PinAlerts.Count > 0;
        PinAlertsText = string.Join("\n", PinAlerts.Select(a => $"{a.Title} — {a.Message}"));
        RefreshAllTiles();
    }

    // ------------------------------------------------------------------
    // TILE SIZE — the Mac version's "Text size", for people who need bigger
    // text and targets. Windows' own Settings › Accessibility › Text size
    // makes text bigger too, so tiles grow with BOTH (otherwise enlarged
    // text would be cut off inside fixed-size tiles).
    // ------------------------------------------------------------------

    private const double BaseTileWidth = 116;   // layout units at standard size
    private const double BaseTileHeight = 108;

    public double TileWidth { get; private set; }
    public double TileHeight { get; private set; }

    /// <summary>A font size scaled by the tile-size setting (Windows adds its own text scaling on top).</summary>
    public double FontSize(double standard) => Math.Round(standard * Settings.PanelTextSize.Scale());

    /// <summary>
    /// A fixed shape size (e.g. the number badge) scaled by the tile-size
    /// setting AND Windows' text size, so the text inside always fits.
    /// </summary>
    public double BoxSize(double standard) =>
        Math.Round(standard * Settings.PanelTextSize.Scale() * Math.Max(1, AccessibilityInfo.TextScale));

    /// <summary>Recalculates tile sizes (also called each time the panel opens, in case Windows' text size changed).</summary>
    public void UpdateTileMetrics()
    {
        var scale = Settings.PanelTextSize.Scale() * Math.Max(1, AccessibilityInfo.TextScale);
        TileWidth = Math.Round(BaseTileWidth * scale);
        TileHeight = Math.Round(BaseTileHeight * scale);
        RefreshAllTiles();
        PanelLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>0 = standard, 1 = large, 2 = extra large, 3 = largest (the Settings combo box).</summary>
    public int PanelTextSizeIndex
    {
        get => (int)Settings.PanelTextSize;
        set => SetPanelTextSize((PanelTextSize)Math.Clamp(value, 0, 3));
    }

    public bool CanMakeBigger => Settings.PanelTextSize != PanelTextSize.Largest;
    public bool CanMakeSmaller => Settings.PanelTextSize != PanelTextSize.Standard;

    [RelayCommand] private void MakeBigger() => SetPanelTextSize(Settings.PanelTextSize.Bigger());
    [RelayCommand] private void MakeSmaller() => SetPanelTextSize(Settings.PanelTextSize.Smaller());
    [RelayCommand] private void ResetSize() => SetPanelTextSize(PanelTextSize.Standard);

    private void SetPanelTextSize(PanelTextSize size)
    {
        if (size == Settings.PanelTextSize) return;
        Settings.PanelTextSize = size;
        Save();
        OnPropertyChanged(nameof(PanelTextSizeIndex));
        OnPropertyChanged(nameof(CanMakeBigger));
        OnPropertyChanged(nameof(CanMakeSmaller));
        UpdateTileMetrics();
        Announce?.Invoke(this, $"Tile size {size switch
        {
            PanelTextSize.Large => "large",
            PanelTextSize.ExtraLarge => "extra large",
            PanelTextSize.Largest => "largest",
            _ => "standard",
        }}");
    }

    // ------------------------------------------------------------------
    // SETTINGS — each property saves itself when changed.
    // The panel's ToggleSwitches and ComboBoxes are bound to these.
    // ------------------------------------------------------------------

    public bool ShowSwitchBanner
    {
        get => Settings.ShowSwitchBanner;
        set { Settings.ShowSwitchBanner = value; SettingChanged(); }
    }

    public bool ShowDesktopLabel
    {
        get => Settings.ShowDesktopLabel;
        set { Settings.ShowDesktopLabel = value; SettingChanged(overlays: true); }
    }

    /// <summary>0 = top left, 1 = top right, 2 = bottom left, 3 = bottom right.</summary>
    public int LabelCornerIndex
    {
        get => (int)Settings.LabelCorner;
        set { Settings.LabelCorner = (LabelCorner)Math.Clamp(value, 0, 3); SettingChanged(overlays: true); }
    }

    /// <summary>Opacity as a percentage (30–100) for the slider.</summary>
    public double LabelOpacityPercent
    {
        get => Math.Round(Settings.LabelOpacity * 100);
        set { Settings.LabelOpacity = Math.Clamp(value / 100, 0.3, 1); SettingChanged(overlays: true); }
    }

    /// <summary>0 = standard, 1 = large, 2 = extra large.</summary>
    public int TextSizeIndex
    {
        get => (int)Settings.OverlayTextSize;
        set { Settings.OverlayTextSize = (OverlayTextSize)Math.Clamp(value, 0, 2); SettingChanged(overlays: true); }
    }

    public bool OpenWithHotKey
    {
        get => Settings.OpenWithHotKey;
        set { Settings.OpenWithHotKey = value; SettingChanged(); HotKeySettingChanged?.Invoke(this, EventArgs.Empty); }
    }

    public bool NotifyPinMoves
    {
        get => Settings.NotifyPinMoves;
        set { Settings.NotifyPinMoves = value; SettingChanged(); }
    }

    private bool _launchAtSignIn;
    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            // SECURITY: don't start automatically from a folder that other things
            // can change (OneDrive, Downloads…) — see Services/InstallService.cs.
            if (value && InstallService.RiskyLocationReason is { } reason)
            {
                StatusMessage = $"{reason} Click “Install” first, then turn on Launch at sign-in.";
                OnPropertyChanged();
                return;
            }
            try
            {
                StartupService.SetEnabled(value);
                _launchAtSignIn = value;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Couldn't change the sign-in setting: {ex.Message}";
            }
            OnPropertyChanged();
        }
    }

    private void SettingChanged(bool overlays = false, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        Save();
        OnPropertyChanged(property);
        if (overlays) OverlaySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save() => _store.Save(_state);

    // ------------------------------------------------------------------
    // DIAGNOSTICS — shown in the panel's "Diagnostics" expander
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // INSTALL — copy SpaceKeeper to a safe, private folder (InstallService)
    // ------------------------------------------------------------------

    /// <summary>Show the "Install SpaceKeeper" bar? (Not when already installed.)</summary>
    public bool ShowInstallPrompt => !InstallService.IsInstalled;

    /// <summary>The bar's text: a warning for risky folders, a suggestion otherwise.</summary>
    public string InstallPromptText => InstallService.RiskyLocationReason is { } reason
        ? $"{reason} Install it to your own private apps folder before using Launch at sign-in."
        : "Install SpaceKeeper to your own private apps folder so it can start safely at sign-in.";

    /// <summary>Raised after the installed copy has started; App.xaml.cs then closes this copy.</summary>
    public event EventHandler? InstallStarted;

    [RelayCommand]
    private void Install()
    {
        if (InstallService.InstallAndStart() is { } error)
        {
            StatusMessage = error;
            return;
        }
        InstallStarted?.Invoke(this, EventArgs.Empty);
    }

    public string HotKeyStatus { get; set; } = "off";
    public string DoubleTapStatus { get; set; } = "off";

    /// <summary>Lets App.xaml.cs supply a live double-tap count for the report.</summary>
    public Func<string>? DoubleTapStatusSource { get; set; }

    /// <summary>The report with your user folder written as %USERPROFILE% (for sharing).</summary>
    public string SharableDiagnosticsReport =>
        PrivacyText.Redact(DiagnosticsReport, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string DiagnosticsReport =>
        $"""
        Virtual desktops: {(_desktops.IsAvailable ? $"connected, {_live.Count} desktops" : _desktops.StartupError)}
        Windows: {Environment.OSVersion.Version}
        Double-tap Ctrl: {DoubleTapStatusSource?.Invoke() ?? DoubleTapStatus}
        Ctrl+Alt+S: {HotKeyStatus}
        Tile size: {Settings.PanelTextSize}, Windows text size {AccessibilityInfo.TextScale:P0}
        Saved data: {_store.FilePath}
        Running from: {InstallService.CurrentFolder} ({(InstallService.IsInstalled ? "installed" : InstallService.RiskyLocationReason ?? "not installed")})
        Pins: {_state.Pins.Count}, custom list order: {(HasCustomOrder ? "yes" : "no")}
        """;
}
