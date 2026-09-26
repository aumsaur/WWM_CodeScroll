# WWM Code Scroll

Redeem **Where Winds Meet** codes without leaving the game. A small HUD sits at the bottom of the screen showing the code Ctrl+V will paste; every paste loads the next one, so it's just Ctrl+V → redeem → Ctrl+V → redeem.

Codes are fetched fresh from [codes.yar.gg](https://codes.yar.gg/) (its `/api/codes` feed) every time it starts.

## Get it

**[Download the latest release](https://github.com/aumsaur/wwm_code_scroll/releases/latest)**, unzip it anywhere, and double-click `WWM-Redeem.exe`.

No install, and no .NET or other runtime needed: everything is packed inside the exe (about 72 MB), so you can copy it to any folder or PC running Windows 10 or 11 and run it from there. It is a **64-bit** build, which is part of what the size is for; on an ARM machine Windows runs it under emulation.

The first launch is slower than every launch after it. A single-file build unpacks itself into a temp folder before it starts, and that happens once per version.

Two prompts on the way in, both expected:

- **"Windows protected your PC"** — the exe is not code-signed, and a certificate costs real money for a free tool. Click **More info**, then **Run anyway**. The same two facts — unsigned, and a large file that unpacks itself — are why an antivirus scanner occasionally takes an interest.
- **The administrator prompt** — this one is not optional, and there is a reason for it further down: the game runs elevated, and Windows hides an admin window's key presses from programs that are not.

Codes come from [codes.yar.gg](https://codes.yar.gg/), which is somebody else's site. If it goes down or changes its feed, this stops finding new codes until it is updated.

## Run

Everything is controlled from the HUD. Clicking it never takes focus from the game.

- **✕**: exit the app
- **🔇 / 🔊**: sound, off by default. When on, it beeps on Ctrl+→ / Ctrl+← and after the last code.
- **⋯**, or right-click anywhere on the HUD, opens a row with:
  - **Refresh codes**: fetch again
  - **Mark all as pasted**: if you've already redeemed everything on the list
  - **Forget pasted codes**: start the whole list over
  - **Open folder**

The tray icon (jade ticket; on Windows 11 it may be under the **^** arrow by the clock) is a backup: double-click it or right-click › **Show / hide HUD** to bring back a hidden HUD, or **Exit**. If nothing responds at all, close it from Task Manager (Ctrl+Shift+Esc › WWM-Redeem › End task).

## Keys

These only work while the game is the front window, and the game still gets the keys too.

| Key | Does |
|---|---|
| Ctrl+V | Paste the current code; the next one loads right after |
| Ctrl+→ | Skip to the next code |
| Ctrl+← | Back to the previous code |
| Ctrl+↓ | Hide / show the HUD |

## How it behaves

- **Starts with a fetch.** If the site can't be reached it uses the last list it saved and says so in the HUD.
- **Remembers what you've pasted.** Codes you paste are written to `wwm-used.txt` next to the exe and skipped on later runs, so each run starts at the codes that are new since last time.
- **Why admin:** the game (`wwm.exe`) runs as administrator, and Windows hides an admin window's key presses from non-admin programs.
- **The HUD shows over the game in borderless or windowed mode.** Exclusive fullscreen hides every overlay; the keys still work there, and turning sound on (🔊) adds beeps so you can tell a skip landed.
- The HUD never takes focus: clicking its buttons leaves the game focused. Clicks on the HUD go to its buttons, not the game underneath, so press Ctrl+↓ to hide it when it's in the way. The app only watches the keyboard (a low-level keyboard hook) and sets the clipboard; it never sends input to the game.
- Files it writes next to the exe: `wwm-codes-cache.json` (last fetched list), `wwm-used.txt` (pasted codes), `wwm-redeem.log` (errors only), `wwm-settings.json` (the sound setting).

Options: `WWM-Redeem.exe --process <name>` listens to a different game process name (default `wwm`).

## Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/) (only to build, not to run).

```powershell
.\build.ps1              # -> dist\WWM-Redeem.exe
.\build.ps1 -AsInvoker   # -> dist-test\, a copy without the admin prompt, for testing
```

Close the running exe first (✕ on the HUD); Windows locks it while it runs.

The icon is drawn in `src\WwmRedeem\Assets\AppIcon.xaml`. After changing it, run `tools\make-icon.ps1` to regenerate `app.ico`.

For a quick visual check, `WWM-Redeem.exe --snapshot hud.png` fetches the codes, saves an image of the HUD, and exits without hooking the keyboard.

## Files

```
wwm_code_scroll/
├── build.ps1                 # Publishes the portable exe
├── dist/WWM-Redeem.exe       # Build output (not in git)
├── src/WwmRedeem/
│   ├── App.xaml(.cs)         # Startup, code queue, tray menu
│   ├── HudWindow.xaml(.cs)   # Bottom-of-screen HUD
│   ├── KeyWatcher.cs         # Keyboard hook (only while the game is in front)
│   ├── CodeStore.cs          # Fetch, cache, pasted-codes list
│   └── Assets/               # Icon drawing + app.ico
├── tools/make-icon.ps1       # Renders AppIcon.xaml into app.ico
└── script/                   # The earlier PowerShell-only version
```
