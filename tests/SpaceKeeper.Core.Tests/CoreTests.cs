// Tests for the pin check and list order. Each [Fact] is one small scenario:
// set up some desktops, run the code, and check the result is what we expect.

using SpaceKeeper.Core;
using Xunit;

namespace SpaceKeeper.Core.Tests;

public class PinEvaluatorTests
{
    // Helper: make desktops named A, B, C… in the given Task View order.
    private static (List<DesktopInfo> desktops, Dictionary<string, Guid> ids) Make(params string[] names)
    {
        var ids = names.ToDictionary(n => n, _ => Guid.NewGuid());
        var desktops = names.Select((n, i) => new DesktopInfo(ids[n], n, i + 1, i == 0)).ToList();
        return (desktops, ids);
    }

    private static List<DesktopInfo> Reorder(List<DesktopInfo> desktops, params string[] names) =>
        names.Select((n, i) => desktops.First(d => d.Name == n) with { Index = i + 1 }).ToList();

    [Fact]
    public void NoAlerts_WhenPinnedDesktopsKeepTheirOrder()
    {
        var (desktops, ids) = Make("Mail", "Code", "Web");
        var pins = new Dictionary<Guid, int> { [ids["Mail"]] = 0, [ids["Web"]] = 1 };
        // Adding an unpinned desktop in between must not raise an alert.
        var withExtra = Reorder(desktops.Append(new DesktopInfo(Guid.NewGuid(), "New", 4, false)).ToList(), "Mail", "New", "Code", "Web");
        Assert.Empty(PinEvaluator.Evaluate(withExtra, pins));
    }

    [Fact]
    public void FlagsOnlyTheDesktopThatMoved()
    {
        var (desktops, ids) = Make("A", "B", "C", "D");
        var pins = ids.Values.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var moved = Reorder(desktops, "A", "C", "D", "B"); // B dragged to the end
        var alerts = PinEvaluator.Evaluate(moved, pins);
        var alert = Assert.Single(alerts);
        Assert.Equal("B", alert.Name);
        Assert.Equal("A", alert.ShouldFollow);
        Assert.Equal("C", alert.ShouldPrecede);
    }

    [Fact]
    public void ReportsClosedPinnedDesktops()
    {
        var (desktops, ids) = Make("A", "B");
        var pins = new Dictionary<Guid, int> { [ids["A"]] = 0, [Guid.NewGuid()] = 1 };
        var alert = Assert.Single(PinEvaluator.Evaluate(desktops, pins));
        Assert.Equal(PinProblem.Missing, alert.Problem);
    }

    [Fact]
    public void AcceptCurrentOrder_ClearsAlerts()
    {
        var (desktops, ids) = Make("A", "B", "C");
        var pins = ids.Values.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var moved = Reorder(desktops, "C", "A", "B");
        Assert.NotEmpty(PinEvaluator.Evaluate(moved, pins));
        Assert.Empty(PinEvaluator.Evaluate(moved, PinEvaluator.AcceptCurrentOrder(moved, pins)));
    }

    [Fact]
    public void AddPin_SlotsIntoCurrentPosition()
    {
        var (desktops, ids) = Make("A", "B", "C");
        var pins = new Dictionary<Guid, int> { [ids["A"]] = 0, [ids["C"]] = 1 };
        var updated = PinEvaluator.AddPin(desktops, pins, ids["B"]);
        Assert.Equal(1, updated[ids["B"]]);
        Assert.Empty(PinEvaluator.Evaluate(desktops, updated));
    }

    [Theory]
    [InlineData(new[] { 1, 4, 2, 3 }, 3)]
    [InlineData(new[] { 3, 2, 1 }, 1)]
    [InlineData(new[] { 1, 2, 3 }, 3)]
    public void LongestIncreasingRun_HasExpectedLength(int[] values, int expected) =>
        Assert.Equal(expected, PinEvaluator.LongestIncreasingRun(values).Count);
}

public class ListOrderTests
{
    private static List<DesktopInfo> Desktops(int count) =>
        Enumerable.Range(1, count).Select(i => new DesktopInfo(Guid.NewGuid(), $"D{i}", i, false)).ToList();

    [Fact]
    public void NewDesktopsGoAfterPlacedOnes()
    {
        var d = Desktops(3);
        var ordered = ListOrder.Apply(d, [d[2].Id, d[0].Id]);
        Assert.Equal(new[] { d[2].Id, d[0].Id, d[1].Id }, ordered.Select(x => x.Id));
    }

