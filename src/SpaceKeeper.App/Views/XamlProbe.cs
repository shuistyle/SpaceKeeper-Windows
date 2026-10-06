// ======================================================================
// XamlProbe.cs — finds WHICH line of the panel's layout won't load
// ======================================================================
// When a window's XAML can't be loaded, WinUI only says "XAML parsing
// failed", with no line number. To pin it down, this re-reads a copy of
// PanelWindow.xaml (built into SpaceKeeper.exe) with XamlReader, which DOES
// report "[Line: n Position: m]". Things XamlReader can't handle (x:Bind,
// event handlers) are blanked out first; they're checked when the app is
// built, so they're never the cause of this kind of error.
//
// Only used after a start-up failure; the result goes into crash.log
// (see Services/CrashReport.cs and App.xaml.cs).
// ======================================================================

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Markup;

namespace SpaceKeeper.App.Views;

public static class XamlProbe
{
    /// <summary>Messages from WinUI about resources it couldn't find (filled in by App.xaml.cs).</summary>
    public static List<string> ResourceProblems { get; } = [];

    public static string Run()
    {
        var report = new StringBuilder("--- Layout check (PanelWindow.xaml) ---\n");
        foreach (var problem in ResourceProblems) report.AppendLine($"Missing resource: {problem}");
        try
        {
            using var stream = typeof(XamlProbe).Assembly.GetManifestResourceStream("PanelWindow.xaml");
            if (stream is null) return report.Append("No built-in copy of the layout to check.\n").ToString();
            var xaml = new StreamReader(stream).ReadToEnd();
            var lines = xaml.Split('\n');

            // Just the window's content (a Window itself can't be loaded this way).
            var start = xaml.IndexOf("<Grid x:Name=\"RootGrid\"", StringComparison.Ordinal);
            var end = xaml.LastIndexOf("</Grid>", StringComparison.Ordinal) + "</Grid>".Length;
            var lineOffset = xaml[..start].Count(c => c == '\n');
            var body = xaml[start..end];

            // Blank out x:Bind, event handlers and x:DataType ([ \t] keeps line numbers the same).
            body = Regex.Replace(body, @"[ \t]+[\w.:]+=""\{x:Bind[^""]*\}""", "");
            body = Regex.Replace(body, @"[ \t]+(Click|Tapped|DoubleTapped|Invoked|Loaded|LostFocus|KeyDown|PreviewKeyDown|DragItemsCompleted|Expanding)=""[^""]*""", "");
            body = Regex.Replace(body, @"[ \t]+x:DataType=""[^""]*""", "");
            body = body.Replace("<Grid x:Name=\"RootGrid\"",
                "<Grid x:Name=\"RootGrid\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:SpaceKeeper.App.ViewModels\" " +
                "xmlns:views=\"using:SpaceKeeper.App.Views\"");

            try
            {
                XamlReader.Load(body);
                report.AppendLine("The layout loaded fine on its own, so the problem is a resource or a value set while loading.");
            }
            catch (Exception ex)
            {
                report.AppendLine($"Problem: {ex.Message}");
                var match = Regex.Match(ex.Message, @"Line:\s*(\d+)");
                if (match.Success)
                {
                    var lineNumber = int.Parse(match.Groups[1].Value) + lineOffset; // line in PanelWindow.xaml
                    report.AppendLine($"PanelWindow.xaml line {lineNumber}:");
                    for (var i = Math.Max(1, lineNumber - 2); i <= Math.Min(lines.Length, lineNumber + 2); i++)
                        report.AppendLine($"{(i == lineNumber ? ">>" : "  ")} {i,4}: {lines[i - 1].TrimEnd()}");
                }
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"The check itself failed: {ex.Message}");
        }
        return report.ToString();
    }
}
