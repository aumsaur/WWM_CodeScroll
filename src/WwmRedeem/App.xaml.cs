using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WwmRedeem;

// Fetches the codes, then keeps the next one on the clipboard: every Ctrl+V in the
// game swaps in the following code, Ctrl+Left/Right step through the list.
//
// Arguments: --process <name>  game process to listen to (default wwm)
//            --snapshot <png>  dev only: render the HUD once the codes load, then exit
public partial class App : Application
{
    // Swap codes this long after Ctrl+V so the game has read the current one first.
    static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(400);

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly Settings _settings = Settings.Load();
    Mutex? _mutex;
    HudWindow _hud = null!;
    Forms.NotifyIcon? _tray;
    string _processName = "wwm";
    string? _snapshotPath;

    IReadOnlyList<string> _active = [];
    List<string> _queue = [];
    int _index;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ReadArgs(e.Args);
        DispatcherUnhandledException += (_, ev) => CodeStore.Log("Crash: " + ev.Exception);

        if (_snapshotPath == null)
        {
            _mutex = new Mutex(true, @"Local\WwmRedeemSingleton", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("WWM Redeem is already running - look for its icon in the tray.", "WWM Redeem");
                Shutdown();
                return;
            }
        }

        _hud = new HudWindow();
        _hud.SetSource("starting", warning: false);
        _hud.SetSound(_settings.Sounds);
        _hud.ExitClicked += () => Shutdown();
        _hud.SoundClicked += () =>
        {
            _settings.Sounds = !_settings.Sounds;
            _settings.Save();
            _hud.SetSound(_settings.Sounds);
        };
        _hud.RefreshClicked += () => _ = RefreshAsync();
        _hud.MarkAllClicked += () => { CodeStore.MarkUsed(_active); RebuildQueue(); };
        _hud.ForgetClicked += () => { CodeStore.ForgetUsed(); RebuildQueue(); };
        _hud.OpenFolderClicked += () => Process.Start("explorer.exe", CodeStore.Folder);
        _hud.Show();
        _tick.Tick += (_, _) => CheckGame();
        _tick.Start();
        CheckGame();

        if (_snapshotPath == null)
        {
            _tray = CreateTray();
            KeyWatcher.Start(Dispatcher, OnHotKey);
        }
        _ = RefreshAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        KeyWatcher.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    void ReadArgs(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--process") _processName = Path.GetFileNameWithoutExtension(args[++i]);
            else if (args[i] == "--snapshot") _snapshotPath = args[++i];
        }
    }

    async Task RefreshAsync()
    {
        _hud.ShowMessage("Fetching codes…", "from codes.yar.gg");
        try
        {
            var list = await CodeStore.LoadAsync();
            _active = list.Codes;
            _hud.SetSource(list.FromCache
                ? $"offline, saved list from {list.UpdatedAt.LocalDateTime:d MMM HH:mm}"
                : $"{list.Codes.Count} active codes, updated {Ago(list.UpdatedAt)}", warning: list.FromCache);
            RebuildQueue();
        }
        catch (Exception ex)
        {
            CodeStore.Log("Fetch failed: " + ex);
            _hud.SetSource("no connection", warning: true);
            _hud.ShowMessage("Couldn't get the codes", "Check your connection, then right-click the tray icon › Refresh codes");
        }

        if (_snapshotPath != null)
        {
            await Task.Delay(500);   // let the code's fade-in finish
            _hud.SaveSnapshot(_snapshotPath);
            Shutdown();
        }
    }

    // Codes pasted in earlier runs are skipped, so each run starts at the new ones.
    void RebuildQueue()
    {
        var used = CodeStore.LoadUsed();
        _queue = _active.Where(c => !used.Contains(c)).ToList();
        _index = 0;
        ShowCurrent();
    }

    void ShowCurrent()
    {
        if (_active.Count == 0)
            _hud.ShowMessage("No active codes right now", "Check back later - right-click the tray icon › Refresh codes");
        else if (_queue.Count == 0)
            _hud.ShowMessage("No new codes", $"All {_active.Count} active codes were pasted before · tray › Forget pasted codes to redo them");
        else if (_index >= _queue.Count)
            _hud.ShowMessage("All done", $"Pasted all {_queue.Count} new codes · Ctrl ← to go back");
        else
        {
            SetClipboard(_queue[_index]);
            _hud.ShowCode(_queue[_index], _index + 1, _queue.Count, _queue.Skip(_index + 1).Take(2).ToList());
        }
    }

    void OnHotKey(HotKey key)
    {
        switch (key)
        {
            case HotKey.Paste: _ = PastedAsync(); break;
            case HotKey.Next: Move(+1); break;
            case HotKey.Prev: Move(-1); break;
            case HotKey.ToggleHud: ToggleHud(); break;
        }
    }

    async Task PastedAsync()
    {
        if (_index >= _queue.Count) return;
        int pastedAt = _index;
        CodeStore.MarkUsed([_queue[pastedAt]]);
        _hud.ShowPasted();
        await Task.Delay(PasteSettle);
        if (_index != pastedAt) return;   // moved with Ctrl+Left/Right in the meantime
        _index++;
        if (_index == _queue.Count) Beep(440, 300);
        ShowCurrent();
    }

    void Move(int step)
    {
        if (_queue.Count == 0) return;
        _index = Math.Clamp(_index + step, 0, _queue.Count - 1);
        // For when the HUD is hidden or behind a fullscreen game: high = forward, low = back.
        Beep(step > 0 ? 1200 : 600, 80);
        ShowCurrent();
    }

    void ToggleHud()
    {
        if (_hud.IsVisible) _hud.Hide();
        else
        {
            _hud.Show();
            _hud.KeepOnTop();
        }
    }

    void CheckGame()
    {
        var pids = Process.GetProcessesByName(_processName).Select(p => { using (p) return p.Id; }).ToArray();
        KeyWatcher.TargetPids = pids;
        _hud.SetGame(pids.Length > 0);
        _hud.KeepOnTop();
    }

    Forms.NotifyIcon CreateTray()
    {
        // Running elevated, Windows can drop the clicks the (non-elevated) taskbar sends
        // to the tray icon, so let them through: 0x800 is WinForms' tray callback message,
        // TaskbarCreated brings the icon back after Explorer restarts.
        NativeMethods.ChangeWindowMessageFilter(0x800, NativeMethods.MSGFLT_ADD);
        NativeMethods.ChangeWindowMessageFilter(NativeMethods.RegisterWindowMessage("TaskbarCreated"), NativeMethods.MSGFLT_ADD);

        // Everything else is on the HUD; the tray is just a way back when it's hidden.
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide HUD", null, (_, _) => ToggleHud());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        var tray = new Forms.NotifyIcon { Icon = LoadTrayIcon(), Text = "WWM Redeem", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ToggleHud();
        return tray;
    }

    static Drawing.Icon LoadTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    static void SetClipboard(string code)
    {
        // Another app can hold the clipboard open for a moment; WinForms retries for us.
        try { Forms.Clipboard.SetDataObject(code, true, 10, 50); }
        catch (Exception ex) { CodeStore.Log($"Couldn't set the clipboard to {code}: {ex.Message}"); }
    }

    void Beep(int frequency, int ms)
    {
        if (_settings.Sounds) Task.Run(() => Console.Beep(frequency, ms));
    }

    static string Ago(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }
}
