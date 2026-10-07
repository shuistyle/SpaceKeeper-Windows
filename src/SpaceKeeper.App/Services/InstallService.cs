// ======================================================================
// InstallService.cs — runs SpaceKeeper from a safe, permanent place
// ======================================================================
// SECURITY: a Windows app loads the DLL files that sit next to it. If
// SpaceKeeper runs from a folder that other things can change — OneDrive
// (which syncs files in from the cloud and your other devices), Downloads
// or a temporary folder — a changed DLL there would run inside SpaceKeeper,
// which can see your keyboard, every time it starts (including at sign-in).
//
// So SpaceKeeper offers to INSTALL itself: it copies its folder to
//     %LOCALAPPDATA%\Programs\SpaceKeeper
// (usually C:\Users\<you>\AppData\Local\Programs\SpaceKeeper — a private
// folder only your Windows account can change, and not synced), starts the
// copy and closes. "Launch at sign-in" is only allowed from there, or from
// another folder that isn't one of the risky places.
//
// Used by MainViewModel (the Install button and the sign-in setting) and
// App.xaml.cs (starting up after an install).
// ======================================================================

using System.Diagnostics;

namespace SpaceKeeper.App.Services;

public static class InstallService
{
    /// <summary>Passed to the installed copy so it waits for this copy to close.</summary>
    public const string AfterInstallArgument = "--after-install";

    public static string InstallFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SpaceKeeper");

    public static string CurrentFolder { get; } = Normalise(AppContext.BaseDirectory);

    /// <summary>True when running from %LOCALAPPDATA%\Programs\SpaceKeeper.</summary>
    public static bool IsInstalled => string.Equals(CurrentFolder, Normalise(InstallFolder), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why the current folder is unsafe to run from regularly, or null if it's fine.
    /// </summary>
    public static string? RiskyLocationReason
    {
        get
        {
            if (IsInstalled) return null;
            var risky = new (string? Folder, string Name)[]
            {
                (Environment.GetEnvironmentVariable("OneDrive"), "OneDrive"),
                (Environment.GetEnvironmentVariable("OneDriveConsumer"), "OneDrive"),
                (Environment.GetEnvironmentVariable("OneDriveCommercial"), "OneDrive"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "your Downloads folder"),
                (Path.GetTempPath(), "a temporary folder"),
            };
            foreach (var (folder, name) in risky)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                if (CurrentFolder.StartsWith(Normalise(folder), StringComparison.OrdinalIgnoreCase))
                    return $"SpaceKeeper is running from {name}, where its files can be changed from elsewhere.";
            }
            if (CurrentFolder.StartsWith(@"\\", StringComparison.Ordinal))
                return "SpaceKeeper is running from a network folder, where its files can be changed by other computers.";
            return null;
        }
    }

    /// <summary>
    /// Copies SpaceKeeper to the install folder and starts that copy.
    /// Returns null on success, or a message saying what went wrong.
    /// The caller then closes this copy.
    /// </summary>
    public static string? InstallAndStart()
    {
        try
        {
            CopyFolder(CurrentFolder, InstallFolder);
            var exe = Path.Combine(InstallFolder, "SpaceKeeper.exe");
            Process.Start(new ProcessStartInfo(exe, AfterInstallArgument) { UseShellExecute = false, WorkingDirectory = InstallFolder });
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't install SpaceKeeper: {ex.Message}";
        }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static string Normalise(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
