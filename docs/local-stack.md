# Local Monitor + Web stack

The always-on deployment on a workstation is two scheduled tasks that run a published
build of `AIUsageTracker.Monitor` and `AIUsageTracker.Web` from a release directory.
`scripts/local-stack.ps1` owns publishing, task wiring, restart order and verification.

```powershell
pwsh -File scripts/local-stack.ps1                 # deploy the current checkout
pwsh -File scripts/local-stack.ps1 -Action status  # inspect without changing anything
Invoke-Pester -Path scripts/local-stack*.Tests.ps1  # helpers and mocked system boundaries
```

## Layout

| Piece | Where | Notes |
|---|---|---|
| Releases | `%LOCALAPPDATA%\Programs\AIUsageTracker\Web\releases\<sha8>-<yyyyMMdd>` | Both projects published into one directory, framework-dependent (`dotnet publish -c Release`). A `-dirty` suffix means tracked modifications or untracked files were present at publish time; ignored build output is excluded. |
| Monitor task | discovered by its action (`AIUsageTracker.Monitor.exe`) | Boot trigger, S4U (no stored password), restart on failure 10 x 1 min, no time limit. |
| Web task | discovered by its action (`AIUsageTracker.Web.exe --urls http://localhost:5100`) | Logon trigger, hidden, same restart policy. |
| Data | `%LOCALAPPDATA%\AIUsageTracker` | `usage.db`, `preferences.json`, `providers.json`, `monitor.json`, `logs\`. Never inside a release directory. |

Task discovery is by executable name, and ownership must be unambiguous: when several tasks
run the same executable, the script refuses to act and names every candidate, because it
cannot know which installation is yours. When neither task exists, they are created under
`-TaskFolder` (default `\SevGrp\AIUsageTracker\`). New folder overrides must name an owner
under `\SevGrp\`; root, `\MyTasks\`, bare `\SevGrp\`, and unrelated namespaces are rejected
before publishing or changing scheduler state. Existing owner folders under `\Sevnet\`
or `\AdminTasks\` are preserved; existing tasks in forbidden folders require remediation
by their owner before this script can deploy.

`-WebUrl` configures the launched Web task's `--urls` argument and its readiness
checks. Existing non-URL arguments are preserved, and rollback restores the
original arguments for both applications. The existing Web listener is stopped
using its previous URL before the new URL is activated. URLs must name one
loopback HTTP(S) origin; ambiguous or malformed existing `--urls` arguments fail
preflight.

Deployment supports Monitor port 5000 and the default Windows LocalApplicationData
root. A different `-MonitorPort` or `AIUSAGETRACKER_LOCAL_APP_DATA_ROOT` is rejected
before publishing or changing tasks because Monitor does not implement those
overrides. Status inspection can still use its explicit probe settings.

## Deploy sequence

1. Preflight: permitted task namespaces, git sha and dirty state, dotnet on PATH, both tasks
   found (or neither), and the installed DevMesh machine-identity verifier resolved from the
   Windows LocalApplicationData known folder. Deployment requires exactly one `VERIFIED`
   result for `snd-desk`, instance `ca96d510-7d87-4cec-8e1a-bd8fc3866903`; an absent verifier
   or any other result stops deployment. `-Action status` remains available without this gate.
2. Publish Monitor and Web into the new release directory and check the required files.
3. Stop the Web task, then the Monitor task. Web goes first because the dashboard can
   relaunch a Monitor it believes is missing, and that launch would come from the old release.
   Only processes whose executable lives in the tasks' own release directories are stopped;
   same-named executables elsewhere (another installation) are preserved. If the port never
   becomes free, deployment is refused instead of continuing behind an old listener.
4. Replace only the task actions (`Set-ScheduledTask -Action`); principal, triggers and
   settings are untouched. The rollback lines are prepared before the first action is
   replaced; if a later update fails, the deploy prints them and restores the tasks it
   already re-pointed at their previous release.
   On a first installation, a failed Web registration removes only the Monitor task
   successfully created by that attempt; pre-existing tasks are never removed.
5. Start Monitor and wait for port 5000 plus a fresh `monitor.json` (`StartedAt`, live pid).
6. Start Web and wait for `GET /` to answer 200 and `/api/monitor/status` to report
   `isRunning` and `isContractCompatible`. A `degraded` service health (a provider failing)
   is reported, not treated as a deploy failure.
7. Print the previous release and the `Set-ScheduledTask` rollback lines.

Old release directories are left in place; the previous one is the rollback target.

## Rollback

Run the lines the deploy printed (they point both tasks back at the previous release), then
stop Web, stop Monitor, start Monitor, start Web. The release directories are immutable once
published, so rolling back is only a task-action change. A failure between the two action
updates is rolled back automatically (both tasks end up at their previous release, with the
printed stop/start lines still to run); failures after both updates are rolled back by
running the printed lines.
