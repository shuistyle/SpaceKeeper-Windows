// ======================================================================
// DesktopTileViewModel.cs — one tile in the desktop grid
// ======================================================================
// Each tile in the panel's grid is bound to one of these. It holds what the
// tile shows (number, name, pinned?, current?, renaming?) and the commands
// its buttons and right-click menu run. The real work is passed back to
// MainViewModel ("Owner").
//
// Created and updated by MainViewModel.SyncTiles().
// Displayed by the DataTemplate in Views/PanelWindow.xaml.
// Click handling (single click = switch, double-click = rename) is in
// Views/PanelWindow.xaml.cs.
// ======================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceKeeper.Core;

namespace SpaceKeeper.App.ViewModels;

public sealed partial class DesktopTileViewModel : ObservableObject
{
    private MainViewModel Owner { get; }

    public DesktopTileViewModel(MainViewModel owner, DesktopInfo info)
    {
        Owner = owner;
        Id = info.Id;
        Name = info.Name;          // (partial properties can't have "= value" defaults)
        DefaultName = info.DefaultName;
        Update(info);
    }

    public Guid Id { get; }

    [ObservableProperty] public partial int Index { get; set; }
    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial string DefaultName { get; set; }
    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    [ObservableProperty] public partial bool IsOutOfOrder { get; set; }

