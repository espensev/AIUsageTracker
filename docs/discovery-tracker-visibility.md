# AI Usage Tracker visibility — completed discovery

The 2026-09-28 discovery found a hidden tray-first desktop app whose duplicate
launches exited without activating the existing window. Startup pointed at a
worktree build. The Web dashboard at `http://localhost:5100/` was the immediate
fallback; a process's absent main-window handle alone did not establish
visibility.

The proposed tray/shortcut work was completed later that day. Current
`AIUsageTracker.UI.Slim/App.TrayIcon.cs`, `App.xaml.cs` and
`Services/SingleInstanceLockService.cs` retain single-click opening,
ShowAndActivate restoration and local/user-scoped duplicate activation.
Startup/test/screenshot duplicates remain silent.

[The completion review](reviews/review-2026-09-28-tracker-visibility.md)
preserves source identity, deployment/shortcut evidence, rollback and
independent installed checks. It records a promoted tray icon, Desktop and
Start-menu native shortcuts, a Desktop Web shortcut and a stable installed
Desktop/current junction while preserving the existing logon task's RunW
wrapper, backend processes and provider settings.

## Remaining limits

- Windows controls foreground focus; restoring/showing a window does not
  guarantee unconditional foreground transfer.
- No full sign-out/reboot was tested. Explicit task launches and unchanged
  task configuration were the startup evidence.
- A global hotkey was unnecessary for this scope.
- App-level X hide must be used when reproducing tray reopening; raw Win32
  hiding bypasses WPF state and WM_CLOSE exits the app.
- These are historical installed observations, not a current runtime census.
  Do not create a second HKCU startup owner or revive shortcuts to worktree
  build outputs from the original discovery.
