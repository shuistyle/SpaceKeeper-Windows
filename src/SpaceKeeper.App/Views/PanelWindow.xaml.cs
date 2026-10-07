// ======================================================================
// PanelWindow.xaml.cs — behaviour of the SpaceKeeper panel
// ======================================================================
// The layout is in PanelWindow.xaml; this "code-behind" file handles the
// window itself and the desktop grid's mouse and keyboard behaviour:
//
//   • SIZE AND PLACE: the panel sits centred just above the taskbar. Its
//     width fits 8 tiles per row (two rows of 8 = 16 desktops), dropping to
//     4 or 2 per row when the tiles are too big for the screen. Its height
//     grows to fit what's inside, so there's no scroll bar unless the
//     screen is too small.
//   • CLICKS: one click switches desktop; a double-click renames. We wait
//     the Windows double-click time before switching, so a double-click
//     doesn't switch first (same as the Mac version).
//   • KEYS: Enter/Space switch, F2 renames, Delete removes, Alt+Shift+arrows
//     move a tile, Esc cancels renaming or closes the panel, Ctrl+Plus /
//     Ctrl+Minus / Ctrl+0 change the tile size, Ctrl+N adds a desktop.
//   • Hides (rather than closes) when it loses focus or you press Esc, so
//     it opens instantly next time — like a Windows 11 flyout.
//   • Screen-reader announcements.
//
// Created by App.xaml.cs. Shown by the tray icon, double-tap Ctrl or Ctrl+Alt+S.
// ======================================================================

using System.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceKeeper.App.Services;
using SpaceKeeper.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
// Only the two keyboard types are taken from Windows.System. Importing the
// whole namespace would clash with Microsoft.UI.Dispatching, which has
// classes with the same names (DispatcherQueueTimer, DispatcherQueuePriority).
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace SpaceKeeper.App.Views;

public sealed partial class PanelWindow : Window
{
    // Sizes in layout units ("effective pixels"); multiplied by the screen scale for real pixels.
    private const double TileGap = 8;            // matches the 4-pixel item margin in the XAML
    private const double PanelPadding = 16;
    private const double ScrollBarAllowance = 24;
    private const double MinPanelWidth = 560;
    private const double ScreenMargin = 12;
    private static readonly int[] ColumnChoices = [8, 4, 2];

    private bool _isShown;
    private bool _reallyClosing;
    private double _panelWidth = 1000;           // set by ApplyLayout
    private int _columns = 8;
    private long _hiddenAt;                      // when the panel last hid (milliseconds since start-up)

    // Single click → wait for a possible double-click → switch.
    private readonly DispatcherQueueTimer _clickTimer;
    private DesktopTileViewModel? _pendingClick;

    /// <summary>The data the XAML binds to ({x:Bind ViewModel.…}).</summary>
    public MainViewModel ViewModel { get; }

    public PanelWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        WindowHelpers.MakeBorderless(this, alwaysOnTop: true);
        // Acrylic: the frosted Windows 11 background. Windows automatically
        // swaps it for a solid colour when Transparency effects are off or a
        // contrast theme is on.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        _clickTimer = DispatcherQueue.CreateTimer();
        _clickTimer.Interval = TimeSpan.FromMilliseconds(Win32.GetDoubleClickTime());
        _clickTimer.IsRepeating = false;
        _clickTimer.Tick += (_, _) => FinishSingleClick();

        AddSizeShortcuts();

        // Bigger/smaller tiles → new width and height.
        ViewModel.PanelLayoutChanged += (_, _) =>
        {
            if (_isShown) { ApplyLayout(); PositionPanel(); }
        };
        // Switching desktop from anywhere (e.g. the right-click menu) → hide first.
        ViewModel.HideRequested += (_, _) => HidePanel();
        // × / Delete / Remove desktop… → move keyboard focus onto the Remove
        // button (otherwise it's left on the × hidden under the confirmation).
        ViewModel.RemoveConfirmRequested += (_, tile) => FocusInTile(tile, "ConfirmRemoveButton");
        // Double-click / F2 / Rename… → put the cursor in that tile's name box.
        ViewModel.RenameRequested += (_, tile) => FocusNameBox(tile);
        // Content got taller or shorter (settings opened, desktop added…) → fit the window to it.
        PanelContent.SizeChanged += (_, _) =>
        {
            if (_isShown) PositionPanel();
        };

