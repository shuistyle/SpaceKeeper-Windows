// ======================================================================
// ListOrder.cs — your own order for SpaceKeeper's list
// ======================================================================
// You can drag desktops up and down SpaceKeeper's list (or use Move Up /
// Move Down) to keep related ones together. This order belongs to
// SpaceKeeper only — Task View keeps its own order.
//
// The saved order is just a list of desktop IDs (SavedState.ListOrder).
// Used by MainViewModel whenever it rebuilds the list.
// ======================================================================

namespace SpaceKeeper.Core;

public static class ListOrder
{
    /// <summary>
    /// Returns the desktops in the user's saved order. Desktops that aren't in
    /// the saved order yet (e.g. just created) go at the end, in Task View order.
    /// </summary>
    public static IReadOnlyList<DesktopInfo> Apply(IReadOnlyList<DesktopInfo> desktops, IReadOnlyList<Guid> saved)
    {
        if (saved.Count == 0) return desktops;
        var rank = new Dictionary<Guid, int>();
        for (var i = 0; i < saved.Count; i++) rank.TryAdd(saved[i], i);

        var placed = desktops.Where(d => rank.ContainsKey(d.Id)).OrderBy(d => rank[d.Id]);
        var unplaced = desktops.Where(d => !rank.ContainsKey(d.Id));
        return placed.Concat(unplaced).ToList();
    }

    /// <summary>True when the user's order differs from Task View's.</summary>
    public static bool IsCustom(IReadOnlyList<DesktopInfo> desktops, IReadOnlyList<Guid> saved) =>
        !Apply(desktops, saved).Select(d => d.Id).SequenceEqual(desktops.Select(d => d.Id));

    /// <summary>Move Up (-1) / Move Down (+1). Returns the new saved order, or null if it can't move.</summary>
    public static List<Guid>? Move(IReadOnlyList<DesktopInfo> ordered, Guid id, int offset)
    {
        var keys = ordered.Select(d => d.Id).ToList();
        var index = keys.IndexOf(id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= keys.Count) return null;
        (keys[index], keys[target]) = (keys[target], keys[index]);
        return keys;
    }

    /// <summary>Can this desktop move up (-1) or down (+1)?</summary>
    public static bool CanMove(IReadOnlyList<DesktopInfo> ordered, Guid id, int offset)
    {
        var index = ordered.ToList().FindIndex(d => d.Id == id);
        return index >= 0 && index + offset >= 0 && index + offset < ordered.Count;
    }
}
