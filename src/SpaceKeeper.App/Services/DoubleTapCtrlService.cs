// ======================================================================
// DoubleTapCtrlService.cs — open SpaceKeeper by tapping Ctrl twice
// ======================================================================
// The one-handed shortcut. (The Mac version uses fn-S, but on almost every
// PC keyboard the fn key is handled inside the keyboard itself and Windows
// never hears about it, so fn-S can't work on Windows.)
//
// HOW IT WORKS
// Windows' normal hotkey function (RegisterHotKey, see HotKeyService.cs)
// only understands "modifier + key" combinations, not "tap a key twice".
// So we install a "low-level keyboard hook": Windows shows us every key
// press in any app, a moment before the app gets it. We only LOOK — every
// key is passed straight on (CallNextHookEx), so typing is never affected.
//
// A "tap" is Ctrl pressed and released quickly with no other key in
// between. That way Ctrl+C, Ctrl+V and holding Ctrl to click never count.
// Two taps within the Windows double-click time (Settings › Bluetooth &
// devices › Mouse › Additional mouse settings) = open/close the panel.
//
// The hook runs on its OWN background thread. Windows gives a hook only a
// fraction of a second to answer; if the hook shared the panel's thread
// and the panel was busy, Windows could quietly switch the hook off.
//
// Privacy: nothing is recorded or stored; we only compare key codes with
// the Ctrl key and count taps for the Diagnostics report.
//
// Limitation: Windows doesn't show hooks the keys typed into apps running
// "as administrator", so double-tapping Ctrl there does nothing. Ctrl+Alt+S
// still works in those apps.
//
// Created by App.xaml.cs; turned on/off (together with Ctrl+Alt+S) by the
// "Open with double-tap Ctrl" setting (MainViewModel.OpenWithHotKey).
// ======================================================================

using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace SpaceKeeper.App.Services;

public sealed class DoubleTapCtrlService : IDisposable
{
    /// <summary>A tap must be shorter than this, so holding Ctrl doesn't count.</summary>
    private const int MaxTapMilliseconds = 350;

    private readonly DispatcherQueue _ui;
    private readonly Win32.LowLevelKeyboardProc _proc; // kept in a field so it isn't garbage-collected
    private readonly int _doubleTapWindow = (int)Win32.GetDoubleClickTime();
    private IntPtr _hook;
    private Thread? _thread;               // the background thread that owns the hook
    private uint _threadId;                // its Windows thread ID (to tell it to stop)

    // State of the current tap sequence.
    private bool _ctrlDown;            // Ctrl is being held right now
    private bool _otherKeyUsed;        // another key was pressed while Ctrl was down
    private long _ctrlDownAt;          // when Ctrl went down (Stopwatch ticks)
    private long _lastTapAt;           // when the previous clean tap finished (0 = none)

    /// <summary>Called on the UI thread after a double tap.</summary>
    public event EventHandler? Pressed;

    /// <summary>For the Diagnostics report.</summary>
    public string Status { get; private set; } = "off";

    /// <summary>How many double taps have been recognised (Diagnostics).</summary>
    public int PressCount { get; private set; }

    public DoubleTapCtrlService(DispatcherQueue ui)
    {
        _ui = ui;
        _proc = HookProc;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && _thread is null)
        {
            using var started = new ManualResetEventSlim();
            _thread = new Thread(() => RunHookThread(started)) { IsBackground = true, Name = "SpaceKeeper keyboard hook" };
            _thread.Start();
            started.Wait(TimeSpan.FromSeconds(2)); // wait until the hook is installed (or failed)
        }
        else if (!enabled && _thread is not null)
        {
            // Ask the hook thread to end its message loop; it removes the hook itself.
            Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
            _thread = null;
            Status = "off";
        }
    }

    /// <summary>
    /// The background thread: installs the hook, then waits for messages.
    /// Windows calls HookProc on this thread whenever a key is pressed.
    /// The loop ends when SetEnabled(false) posts WM_QUIT.
    /// </summary>
    private void RunHookThread(ManualResetEventSlim started)
    {
        _threadId = Win32.GetCurrentThreadId();
        ResetSequence();
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        Status = _hook != IntPtr.Zero ? "listening" : "couldn't start — Ctrl+Alt+S still works";
        started.Set();
        if (_hook == IntPtr.Zero) return;

        while (Win32.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref message);
            Win32.DispatchMessage(ref message);
        }

        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>
    /// Windows calls this for every key press and release. It must be quick
    /// (Windows skips hooks that take too long), so it only does a few
    /// comparisons and always passes the key on.
    /// </summary>
    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var message = (int)wParam;
                var isDown = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                var isUp = message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
                var injected = (info.flags & Win32.LLKHF_INJECTED) != 0; // typed by software, not a person
                if (!injected) Handle(info.vkCode, isDown, isUp);
            }
            catch (Exception)
            {
                // Never let a mistake here interfere with the user's typing.
                ResetSequence();
            }
        }
        return Win32.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Handle(uint vk, bool isDown, bool isUp)
    {
        var isCtrl = vk is Win32.VK_CONTROL or Win32.VK_LCONTROL or Win32.VK_RCONTROL;
        var now = Stopwatch.GetTimestamp();

        if (!isCtrl)
        {
            // Any other key (including Shift, Alt, Win) cancels the sequence:
            // this is a shortcut like Ctrl+C, or ordinary typing.
            if (isDown)
            {
                if (_ctrlDown) _otherKeyUsed = true;
                _lastTapAt = 0;
            }
            return;
        }

        if (isDown)
        {
            if (_ctrlDown) return; // auto-repeat while held
            _ctrlDown = true;
            _otherKeyUsed = false;
            _ctrlDownAt = now;
            return;
        }

        if (!isUp || !_ctrlDown) return;
        _ctrlDown = false;

        var heldFor = Stopwatch.GetElapsedTime(_ctrlDownAt, now).TotalMilliseconds;
        var cleanTap = !_otherKeyUsed && heldFor <= MaxTapMilliseconds;
        if (!cleanTap)
        {
            _lastTapAt = 0;
            return;
        }

        if (_lastTapAt != 0 && Stopwatch.GetElapsedTime(_lastTapAt, now).TotalMilliseconds <= _doubleTapWindow)
        {
            // Second tap: that's the shortcut. A third tap starts afresh.
            _lastTapAt = 0;
            PressCount++;
            // Open the panel a moment later, outside the hook, so the hook
            // itself stays fast and Windows never has to wait for us.
            _ui.TryEnqueue(() => Pressed?.Invoke(this, EventArgs.Empty));
        }
        else
        {
            _lastTapAt = now; // first tap; wait for the second
        }
    }

    private void ResetSequence()
    {
        _ctrlDown = false;
        _otherKeyUsed = false;
        _lastTapAt = 0;
    }

    public void Dispose() => SetEnabled(false);
}
