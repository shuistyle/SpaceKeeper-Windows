// ======================================================================
// HotKeyService.cs — the Ctrl+Alt+S shortcut that opens SpaceKeeper
// (the backup to double-tapping Ctrl; see DoubleTapCtrlService.cs)
// ======================================================================
// RegisterHotKey asks Windows to tell us whenever Ctrl+Alt+S is pressed,
// in ANY app. Windows delivers that as a WM_HOTKEY message to one of our
// windows, so we "subclass" the panel window to listen for it.
//
// A nice side effect: Windows lets the app that owns a hotkey bring its
// window to the front, so the panel can take keyboard focus straight away.
//
// Created by App.xaml.cs; turned on/off by the "Open with double-tap Ctrl
// (or Ctrl+Alt+S)" setting (MainViewModel.OpenWithHotKey).
// ======================================================================

namespace SpaceKeeper.App.Services;

public sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0x534B; // any number unique to this app
    private readonly IntPtr _hwnd;
    private readonly Win32.SubclassProc _proc; // kept in a field so it isn't garbage-collected
    private bool _registered;

    /// <summary>Called on the UI thread when the hotkey is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>For the Diagnostics report.</summary>
    public string Status { get; private set; } = "off";

    public HotKeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WindowProc;
        Win32.SetWindowSubclass(_hwnd, _proc, (IntPtr)1, IntPtr.Zero);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !_registered)
        {
            _registered = Win32.RegisterHotKey(_hwnd, HotKeyId,
                Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, Win32.VK_S);
            Status = _registered ? "registered" : "couldn't register — another app may use Ctrl+Alt+S";
        }
        else if (!enabled && _registered)
        {
            Win32.UnregisterHotKey(_hwnd, HotKeyId);
            _registered = false;
            Status = "off";
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        if (msg == Win32.WM_HOTKEY && wParam == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }
        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        SetEnabled(false);
        Win32.RemoveWindowSubclass(_hwnd, _proc, (IntPtr)1);
    }
}
