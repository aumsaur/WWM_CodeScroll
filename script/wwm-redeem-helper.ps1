# FIFO clipboard for Where Winds Meet redeem codes, driven from inside the game:
#   Ctrl+V      paste the current code; the next one is swapped in right after
#   Ctrl+Right  skip to the next code
#   Ctrl+Left   back to the previous code
# The first Ctrl+V picks the window it listens to (so paste into the game first);
# these keys in other apps are ignored after that.
# Keys in this window: r = re-pick the window, q = quit.
# To resume later: -Start <number of the code to begin at>.
param([int]$Start = 1)

$codes = @(Get-Content -Path (Join-Path $PSScriptRoot 'wwm-codes.txt') | ForEach-Object { $_.Trim() } | Where-Object { $_ })

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class WwmPasteWatcher
{
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

    // Kept in a static field so the GC can't collect the delegate while Windows still calls it.
    static readonly HookProc proc = OnKey;
    static readonly bool[] held = new bool[256];
    static IntPtr hook;
    static uint hookThreadId;
    static uint pasteVk, prevVk, nextVk;
    static bool ctrlDown;
    static int pasteCount, prevCount, nextCount, lockedPid;

    public static int PasteCount { get { return Thread.VolatileRead(ref pasteCount); } }
    public static int PrevCount { get { return Thread.VolatileRead(ref prevCount); } }
    public static int NextCount { get { return Thread.VolatileRead(ref nextCount); } }
    public static int LockedPid { get { return Thread.VolatileRead(ref lockedPid); } }
    public static void Unlock() { Interlocked.Exchange(ref lockedPid, 0); }

    // The hook gets its own thread with a message loop: Windows silently drops a
    // low-level hook whose thread is busy (e.g. sleeping in PowerShell) when a key arrives.
    public static void Start(uint paste, uint prev, uint next)
    {
        pasteVk = paste; prevVk = prev; nextVk = next;
        string error = null;
        var ready = new ManualResetEvent(false);
        var thread = new Thread(() =>
        {
            hookThreadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = "SetWindowsHookEx failed, error " + Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
        });
        thread.IsBackground = true;
        thread.Start();
        ready.WaitOne();
        if (error != null) throw new InvalidOperationException(error);
    }

    public static void Stop() { PostThreadMessage(hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero); }

    // Only counts key presses; PowerShell does the clipboard work. Keys are always
    // passed on, so the game still sees them.
    static IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint vk = (uint)Marshal.ReadInt32(lParam) & 0xFF;
            int msg = wParam.ToInt32();
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (vk == 0xA2 || vk == 0xA3 || vk == 0x11)   // left, right or generic Ctrl
            {
                if (down) ctrlDown = true; else if (up) ctrlDown = false;
            }
            // Holding a key auto-repeats key-downs; only the first one counts.
            else if (down && !held[vk] && ctrlDown)
            {
                if (vk == pasteVk && InLockedProcess(true)) Interlocked.Increment(ref pasteCount);
                else if (vk == prevVk && InLockedProcess(false)) Interlocked.Increment(ref prevCount);
                else if (vk == nextVk && InLockedProcess(false)) Interlocked.Increment(ref nextCount);
            }
            if (down) held[vk] = true; else if (up) held[vk] = false;
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    // The first paste picks the process (claim = true); after that, keys pressed
    // in any other process are ignored.
    static bool InLockedProcess(bool claim)
    {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        int locked = claim ? Interlocked.CompareExchange(ref lockedPid, (int)pid, 0) : LockedPid;
        return locked == 0 || locked == (int)pid;
    }
}
'@

function Set-Code([string]$code) {
    # Another app can hold the clipboard open for a moment, so retry briefly.
    for ($try = 0; $try -lt 20; $try++) {
        try {
            Set-Clipboard -Value $code
            if ((Get-Clipboard -Raw) -ceq $code) { return }
        } catch { }
        Start-Sleep -Milliseconds 50
    }
    Write-Warning "Couldn't put $code on the clipboard - type that one by hand."
}

function Go-To([int]$index) {
    $script:i = [Math]::Min([Math]::Max(0, $index), $codes.Count - 1)
    Set-Code $codes[$script:i]
    Write-Host ("[{0}/{1}] {2} is on the clipboard" -f ($script:i + 1), $codes.Count, $codes[$script:i])
}

# The game runs as administrator, and Windows hides an admin window's key presses
# from non-admin programs, so relaunch elevated.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "The game runs as administrator, so this has to as well - approve the Windows prompt."
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Start', $Start -ErrorAction Stop
        Write-Host "Opened in a new admin window - use that one."
    } catch {
        Write-Host "Not elevated, so the keys won't work while the game is focused." -ForegroundColor Yellow
    }
    return
}
Write-Host "In game: Ctrl+V paste (then next code), Ctrl+Right skip, Ctrl+Left back. Paste into the game first - that's the window I'll listen to."
Write-Host "Keys here: r = re-pick window, q = quit."

Go-To ($Start - 1)
[WwmPasteWatcher]::Start(0x56, 0x25, 0x27)   # V, Left, Right
$seenPaste = 0; $seenPrev = 0; $seenNext = 0
$quit = $false
$canReadKeys = -not [Console]::IsInputRedirected
try {
    while (-not $quit) {
        Start-Sleep -Milliseconds 50
        if ([WwmPasteWatcher]::PasteCount -ne $seenPaste) {
            Start-Sleep -Milliseconds 400   # let the game finish reading the clipboard first
            $seenPaste = [WwmPasteWatcher]::PasteCount
            $app = (Get-Process -Id ([WwmPasteWatcher]::LockedPid) -ErrorAction SilentlyContinue).ProcessName
            Write-Host "  pasted into $app"
            if ($i -eq $codes.Count - 1) { Write-Host "That was the last code."; [Console]::Beep(440, 300) }
            else { Go-To ($i + 1) }
        }
        $prev = [WwmPasteWatcher]::PrevCount; $next = [WwmPasteWatcher]::NextCount
        if ($prev -ne $seenPrev -or $next -ne $seenNext) {
            $steps = ($next - $seenNext) - ($prev - $seenPrev)
            $seenPrev = $prev; $seenNext = $next
            Go-To ($i + $steps)
            # The console isn't visible in game, so beep: high = forward, low = back.
            if ($steps -ge 0) { [Console]::Beep(1200, 80) } else { [Console]::Beep(600, 80) }
        }
        if ($canReadKeys -and [Console]::KeyAvailable) {
            switch ("$([Console]::ReadKey($true).KeyChar)") {
                'q' { $quit = $true; Write-Host "Stopped. To continue later: -Start $($i + 1)" }
                'r' { [WwmPasteWatcher]::Unlock(); Write-Host "The next Ctrl+V picks the window again." }
            }
        }
    }
} finally {
    [WwmPasteWatcher]::Stop()
}
Read-Host "Press Enter to close"
