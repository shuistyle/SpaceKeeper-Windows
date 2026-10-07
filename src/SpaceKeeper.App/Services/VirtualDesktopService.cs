// ======================================================================
// VirtualDesktopService.cs — everything SpaceKeeper asks Windows about desktops
// ======================================================================
// This is the Windows equivalent of SpaceReader + SpaceSwitcher +
// MissionControl in the Mac version — but simpler, because Windows 11 can
// list, name, switch, create and remove desktops directly.
//
// Windows doesn't publish an official API for this, so we use the
// open-source Slions.VirtualDesktop library (namespace "WindowsDesktop"),
// which wraps the undocumented Windows interfaces and is updated for new
// Windows 11 releases. If a future Windows update breaks it, updating that
// NuGet package is usually the fix.
//
// Used by MainViewModel (list, switch, rename, add, remove) and by
// OverlayWindow (to show the label on every desktop).
//
// Threading: Windows tells us about desktop switches on a background
// thread. WinUI only lets the UI be changed from the UI thread, so
// `Changed` is always raised through the DispatcherQueue (the UI thread's
// to-do list).
// ======================================================================

using Microsoft.UI.Dispatching;
using SpaceKeeper.Core;
using WindowsDesktop;

namespace SpaceKeeper.App.Services;

public sealed class VirtualDesktopService
{
    private readonly DispatcherQueue _ui;

    /// <summary>Raised (on the UI thread) when you switch desktop.</summary>
    public event EventHandler? CurrentChanged;

    /// <summary>A short explanation if the desktop system couldn't be started.</summary>
    public string? StartupError { get; }

    public VirtualDesktopService(DispatcherQueue ui)
    {
        _ui = ui;
        try
        {
            // Prepares the library for this exact Windows build. The library
            // generates a little code to match your Windows version. By default
            // it saves that code as a DLL file and loads it again next time, so
            // another program could swap the file and get its own code run
            // inside SpaceKeeper. SECURITY: keep the generated code in memory
            // only (SaveCompiledAssembly = false), and point the "saved code"
            // folder at a random folder that doesn't exist, because the library
            // loads any DLL it finds in that folder even when saving is off.
            VirtualDesktop.Configure(new WindowsDesktop.Properties.VirtualDesktopConfiguration
            {
                SaveCompiledAssembly = false,
                CompiledAssemblySaveDirectory = new DirectoryInfo(
                    Path.Combine(Path.GetTempPath(), "SpaceKeeper-no-saved-code-" + Guid.NewGuid().ToString("N"))),
            });
            VirtualDesktop.CurrentChanged += (_, _) => _ui.TryEnqueue(() => CurrentChanged?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            StartupError = $"Couldn't connect to Windows' virtual desktops ({ex.Message}). " +
                           "A Windows update may need a newer version of SpaceKeeper.";
        }
    }

    public bool IsAvailable => StartupError is null;

    /// <summary>All desktops in Task View order (left to right).</summary>
    public IReadOnlyList<DesktopInfo> GetDesktops()
    {
        if (!IsAvailable) return [];
        try
        {
            var currentId = VirtualDesktop.Current.Id;
            return VirtualDesktop.GetDesktops()
                .Select((d, i) => new DesktopInfo(d.Id, d.Name ?? "", i + 1, d.Id == currentId))
                .ToList();
        }
        catch (Exception)
        {
            // Explorer may be restarting; try again on the next refresh.
            return [];
        }
    }

    /// <summary>Switches to a desktop (Windows plays its usual slide animation).</summary>
    public bool Switch(Guid id) => Try(() => VirtualDesktop.FromId(id)?.Switch());

    /// <summary>Renames a desktop in Windows itself — the name shows in Task View too.</summary>
    public bool Rename(Guid id, string name) => Try(() =>
    {
        // Max 60 characters, no line breaks or control characters (Core/TextRules.cs).
        if (VirtualDesktop.FromId(id) is { } desktop) desktop.Name = DesktopNames.Clean(name);
    });

    /// <summary>Adds a new desktop at the end. Returns its ID.</summary>
    public Guid? Create()
    {
        Guid? id = null;
        Try(() => id = VirtualDesktop.Create().Id);
        return id;
    }

    /// <summary>Closes a desktop. Windows moves its windows to a neighbouring desktop.</summary>
    public bool Remove(Guid id) => Try(() => VirtualDesktop.FromId(id)?.Remove());

    /// <summary>Makes one of our windows appear on every desktop (used for the overlays).</summary>
    public void ShowOnAllDesktops(IntPtr hwnd) => Try(() => VirtualDesktop.PinWindow(hwnd));

    private bool Try(Action action)
    {
        if (!IsAvailable) return false;
        try { action(); return true; }
        catch (Exception) { return false; }
    }
}
