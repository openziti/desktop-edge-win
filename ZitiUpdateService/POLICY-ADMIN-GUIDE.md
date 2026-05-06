# Ziti Desktop Edge for Windows — Administrator Policy Guide

Ziti Desktop Edge for Windows (ZDEW) can be managed by IT administrators via Windows
registry policy keys. Policy can be deployed through Group Policy Objects (GPO), Microsoft
Intune, any MDM tool that writes the registry, or directly with `reg.exe` / PowerShell.

Once policy is applied:

- The **monitor service** (`ziti-monitor`) enforces policy server-side — any attempt to
  change a locked setting via the UI is rejected with `MANAGED_BY_POLICY`.
- The **UI** disables locked controls and shows a **"Managed by your organization"**
  banner on the Automatic Upgrades screen.
- Policy changes are detected **live** — no service restart required.

---

## Quick start

On a test or production endpoint, run this PowerShell snippet **elevated** to set the
most common policies:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord  -Value 0 -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdateURL       -PropertyType String -Value 'https://get.openziti.io/zdew/stable.json' -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowStart   -PropertyType DWord  -Value 22 -Force | Out-Null
New-ItemProperty -Path $reg -Name MaintenanceWindowEnd     -PropertyType DWord  -Value  6 -Force | Out-Null
```

That pins the update stream, enables automatic updates, and restricts installs to 22:00–06:00 local.

Within ~30 seconds (next update-timer tick) the running service picks up the change. The
user sees a "Managed by your organization" banner in the ZDEW UI. See
[Verifying policy is applied](#verifying-policy-is-applied).

---

## Registry layout

Policy lives under two subkeys of `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge
for Windows\`:

| Subkey | Consumer | Purpose |
|---|---|---|
| `ziti-monitor-service` | `ziti-monitor` service | Update/install behavior |
| `ui` | ZDEW UI | UI-only settings (e.g. default ext-auth provider) |

The parent path (`HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows`) is
the Windows standard managed-policy hive. Admins don't need to create it manually; ADMX
ingestion (GPO / Intune) or the `New-Item -Force` PowerShell creates it on first use.

Registry writes are ACL'd such that only administrators can modify them — this is
standard `HKLM\SOFTWARE\Policies\...` protection from Windows.

---

## Settings reference

### `ziti-monitor-service` subkey

| Value name | Type | Units / range | Default | Effect when set |
|---|---|---|---|---|
| `AutomaticUpdatesDisabled` | DWORD | `0` or `1` | `0` (enabled) | `1` = master kill-switch: no update checks, no installs, update UI hidden |
| `AutomaticUpdateURL` | String | URL | OpenZiti stable stream | Pin the release-stream JSON the service fetches |
| `UpdateTimer` | DWORD | seconds, min 600 | 600 (10 min) | How often the service polls for updates (values < 600 are clamped to 600) |
| `InstallationReminder` | DWORD | seconds | 86400 (24 h) | How often to re-remind the user about a pending update |
| `InstallationCritical` | DWORD | seconds | 604800 (7 d) | Age at which a pending update becomes "critical" and auto-installs without user action |
| `AlivenessChecksBeforeAction` | DWORD | count, min 1 | 12 | Consecutive ziti-edge-tunnel health-check failures before forcing a restart |
| `DeferInstallToRestart` | DWORD | `0` or `1` | `0` | `1` = don't install immediately; stage the installer to run at next system restart |
| `MaintenanceWindowStart` | DWORD | hour 0–23 | unset | Start hour (local time) of the allowed install window |
| `MaintenanceWindowEnd` | DWORD | hour 0–23 | unset | End hour (local time) of the allowed install window |

### `ui` subkey

| Value name | Type | Effect |
|---|---|---|
| `DefaultExtAuthProvider` | String | Pins the default external authentication provider in the identity enrollment screen. User cannot change it. |

### Value type notes

- **DWORD** — 32-bit integer. In `reg.exe`: `/t REG_DWORD`. In PowerShell:
  `New-ItemProperty ... -PropertyType DWord`.
- **String** — plain text. In `reg.exe`: `/t REG_SZ`. In PowerShell:
  `-PropertyType String`.
- Writing the wrong type is silently ignored (the service stays on its default). ADMX
  templates enforce correct types automatically.

---

## Precedence

For every setting the effective value is resolved in this order:

1. **Registry policy** (if present) — locks the setting; user cannot change it via UI.
2. **`settings.json`** (user-changeable via the UI) — whatever the user last saved.
3. **`App.config` default** (shipped with the installer) — fallback.

Remove a policy value and the setting falls through to whatever `settings.json` held
most recently, or to the `App.config` default if the user never set it.

---

## Maintenance window

The maintenance window constrains **when installs actually run**. It does not affect
update-check polling.

- Both `MaintenanceWindowStart` and `MaintenanceWindowEnd` are hours in local time
  (0–23).
- `start == end` (e.g. both `0`, or both `10`) means **"any time"** — install whenever.
- `start < end` — install window is `start ≤ hour < end` (for example, `9`–`17` is
  09:00–17:00, including 09:xx but not 17:xx).
- `start > end` — window crosses midnight (for example, `22`–`6` is 22:00–06:00,
  excluding 06:xx).
- If only one of `Start`/`End` is set, the service treats the window as "any time"
  (both must be set to take effect).

When a user clicks **Perform Update** outside the window, the install is deferred
and scheduled for the next window opening. The UI status shows
**"Update scheduled for maintenance window"**.

---

## Deferred-install-to-restart

When `DeferInstallToRestart=1` and the user clicks **Perform Update** (or an install is
triggered automatically), the service does **not** run the installer immediately.
Instead:

1. The installer EXE is downloaded and hash-verified in the background.
2. A Task Scheduler task is registered at
   `Task Scheduler Library\NetFoundry\ZitiDesktopEdge-PendingUpdate`. Trigger:
   "At system startup". Run as: SYSTEM. Run level: Highest.
3. On the next system reboot, the task fires the installer silently before the user
   logs in.
4. When the service starts back up it detects and removes the stale task.

Use this when you want to avoid disrupting user sessions with ziti-monitor restarts
during work hours.

---

## Critical-install threshold

`InstallationCritical` (seconds, default 7 days) controls at what age a release
becomes "critical". Once `now > release_publish_time + InstallationCritical`, the
service auto-installs the release without waiting for a user click.

- Set low (e.g. `86400` = 1 day) to force fast rollout of security fixes.
- Set high to give users plenty of lead time.

> **Warning — `InstallationCritical=0` is dangerous.** A value of `0` means *every*
> release becomes immediately critical, because the check `now > publish + 0` is
> always true. Every published version auto-installs on the next poll cycle, with no
> opportunity for staged rollout. Use a non-zero value unless you specifically want
> this.

---

## Deployment methods

### 1. Group Policy (ADMX)

ADMX/ADML templates are included in `ZitiUpdateService\windows\gpo\`.

1. On a domain controller (or any machine with GPMC):
   - Copy `NetFoundry.ZitiMonitorService.admx` to
     `C:\Windows\PolicyDefinitions\` (or your central store:
     `\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\`).
   - Copy `en-US\NetFoundry.ZitiMonitorService.adml` to the matching `en-US`
     subfolder.
   - Repeat for `NetFoundry.ZitiDesktopEdgeUI.admx` / `.adml`.
2. Open **Group Policy Management Editor** for the target GPO.
3. Navigate to **Computer Configuration → Policies → Administrative Templates →
   NetFoundry → Ziti Desktop Edge for Windows**.
4. Configure the settings you want to enforce.
5. Run `gpupdate /force` on a test endpoint and verify.

### 2. Microsoft Intune

Intune can ingest the same ADMX files:

1. **Microsoft Intune admin center → Devices → Configuration profiles → Create
   profile → Windows 10 and later → Templates → Custom**.
2. Under **OMA-URI**, add an entry that ingests the ADMX (using the
   `./Vendor/MSFT/Policy/ConfigOperations/ADMXInstall/...` path), or use
   **Import ADMX** under **Settings Catalog** if available.
3. Once ingested, the NetFoundry settings appear in the Settings Catalog
   picker. Create a policy and assign it to device groups.

### 3. Mobile Device Management (generic MDM)

Any MDM that supports writing arbitrary registry values can set these keys directly.
The target path is:

```
HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\{subkey}
```

### 4. Direct registry (scripts / manual)

Useful for testing, lab deployments, or scenarios without MDM/AD. PowerShell:

```powershell
$reg = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name AutomaticUpdatesDisabled -PropertyType DWord -Value 1 -Force
```

Or `reg.exe`:

```cmd
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v AutomaticUpdatesDisabled /t REG_DWORD /d 1 /f
```

A helper script is included at
`ZitiUpdateService\windows\gpo\Set-GpoRegistryValues.ps1` that wraps these registry
writes with parameter validation.

---

## How policy changes are detected at runtime

The `ziti-monitor` service uses two mechanisms to pick up policy changes without
requiring a restart:

- **WMI registry tree watcher** — registered against
  `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\`. Fires on any
  value create / modify / delete anywhere under that key. Debounced to 500 ms so
  rapid changes (e.g. writing many values in sequence) collapse into one reload.
- **Startup poll timer** — when the service starts and the policy key doesn't exist
  yet (common during boot, before Group Policy has applied), it polls every 5 s for
  up to 2 minutes. As soon as the key appears, policy is loaded and the update
  check proceeds.

### One important caveat

If the policy key is **absent** when the service starts (or is deleted at runtime),
the WMI watcher is torn down — there's no key to watch. The watcher is re-created on
the next update-timer cycle if the key is detected. So **the very first policy write
after a wipe (or from scratch) can take up to one update-timer interval (default
~10 min) to take effect**. Subsequent writes to an existing key are detected in
~1 second.

---

## Verifying policy is applied

### Service log

The monitor service logs every policy reload. Find the log at:

```
C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log
```

Look for:

```
INFO  Policy overrides loaded: AutomaticUpdatesDisabled=False, AutomaticUpdateURL=<your URL>, ...
```

Every registry value you wrote should appear in this line. Values not written show
as `(not set)`.

### UI

Open ZDEW → hamburger menu → **Automatic Upgrades**. With policy applied:

- A **"Managed by your organization"** banner appears at the top.
- Locked controls are greyed out and non-interactive.
- Effective values shown in the UI reflect the policy values, not the user's
  `settings.json`.

### PowerShell check

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
```