        // Closing the window (e.g. Alt+F4) just hides it; Quit really closes.
        AppWindow.Closing += (_, args) =>
        {
            if (_reallyClosing) return;
            args.Cancel = true;
            HidePanel();
        };

        // Hide when you click somewhere else, like a Windows flyout.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _isShown) HidePanel();
        };
    }

    // ------------------------------------------------------------------
    // Showing and hiding
    // ------------------------------------------------------------------

    public void Toggle()
    {
        if (_isShown) { HidePanel(); return; }
        // Clicking the tray icon while the panel is open first takes focus
        // away (which hides the panel) and THEN asks us to toggle. Without
        // this check, that click would hide the panel and instantly reopen it.
        if (Environment.TickCount64 - _hiddenAt < 300) return;
        ShowPanel();
    }

    public void ShowPanel()
    {
        ViewModel.Refresh();
        ViewModel.UpdateTileMetrics();   // picks up any change to Windows' text size
        ApplyLayout();
        PositionPanel();
        AppWindow.Show();
        Activate();
        WindowHelpers.BringToFront(this);
        _isShown = true;

        // Put keyboard focus on the current desktop's tile, ready to use.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            PositionPanel(); // now that the content has been laid out
            var current = ViewModel.Tiles.FirstOrDefault(t => t.IsCurrent) ?? ViewModel.Tiles.FirstOrDefault();
            if (current is not null) FocusTile(current, FocusState.Programmatic);
        });
    }

    public void HidePanel()
    {
        if (_isShown) _hiddenAt = Environment.TickCount64;
        _isShown = false;
        _clickTimer.Stop();
        _pendingClick = null;
        foreach (var tile in ViewModel.Tiles)
        {
            if (tile.IsRenaming) tile.CancelRename();
            tile.IsConfirmingRemove = false;
        }
        AppWindow.Hide();
    }

    /// <summary>Really closes the window (used when quitting).</summary>
    public void CloseForReal()
    {
        _reallyClosing = true;
        Close();
    }

    // ------------------------------------------------------------------
    // Size and position
    // ------------------------------------------------------------------

    /// <summary>
    /// Chooses how many tiles fit in a row (8, else 4, else 2) and the panel
    /// width that holds them, then lays out Settings in two columns (wide
    /// panel) or one (narrow panel).
    /// </summary>
    private void ApplyLayout()
    {
        var scale = WindowHelpers.Scale(this);
        var screenWidth = DisplayArea.Primary.WorkArea.Width / scale;
        var cell = ViewModel.TileWidth + TileGap;

        _columns = ColumnChoices.FirstOrDefault(c => WidthFor(c) <= screenWidth * 0.94, ColumnChoices[^1]);
        _panelWidth = Math.Min(Math.Max(WidthFor(_columns), MinPanelWidth), screenWidth - 2 * ScreenMargin);

        if (DesktopGrid.ItemsPanelRoot is ItemsWrapGrid wrap)
        {
            wrap.MaximumRowsOrColumns = _columns;
        }

        // Settings: side by side when the panel is wide, stacked when it isn't.
        var twoColumns = _columns >= 8;
        SettingsGrid.ColumnDefinitions[1].Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SettingsGrid.ColumnSpacing = twoColumns ? 32 : 0;
        Grid.SetColumn(SettingsColumnB, twoColumns ? 1 : 0);
        Grid.SetRow(SettingsColumnB, twoColumns ? 0 : 1);

        double WidthFor(int columns) => columns * cell + 2 * PanelPadding + ScrollBarAllowance;
    }

    /// <summary>
    /// Sizes the window to fit its content (up to the screen height) and
    /// places it centred just above the taskbar. Positions are in physical
    /// screen pixels, so we multiply by the scale.
    /// </summary>
    private void PositionPanel()
    {
        var area = DisplayArea.Primary.WorkArea;
        var scale = WindowHelpers.Scale(this);
        var margin = (int)(ScreenMargin * scale);

        var contentHeight = PanelContent.ActualHeight > 0 ? PanelContent.ActualHeight : 600;
        var width = (int)(_panelWidth * scale);
        var height = (int)Math.Min(contentHeight * scale + 2, area.Height - 2 * margin);
        var x = area.X + (area.Width - width) / 2;
        var y = area.Y + area.Height - height - margin;

        var now = AppWindow.Position;
        var size = AppWindow.Size;
        if (now.X == x && now.Y == y && size.Width == width && Math.Abs(size.Height - height) <= 1) return;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout();
        if (_isShown) PositionPanel();
    }

    /// <summary>Ctrl+Plus, Ctrl+Minus and Ctrl+0 (main keyboard and number pad) change the tile size.</summary>
    private void AddSizeShortcuts()
    {
        void Add(VirtualKey key, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) =>
            {
                if (IsTyping()) return; // leave Ctrl+0 etc. to the text box
                args.Handled = true;
                action();
            };
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }

        const VirtualKey plusKey = (VirtualKey)187;   // the "=/+" key next to Backspace
        const VirtualKey minusKey = (VirtualKey)189;  // the "-/_" key
        Add(plusKey, () => ViewModel.MakeBiggerCommand.Execute(null));
        Add(VirtualKey.Add, () => ViewModel.MakeBiggerCommand.Execute(null));
        Add(minusKey, () => ViewModel.MakeSmallerCommand.Execute(null));
        Add(VirtualKey.Subtract, () => ViewModel.MakeSmallerCommand.Execute(null));
        Add(VirtualKey.Number0, () => ViewModel.ResetSizeCommand.Execute(null));
        Add(VirtualKey.NumberPad0, () => ViewModel.ResetSizeCommand.Execute(null));
    }

    // ------------------------------------------------------------------
    // Tiles: clicking, double-clicking, keys
    // ------------------------------------------------------------------

    /// <summary>
    /// One click on a tile. Clicks on the tile's own buttons (pin, remove)
    /// or name box are left to them. Otherwise we select the tile and start
    /// the double-click timer; if no second click comes, FinishSingleClick switches.
    /// </summary>
    private void OnTileTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DesktopTileViewModel tile } root) return;
        if (IsInsideControl(e.OriginalSource as DependencyObject, root)) return;
        if (tile.IsRenaming || tile.IsConfirmingRemove) return;

        DesktopGrid.SelectedItem = tile;
        _pendingClick = tile;
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    /// <summary>Double-click on a tile: rename it (and cancel the pending switch).</summary>
    private void OnTileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DesktopTileViewModel tile } root) return;
        _clickTimer.Stop();
        _pendingClick = null;
        if (IsInsideControl(e.OriginalSource as DependencyObject, root)) return;
        e.Handled = true;
        ViewModel.RequestRename(tile);
    }

    /// <summary>The double-click time passed with only one click: switch desktop.</summary>
    private void FinishSingleClick()
    {
        _clickTimer.Stop();
        var tile = _pendingClick;
        _pendingClick = null;
        if (tile is null || tile.IsRenaming || !tile.CanSwitch) return;
        SwitchAndHide(tile);
    }

    /// <summary>
    /// Switches desktop. The panel is hidden first: it lives on the current
    /// desktop, so it would disappear mid-switch anyway.
    /// </summary>
    private void SwitchAndHide(DesktopTileViewModel tile) => tile.SwitchCommand.Execute(null); // MainViewModel asks us to hide first

    /// <summary>
    /// Keys on a tile. "Preview" means we see the key before the grid does,
    /// so Space switches desktop instead of just selecting the tile.
    /// </summary>
    private void OnGridPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.OriginalSource is ButtonBase) return; // typing a name, or on a button
        if (FocusedTile() is not { } tile) return;

        switch (e.Key)
        {
            case VirtualKey.Enter or VirtualKey.Space:
                e.Handled = true;
                if (tile.CanSwitch) SwitchAndHide(tile);
                break;
            case VirtualKey.F2:
                e.Handled = true;
                ViewModel.RequestRename(tile);
                break;
            case VirtualKey.Delete:
                if (!tile.CanRemove) break;
                e.Handled = true;
                tile.RequestRemoveCommand.Execute(null);
                break;
        }
    }



    private void OnMoveEarlier(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => MoveFocused(-1, args);
    private void OnMoveLater(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => MoveFocused(1, args);

    /// <summary>Alt+Shift+arrows: moves the focused tile and keeps it selected and focused.</summary>
    private void MoveFocused(int offset, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping() || FocusedTile() is not { } tile) return;
        args.Handled = true;
        ViewModel.Move(tile.Id, offset);
        DispatcherQueue.TryEnqueue(() => FocusTile(tile, FocusState.Keyboard));
    }

    /// <summary>After a drag-and-drop in the grid, save the new order.</summary>
    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        ViewModel.SaveOrderFromTiles();

    // ------------------------------------------------------------------
    // Renaming
    // ------------------------------------------------------------------

    /// <summary>Puts the cursor in a tile's name box with the old name selected.</summary>
    private void FocusNameBox(DesktopTileViewModel tile)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Find<TextBox>(Container(tile), "NameBox") is not { } box) return;
            box.Text = tile.Name;
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        });
    }

    /// <summary>Rename when you click away from the name box…</summary>
    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DesktopTileViewModel tile } box) tile.CommitName(box.Text);
    }

    /// <summary>…or press Enter in it.</summary>
    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox { DataContext: DesktopTileViewModel tile } box)
        {
            e.Handled = true;
            tile.CommitName(box.Text);
            FocusTile(tile, FocusState.Keyboard);
        }
    }

    // ------------------------------------------------------------------
    // Finding things on screen
    // ------------------------------------------------------------------

    /// <summary>The on-screen container (GridViewItem) for a tile, if it's been created.</summary>
    private GridViewItem? Container(DesktopTileViewModel tile) => DesktopGrid.ContainerFromItem(tile) as GridViewItem;

    /// <summary>Selects a tile and gives it keyboard focus.</summary>
    private void FocusTile(DesktopTileViewModel tile, FocusState how)
    {
        DesktopGrid.SelectedItem = tile;
        Container(tile)?.Focus(how);
    }

    /// <summary>Focuses a named control inside a tile (after the screen has updated).</summary>
    private void FocusInTile(DesktopTileViewModel tile, string name)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            Find<Control>(Container(tile), name)?.Focus(FocusState.Keyboard));
    }

    /// <summary>The tile that has keyboard focus (or is selected).</summary>
    private DesktopTileViewModel? FocusedTile()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is GridViewItem item) return DesktopGrid.ItemFromContainer(item) as DesktopTileViewModel;
        }
        return DesktopGrid.SelectedItem as DesktopTileViewModel;
    }

    /// <summary>True while a text box (a tile's name box) has the keyboard.</summary>
    private bool IsTyping() => FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox;

    /// <summary>True if a click landed on a button or text box inside the tile, rather than the tile itself.</summary>
    private static bool IsInsideControl(DependencyObject? source, DependencyObject tileRoot)
    {
        for (var node = source; node is not null && node != tileRoot; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or TextBox) return true;
        }
        return false;
    }

    /// <summary>Searches inside an element for a child of type T (optionally with a given x:Name).</summary>
    private static T? Find<T>(DependencyObject? parent, string? name = null) where T : FrameworkElement
    {
        if (parent is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (name is null || match.Name == name)) return match;
            if (Find<T>(child, name) is { } deeper) return deeper;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Screen-reader announcements (Narrator, NVDA, JAWS)
    // ------------------------------------------------------------------

    /// <summary>Asks the screen reader to speak a message.</summary>
    public void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var peer = FrameworkElementAutomationPeer.FromElement(RootGrid)
                   ?? FrameworkElementAutomationPeer.CreatePeerForElement(RootGrid);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            text,
            "SpaceKeeperAnnouncement");
    }

    // ------------------------------------------------------------------
    // Other event handlers named in the XAML
    // ------------------------------------------------------------------

    /// <summary>
    /// Esc, in order: cancel renaming → cancel a remove confirmation →
    /// close the panel. So Esc never loses more than you expect.
    /// </summary>
    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ViewModel.Tiles.FirstOrDefault(t => t.IsRenaming) is { } renaming)
        {
            renaming.CancelRename();
            FocusTile(renaming, FocusState.Keyboard);
            return;
        }
        if (ViewModel.Tiles.FirstOrDefault(t => t.IsConfirmingRemove) is { } removing)
        {
            removing.IsConfirmingRemove = false;
            FocusTile(removing, FocusState.Keyboard);
            return;
        }
        HidePanel();
    }

    private void OnAddDesktopShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.AddDesktopCommand.Execute(null);
    }

    /// <summary>Opens Task View (the same as pressing Win+Tab).</summary>
    private void OnOpenTaskView(object sender, RoutedEventArgs e)
    {
        HidePanel();
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}")
        {
            UseShellExecute = true,
        });
    }

    private void OnDiagnosticsExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        DiagnosticsText.Text = ViewModel.SharableDiagnosticsReport;

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.SharableDiagnosticsReport);
        Clipboard.SetContent(package);
        Announce("Diagnostics report copied");
    }

    private void OnQuit(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();
}
