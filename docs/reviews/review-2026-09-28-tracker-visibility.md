# Review — AI Usage Tracker visibility

**Date:** 2026-09-28
**Surface:** `2cb61bce..290399da`, plus the installed desktop UI on `snd-desk`.
**Spec source:** User request to improve visibility through the tray or shortcuts, then address and review the findings.
**Standards:** `AGENTS.md`, `docs/wpf_async_best_practices.md`, existing tray/window and single-instance patterns.
**Verdict:** PASS WITH NOTES — source accepted and installed visibility criteria verified independently.

## Findings

No blocking source findings. An independent reviewer accepted all five changed files after checking startup races, queued and repeated activation, mutex exclusion, launch modes, listener teardown, dispatcher shutdown, and the tests.

The main icon now opens with a single left click. Tray opening and ordinary duplicate launches use the existing `ShowAndActivate()` path, which shows and restores the window. Duplicate launches request activation through a named event in the existing local/user mutex scope. Startup/test/screenshot duplicates remain silent.

## Applied locally

- Verified controller: `snd-desk`, instance `ca96d510-7d87-4cec-8e1a-bd8fc3866903`.
- Promoted the tracker tray entry. Main tracker and existing Claude usage icons are visible.
- Added **AI Usage Tracker** shortcuts to Desktop and Start menu.
- Added **AI Usage Tracker Web** on Desktop; the existing Start menu Web shortcut remains available at `http://localhost:5100/`.
- Published only the desktop UI from commit `290399dad37386bce931758966b9975e932a1583` into `%LOCALAPPDATA%\Programs\AIUsageTracker\Desktop\releases\290399da-20260928`.
- Shortcuts and the existing logon task use the stable `Desktop\current` junction.
- Preserved the task's `runw` wrapper. XML comparison confirmed unchanged triggers, principal, settings, and registration information.
- Monitor PID 12268 and Web PID 18044 retained their original executable paths. Provider settings and backend tasks were not edited.

## Verification

- Release Slim build and UI-only publish: pass, zero errors; eight existing analyzer-configuration warnings.
- Startup/window/IPC/guardrail tests: **68 passed, zero failed or skipped**.
- Independent frozen activation/window tests: **12 passed**.
- `git diff --check 2cb61bce`: pass.
- Installed activation checks: actual app **X** hides; one tray invocation opens; Desktop and Start menu shortcuts restore hidden and minimized windows on the same process; ordinary duplicates exit; `--startup` duplicates leave the window hidden.
- Installed restart: pass. Final UI PID 66968 is visible and restored; its promoted main tray icon survives restart. After restart, actual X hide followed by one tray invocation restored the same new process.
- Web dashboard and Monitor health endpoints: HTTP 200. A transient Kimi/Z.ai refresh-degradation flag observed during QA subsequently cleared without intervention.

Initial automation checks were corrected to use the app's actual X button and to serialize duplicate-process completion. Raw Win32 hiding bypasses WPF visibility state; WM_CLOSE exits this app. Those operations do not reproduce its custom X button's hide behavior.

The graceful shutdown exceeded the independent harness's 15-second deadline. The raw timeout receipt was preserved; a separate completion check verified the old process exited, the UI restarted, and tray visibility persisted. The final backend check reported both health fields healthy with no failing providers.

## Coverage and remaining limits

Deep review covered `App.xaml.cs`, `App.TrayIcon.cs`, `Services/SingleInstanceLockService.cs`, `UI/SingleInstanceLockServiceTests.cs`, and `UI/DialogOpenBehaviorTests.cs`. The source base differs from the previously running build only in documentation, Seeder, and tests; no unrelated product changes were introduced by selecting it.

Windows controls foreground focus. The app requests activation and restores visibility; foreground transfer was not treated as an unconditional Windows guarantee. The automated WPF regression tests the real restore handler; installed QA covers the tray event and separate-process launches.

A full Windows sign-out/reboot was not performed. Task configuration and explicit task launches are the startup evidence. No global hotkey or provider configuration change was needed for this scope.

## Delivery and rollback

Code is committed on `fix/tracker-visibility-20260928` in the isolated `tracker-visibility` worktree. The canonical checkout's unrelated changes and the old running worktree were preserved. No remote publication was performed.

Local evidence is under the AI4000 workspace's `_organization/tracker-visibility-2026-09-28/`: original/new task XML, deployment and shortcut manifests, test/build logs, backend checks, `activation-independent.json`, and `restart-independent.json`. The old executable is retained. To roll back the UI, restore the original task action from `ui-task-before.xml`, stop only the verified new UI process, and launch the original task. Backend processes and user data need no rollback.
