// ======================================================================
// PinEvaluator.cs — checks that pinned desktops are still in order
// ======================================================================
// A "pin" remembers where a desktop sits RELATIVE TO YOUR OTHER PINNED
// desktops (not its exact number). So adding, closing or moving unpinned
// desktops never counts as a problem — only pinned desktops swapping
// places, or a pinned desktop being closed.
//
// Note: in Windows, "pin" also means "show a window on all desktops".
// SpaceKeeper's pin is different: it pins a desktop's PLACE in the order.
//
// Called by MainViewModel (SpaceKeeper.App/ViewModels) after every refresh.
// Everything here is "static": plain functions, no saved state of their own.
// ======================================================================

namespace SpaceKeeper.Core;

public static class PinEvaluator
{
    /// <summary>
    /// Compares the live desktops with the saved pins and returns any warnings.
    /// </summary>
    /// <param name="desktops">Desktops in Task View order.</param>
    /// <param name="pins">Pinned desktop IDs → rank (from <see cref="SavedState.Pins"/>).</param>
    /// <param name="lastKnownNames">Names to use for desktops that no longer exist.</param>
    public static IReadOnlyList<PinAlert> Evaluate(
        IReadOnlyList<DesktopInfo> desktops,
        IReadOnlyDictionary<Guid, int> pins,
        IReadOnlyDictionary<Guid, string>? lastKnownNames = null)
    {
        var alerts = new List<PinAlert>();
        var byId = desktops.ToDictionary(d => d.Id);

        // Pinned desktops in their SAVED order (rank), split into present / missing.
        var present = new List<DesktopInfo>();
        foreach (var (id, _) in pins.OrderBy(p => p.Value).ThenBy(p => p.Key))
        {
            if (byId.TryGetValue(id, out var desktop))
            {
                present.Add(desktop);
            }
            else
            {
                var name = lastKnownNames?.GetValueOrDefault(id) ?? "Pinned desktop";
                alerts.Add(new PinAlert(id, name, PinProblem.Missing, null, null));
            }
        }

        // Keep the largest group already in the right order; flag only the rest.
        // Example: current positions [1, 4, 2, 3] → 1, 2, 3 are fine, only "4" is flagged.
        var inOrder = LongestIncreasingRun(present.Select(d => d.Index).ToList());
        for (var i = 0; i < present.Count; i++)
        {
            if (inOrder.Contains(i)) continue;

            // The nearest in-order pinned neighbours tell the user where it belongs.
            var after = Enumerable.Range(0, i).Reverse().FirstOrDefault(j => inOrder.Contains(j), -1);
            var before = Enumerable.Range(i + 1, present.Count - i - 1).FirstOrDefault(j => inOrder.Contains(j), -1);
            alerts.Add(new PinAlert(
                present[i].Id,
                present[i].DisplayName,
                PinProblem.OutOfOrder,
                after >= 0 ? present[after].DisplayName : null,
                before >= 0 ? present[before].DisplayName : null));
        }

        return alerts.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Pins a desktop, slotting it into the saved order according to where it
    /// sits right now. Returns the new pin dictionary.
    /// </summary>
    public static Dictionary<Guid, int> AddPin(IReadOnlyList<DesktopInfo> desktops, IReadOnlyDictionary<Guid, int> pins, Guid id)
    {
        var target = desktops.FirstOrDefault(d => d.Id == id);
        if (target is null) return new Dictionary<Guid, int>(pins);

        var ordered = pins.OrderBy(p => p.Value).Select(p => p.Key).Where(k => k != id).ToList();
        // Insert before the first pinned desktop that currently sits to its right.
        var insertAt = ordered.FindIndex(k => desktops.FirstOrDefault(d => d.Id == k) is { } other && other.Index > target.Index);
        ordered.Insert(insertAt < 0 ? ordered.Count : insertAt, id);
        return Rank(ordered);
    }

    /// <summary>Removes a pin and re-numbers the rest.</summary>
    public static Dictionary<Guid, int> RemovePin(IReadOnlyDictionary<Guid, int> pins, Guid id) =>
        Rank(pins.OrderBy(p => p.Value).Select(p => p.Key).Where(k => k != id));

    /// <summary>
    /// "Accept New Order": saves the current arrangement as the pinned order.
    /// Pins for desktops that no longer exist are kept (so their warning stays
    /// until the user unpins them).
    /// </summary>
    public static Dictionary<Guid, int> AcceptCurrentOrder(IReadOnlyList<DesktopInfo> desktops, IReadOnlyDictionary<Guid, int> pins)
    {
        var present = desktops.Where(d => pins.ContainsKey(d.Id)).OrderBy(d => d.Index).Select(d => d.Id);
        var missing = pins.Keys.Where(k => desktops.All(d => d.Id != k));
        return Rank(present.Concat(missing));
    }

    /// <summary>Turns an ordered list of IDs into ID → rank (0, 1, 2…).</summary>
    private static Dictionary<Guid, int> Rank(IEnumerable<Guid> ordered) =>
        ordered.Select((id, rank) => (id, rank)).ToDictionary(x => x.id, x => x.rank);

    /// <summary>
    /// Indices of the longest strictly increasing subsequence of <paramref name="values"/>.
    /// A standard algorithm: for each item, find the longest increasing run that
    /// ends there, remembering the previous item so the run can be traced back.
    /// </summary>
    public static HashSet<int> LongestIncreasingRun(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return [];
        var length = Enumerable.Repeat(1, values.Count).ToArray();
        var previous = Enumerable.Repeat(-1, values.Count).ToArray();

        for (var i = 0; i < values.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (values[j] < values[i] && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }
        }

        var best = Array.IndexOf(length, length.Max());
        var result = new HashSet<int>();
        for (var k = best; k >= 0; k = previous[k]) result.Add(k);
        return result;
    }
}
