# Manual + Automated Test Run — Automatic Updates Policy

Verifies that registry-driven Group Policy actually controls the ZDEW monitor service (auto-update settings),
that the UI reflects the locked state, and that the bug fixes for sentinel UX, deferred-install lag, CheckUpdate
concurrency, and WMI watcher value-write-after-recreate are all in place.

---

## Audience — read the section that fits you

### 🤖 If you're an automation agent (Claude Code or similar)

You drive everything you can over SSH and read the service log directly. You ask the human only for things
that physically require eyes/clicks/reboot:

- **SSH** to the test VM as an admin (`LocalAccountTokenFilterPolicy=1` on the VM, `Bash(ssh.exe *)` on your allowlist).
- **Read** the service log directly at `z:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log`
  (Z: maps to the VM's C:). Don't ask the human to tail/paste.
- **Drop reusable PowerShell** in `z:\test\` (= `C:\test\` on the VM). Invoke via SSH:
  `ssh.exe -o BatchMode=yes user@<vm-ip> powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\test\<name>.ps1'`.
- **Don't use `tail -F` over the SMB share** — it doesn't reliably see live writes. Wait briefly, then
  `grep` the file fresh.
- **Restart the service in your setup scripts** (`Restart-Service ziti-monitor`) instead of waiting for natural
  poll ticks. The service performs an immediate first update check at startup, so you don't get stuck behind
  the FastInterval persistence quirk that can revert polling to 10 minutes.
- **You can't drive UI clicks, reboots, or relaunching the UI from interactive desktop session.** Those
  Block D/E/F tests are still human-driven.

### 👤 If you're a human

You're running from a pwsh prompt on a Windows test VM (or two boxes — server + VM). You'll paste the
`ACTION` blocks, observe the UI for visual checks, and click buttons / reboot / relaunch when asked.
Skip the agent-specific notes above.

---

## Machines

- 🧪 **TEST VM** — where ZDEW is installed, where the registry policy is written, where the UI is observed.
- 🖥️ **SERVER** — where you build dev installers and serve `release-streams/` over `python -m http.server`.
  Can be the same machine as the VM, but separating them is cleaner.

The driver = me, talking to the test VM via SSH and reading
`z:\…\ZitiUpdateService.log` directly.

---

## Prep — do once per session

### Both machines, every new pwsh shell

```powershell
$ServerHost   = 'your.actual.server.name'
$ServerPort   = 8000
$StreamUrl    = "http://${ServerHost}:${ServerPort}/release-streams/local"
$LocalJsonUrl = "$StreamUrl/local.json"

# Polling intervals for dev builds.
# $fast = 10  : default, used by every test except Test 7
# $slow = 60  : used by Test 7's first build to disambiguate the Thread.Sleep(30) trigger from the next poll
$fast = 10
$slow = 60

Write-Host "StreamUrl=$StreamUrl  fast=$fast  slow=$slow"
```

### VM — fresh ZDEW install + clean policy state

```powershell
.\prepare-for-tests.ps1 -PurgeData
Get-Service ziti-monitor | Format-Table -AutoSize
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue
```

Launch the ZDEW UI from the Start Menu. **Expected:** service running, no policy in registry, UI visible.

### Server — clean stale build artifacts and start the HTTP server

```powershell
$repoRoot  = '<path-to-the-conflict-free-worktree-or-wherever-build-output-lands>'
$localDir  = "$repoRoot\release-streams\local"
if (Test-Path $localDir) { Remove-Item -Recurse -Force "$localDir\*" }

# In its own pwsh window — leave running for the whole session:
python -m http.server $ServerPort --directory $repoRoot
```

`http://${ServerHost}:${ServerPort}/release-streams/local/local.json` should resolve from the VM.

### Server — build the dev installer that the test VM will run

```powershell
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval $fast
```

After it completes, click **Check for updates** in the VM UI to install it. Confirm in the service log:

```
Version Checker is running every 10 seconds
```

You're now on a dev build with policy support and 10-second polling. Ready for the tests.

---

## Test legend

| Tag | Meaning |
|---|---|
| 🤖 AUTO       | Driver runs everything via SSH and watches the log. No human attention required. |
| 👀 VISUAL     | Driver runs the registry/build steps; the human glances at the UI for the banner / lock / dimming. |
| 👆 CLICK      | Requires the human to click a UI button (Perform Update, identity selector, etc.). |
| 🔄 REBOOT     | Requires the VM to be rebooted. |
| 🪟 UI-LAUNCH  | Requires the human to relaunch the UI after a kill (`Start-Process` over SSH lands in the wrong session). |

Tests are grouped by automation tier. Inside each group, ordering minimizes rebuilds and shared-state churn.

---

# Block A — 🤖 AUTO, no dev install required

These verify policy load / logging / debounce behavior without auto-installing anything. Fastest section.

## Test 6 — `AlivenessChecksBeforeAction` policy loads

**Checks:** registry override of aliveness threshold reaches PolicySettings and is reflected in the
loaded-policy log line.

- ACTION (vm): write `AlivenessChecksBeforeAction=3` to the policy key, no other values.
- EXPECT: `Policy overrides loaded: ... AlivenessChecksBeforeAction=3 ...` within ~10 s.
- CLEANUP: wipe `HKLM:\SOFTWARE\Policies\NetFoundry`.

## Test 11 — Unreachable update URL → graceful retry

**Checks:** WebException on the policy URL doesn't crash the service or stop polling.

- ACTION (vm): policy `AutomaticUpdateURL=http://192.0.2.1:9999/nope.json`,
  `AutomaticUpdatesDisabled=0`.
- EXPECT: at least two `WebException ... 192.0.2.1:9999` lines on consecutive ticks (~10 s apart),
  no `OnStop`, no crash.
- CLEANUP: wipe policy.

## Test 19 — Rapid policy toggle → debounce settles to final state

**Checks:** the 500 ms debounce collapses 5 rapid value writes into a single `Load()`, and the loaded
state matches the last write.

- ACTION (vm): pre-create the policy key with `AutomaticUpdateURL` set, `Restart-Service ziti-monitor`
  (so the watcher is alive), then toggle `AutomaticUpdatesDisabled` 5 times rapidly: `1,0,1,0,1`.
- EXPECT: exactly one `Policy overrides loaded` line after the burst, with `AutomaticUpdatesDisabled=True`.
  (Multiple `Policy registry event received` DEBUG lines is fine — we want one Load, not one per event.)
- CLEANUP: wipe policy.

## Test 15 — Restart-Service with policy already in registry (startup load path)

**Checks:** policy is loaded *before* `Initializing` and `starting service watchers`, so the very first
update check uses policy values.

- ACTION (vm): write full policy (URL + window 22-6 + `AutomaticUpdatesDisabled=0` +
  `DeferInstallToRestart=1` + reminder/critical/aliveness), then `Restart-Service ziti-monitor`.
- EXPECT, in order:
  1. `Policy overrides loaded:` (with our values)
  2. `Initializing` / `starting service watchers`
  3. NO `No policy found at startup; polling every 5s` line
- CLEANUP: wipe policy.

---

# Block B — 🤖 AUTO, requires a dev build but does not install anything

The dev build creates a *target* version on the local stream so the running service can see it; the test
verifies what the service does about it. Each test here fits in one new build.

## Test 10 — Downgrade refused

**Checks:** if the stream advertises a version *older* than running, the service does nothing.

- ACTION (server): edit `release-streams/local/local.json` so `name`/`tag_name` are e.g. `2.9.0.0`.
- ACTION (vm): apply URL-only policy.
- EXPECT: `the version installed: <running> is newer than the released version: 2.9.0.0` every poll tick.
  Zero downloads, zero install attempts.
- CLEANUP: wipe policy; restore `local.json` to a real version.

## Test 13 — Switch URL mid-session

**Checks:** changing `AutomaticUpdateURL` while running causes the next poll to fetch from the new URL.
Includes regression coverage for the WMI-watcher delete-and-recreate bug fix.

- ACTION (server): create `release-streams/local2/local.json` advertising `9.9.9.9`.
- ACTION (vm): policy with `AutomaticUpdateURL` pointing at `local`.
- EXPECT: log shows `upgrade <local-version> is available`.
- ACTION (vm): change `AutomaticUpdateURL` to point at `local2`.
- EXPECT: within ~10 s, log shows `upgrade 9.9.9.9 is available`.
- CLEANUP: wipe policy; remove `release-streams/local2/`.

## Test 18 — Corrupt SHA256 sidecar rejected

**Checks:** when the EXE's `.sha256` doesn't match the computed hash, the install is blocked, the EXE is
deleted, the service keeps polling.

- ACTION (server): build N+1.
- ACTION (server): overwrite `release-streams/local/<N+1>/Ziti.Desktop.Edge.Client-<N+1>.exe.sha256`
  with `DEADBEEF...`.
- ACTION (vm): policy with `AutomaticUpdatesDisabled=0`, `AutomaticUpdateURL=...local`,
  `InstallationCritical=1` (force the auto-install path), then `Restart-Service ziti-monitor`.
- EXPECT: `comparing computed hash: <real> to downloaded hash: DEADBEEF...` followed by
  `WARN ... hash is not valid. The file will be removed: <path>`. No `Running update package`. Service
  keeps polling.
- CLEANUP: restore the real sha256; wipe policy.

## Test 12 — `AutomaticUpdatesDisabled=1` overrides `InstallationCritical=1`

**Checks:** master switch wins — even when every release would otherwise be critical, no toast and no install.
Also verifies the bug-fix that drops concurrent CheckUpdate invocations (one log line per tick, not eight).

- ACTION (vm): policy with `AutomaticUpdatesDisabled=1`, `AutomaticUpdateURL=...local`,
  `InstallationCritical=1`, then `Restart-Service ziti-monitor`.
- EXPECT: `Update <version> is critical but AutomaticUpdatesDisabled is set; skipping notification and install`
  on every poll tick. **Exactly one such log line per tick** (no 8x duplication). Zero downloads.
- CLEANUP: wipe policy.

---

# Block C — 🤖 AUTO, full critical-install path (each one upgrades the running service)

These actually install a new dev build. Order matters: Test 7's first build puts the service on a
60 s poll interval, then Test 7's second build returns it to `$fast` (10 s).

## Test 7 — Auto-install via short policy-set `InstallationCritical`

**Checks:** when a release is past the policy `InstallationCritical` threshold, the service auto-installs
without any user click. Verifies the install fires from the 30 s `Thread.Sleep`, not from the next poll
tick.

- ACTION (server): build #1 with `-FastInterval $slow` (60 s).
- EXPECT: service auto-installs build #1 (or driver issues `Restart-Service` to force immediate check).
  Log shows `Version Checker is running every 60 seconds`.
- ACTION (server): build #2 with `-FastInterval $fast` (10 s).
- ACTION (vm): policy with `InstallationCritical=1`, `AutomaticUpdateURL=...local`,
  `AutomaticUpdatesDisabled=0`. **Do not** restart the service — let the natural 60 s poll fire.
- EXPECT, in order:
  1. `Installation is critical!` (call this **T=0**, ≤ 60 s after policy applies)
  2. `installZDE called` at **T=30** (Thread.Sleep) — must precede any `Timer triggered CheckUpdate`
     line at T=60
  3. Service back up on build #2 with `Version Checker is running every 10 seconds`.
- CLEANUP: wipe policy.

## Test 8 — Auto-install via backdated `published_at` exceeding default 7-day threshold

**Checks:** with no `InstallationCritical` policy override (default 7 d), a release whose `published_at` is
older than 7 days triggers the critical path.

- ACTION (server): build with `-published_at $((Get-Date).AddDays(-8).ToUniversalTime())`.
- ACTION (vm): URL-only policy, `Restart-Service ziti-monitor`.
- EXPECT: `Installation is critical! ... approximate install time: T+30s` followed by
  `installZDE called` ~30 s later. Service comes up on the new version.
- CLEANUP: wipe policy.

## Test 20 — Timezone correctness (regression guard)

**Checks:** displayed and triggered install times are in local wall-clock, not shifted by the UTC offset.

- ACTION (server): build with `-published_at $now` (UTC).
- Predicted install time = published_at_local + 2 minutes.
- ACTION (vm): policy `AutomaticUpdatesDisabled=0`, URL=local, `InstallationCritical=120`,
  `Restart-Service ziti-monitor`.
- EXPECT (first poll): `approximate install time: <publish_local + 2 min> (local)` matches the prediction
  (no UTC offset shift).
- ACTION (vm, ~2 min later): `Restart-Service ziti-monitor` to force a poll past the critical threshold.
- EXPECT: `Installation is critical!` followed by `installZDE called` ~30 s later, matching predicted
  install time.
- CLEANUP: wipe policy.

## Test 3 — Deferred install fires immediately when policy unblocks it

**Checks:** clearing `MaintenanceWindowStart`/`End` + `DeferInstallToRestart` while a deferred install is
queued causes the install to fire immediately (bug-fix), not wait for the next poll tick.

- Precondition: a deferred install must be pending (run Test 2 first or recreate the state). Setup:
  apply Test-1 policy (window 22-6, defer=1), wait for the service to detect a newer build, click
  **Perform Update** so service goes to deferred state.
- ACTION (vm): remove `MaintenanceWindowStart`, `MaintenanceWindowEnd`, `DeferInstallToRestart`.
- EXPECT, in order:
  1. `Policy overrides loaded: ... MaintenanceWindowStart=(not set) ...`
  2. `Policy change unblocked deferred install; proceeding immediately with install of <ver>`
     (bug-fix marker; before the fix this used to fire on the next 10-min poll tick)
  3. `installZDE called`, normal install path.
- CLEANUP: wipe policy.

---

# Block D — 👀 VISUAL, driver runs steps but you eyeball the UI

The log already confirms server-side behavior; you just glance at the UI for the banner / lock / dimming.

## Test 1 — Policy written → UI locks within one poll cycle

**Checks:** non-empty registry under `…\NetFoundry\…\ziti-monitor-service` causes the Automatic Upgrades
panel to show "Managed by your organization" and grey out all controls within ~10 s.

- ACTION (vm): write the full canonical Test-1 policy.
- EXPECT (log): `Policy overrides loaded:` with all the values.
- EXPECT (UI): banner appears, all Automatic Upgrades controls greyed out, values match what we wrote.
- CLEANUP: wipe policy.

## Test 5 — Wipe policy → UI unlocks immediately

**Checks:** removing the entire `NetFoundry` tree returns the UI to the unmanaged state without a service
restart.

- Precondition: any policy applied (e.g. coming straight off Test 1).
- ACTION (vm): `Remove-Item -Recurse -Force HKLM:\SOFTWARE\Policies\NetFoundry`.
- EXPECT (log): `Policy overrides loaded: ... (not set)` for all values.
- EXPECT (UI): banner gone within ~1 s, controls re-enabled, values fall back to defaults.
- CLEANUP: none.

## Test 16 — Partial policy → whole panel locks, defaults fill the rest

**Checks:** a single value in policy is enough to lock the entire Automatic Upgrades panel; un-set fields
display sensible defaults rather than the registry's blanks.

- ACTION (vm): policy with **only** `AutomaticUpdateURL` set.
- EXPECT (log): `Policy overrides loaded:` with `AutomaticUpdateURL=<url>`, everything else `(not set)`.
- EXPECT (UI):
  - Banner visible.
  - All controls greyed out.
  - URL field shows our URL; enable/disable shows enabled (default); window shows "any time"
    (00:00–00:00); defer-to-restart unchecked.
- CLEANUP: wipe policy.

---

# Block E — 👆 CLICK, you click a button

## Test 2 — Click Perform Update outside maintenance window → deferred

**Checks:** policy maintenance window blocks click-time installs; service queues the install for when
the window opens.

- ACTION (server): build N+1.
- ACTION (vm): full Test-1 policy (window 22-6, defer=1).
- EXPECT (log): service detects N+1.
- ACTION (you, in UI): click **Perform Update**. Local clock outside 22-6.
- EXPECT (log):
  - `TriggerUpdate requested. MaintenanceWindow=22-6, ... inWindow=False`
  - `TriggerUpdate deferred: outside maintenance window 22-6. Will install when window opens at <date> 10:00 PM`
  - Subsequent `Deferred install pending` lines on each poll tick.
- EXPECT (UI): "Update scheduled for maintenance window" status; window combos show 22 / 6 greyed out;
  **no** premature "Updating" sentinel window (UX-fix verification).
- CLEANUP: wipe policy.

## Test 4 — `AutomaticUpdatesDisabled=1` cancels pending deferred install

**Checks:** flipping the master switch on while an install is queued cancels the queue and clears all
UI update state immediately.

- Precondition: deferred install pending (run Test 2 first).
- ACTION (vm): set `AutomaticUpdatesDisabled=1`.
- EXPECT (log):
  - `Policy AutomaticUpdatesDisabled False -> True`
  - `AutomaticUpdatesDisabled is now set; cancelling pending deferred install`
  - subsequent ticks log `Update <ver> available but AutomaticUpdatesDisabled is set; skipping notification`
- EXPECT (UI): "Update scheduled" gone, update button hidden, tray badge gone, main-menu badge gone within ~1 s.
- CLEANUP: wipe policy.

## Test 14 — `DefaultExtAuthProvider` auto-selects + locks

**Checks:** policy under the `…\ui` subkey forces a specific ext-auth provider.

- Precondition: an identity enrolled with **≥ 2** external signers.
- ACTION (vm): write `DefaultExtAuthProvider=<provider-name>` under
  `HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ui`.
- ACTION (you, in UI): open the identity. The ext-auth provider dropdown / "use as default" checkbox
  should auto-select your provider and be greyed out.
- EXPECT (UI): provider auto-selected; "use as default" greyed; trying to switch is blocked.
- CLEANUP: wipe policy. Reopen identity — checkbox editable again.

> Outstanding: an identity with ≥ 2 ext-auth signers may not currently be surfaced as ext-auth in the
> UI. Test 14 is blocked until the UI side handles the multi-signer enrolment correctly.

---

# Block F — 🔄/🪟 Big-action, one-off

## Test 9 — `DeferInstallToRestart` stages installer + fires on reboot

**Checks:** the Task Scheduler entry is registered, the install does not run until the system reboots,
and the entry is auto-removed after the install runs.

- ACTION (server): build N+1.
- ACTION (vm): policy with `AutomaticUpdatesDisabled=0`, URL=local, `DeferInstallToRestart=1`.
- ACTION (you, in UI): click **Perform Update**.
- EXPECT (log): `TriggerUpdate: DeferInstallToRestart=True, staging installer for next restart`,
  `StageInstallForRestart: download complete`, `Deferred install task registered successfully`,
  `StageInstallForRestart: installer staged at <path>; will run on next system restart`.
- EXPECT (vm): `schtasks /query /tn "NetFoundry\ZitiDesktopEdge-PendingUpdate"` lists the task.
- ACTION (you): reboot the VM.
- EXPECT (post-reboot log): `version : <new>`,
  `stale download check: file=<old>, running=<new>, isOlder=True`,
  `Deferred install task is registered at startup; removing it`, `Deferred install task removed`.
- EXPECT (vm): `schtasks /query` returns "cannot find file".
- CLEANUP: wipe policy.

## Test 17 — UI close/reopen → reconnect receives locked state immediately

**Checks:** the UI's first connect to the service after a relaunch immediately receives the locked
policy state — no stale "unlocked" flash.

- ACTION (vm): full Test-1 policy + `Restart-Service ziti-monitor` so the policy is loaded synchronously
  at service startup. Then `Stop-Process -Name ZitiDesktopEdge`.
- ACTION (you, on the VM desktop): relaunch ZDEW from Start Menu / shortcut. (Driver can't do this —
  `Start-Process` over SSH spawns in a different session.)
- EXPECT (UI): "Managed by your organization" banner is visible immediately on connect, **no** flash of
  unlocked controls before locking.
- CLEANUP: wipe policy.

---

## Suggested run order

1. Block A (4 tests, ~5 min, fully unattended)
2. Block B (4 tests, ~5 min, fully unattended; one rebuild between B and C)
3. Block C (4 tests, ~10 min, includes auto-installs)
4. Block D (3 tests, glance at UI)
5. Block E (3 tests, click required)
6. Block F (1 test reboot, 1 test UI launch)

Counts: 12 fully-unattended automatable, 3 visual-only, 3 click-required, 1 reboot, 1 UI-launch. Test 14
is currently blocked by an unrelated UI-side issue.
