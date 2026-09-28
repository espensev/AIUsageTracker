# Discovery — AI Usage Tracker visibility

**Goal:** Find AI Usage Tracker and evaluate easier access through the tray or shortcuts.
**Date:** 2026-09-28 (Europe/Berlin)
**Status:** complete
**Recommended next:** Surface the existing tray icon; use a Web shortcut as fallback. Native shortcuts need an activation fix.

## Questions

1. Where is the tracker, and which launch surfaces exist?
2. Does the desktop app already support a tray icon?
3. Would shortcuts reliably reopen it?

## Findings

### 1. Location and current access

Source is `D:\AI4000\usage\AIUsageTracker`. The running desktop executable is
`D:\AI4000\worktrees\AIUsageTracker\cli-home-env\AIUsageTracker.UI.Slim\bin\Release\net10.0-windows10.0.17763.0\AIUsageTracker.exe`.
It reports version `2.4.7+af688ee29c3f3f3826134555e905a18a57b3a7c7`.

Live checks on verified controller `snd-desk`, instance
`ca96d510-7d87-4cec-8e1a-bd8fc3866903`:

- `Get-Process`: desktop PID 27868; no reported main window handle/title. This alone does not prove the window's visibility because it is excluded from the taskbar.
- `Get-ScheduledTask` / `Get-ScheduledTaskInfo`: existing `AIUsageTracker UI` logon task launches that executable; last run 2026-09-27 22:40:29 local, result 0.
- Start menu and desktop enumeration: only `AI Usage Tracker Web.url` found, under the user's Start menu Programs folder, targeting `http://localhost:5100/`. No native app or desktop shortcut found in the current/common Desktop and Programs folders.
- `GET http://localhost:5100/`: HTTP 200, title `Dashboard - AI Usage Tracker`.
- `GET http://localhost:5000/api/health`: HTTP 200, status `healthy`.
- Monitor and Web processes run from `%LOCALAPPDATA%\Programs\AIUsageTracker\Web\releases\0168bb1b-20260922`.

**Implication:** The tracker is already running and its Web dashboard is immediately usable. Desktop startup is tied to a development build directory.

### 2. Existing tray behavior

The main tray icon is created during ordinary startup (`AIUsageTracker.UI.Slim/App.xaml.cs:151`). Double-click opens the window; right-click offers **Show** (`AIUsageTracker.UI.Slim/App.TrayIcon.cs:238`, `:259`). The main icon has no single-click open handler.

Windows has a `NotifyIconSettings` entry matching the running executable; `IsPromoted` is absent. A passive UI Automation inspection of `Shell_TrayWnd` found **Show Hidden Icons** but no visible AI Usage Tracker icon. This supports checking the overflow area; the overflow panel itself was not opened, so placement there is not directly verified.

The main window sets `ShowInTaskbar="False"` (`AIUsageTracker.UI.Slim/MainWindow.xaml:14`). Closing it or pressing Escape/Ctrl+Q hides it (`MainWindow.xaml.cs:720`, `:881`, `:890`).

Optional provider usage icons already exist under **Settings → provider → Tray** (`SettingsWindow.Providers.cs:512`). They require an enabled, available provider and a description without “unknown” (`App.TrayIcon.cs:89`). Clicking a provider icon opens the main window (`App.TrayIcon.cs:155`).

**Implication:** Promote the existing main icon first. One or two provider icons can add usage visibility without opening the window. Microsoft documents dragging an icon from overflow into the visible system tray: [Customize the taskbar](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows).

### 3. Shortcut reliability

A duplicate launch acquires no instance lock and exits without signaling the running window (`AIUsageTracker.UI.Slim/App.xaml.cs:100`; `Services/SingleInstanceLockService.cs:61`). A plain executable shortcut therefore cannot reliably reopen an already-running hidden instance. No global show hotkey or `--show` activation path was found.

The installer normally supplies a Start menu shortcut and offers an optional desktop shortcut (`scripts/setup.iss:229`, `:244`, `:249`). That does not solve duplicate-instance activation.

**Implication:** A shortcut to `http://localhost:5100/` is the reliable immediate fallback while the Web service is running. Native shortcuts should follow a fix that tells the existing instance to show and activate its window.

## Constraints and risks

- Preserve the existing logon task. The app's **Start with Windows** option writes a separate HKCU Run entry targeting the current executable directory (`WindowsStartupService.cs:29`); enabling it now risks redundant startup ownership.
- A shortcut to the worktree build is fragile if that worktree or build output moves. Prefer a stable installed launcher for a durable native shortcut.
- Main tray click currently calls `Show()` / `Activate()` (`App.TrayIcon.cs:282`). The existing `ShowAndActivate()` helper also restores minimized state (`MainWindow.xaml.cs:506`); use that helper for future activation improvements.
- Tray, startup, main-window, and instance-lock source files match between canonical checkout and running worktree, and have no changes from the build's recorded commit. No runtime interaction test of tray opening was performed.
- Existing canonical changes were preserved: modified `README.md` and untracked `docs/reviews/review-2026-09-21-monitor-stack.md`. The running worktree was clean.

## Recommendation

1. **Tray first:** expose the existing main icon in Windows' visible system tray. Double-click it, or use **right-click → Show**. Optionally enable selected provider usage icons.
2. **Immediate fallback:** add a desktop shortcut to the healthy Web dashboard; Start search already offers **AI Usage Tracker Web**.
3. **Small product improvement:** add single-click main-tray opening and duplicate-instance activation through `ShowAndActivate()`. Then provide durable native Start menu/desktop shortcuts, with an optional global hotkey.

This evaluation changed only this document. App settings, registry, shortcuts, startup tasks, running processes, and remote surfaces were left unchanged. Tray placement and reopening should be verified interactively when applying the recommendation.