    /// <summary>True while the name box is showing (double-click, F2 or right-click › Rename).</summary>
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    /// <summary>True while the "Remove / Cancel" confirmation covers the tile.</summary>
    [ObservableProperty] public partial bool IsConfirmingRemove { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name;

    /// <summary>
    /// The small line under the name: "Desktop 3" when the desktop has its own
    /// name, plus "current" — so the current desktop is marked in words as
    /// well as with a thick border (never colour alone).
    /// </summary>
    public string Subtitle => string.Join(" · ", new[]
    {
        DisplayName != DefaultName ? DefaultName : null,
        IsCurrent ? "current" : null,
    }.Where(s => s is not null));

    /// <summary>Pin icon: filled when pinned, crossed out when out of order (not colour alone).</summary>
    public string PinGlyph => IsOutOfOrder ? "\uE77A" /* Unpin */ : IsPinned ? "\uE842" /* Pinned (filled) */ : "\uE718" /* Pin */;

    // ----- Sizes (from the "Desktop tile size" setting; see MainViewModel) -----
    public double TileWidth => Owner.TileWidth;
    public double TileHeight => Owner.TileHeight;
    public double NameFontSize => Owner.FontSize(14);
    public double SmallFontSize => Owner.FontSize(12);
    public double IconFontSize => Owner.FontSize(16);
    public double BadgeSize => Owner.BoxSize(26);

    // ----- Spoken labels for screen readers (Narrator, NVDA, JAWS) -----
    public string TileSummary =>
        string.Join(", ", new[]
        {
            DisplayName,
            DisplayName != DefaultName ? DefaultName : null,
            IsCurrent ? "current" : null,
            IsPinned ? "pinned" : null,
            ColorName is { } colour ? $"colour {colour}" : null,
            IsOutOfOrder ? "out of pinned order" : null,
        }.Where(s => s is not null));

    // ----- Colour (SpaceKeeper.Core/DesktopColors.cs) -----
    /// <summary>The tile's colour ID (e.g. "navy"), or null for none.</summary>
    public string? ColorId => Owner.ColorId(Id);
    public bool HasColor => DesktopColors.Find(ColorId) is not null;
    public string ColorSymbol => DesktopColors.Find(ColorId)?.Symbol ?? "";
    public string? ColorName => DesktopColors.Find(ColorId)?.Name;

    /// <summary>Sets the colour from the Colour menu ("" = none).</summary>
    [RelayCommand] private void SetColor(string? id) => Owner.SetColor(Id, string.IsNullOrEmpty(id) ? null : id);
    public string TileHelp => IsCurrent
        ? "Double-click or press F2 to rename. Alt+Shift+arrow keys move it in the grid."
        : "Click or press Enter to switch. Double-click or press F2 to rename. Alt+Shift+arrow keys move it in the grid.";
    public string NameFieldLabel => $"Name for {DefaultName}";
    public string PinLabel => IsPinned ? $"Unpin {DisplayName}" : $"Pin {DisplayName}";
    public string SwitchLabel => $"Switch to {DisplayName}";
    public string RenameLabel => $"Rename {DisplayName}";
    public string RemoveLabel => $"Remove {DisplayName}";
    public string ConfirmRemoveLabel => $"Confirm remove {DisplayName}";
    public string CancelRemoveLabel => $"Cancel removing {DisplayName}";

    public bool CanSwitch => !IsCurrent;
    public bool CanRemove => Owner.CanRemove;
    public bool CanMoveEarlier => Owner.CanMove(Id, -1);
    public bool CanMoveLater => Owner.CanMove(Id, 1);

    /// <summary>
    /// Screen readers use this as the grid item's name (the GridView asks the
    /// item for its text), so each tile is read as one clear summary.
    /// </summary>
    public override string ToString() => TileSummary;

    /// <summary>Copies the latest information from Windows into this tile.</summary>
    public void Update(DesktopInfo info)
    {
        Index = info.Index;
        // Don't overwrite the name while you're typing a new one.
        if (!IsRenaming) Name = info.Name;
        DefaultName = info.DefaultName;
        IsCurrent = info.IsCurrent;
        RefreshState();
    }

    /// <summary>Re-reads pin state and sizes, and tells the screen every derived value may have changed.</summary>
    public void RefreshState()
    {
        IsPinned = Owner.IsPinned(Id);
        IsOutOfOrder = Owner.IsOutOfOrder(Id);
        foreach (var p in new[]
        {
            nameof(DisplayName), nameof(Subtitle), nameof(PinGlyph), nameof(TileSummary), nameof(TileHelp),
            nameof(NameFieldLabel), nameof(PinLabel), nameof(SwitchLabel), nameof(RenameLabel), nameof(RemoveLabel),
            nameof(ConfirmRemoveLabel), nameof(CancelRemoveLabel),
            nameof(CanSwitch), nameof(CanRemove), nameof(CanMoveEarlier), nameof(CanMoveLater),
            nameof(TileWidth), nameof(TileHeight), nameof(NameFontSize), nameof(SmallFontSize),
            nameof(IconFontSize), nameof(BadgeSize),
            nameof(ColorId), nameof(HasColor), nameof(ColorSymbol), nameof(ColorName),
        })
        {
            OnPropertyChanged(p);
        }
    }

    // ----- Renaming -----

    /// <summary>Shows the name box. PanelWindow then puts the cursor in it.</summary>
    public void StartRename()
    {
        IsConfirmingRemove = false;
        IsRenaming = true;
    }

    /// <summary>Called when you press Enter or click away from the name box.</summary>
    public void CommitName(string text)
    {
        if (!IsRenaming) return;
        IsRenaming = false;
        text = DesktopNames.Clean(text); // max 60 characters, no line breaks
        if (text == Name.Trim()) return;
        Owner.Rename(Id, text);
    }

    /// <summary>Esc while renaming: put the old name back.</summary>
    public void CancelRename()
    {
        IsRenaming = false;
        OnPropertyChanged(nameof(Name)); // the box reloads the saved name next time
    }

    // ----- Commands for the buttons and right-click menu -----
    // [RelayCommand] turns each method into a Command, e.g. Switch() → SwitchCommand.

    [RelayCommand] private void Switch() => Owner.SwitchTo(Id);
    [RelayCommand] private void Rename() => Owner.RequestRename(this);
    [RelayCommand] private void TogglePin() => Owner.TogglePin(Id);
    [RelayCommand] private void RequestRemove() => Owner.RequestRemove(this);
    [RelayCommand] private void CancelRemove() => IsConfirmingRemove = false;
    [RelayCommand] private async Task ConfirmRemove()
    {
        IsConfirmingRemove = false;
        await Owner.RemoveAsync(Id);
    }
    [RelayCommand] private void MoveEarlier() => Owner.Move(Id, -1);
    [RelayCommand] private void MoveLater() => Owner.Move(Id, 1);
}