    [Fact]
    public void MoveSwapsNeighbours_AndRespectsEdges()
    {
        var d = Desktops(3);
        Assert.Null(ListOrder.Move(d, d[0].Id, -1));
        Assert.Equal(new[] { d[1].Id, d[0].Id, d[2].Id }, ListOrder.Move(d, d[0].Id, 1)!);
    }

    [Fact]
    public void IsCustom_DetectsDifferentOrder()
    {
        var d = Desktops(2);
        Assert.False(ListOrder.IsCustom(d, []));
        Assert.True(ListOrder.IsCustom(d, [d[1].Id]));
    }
}

public class StateStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sk-{Guid.NewGuid()}.json");
        var store = new StateStore(path);
        var state = new SavedState { Settings = { LabelCorner = LabelCorner.TopRight } };
        state.Pins[Guid.NewGuid()] = 0;
        store.Save(state);
        var loaded = store.Load();
        Assert.Equal(LabelCorner.TopRight, loaded.Settings.LabelCorner);
        Assert.Single(loaded.Pins);
        File.Delete(path);
    }

    [Fact]
    public void DamagedFile_LoadsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sk-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "{ not json");
        Assert.True(new StateStore(path).Load().Settings.ShowDesktopLabel);
        File.Delete(path);
    }
}

public class PanelTextSizeTests
{
    [Fact]
    public void BiggerAndSmaller_StopAtTheEnds()
    {
        Assert.Equal(PanelTextSize.Largest, PanelTextSize.Largest.Bigger());
        Assert.Equal(PanelTextSize.Standard, PanelTextSize.Standard.Smaller());
        Assert.Equal(PanelTextSize.Large, PanelTextSize.Standard.Bigger());
        Assert.Equal(PanelTextSize.ExtraLarge, PanelTextSize.Largest.Smaller());
    }

    [Fact]
    public void Scales_MatchTheMacVersion()
    {
        Assert.Equal([1.0, 1.3, 1.6, 2.0], Enum.GetValues<PanelTextSize>().Select(s => s.Scale()));
    }

    [Fact]
    public void OldSaveFiles_WithoutTheSetting_LoadAsStandard()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"ShowSwitchBanner\":false}")!;
        Assert.Equal(PanelTextSize.Standard, settings.PanelTextSize);
        Assert.False(settings.ShowSwitchBanner);
    }
}

public class TextRulesTests
{
    [Fact]
    public void Clean_TidiesLineBreaksTabsAndSpaces()
    {
        Assert.Equal("Mail and Calendar", DesktopNames.Clean("  Mail\n\tand   Calendar\r\n"));
    }

    [Fact]
    public void Clean_CutsAt60Characters()
    {
        Assert.Equal(60, DesktopNames.Clean(new string('a', 200)).Length);
    }

    [Fact]
    public void Clean_KeepsEmojiWhole()
    {
        var name = string.Concat(Enumerable.Repeat("👩‍💻", 70));
        var cleaned = DesktopNames.Clean(name);
        Assert.Equal(string.Concat(Enumerable.Repeat("👩‍💻", 60)), cleaned);
    }

    [Fact]
    public void Redact_HidesUserFolderAndName()
    {
        var text = @"Saved data: C:\Users\AndrewFlowerdew\AppData\Local\SpaceKeeper\state.json (AndrewFlowerdew)";
        var redacted = PrivacyText.Redact(text, @"C:\Users\AndrewFlowerdew");
        Assert.DoesNotContain("AndrewFlowerdew", redacted);
        Assert.Contains(@"%USERPROFILE%\AppData", redacted);
    }
}

public class DesktopColorTests
{
    [Fact]
    public void ContrastIsAtLeast7To1()
    {
        foreach (var color in DesktopColors.All)
        {
            var contrast = DesktopColors.Contrast(color, againstBlack: color.UsesDarkText);
            Assert.True(contrast >= 7.0, $"{color.Name}: {contrast:F2}:1");
        }
    }

    [Fact]
    public void IdsNamesAndSymbolsAreUnique()
    {
        Assert.Equal(DesktopColors.All.Count, DesktopColors.All.Select(c => c.Id).Distinct().Count());
        Assert.Equal(DesktopColors.All.Count, DesktopColors.All.Select(c => c.Name).Distinct().Count());
        Assert.Equal(DesktopColors.All.Count, DesktopColors.All.Select(c => c.Symbol).Distinct().Count());
    }

    [Fact]
    public void UnknownIdIsIgnored()
    {
        Assert.Null(DesktopColors.Find("ultraviolet"));
        Assert.Equal("Navy", DesktopColors.Find("navy")?.Name);
    }
}
