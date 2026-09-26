using System.Runtime.InteropServices;
using System.Windows.Threading;
using static WwmRedeem.NativeMethods;

namespace WwmRedeem;

public enum HotKey { Paste, Prev, Next, ToggleHud }

// Global low-level keyboard hook. It only reacts while the game's window is in
// front and never swallows keys, so the game still sees every press.
public static class KeyWatcher
{
    const uint VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    const uint VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_V = 0x56;

    // Kept in a static field so the GC can't collect the delegate while Windows still calls it.
    static readonly HookProc Proc = OnKey;
    static readonly bool[] Held = new bool[256];
    static IntPtr _hook;
    static uint _threadId;
    static bool _ctrlDown;
    static Dispatcher? _dispatcher;
    static Action<HotKey>? _handler;
    static volatile int[] _targetPids = [];

    /// <summary>Process ids of the game; keys only count while one of their windows is in front.</summary>
    public static int[] TargetPids { get => _targetPids; set => _targetPids = value; }

    // The hook gets its own thread with a message loop, and the callback only posts to
    // the UI thread: Windows silently removes a low-level hook whose callback runs long.
    public static void Start(Dispatcher dispatcher, Action<HotKey> handler)
    {
        _dispatcher = dispatcher;
        _handler = handler;
        string? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) error = "SetWindowsHookEx failed, error " + Marshal.GetLastWin32Error();
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_hook);
        }) { IsBackground = true, Name = "KeyWatcher" };
        thread.Start();
        ready.Wait();
        if (error != null) throw new InvalidOperationException(error);
    }

    public static void Stop()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    static IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint vk = (uint)Marshal.ReadInt32(lParam) & 0xFF;
            int msg = (int)wParam;
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            if (vk is VK_LCONTROL or VK_RCONTROL or VK_CONTROL)
            {
                if (down) _ctrlDown = true; else if (up) _ctrlDown = false;
            }
            // Holding a key auto-repeats key-downs; only the first one counts.
            else if (down && !Held[vk] && _ctrlDown)
            {
                HotKey? key = vk switch
                {
                    VK_V => HotKey.Paste,
                    VK_LEFT => HotKey.Prev,
                    VK_RIGHT => HotKey.Next,
                    VK_DOWN => HotKey.ToggleHud,
                    _ => null,
                };
                if (key is { } k && GameInFront()) _dispatcher!.BeginInvoke(_handler!, k);
            }
            if (down) Held[vk] = true; else if (up) Held[vk] = false;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static bool GameInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return Array.IndexOf(_targetPids, (int)pid) >= 0;
    }
}
