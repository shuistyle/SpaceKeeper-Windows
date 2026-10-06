// ======================================================================
// CrashReport.cs — shows what went wrong instead of quitting silently
// ======================================================================
// If something goes wrong that SpaceKeeper can't recover from (most often
// while starting up), this:
//   1. writes the full technical details to
//      %LOCALAPPDATA%\SpaceKeeper\crash.log
//      (usually C:\Users\<you>\AppData\Local\SpaceKeeper\crash.log), and
//   2. shows a plain Windows message box saying what happened and where
//      the log is, so the problem can be reported and fixed.
//
// It uses the classic Windows MessageBox, not WinUI, so it still works
// when the WinUI part of the app is what failed.
//
// Called from App.xaml.cs (start-up and the "unhandled exception" events).
// ======================================================================

using System.Runtime.InteropServices;

namespace SpaceKeeper.App.Services;

public static class CrashReport
{
    private static bool _shown;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpaceKeeper", "crash.log");

    /// <summary>Saves the details and, the first time, tells the user.</summary>
    public static void Show(string when, Exception? ex)
    {
        var details =
            $"""
            ===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {when} =====
            SpaceKeeper {typeof(CrashReport).Assembly.GetName().Version}
            Windows {Environment.OSVersion.Version}, {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}
            {ex}

            """;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, details);
        }
        catch (Exception)
        {
            // If even the log can't be written, the message box still shows.
        }

        if (_shown) return;
        _shown = true;
        var summary = ex is null ? "Unknown error" : $"{ex.GetType().Name}: {ex.Message}";
        MessageBox(IntPtr.Zero,
            $"SpaceKeeper ran into a problem {when}.\n\n{summary}\n\nThe full details are saved in:\n{LogPath}\n\nPlease send that file (or this message) so it can be fixed.",
            "SpaceKeeper", 0x10 /* error icon */);
    }

    /// <summary>A plain information message (no log).</summary>
    public static void Inform(string text) => MessageBox(IntPtr.Zero, text, "SpaceKeeper", 0x40 /* information icon */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
