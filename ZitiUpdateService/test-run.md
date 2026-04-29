# Manual Test Run — Automatic Updates Policy

Step-by-step imperative runbook.

- **One-time setup** — share files between the host and the VM.
- **Environment prep (Prep 1–9)** — one-shot preconditions for the run.
- **Tests (1–20)** — each follows a consistent pattern:
  - **Setup** — arrange preconditions (registry state, server state, UI state).
  - **Action** — the thing under test (write policy, click update, restart, etc.).
  - **Expected** — what "pass" looks like in the log and UI.
  - **Cleanup** — return to a clean state for the next test.

Only stop mid-test if the **Expected** outcome does not happen.

---

## Audience — read the section that fits you

### 👤 If you're a human

Run the tests **in numeric order, 1 → 20**. Each one tells you what to paste and
what to look for. Skip the agent section below.

### 🤖 If you're an automation agent (Claude Code or similar)

Don't make the human run all 20 in order — you can drive the boring ones over
SSH and only bring the human in for clicks / reboots / UI-launch. Re-order into
the **Block A–F flow** at the bottom of this file ("Suggested run order for an
agent driver"). Operating notes:

- **SSH** to the test VM as an admin (`LocalAccountTokenFilterPolicy=1` on the
  VM, `Bash(ssh.exe *)` on your allowlist).
- **Read the service log directly** at
  `z:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log`
  (Z: maps to the VM's C:). Don't ask the human to tail/paste.
- **Drop reusable PowerShell** in `z:\test\` (= `C:\test\` on the VM). Invoke via
  SSH: `ssh.exe user@<vm> powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\test\<name>.ps1'`.
- **Don't `tail -F` over the SMB share** — it doesn't reliably see live writes.
  Wait briefly, then `grep` the file fresh.
- **Restart the service in setup scripts** (`Restart-Service ziti-monitor`)
  rather than waiting for natural poll ticks. Startup performs an immediate
  update check, so it kicks off in seconds.
- **You can't drive UI clicks, reboots, or relaunch the UI from the interactive
  desktop session.** Block D / E / F tests stay human-driven.

## Machine roles

- 🧪 **TEST** — the Windows machine where ZDEW is installed, registry policy is
  written, and the UI is observed. In this runbook it's a VM.
- 🖥️ **SERVER** — the machine where the dev build is compiled and where a small
  Python HTTP server serves a release-stream JSON + the installer EXE.

The two roles can be the same machine, but separating them is cleaner: the test VM
can be reset freely without losing your build environment.

Each step below is tagged with the machine it runs on.

---

# One-time setup

## Share files between server and test machine

The runbook needs `prepare-for-tests.ps1` on the test VM at `C:\test\`, and it's
convenient to be able to drop other files onto the VM. The easiest way is a mapped
drive from the server (host) to the VM.

Recommended setup:
1. On the 🖥️ server: create a `C:\test\` folder (or reuse an existing location).
2. On the 🧪 VM: map that folder so it appears as `C:\test\` inside the VM. Mechanism
   depends on your hypervisor:
   - **Hyper-V** — Enhanced Session Mode → share a host folder.
   - **VMware** — Shared Folders, mount as `C:\test\`.
   - **VirtualBox** — Shared Folders with Auto-mount + mount point `C:\test\`.
   - **Other** — any SMB share / USB / copy that lands at `C:\test\` on the VM.

## Copy `prepare-for-tests.ps1` into `C:\test\`

From the repo root on the 🖥️ server, copy the script into the shared folder that
surfaces as `C:\test\` on the VM:

```powershell
Copy-Item -Force ".\ZitiUpdateService\prepare-for-tests.ps1" "C:\test\prepare-for-tests.ps1"

```

Adjust the destination if your share lands at a different host-side path (e.g.
`Z:\test\` on the host → `C:\test\` on the VM).

---

# Environment prep

The Prep steps are not tests — they get you to a clean "dev build running, no
policy applied" state. Run once per session.

## Prep 1 — 🧪🖥️ BOTH — Set your test session variables

Set these at the top of **every new pwsh shell** you open on either machine. They
are referenced by every paste block below. Substitute your actual server
hostname/port:

```powershell
$ServerHost   = 'your.actual.server.name'
$ServerPort   = 8000
$StreamUrl    = "http://${ServerHost}:${ServerPort}/release-streams/local"
$LocalJsonUrl = "$StreamUrl/local.json"

Write-Host "StreamUrl    = $StreamUrl"
Write-Host "LocalJsonUrl = $LocalJsonUrl"
```

**Expected:** the two `Write-Host` lines echo your real server URL, not
`your.actual.server.name`.

## Prep 2 — 🧪 TEST — Prepare the test VM

Launch `pwsh.exe` **as Administrator**, then run:

```powershell
.\prepare-for-tests.ps1 -PurgeData

```

The script verifies PowerShell 7+, verifies elevation, uninstalls any existing ZDEW,
wipes leftover identities/settings, installs the current stable ZDEW from
`https://get.openziti.io/zdew/stable.json`, and prints the installed version.

**Expected:** `Installed: Ziti Desktop Edge vX.Y.Z.W` in green. That is your **N**.

## Prep 3 — 🧪 TEST — Confirm the service is running and launch the UI

```powershell
Get-Service ziti-monitor | Format-Table -AutoSize

```

**Expected:** `Status: Running`. Then launch the UI: **Start Menu → "Ziti Desktop
Edge"**. The tray icon should appear and the main window should open.

## Prep 4 — 🧪 TEST — Wipe any existing policy

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue
Get-Item    'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

**Expected:** the second command returns nothing.

## Prep 5 — 🖥️ SERVER — Clean up old build artifacts

Wipe stale EXEs, SHA256 sidecars, and old `local.json` files from the served dir:

```powershell
$dir = 'C:\path\to\release-streams\local'
if (Test-Path $dir) {
    Get-ChildItem $dir | ForEach-Object { Write-Host "  removing $($_.Name)" }
    Remove-Item -Recurse -Force "$dir\*"
    Write-Host "Cleaned $dir." -ForegroundColor Green
} else {
    Write-Host "Nothing to clean ($dir does not exist)." -ForegroundColor Yellow
}

```

The Python HTTP server (started in Prep 6) can keep running while you do this —
Python's `http.server` doesn't hold file locks.

**Expected:** the green `Cleaned` line, or the yellow `Nothing to clean` line.

## Prep 6 — 🖥️ SERVER — Serve the `local` release stream over HTTP

Start a simple HTTP server against the release-streams directory:

```powershell
python -m http.server $ServerPort --directory C:\path\to\release-streams

```

**Expected:** in a browser on the test VM,
`http://${ServerHost}:${ServerPort}/` returns a directory listing containing
`release-streams/`. Leave this server running for the whole session.

## Prep 7 — 🖥️ SERVER — Build the dev installer with fast-interval

On the server, check out the `allow-registry-overrides` branch of
`openziti/desktop-edge-win`. Follow `BUILDING.md` at the repo root for prerequisites
(VS 2022 or MSBuild + .NET Framework 4.8 targeting pack, NuGet CLI, PowerShell 7+,
Advanced Installer).

`-FastInterval 30` bakes a 30-second update-check interval into the binary (vs.
the 10-minute production floor) so tests can progress quickly. Pass any number of
seconds; `0` or omit the parameter to build with production defaults.

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
Write-Host "Building with -url $StreamUrl"
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

```

**Expected:**
- Build completes without errors.
- `release-streams\local\` now contains a `local.json` and a signed
  `Ziti.Desktop.Edge.Client-<version>.exe`. That is your **N+1**.
- Browsing to `$LocalJsonUrl` from the test VM returns the JSON, and the
  `assets[0].browser_download_url` inside points at the EXE on this server.

## Prep 8 — 🧪 TEST — Point ZDEW at the local stream

In the ZDEW UI on the test VM:
1. Hamburger menu → **Automatic Upgrades**.
2. Set the update URL to the value of your `$LocalJsonUrl`. Echo it first if you
   need a reminder:
   ```powershell
   Write-Host $LocalJsonUrl

   ```
3. Save.
4. Click **Check for updates**. Don't wait for the 10-minute default poll —
   you're still on stable N which polls every 10 min.

**Expected:**
- The UI accepts the URL without error.
- The Python http.server console on the 🖥️ server shows
  `GET /release-streams/local/local.json` from the test VM.
- The UI surfaces an "Update available" indicator for N+1.

## Prep 9 — 🧪 TEST — Upgrade to N+1

Click whatever button triggers the update in the ZDEW UI (label varies by build —
**Perform Update**, **Update Now**, or similar). The installer downloads from the
local server, runs, and the service restarts on N+1.

**Expected:**
- A `GET` for the EXE appears in the Python http.server console.
- The installer runs (30–60 s).
- After install, `Get-Service ziti-monitor` still shows `Running`.
- ZDEW UI re-launches; About shows N+1.
- Service log shows fast-interval polling active:
  ```
  INFO  Version Checker is running every 0.5 minutes
  ```

You are now on the dev build with policy support. Ready for the tests.

---

# Policy tests

> **Watcher behavior** — when the policy registry key is *absent* at service startup
> (or after a wipe), the WMI registry watcher is dormant. The next update-timer
> cycle recreates it. So **the first policy write after a wipe can take up to one
> cycle (~30 s with fast-interval) to take effect**, not the ~1 second you'll see
> for subsequent live changes.

> **Skipping the wait for a poll tick** — whenever a test says "wait for the next
> 30 s tick" to get the service to notice a new version or fetch a URL, you can
> click **Check for updates** in the ZDEW hamburger menu to trigger that
> immediately. This is a UI shortcut for the same code path the timer fires.
>
> **Exception:** Tests 7, 8, and 12 specifically verify *automatic* behavior
> (auto-install without user action). Don't use Check for updates there — the
> whole point is to confirm the service acts on its own.

---

## Test 1 — Policy written to an empty registry locks the UI within one poll cycle

🧪 TEST-only.

**Goal:** verify we can go from *no registry settings* to *registry settings* and
have the policy take effect — no service restart, no UI restart. Because the WMI
watcher is dormant when no policy key exists, the change is picked up on the next
update-timer cycle (up to 30 s with fast-interval).

### Setup

1. Confirm no policy is applied (rerun Prep 4 if unsure).
2. In the ZDEW UI, open **Automatic Upgrades** and confirm controls are interactive
   and no "Managed by your organization" banner is visible.
3. On the 🧪 test VM, tail the service log in another elevated `pwsh` window:
   ```powershell
   Get-Content 'C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log' -Tail 0 -Wait

   ```

### Action

In an elevated `pwsh` on the 🧪 test VM (with `$LocalJsonUrl` set from Prep 1),
paste:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled    -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL          -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowStart      -PropertyType DWord  -Value 22            -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowEnd        -PropertyType DWord  -Value  6            -Force | Out-Null
New-ItemProperty -Path $reg -Name DeferInstallToRestart       -PropertyType DWord  -Value  1            -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationReminder        -PropertyType DWord  -Value 86400         -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical        -PropertyType DWord  -Value 604800        -Force | Out-Null
New-ItemProperty -Path $reg -Name AlivenessChecksBeforeAction -PropertyType DWord  -Value 12            -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Within up to 30 seconds (the next update-timer cycle), the service log shows the
`Policy overrides loaded:` line — that's the marker that the WMI watcher fired and
`Load()` ran:

```
INFO  ZitiUpdateService.Utils.PolicySettings  Policy overrides loaded: AutomaticUpdatesDisabled=False, AutomaticUpdateURL=<your URL>, UpdateTimer=(not set), InstallationReminder=86400, InstallationCritical=604800, AlivenessChecksBeforeAction=12, DeferInstallToRestart=True, MaintenanceWindowStart=22, MaintenanceWindowEnd=6
```

In the UI, on the same cycle:
- "Managed by your organization" banner appears at the top of Automatic Upgrades.
- All controls become disabled / greyed out.
- Displayed values reflect the registry writes.

**Pass:** all of the above happen without restarting the service or the UI.

### Cleanup

None — Test 2 reuses this policy.

> Subsequent writes while the watcher is alive are detected within ~1 second.
> Tests 4 and 5 exercise that path.

---

## Test 2 — Clicking update outside the maintenance window defers the install

🖥️ SERVER + 🧪 TEST.

**Goal:** with Test 1's policy active (window 22:00–06:00), clicking **Perform
Update** during the day must *not* install immediately. The install is deferred
until the window next opens; the UI reflects the pending state.

### Setup

On the 🖥️ server, build N+2:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

```

A new `Ziti.Desktop.Edge.Client-<new>.exe` + updated `local.json` should land in
`release-streams\local\`. That's your **N+2**.

On the 🧪 test VM, wait up to 30 s without clicking anything. Service log should show:
```
INFO  upgrade <N+2> is available. Published version: <N+2> is newer than the current version: <N+1>
INFO  update is available.
INFO  Installation reminder for ZDE version: <N+2>...
```
UI should show an "Update available" indicator for N+2.

### Action

Click whatever button triggers the update in the ZDEW UI. Your local clock should
be outside 22:00–06:00.

### Expected

Service log:
```
INFO  TriggerUpdate requested. MaintenanceWindow=22-6, anyTime=False, inWindow=False, now=HH:mm (local)
INFO  TriggerUpdate deferred: outside maintenance window 22-6. Will install when window opens at <YYYY-MM-DD> 10:00 PM (local)
```

In the UI, on the **main window** (identity list view), the two status-text lines
near the bottom (around the "Check for updates" control) change:
- The short status line — "Update scheduled for maintenance window" (was
  `update <N+2> is available`).
- The countdown / install-time line — also "Update scheduled for maintenance
  window" (was `Automatic update to <N+2> will occur on or after <time>`).

On the **Automatic Upgrades screen** (hamburger menu):
- Same "Update scheduled for maintenance window" status.
- Window start/end hour combos show `22` and `06`, **greyed out** (policy-locked).
- "Defer install to next restart" checkbox shows its locked state.

**System tray:** update-pending indicator still visible — update is *deferred*, not
cancelled.

**Pass:** no new download/install happens, log lines match, UI shows pending state.

> If `now=HH:mm` in the log falls *inside* 22:00–06:00 on your clock, flip the
> window temporarily to make "outside" true:
> ```powershell
> $reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
> New-ItemProperty -Path $reg -Name MaintenanceWindowStart -PropertyType DWord -Value  6 -Force | Out-Null
> New-ItemProperty -Path $reg -Name MaintenanceWindowEnd   -PropertyType DWord -Value 22 -Force | Out-Null
>
> ```

### Cleanup

None — Test 3 uses the pending deferred install.

---

## Test 3 — Clearing the maintenance window fires the deferred install

🧪 TEST-only.

**Goal:** with Test 2's deferred install still pending, removing the maintenance
window policy values should cause the service to install the queued update on the
next timer tick — no user interaction.

### Setup

Test 2 leaves you with `Deferred install pending, update <N+2> queued...` ticking
every 30 s in the service log. Leave it running.

Test 1's policy also set `DeferInstallToRestart=1`. With that still active, clearing
only the window would cause the service to *stage* the installer for next restart
(Test 9's path), not install now. We need to clear **both**.

### Action

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
Remove-ItemProperty -Path $reg -Name MaintenanceWindowStart -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $reg -Name MaintenanceWindowEnd   -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $reg -Name DeferInstallToRestart  -ErrorAction SilentlyContinue
Get-ItemProperty $reg

```

`Get-ItemProperty` output should no longer list any of those three values.

### Expected

Within ~1 s (watcher is alive, this is a live mutation), service log:
```
INFO  Policy overrides loaded: ...MaintenanceWindowStart=(not set), MaintenanceWindowEnd=(not set)
INFO  Policy MaintenanceWindowStart 22 -> (not set)
INFO  Policy MaintenanceWindowEnd 6 -> (not set)
```

On the next 30 s tick (or click **Check for updates** in the UI to trigger it
immediately), the deferred install fires:
```
INFO  Deferred install: maintenance window is now open, proceeding with install of <N+2>
INFO  package is in ...Ziti.Desktop.Edge.Client-<N+2>.exe - moving to install phase
INFO  verifying file [...]
INFO  SignedFileValidator complete
INFO  Running update package: ...Ziti.Desktop.Edge.Client-<N+2>.exe
```

Installer runs (30–60 s), service restarts at N+2:
```
- version   : <N+2>
stale download check: file=<N+1>, running=<N+2>, isOlder=True
Removing old download: Ziti.Desktop.Edge.Client-<N+1>.exe
```

**Pass:** install fires automatically without any button click, service restarts on
N+2, old installer cleaned up.

### Cleanup

None.

---

## Test 4 — `AutomaticUpdatesDisabled=1` cancels a pending deferred install and clears update UI

🖥️ SERVER + 🧪 TEST.

**Goal:** with a deferred install already pending, setting `AutomaticUpdatesDisabled=1`
must immediately cancel it, suppress notifications, and clear the update button,
status labels, tray badge, and main-menu badge.

### Setup

On the 🖥️ server, build N+3 so there's something to defer:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

```

On the 🧪 test VM, re-apply the restrictive window and `DeferInstallToRestart` (Test 3
cleared these):

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-ItemProperty -Path $reg -Name MaintenanceWindowStart -PropertyType DWord -Value 22 -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowEnd   -PropertyType DWord -Value  6 -Force | Out-Null
New-ItemProperty -Path $reg -Name DeferInstallToRestart  -PropertyType DWord -Value  1 -Force | Out-Null
Get-ItemProperty $reg

```

Wait for the service to detect N+3 (~30 s), then click **Perform Update** to put the
service into the "scheduled for maintenance window" state. Confirm:
```
TriggerUpdate deferred: outside maintenance window 22-6. Will install when window opens at ...
```
UI shows "Update scheduled for maintenance window" + tray / main-menu badge.

### Action

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 1 -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Within ~1 s (live policy change, watcher is alive), service log:
```
INFO  Policy AutomaticUpdatesDisabled False -> True
INFO  AutomaticUpdatesDisabled is now set; cancelling pending deferred install
```

In the UI, within ~1 s:
- "Update scheduled for maintenance window" text gone from Automatic Upgrades.
- Update button collapsed / hidden.
- Tray icon badge gone.
- Main-menu badge gone.

**Pass:** all UI update state clears immediately, mid-deferred-install.

### Cleanup

None — Test 5 wipes everything.

---

## Test 5 — Removing all policy unlocks the UI immediately

🧪 TEST-only.

**Goal:** wiping the policy tree returns the service and UI to an unmanaged state
immediately — no restart, no poll-cycle wait. Effective settings fall back to
`settings.json` / `App.config`.

### Setup

Coming out of Test 4: `AutomaticUpdatesDisabled=1` is set and the Automatic Upgrades
screen is locked with a "Managed by your organization" banner.

### Action

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue
Get-Item 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

`Get-Item` returns nothing.

### Expected

Within ~1 s (live watcher firing on the parent-tree delete), service log:
```
INFO  Policy overrides loaded: AutomaticUpdatesDisabled=(not set), AutomaticUpdateURL=(not set), UpdateTimer=(not set), InstallationReminder=(not set), InstallationCritical=(not set), AlivenessChecksBeforeAction=(not set), DeferInstallToRestart=(not set), MaintenanceWindowStart=(not set), MaintenanceWindowEnd=(not set)
INFO  Policy AutomaticUpdatesDisabled True -> (not set)
INFO  Policy MaintenanceWindowStart 22 -> (not set)
INFO  Policy MaintenanceWindowEnd 6 -> (not set)
INFO  Policy DeferInstallToRestart True -> (not set)
...
```

UI, within ~1 s:
- "Managed by your organization" banner disappears.
- Controls re-enable.
- Displayed values fall back to `settings.json` / `App.config`, not the
  registry values we just wiped.

**Pass:** banner gone, controls interactive, no service restart.

> Toast notifications do not re-fire when policy clears — toast reappearance waits
> for the next `InstallationReminder` interval (default 24 h). In-UI badge and
> button state is what you verify here.

### Cleanup

Already clean.

---

## Test 6 — `AlivenessChecksBeforeAction` policy value is loaded and effective

🧪 TEST-only.

**Goal:** verify the service reads `AlivenessChecksBeforeAction` from policy and
reports it in `Policy overrides loaded:`.

### Setup

None — Test 5 wiped all policy. Expect the watcher to be dormant.

> **Known limitation** — killing ziti-edge-tunnel with `Stop-Process` does not
> trigger aliveness failures, because the IPC pipe closes cleanly and
> `Svc_OnClientDisconnected` disables the health-check timer. The aliveness check
> catches *hung* tunnels only (process alive, IPC blocked), not killed ones. A full
> runtime verification requires PsSuspend or similar. This test only verifies
> policy loading.

### Action

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AlivenessChecksBeforeAction -PropertyType DWord -Value 3 -Force | Out-Null
Get-ItemProperty $reg

```

Wait up to 30 s for the dormant watcher to be recreated on the next timer cycle.

### Expected

Service log:
```
INFO  Policy overrides loaded: ...AlivenessChecksBeforeAction=3...
```

**Pass:** `AlivenessChecksBeforeAction=3` appears in the loaded-policy log line,
confirming the registry override is active (default is 12).

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 7 — Auto-install fires when policy-set critical threshold is short

🖥️ SERVER + 🧪 TEST.

**Goal:** with `InstallationCritical` set to a very low value via policy, a
newly-published version triggers the critical-install code path automatically on
the next timer tick — no user interaction, no button click. The toast shown to the
user promises install "in the next minute" and the install must kick off within
that window.

### Why two builds are needed

When a release crosses the critical threshold, the service fires the toast,
sleeps 30 s (so the user can see it), then runs `installZDE`. With a 30 s poll
interval, the post-Thread.Sleep install kick-off lands right when the *next*
poll tick fires — you can't tell which path triggered it. So the running
service for this test must be on a 60 s interval (build #1), and the install
target lands on 30 s again (build #2) so subsequent tests run fast.

### Setup

For this test we need a longer poll interval so the Thread.Sleep-based install
doesn't collide with the next poll tick. That requires **two builds**:

1. First build with `-FastInterval 60` — this one the service will upgrade to, so
   the running monitor is on a 60-second poll interval during the test.
2. Second build with `-FastInterval 30` — this is the version the service will
   auto-install via the critical path. Building it with 30 s means once the
   test completes and the service has upgraded to it, subsequent tests run at
   the fast 30 s interval again.

#### 🖥️ SERVER — build #1 (60-second poll, will become the running monitor)

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 60

```

#### 🧪 TEST — upgrade to the 60-second-interval build

Click **Perform Update** in the ZDEW UI to pull build #1. After install, the
service log should show:
```
INFO  Version Checker is running every 1 minutes
```

#### 🖥️ SERVER — build #2 (the install target, back to 30 s interval)

Bump again so there's a new version waiting to be auto-installed. Build with
`-FastInterval 30` so the post-upgrade service is ready for Tests 8+:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

```

#### 🧪 TEST — apply policy

`InstallationCritical=1` (1 second) means any release older than 1 s triggers the
critical path. URL points at your local server, updates enabled, no window
restriction:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical     -PropertyType DWord  -Value 1             -Force | Out-Null
Get-ItemProperty $reg

```

Wait for `Policy overrides loaded:` in the service log showing
`InstallationCritical=1`. Policy was wiped between tests, so the watcher is
dormant — wait up to one poll cycle (60 s with our new interval) for it to pick up.

### Action

Do **not** click anything in the UI. Let the next timer tick (up to 60 s away)
detect the release. Because the release is more than 1 s old, the critical path
fires automatically.

Tail the service log on the 🧪 test VM so you can watch the sequence live:
```powershell
Get-Content 'C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log' -Tail 0 -Wait

```

### Expected

Mark the log timestamp when you see `Installation is critical!` — call it **T=0**.

**At T=0** — detection + toast:
```
INFO  upgrade <version> is available.
DEBUG InstallationIsCritical check: publishDate=<...> (UTC), threshold=00:00:01, criticalAfter=<...> (local), now=<...> (local)
WARN  Installation is critical! for ZDE version: <version>. update published at: <...>. approximate install time: T+30s (local)
DEBUG NotifyInstallationUpdates: sent for version <version> is sent to the events pipe...
```

A toast appears on the 🧪 test VM: **"Ziti Desktop Edge will initiate auto
installation in the next minute!"**.

**Between T=0 and T=30** — the service thread is in `Thread.Sleep(30000)`. No log
lines in this interval. The UI stays on its current version.

**At T=30** — install kicks off (about 30 s after the toast, well inside the
"next minute" promise):
```
INFO  installZDE called at <T=30 local>. MaintenanceWindow=any. InWindow=True. Version=<version>
INFO  copying update package begins
INFO  download started for: ...
INFO  download complete to: ...
INFO  verifying file [...]
INFO  SignedFileValidator complete
INFO  Running update package: ...Ziti.Desktop.Edge.Client-<version>.exe
```

**At T=30 + installer time (~30–60 s)** — service stops, installer runs, service
restarts on the new version.

**Critical checkpoint:** the next scheduled poll tick is at T=60 (one minute after
the policy watcher ran). The install must start **before** T=60. If you see
`Timer triggered CheckUpdate` at ~T=60 *before* the `installZDE called` line, the
install is firing on the next poll cycle, not from the Thread.Sleep — that's a
regression and a fail.

**Pass criteria:**
- `Installation is critical!` log line at T=0.
- Toast "will initiate auto installation in the next minute!" visible on the VM.
- `installZDE called` log line at approximately T=30 (from the Thread.Sleep), NOT
  at T=60 (next poll tick).
- Service restarts on the new version within ~60–90 s of the toast.

### Cleanup

Remove the policy. Build #2 was baked with `-FastInterval 30`, so after the
service auto-installs it during this test the running monitor is back on a
30-second poll interval — ready for Tests 8+.

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

Confirm the service log shows `Version Checker is running every 0.5 minutes`.

---

## Test 8 — Critical install fires when `published_at` exceeds default 7-day threshold

🖥️ SERVER + 🧪 TEST.

**Goal:** without any policy on `InstallationCritical` (default 7 d from
`App.config`), a release whose `published_at` is backdated to more than 7 days ago
triggers an automatic critical install.

### Setup

On the 🖥️ server, build the next version with `published_at` set 8 days in the
past. `build-test-release.ps1` takes a `-published_at` parameter that writes
directly into `local.json`, so no post-build JSON editing is needed:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
$backdated = (Get-Date).AddDays(-8).ToUniversalTime()
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30 -published_at $backdated

```

After the build, verify `local.json` shows the backdated timestamp:

```powershell
Get-Content ".\release-streams\local\local.json"

```

Confirm `published_at` is ~8 days in the past.

On the 🧪 test VM, apply URL-only policy (no `InstallationCritical` override):

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
Get-ItemProperty $reg

```

Wait for `Policy overrides loaded:` — note `InstallationCritical=(not set)`, meaning
the default 7-day threshold applies.

### Action

Do nothing. The next 30 s tick finds the release, sees `published_at + 7 days < now`,
and auto-installs.

### Expected

Service log:
```
INFO  InstallationIsCritical check: publishDate=<8 days ago>, threshold=7.00:00:00, criticalAfter=<1 day ago>
WARN  Installation is critical! for ZDE version: <version>...
INFO  installZDE called for version <version>
INFO  Running update package: ...Ziti.Desktop.Edge.Client-<version>.exe
```

Service restarts on the new version.

**Pass:** auto-install triggered by the **default** threshold (no policy override),
because the backdated `published_at` pushed the release past it.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 9 — `DeferInstallToRestart` stages installer via Task Scheduler, fires on reboot

🖥️ SERVER + 🧪 TEST.

**Goal:** with `DeferInstallToRestart=1`, clicking **Perform Update** stages the
installer as a Windows Task Scheduler task that runs on next system startup,
instead of installing immediately.

### Setup

On the 🖥️ server, build the next version:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

```

On the 🧪 test VM, apply policy with `DeferInstallToRestart=1` and no window
restriction:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name DeferInstallToRestart    -PropertyType DWord  -Value 1             -Force | Out-Null
Get-ItemProperty $reg

```

Wait for `Policy overrides loaded:` showing `DeferInstallToRestart=True`. Wait for
the service to detect the new version (~30 s).

### Action

Click whatever button triggers the update in the ZDEW UI.

### Expected (stage phase)

Service log:
```
INFO  TriggerUpdate: DeferInstallToRestart=True, staging installer for next restart
INFO  StageInstallForRestart: downloading installer
INFO  Download verification complete. ...
INFO  Registering deferred install task for: ...Ziti.Desktop.Edge.Client-<version>.exe
INFO  Deferred install task registered successfully
INFO  StageInstallForRestart: installer staged at ...; will run on next system restart
```

UI status reads **"Update staged for next restart"** (or equivalent).

Verify the task exists:
```powershell
schtasks /query /tn "NetFoundry\ZitiDesktopEdge-PendingUpdate" /v /fo LIST

```
Expect (in output): `Schedule Type: On system start up` and `Run As User: SYSTEM`.

### Action (reboot)

Restart the VM (full reboot, not just log-out/log-in).

### Expected (post-reboot)

After boot and login, service log:
```
INFO  ========================= ziti-monitor started =========================
INFO      - version   : <new version>
INFO  stale download check: file=<old version>, running=<new version>, isOlder=True
INFO  Removing old download: Ziti.Desktop.Edge.Client-<old version>.exe
```

Staged task is cleaned up:
```powershell
schtasks /query /tn "NetFoundry\ZitiDesktopEdge-PendingUpdate" 2>&1

```
Expect: `ERROR: The system cannot find the file specified.` — task was removed
after the install ran.

**Pass:** installer staged cleanly, fired on reboot, service came up at new version,
task cleaned up automatically.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 10 — Downgrade refused: service does not install an older version

🖥️ SERVER + 🧪 TEST.

**Goal:** when an admin switches streams and the new stream advertises an older
version than what's installed, the service must not downgrade.

### Setup

On the 🖥️ server, edit `release-streams\local\local.json` to advertise a version
**lower** than what's currently installed (e.g. if you're on 2.11.0.9, set
`2.9.0.0`):

```powershell
$json = Get-Content ".\release-streams\local\local.json" -Raw | ConvertFrom-Json
$json.name     = "2.9.0.0"
$json.tag_name = "2.9.0.0"
$json | ConvertTo-Json -Depth 5 | Set-Content ".\release-streams\local\local.json"
Get-Content ".\release-streams\local\local.json"

```

### Action

On the 🧪 test VM, apply URL-only policy pointing at the local stream:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
Get-ItemProperty $reg

```

Wait for the next 30 s tick, or click **Check for updates** in the UI to
trigger it immediately.

### Expected

Service log:
```
INFO  the version installed is newer than the released version
```

No download, no install, no UI badge. Service continues polling normally.

**Pass:** service does not downgrade; log explicitly states the installed version
is newer.

### Cleanup

Re-run `build-test-release.ps1` on the server to regenerate `local.json` with a real
version, or revert your edit.

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 11 — Unreachable update server: graceful retry

🧪 TEST-only.

**Goal:** when the policy URL is unreachable, the service logs an error and retries
on the next timer tick without crashing.

### Setup

None — any clean state works.

### Action

Point the policy URL at a nonexistent server:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0                                  -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value 'http://192.0.2.1:9999/nope.json' -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Within 30 s, service log:
```
ERROR  Unexpected error has occurred during the check for ZDE updates  System.Net.WebException: ...
```

On the next tick (~30 s later), same error again. Service does NOT crash, does NOT
enter a "stopped checking" state, does NOT show a false "up to date" message.

**Pass:** error logged, retry continues, service stays running.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 12 — `AutomaticUpdatesDisabled=1` blocks critical install

🧪 TEST-only.

**Goal:** even when `InstallationCritical=1` would make every release critical,
`AutomaticUpdatesDisabled=1` is the master switch and must prevent all installs.

### Setup

Ensure a newer version is published on the 🖥️ server (rebuild if necessary).

### Action

On the 🧪 test VM, apply both settings simultaneously:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 1             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical     -PropertyType DWord  -Value 1             -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Wait two 30 s ticks. Service log:
```
INFO  Update <version> is critical but AutomaticUpdatesDisabled is set; skipping notification and install
```

No download, no install, no UI badge.

**Pass:** `AutomaticUpdatesDisabled` overrides `InstallationCritical`.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 13 — Switching `AutomaticUpdateURL` mid-session picks up new stream

🖥️ SERVER + 🧪 TEST.

**Goal:** changing the policy URL while the service is running causes it to fetch
from the new URL on the next tick. The old cached check is replaced by the new
stream's version.

### Setup

On the 🖥️ server, create a second stream by copying the directory and rewriting
the `name` / `tag_name` in its `local.json` to something obviously different (e.g.
`9.9.9.9`). A real EXE isn't needed for this test — we just want to see the
service detect the different version.

```powershell
Copy-Item -Recurse ".\release-streams\local" ".\release-streams\local2" -Force

$json = Get-Content ".\release-streams\local2\local.json" -Raw | ConvertFrom-Json
$json.name     = "9.9.9.9"
$json.tag_name = "9.9.9.9"
$json | ConvertTo-Json -Depth 5 | Set-Content ".\release-streams\local2\local.json"
Get-Content ".\release-streams\local2\local.json"

```

### Action

On the 🧪 test VM, apply policy pointing at stream 1:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
Get-ItemProperty $reg

```

Wait for the 30 s tick (or click **Check for updates** in the UI). Service log
should show whatever is in `local/local.json`.

Then switch to stream 2:

```powershell
$stream2Url = $StreamUrl -replace '/local$', '/local2'
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-ItemProperty -Path $reg -Name AutomaticUpdateURL -PropertyType String -Value "$stream2Url/local.json" -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Within ~1 s (watcher alive), policy reload log shows the new URL.

On the next 30 s tick (or click **Check for updates**), service log shows it
fetched `local2/local.json`:
```
INFO  upgrade 9.9.9.9 is available...
```

**Pass:** service picks up the new URL from policy and fetches from it on the next
tick. No service restart.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 14 — `DefaultExtAuthProvider` auto-selects and locks the provider

🧪 TEST-only.

**Goal:** setting `DefaultExtAuthProvider` under the `ui` registry subkey
auto-selects that provider in the identity enrollment screen and locks the selector.

> **Prerequisite:** you need an identity enrolled with **at least two** external
> auth providers. One provider is not enough — you can't observe "locked to the
> policy-selected one" if there's nothing else to compare against. Skip this test
> if no multi-provider ext-auth identity is available.

### Setup

None — this test writes its own policy under a different subkey (`ui` vs
`ziti-monitor-service`).

### Action

```powershell
$uiReg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ui'
New-Item -Path $uiReg -Force | Out-Null
New-ItemProperty -Path $uiReg -Name DefaultExtAuthProvider -PropertyType String -Value 'your-provider-name-here' -Force | Out-Null
Get-ItemProperty $uiReg

```

Substitute your actual ext-auth provider name.

### Expected

Open an identity with ext-auth providers in the ZDEW UI:
- The matching provider is auto-selected in the dropdown.
- The "use as default" checkbox is checked and **greyed out** (locked).
- Unchecking / selecting a different provider is blocked.

**Pass:** provider forced by policy; user cannot override.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

Reopen the identity — the checkbox should be editable again.

---

## Test 15 — Service restart with policy already in registry (startup load path)

🧪 TEST-only.

**Goal:** verify the startup code path (not WMI). When policy is already present in
the registry before the service starts, it must be loaded at startup — before the
first update check, before the WMI watcher activates, and before the startup poll
timer times out.

### Setup

Apply full policy:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled    -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL          -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowStart      -PropertyType DWord  -Value 22            -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowEnd        -PropertyType DWord  -Value  6            -Force | Out-Null
Get-ItemProperty $reg

```

### Action

```powershell
Restart-Service ziti-monitor

```

### Expected

Service log, in this order:
1. `Policy overrides loaded:` — **before** `starting service watchers`.
2. **No** `No policy found at startup; polling every 5s...` line (policy was found
   immediately at startup).
3. `Version Checker is running every 0.5 minutes`.
4. First update check uses the policy URL (visible as a `GET` in the Python
   http.server console).

UI (after reconnect): "Managed by your organization" banner immediately, no
poll-cycle wait.

**Pass:** policy loaded at startup via the constructor path, not via WMI or the
startup poll.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 16 — Partial policy: any value set → whole panel locked, defaults fill in the rest

🧪 TEST-only.

**Goal:** when **any** policy value is set in the registry, the entire Automatic
Upgrades panel is treated as policy-managed: the "Managed by your organization"
banner appears, **all** controls are disabled, and unset fields display sensible
defaults.

Rationale: a partially-configured policy is still a policy. Letting the user edit
non-locked settings while others are admin-controlled would be confusing and
inconsistent. The whole panel reflects the registry state + defaults.

### Setup

None — any clean state works.

### Action

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Wait for `Policy overrides loaded:` — all fields show `(not set)` except
`AutomaticUpdateURL`.

In the Automatic Upgrades screen:
- **"Managed by your organization" banner visible.**
- **All controls disabled / greyed out** — enable/disable toggle, URL field,
  maintenance window combos, any-time checkbox, DeferInstallToRestart checkbox.
- Displayed values fall through to sensible defaults where policy did not set them:
  - URL field → the policy URL (the one value we wrote).
  - Enable/disable toggle → shows **enabled** (default).
  - Maintenance window → shows **any time** (start == end, both `00:00`).
  - DeferInstallToRestart → **unchecked** (default).

**Pass:** banner visible, every control disabled, unset fields show defaults.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 17 — UI close/reopen — reconnect receives locked state immediately

🧪 TEST-only.

**Goal:** closing and reopening the ZDEW UI while policy is active must immediately
re-lock the UI on reconnect — no poll-cycle wait.

### Setup

None — the Action block below applies the policy, waits for the watcher to lock
the UI, then kills and relaunches ZDEW so the reconnect path is exercised.

### Action

One paste: apply full policy → **restart ziti-monitor** so policy is loaded
synchronously at service startup (faster than waiting for the WMI watcher's
next tick) → kill the UI → relaunch it:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled    -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL          -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowStart      -PropertyType DWord  -Value 22            -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowEnd        -PropertyType DWord  -Value  6            -Force | Out-Null
New-ItemProperty -Path $reg -Name DeferInstallToRestart       -PropertyType DWord  -Value  1            -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationReminder        -PropertyType DWord  -Value 86400         -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical        -PropertyType DWord  -Value 604800        -Force | Out-Null
New-ItemProperty -Path $reg -Name AlivenessChecksBeforeAction -PropertyType DWord  -Value 12            -Force | Out-Null

Restart-Service ziti-monitor

Stop-Process -Name ZitiDesktopEdge -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-Process 'C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\ZitiDesktopEdge.exe'

```

### Expected

- UI opens and connects to the service.
- Automatic Upgrades screen: "Managed by your organization" banner visible
  **immediately** — not after a 30 s delay.
- Locked controls remain locked.

**Pass:** UI receives locked state from the service on reconnect; no stale
"unlocked" flash.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 18 — Hash validation failure: corrupt SHA256 sidecar rejected

🖥️ SERVER + 🧪 TEST.

**Goal:** when the installer EXE's SHA256 sidecar is corrupt, the service must
detect the mismatch, refuse to install, delete the downloaded file, and retry on
the next tick.

### Setup

On the 🖥️ server, build a new version so there's a pending update to attempt,
then corrupt its `.sha256` sidecar so the hash check will fail:

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30

# Corrupt the EXE's sha256, not the MSI's — the service only validates the EXE
$sha = Get-ChildItem ".\release-streams\local" -Recurse -Filter "*.exe.sha256" |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
"DEADBEEF00000000" | Set-Content $sha.FullName
Write-Host "Corrupted: $($sha.FullName)"

```

### Action

On the 🧪 test VM, apply policy to trigger an update:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical     -PropertyType DWord  -Value 1             -Force | Out-Null
Get-ItemProperty $reg

```

Wait for the next tick (or click **Check for updates** in the UI). Service
detects the update, downloads the EXE, downloads
the `.sha256`, and compares.

### Expected

Service log:
```
INFO  comparing computed hash: <real hash> to downloaded hash: DEADBEEF00000000
WARN  The file was downloaded but the hash is not valid!
```

No install. Downloaded EXE is deleted. Service retries on the next tick (same
failure until sidecar is fixed).

**Pass:** hash mismatch detected, install blocked, service keeps retrying.

### Cleanup

On the 🖥️ server, re-run `build-test-release.ps1` to regenerate correct files.

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 19 — Rapid policy toggle: debounce settles to final state

🧪 TEST-only.

**Goal:** rapidly toggling `AutomaticUpdatesDisabled` 0↔1 multiple times settles to
the final value after the 500 ms debounce timer. The UI ends consistent with the
final registry value.

### Setup

The debounce only kicks in when the WMI watcher is already alive. If the
registry key doesn't exist, the watcher is dormant and the rapid writes never
reach the debounce path — so we pre-create the key with a baseline value and
restart the service to guarantee the watcher is listening.

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL -PropertyType String -Value $LocalJsonUrl -Force | Out-Null

Restart-Service ziti-monitor

```

Verify the service log shows `Policy overrides loaded: ... AutomaticUpdateURL=<your URL>...`
before moving on — this confirms the watcher is active and listening.

### Action

Paste the whole block (flips `AutomaticUpdatesDisabled` 5 times in rapid succession,
ending on `1`):

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 0 -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 1 -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 0 -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 1 -Force | Out-Null
Get-ItemProperty $reg

```

### Expected

Service log shows **multiple** `Policy registry event received, restarting
debounce timer` DEBUG lines (one per WMI event from the 5 writes), followed by
**exactly one** `Policy overrides loaded:` line — proving the 500 ms debounce
collapsed all 5 events into a single `Load()` call. The loaded value is
`AutomaticUpdatesDisabled=True` (the final value written).

UI: "Managed by your organization" banner appears; update toggle reflects "disabled".

**Pass:** debounce works — single load, final state matches final registry value.

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

## Test 20 — Timezone correctness: displayed install time matches local wall clock

🖥️ SERVER + 🧪 TEST.

**Goal:** verify that when a release becomes "critical" (past
`InstallationCritical` threshold), the service displays and triggers the
install at the correct *local* wall-clock time — not shifted by the machine's
UTC offset.

### Setup

#### 🖥️ SERVER — build with `published_at = now (UTC)`

```powershell
if (-not $StreamUrl) { Write-Host "ERROR: set `$StreamUrl (see Prep 1) before building" -ForegroundColor Red ; return }
$now = (Get-Date).ToUniversalTime()
.\scripts\build-test-release.ps1 -url $StreamUrl -increment -FastInterval 30 -published_at $now

```

Note the UTC time this publishes at — call it **T0_utc**.

#### 🧪 TEST — compute the expected local install time

Before applying policy, record your wall-clock reference point:

```powershell
$expectedInstallLocal = (Get-Date).AddMinutes(2)
Write-Host "Expected install time (local): $expectedInstallLocal"

```

This is the time the service should display as `approximate install time` and
the time the critical path should fire.

#### 🧪 TEST — apply policy with a 2-minute critical threshold

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0             -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value $LocalJsonUrl -Force | Out-Null
New-ItemProperty -Path $reg -Name InstallationCritical     -PropertyType DWord  -Value 120           -Force | Out-Null
Get-ItemProperty $reg

```

### Action

Do nothing. Let the 30 s poll find the release and log its decision.

### Expected

On the first poll tick after policy applies (≤ 30 s), the service log should
show the **non-critical** path (the release isn't 2 minutes old yet):

```
INFO  Installation reminder for ZDE version: <N+1>. update published at: <T0_utc>:00Z (UTC). approximate install time: <T0_local + 2 min> (local)
```

**Verify:** the `approximate install time` value matches `$expectedInstallLocal`
(±30 s of poll jitter). If it's off by *exactly* your UTC offset (e.g. 4 hours
ahead in EDT), the timezone fix regressed.

On the poll tick that happens ~2 minutes after publish, the service flips into
the critical path:

```
DEBUG InstallationIsCritical check: publishDate=<T0_utc>:00Z (UTC), threshold=00:02:00, criticalAfter=<T0_local + 2 min> (local), now=<current local>
WARN  Installation is critical! for ZDE version: <N+1>. update published at: <T0_utc>:00Z (UTC). approximate install time: <now + 30 s> (local)
```

Then after the 30 s `Thread.Sleep`:

```
INFO  installZDE called at <time> (local). ...
INFO  Running update package: ...Ziti.Desktop.Edge.Client-<N+1>.exe
```

**Verify:** the install fires at approximately `$expectedInstallLocal + 30 s`
(two-minute critical threshold + Thread.Sleep). Total elapsed from `Setup` to
service-on-N+1 is ~2½–3 minutes.

**Pass:**
- Displayed `approximate install time` is in local time, equal to wall clock +
  2 minutes — **not** shifted by your UTC offset.
- Install actually fires at that time (plus the ~30 s Thread.Sleep grace).

### Cleanup

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry' -ErrorAction SilentlyContinue

```

---

# End of tests

If all 20 tests passed:

- **Test 1** — policy materializes mid-run, UI locks within one poll cycle
- **Test 2** — update deferred when outside maintenance window
- **Test 3** — clearing window fires the deferred install
- **Test 4** — `AutomaticUpdatesDisabled=1` cancels pending install, clears UI
- **Test 5** — removing all policy unlocks UI immediately
- **Test 6** — `AlivenessChecksBeforeAction` policy value loaded correctly
- **Test 7** — auto-install via short policy-set `InstallationCritical` (no click)
- **Test 8** — auto-install when backdated `published_at` exceeds default 7-day threshold
- **Test 9** — `DeferInstallToRestart` stages via Task Scheduler, fires on reboot
- **Test 10** — downgrade refused
- **Test 11** — unreachable update server — graceful retry
- **Test 12** — `AutomaticUpdatesDisabled=1` blocks critical install
- **Test 13** — switching `AutomaticUpdateURL` mid-session picks up new stream
- **Test 14** — `DefaultExtAuthProvider` auto-selects and locks provider
- **Test 15** — service restart with policy pre-applied (startup load path)
- **Test 16** — partial policy — single value locked, rest editable
- **Test 17** — UI close/reopen — reconnect receives locked state immediately
- **Test 18** — hash validation failure — corrupt SHA256 sidecar rejected
- **Test 19** — rapid policy toggle — debounce settles to final state
- **Test 20** — timezone correctness — displayed/triggered install time in local wall clock

---

# Known gaps — document only

Edge cases identified during review that are **not easily testable** with the
current infrastructure. Documented here for future reference.

## Maintenance window midnight crossing

A window like 22:00–06:00 crosses midnight. The `IsInWindow` logic uses
`hour >= start || hour < end` for the wrap-around case. Testing this requires
waiting past midnight or changing the VM system clock — both fragile. The code
path is straightforward (single OR branch), review-verified, not runtime-verified.

## Critical install bypasses DeferInstallToRestart

The critical-install code path calls `installZDE()` directly after a 30 s sleep. It
does NOT check `DeferInstallToRestart`. A release that crosses the critical
threshold installs immediately even if the admin set "defer to restart." Arguably
a **bug** — filed for follow-up.

## Race: policy change during active StageInstallForRestart download

`StageInstallForRestart` runs on `Task.Run`. If `AutomaticUpdatesDisabled=1` is
written while the download is in progress, the policy handler clears the flags
and removes the task — but the download thread finishes and re-registers the task.
Timing-dependent; not testable without injecting delays.

## enableHealthCheck / disableHealthCheck have inverted Enabled flags

`disableHealthCheck()` sets `zetHealthcheck.Enabled = true` then calls `Stop()`.
`enableHealthCheck()` sets `zetHealthcheck.Enabled = false` then calls `Start()`.
The `Start()`/`Stop()` calls likely override the bad assignment, but it is
backwards and fragile. Functional impact is minimal.

## InstallationCritical=0 means "every release is immediately critical"

An admin could set 0 thinking "never force-install." Instead, `Math.Max(0, v)`
allows 0, and `DateTime.Now > publishDate + 0` is always true. Every release
auto-installs instantly. Consider clamping to a minimum (e.g., 3600 s) or
documenting the behavior prominently in the ADMX help text.

## Non-integer registry type for DWORD fields

If a REG_SZ is written where REG_DWORD is expected, `ReadDword` checks `v is int`
and silently ignores the value (field stays null = not policy-controlled). Service
does not crash. Low risk since ADMX templates enforce correct types.

---

# Suggested run order for an agent driver

The numeric 1–20 ordering is for humans. An agent driving over SSH should re-group
into these blocks to minimize human attention and rebuild churn. Inside each
block, ordering minimizes shared-state churn between tests.

| Tag | Meaning |
|---|---|
| 🤖 AUTO       | Driver runs everything via SSH and watches the log. No human attention required. |
| 👀 VISUAL     | Driver runs the registry/build steps; the human glances at the UI for the banner / lock / dimming. |
| 👆 CLICK      | Requires the human to click a UI button (Perform Update, identity selector, etc.). |
| 🔄 REBOOT     | Requires the VM to be rebooted. |
| 🪟 UI-LAUNCH  | Requires the human to relaunch the UI after a kill (`Start-Process` over SSH lands in the wrong session). |

## Block A — 🤖 AUTO, no dev install required

Verifies policy load / logging / debounce behavior without auto-installing anything.
Fastest section, fully unattended.

- **Test 6** — `AlivenessChecksBeforeAction` policy loads
- **Test 11** — Unreachable update URL → graceful retry
- **Test 19** — Rapid policy toggle → debounce settles to final state
- **Test 15** — Restart-Service with policy already in registry (startup load path)

## Block B — 🤖 AUTO, requires a dev build but does not install anything

The build creates a *target* version on the local stream so the running service
can see it; the test verifies what the service does about it.

- **Test 10** — Downgrade refused
- **Test 13** — Switch URL mid-session (covers WMI delete-and-recreate regression)
- **Test 18** — Corrupt SHA256 sidecar rejected
- **Test 12** — `AutomaticUpdatesDisabled=1` overrides `InstallationCritical=1`

## Block C — 🤖 AUTO, full critical-install path (each upgrades the running service)

Actually installs new dev builds. Order matters: Test 7's first build puts the
service on a 60 s poll interval, then Test 7's second build returns it to 30 s.

- **Test 7** — Auto-install via short policy-set `InstallationCritical`
- **Test 8** — Auto-install via backdated `published_at` exceeding default 7-day threshold
- **Test 20** — Timezone correctness (regression guard)
- **Test 3** — Deferred install fires immediately when policy unblocks it

## Block D — 👀 VISUAL, driver runs steps but human eyeballs the UI

Log already confirms server-side behavior; human just glances at the UI.

- **Test 1** — Policy written → UI locks within one poll cycle
- **Test 5** — Wipe policy → UI unlocks immediately
- **Test 16** — Partial policy → whole panel locks, defaults fill the rest

## Block E — 👆 CLICK, human clicks a button

- **Test 2** — Click Perform Update outside maintenance window → deferred
- **Test 4** — `AutomaticUpdatesDisabled=1` cancels pending deferred install
- **Test 14** — `DefaultExtAuthProvider` auto-selects + locks (currently blocked
  by an unrelated UI-side issue)

## Block F — 🔄 / 🪟 Big-action, one-off

- **Test 9** — `DeferInstallToRestart` stages installer + fires on reboot
- **Test 17** — UI close/reopen → reconnect receives locked state immediately

## Counts

12 fully-unattended automatable, 3 visual-only, 3 click-required, 1 reboot,
1 UI-launch. Test 14 is currently blocked by an unrelated UI-side issue.
