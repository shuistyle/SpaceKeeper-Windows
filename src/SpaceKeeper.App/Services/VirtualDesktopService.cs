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

    /// <summary>The desktop you're on now, or null if Windows can't say.</summary>
    public Guid? CurrentId
    {
        get
        {
            if (!IsAvailable) return null;
            try { return VirtualDesktop.Current.Id; }
            catch (Exception) { return null; }
        }
    }

    // ------------------------------------------------------------------
    // Switching even when PowerPoint (or another Office app) is in front
    // ------------------------------------------------------------------
    // When SpaceKeeper's panel closes, Windows gives focus back to the app
    // you were using. Office apps such as PowerPoint then grab focus for
    // their own window while the desktops are sliding, and Windows follows
    // that window straight back to the desktop you were leaving. So:
    //   1. ParkFocusOnTaskbar(): before the panel closes, hand focus to the
    //      taskbar, which is on every desktop and doesn't pull you back;
    //   2. switch, then wait a moment and check you're still there (Office
    //      pulls back a fraction of a second later); if not, try once more;
    //   3. give focus to the top window on the new desktop, so you can carry
    //      on typing there.

    /// <summary>Gives keyboard focus to the taskbar. Call while SpaceKeeper is still the active app.</summary>
    public static void ParkFocusOnTaskbar()
    {
        var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero) Win32.SetForegroundWindow(taskbar);
    }

    /// <summary>
    /// Switches and checks the switch stuck (see above). Returns false if
    /// Windows still isn't on that desktop after two tries.
    /// </summary>
    public async Task<bool> SwitchReliablyAsync(Guid id)
    {
        if (!IsAvailable) return false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0) ParkFocusOnTaskbar();
            if (!Switch(id)) return false;

            // Wait for the switch (up to ~1 second)…
            for (var i = 0; i < 10 && CurrentId != id; i++) await Task.Delay(100);
            // …then make sure nothing pulled us back.
            await Task.Delay(400);
            if (CurrentId == id)
            {
                FocusTopWindowOn(id);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// If focus is still on the taskbar, gives it to the window you used
    /// most recently on that desktop (the highest one on screen).
    /// </summary>
    private static void FocusTopWindowOn(Guid id)
    {
        try
        {
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (Win32.GetForegroundWindow() != taskbar) return; // Windows already chose a window

            // Top-level windows, from the front to the back.
            for (var hwnd = Win32.GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = Win32.GetWindow(hwnd, Win32.GW_HWNDNEXT))
            {
                if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) continue;
                if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) continue; // dialogs and pop-ups
                var exStyle = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
                if ((exStyle & Win32.WS_EX_TOOLWINDOW) != 0) continue;            // tool windows, our overlays
                if (VirtualDesktop.IsPinnedWindow(hwnd)) continue;                // on every desktop
                if (VirtualDesktop.FromHwnd(hwnd)?.Id != id) continue;
                Win32.SetForegroundWindow(hwnd);
                return;
            }
        }
        catch (Exception)
        {
            // Not important enough to fail the switch for.
        }
    }

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