Returns the values you wrote.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Banner doesn't appear after writing policy | Service hasn't polled yet, or watcher was dormant | Wait up to one `UpdateTimer` interval (default 10 min) for first-apply; subsequent changes are live. |
| Policy value shows in registry but not in `Policy overrides loaded:` log | Wrong value type (e.g. REG_SZ for a DWORD field) | Match the type in the [settings reference](#settings-reference). |
| `UpdateTimer < 600` not honored | Clamped to 600 (10 min) minimum by design | This is intentional to avoid pathological polling. Dev builds with `ALLOWFASTINTERVAL` bypass the clamp. |
| Install doesn't happen at the scheduled maintenance-window time | Clock skew, timezone confusion, or service was stopped during the window | Confirm local-time clock and check the log around the expected install time. |
| Installer runs but UI doesn't reopen after upgrade | UI wasn't running at the time of install; the relaunch sentinel is started by the UI, not the service | Launch the UI manually after the install; see [Known limitations](#known-limitations). |
| `Policy registry watcher unavailable` in log | WMI subsystem not yet ready at boot | Self-heals on the next `CheckUpdate` tick. |

---

## Known limitations

- **Cross-midnight maintenance windows are reviewed in code but not runtime-verified.**
  The wrap-around logic (e.g. 22–06) uses `hour >= start || hour < end` — review
  confirms this is correct.
- **Critical install bypasses `DeferInstallToRestart`.** A release that crosses the
  `InstallationCritical` threshold installs immediately regardless of
  `DeferInstallToRestart`. Future fix planned.
- **UI auto-relaunch post-install only works if the UI was already running.** The
  "upgrade sentinel" that relaunches the UI is started from the UI itself in response
  to the "Upgrading" service event. If the UI is closed when the install fires, the
  sentinel never starts and the user must manually relaunch ZDEW after the upgrade.
- **Toast notifications do not re-fire when policy clears.** Toast reappearance waits
  for the next `InstallationReminder` interval (default 24 h). The in-UI badge /
  button state restores immediately regardless.
- **`InstallationCritical=0` means "install every release immediately".** Not a bug
  but a sharp edge — see the warning under
  [Critical-install threshold](#critical-install-threshold).

---

## Further reading

- `ZitiUpdateService\windows\gpo\README.md` — specifics of the ADMX templates.
- `ZitiUpdateService\windows\gpo\DEPLOYMENT.md` — step-by-step deployment walkthroughs
  for GPO and Intune.
- `ZitiUpdateService\AUTOUPDATE-CONFIG.md` — lower-level reference for the
  non-policy configuration files (`App.config`, `settings.json`).
- `ZitiUpdateService\manually-testing-automatic-updates.md` — engineering test
  procedures (for developers, not admins).
