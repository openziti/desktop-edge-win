# Group Policy Support — ZitiUpdateService (ziti-monitor-service)

This directory contains Windows Group Policy administrative templates (ADMX/ADML) for
`ZitiUpdateService`, the automatic-update and aliveness-monitor component of
**Ziti Desktop Edge for Windows**.

Group Policy allows IT administrators to lock specific update settings across a fleet of
managed endpoints. When a setting is controlled by policy, `ZitiUpdateService` applies the
GPO value at startup and ignores any value in `settings.json` or `ZitiUpdateService.exe.config`
for that setting. Any IPC or file-based attempt to change a locked setting is silently ignored.

---

## Registry Path

All `ZitiUpdateService` policy values live under:

```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
```

The presence of a named value under this key is the authoritative signal that the setting is
GPO-controlled. If the value is absent (policy "Not Configured"), the setting is read from
`settings.json` or `ZitiUpdateService.exe.config` as normal.

> **Note:** The parent key
> `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\` is shared with other
> components of the product (e.g., `ziti-edge-tunnel` uses a sibling subkey). Each service
> has its own ADMX file and its own subkey.

---

## Policy Settings

All policies are **Computer Configuration** (machine-wide). There are no user-level policies.

| Policy Name | Registry Value | Type | Valid Values | Overrides |
|---|---|---|---|---|
| Disable Automatic Updates | `DisableAutomaticUpdates` | `REG_DWORD` | `1` (disabled) or `0` (enabled) | `AutomaticUpdatesDisabled` in `settings.json` |
| Configure Update Stream URL | `UpdateStreamURL` | `REG_SZ` | Any valid HTTPS URL | `AutomaticUpdateURL` in `settings.json`; `UseBetaReleases` in `App.config` |
| Configure Update Check Interval | `UpdateIntervalSeconds` | `REG_DWORD` | ≥ `600` (10 min) | `UpdateTimer` in `App.config` |
| Configure Installation Reminder Interval | `InstallationReminderSeconds` | `REG_DWORD` | Any positive integer (seconds) | `InstallationReminder` in `App.config` |
| Configure Critical Installation Threshold | `InstallationCriticalSeconds` | `REG_DWORD` | Any non-negative integer; `0` = force-install immediately | `InstallationCritical` in `App.config` |
| Configure Aliveness Check Threshold | `AlivenessChecksBeforeAction` | `REG_DWORD` | Any positive integer; default `12` (~60 s) | `AlivenessChecksBeforeAction` in `settings.json` |

### Setting details

#### `DisableAutomaticUpdates`

Prevents `ZitiUpdateService` from automatically downloading and applying updates. When set to
`1`, the service will still detect available updates and may notify users, but will never
initiate an installation — including when the `InstallationCriticalSeconds` threshold is
reached. Set to `0` to explicitly re-enable automatic updates via policy (equivalent to
"Not Configured" but explicit).

#### `UpdateStreamURL`

Specifies the full URL of the release feed that the service polls for new versions. Set this
to control which release stream endpoints receive updates from — stable, beta, or any internal
mirror. This single value replaces both `AutomaticUpdateURL` in `settings.json` and
`UseBetaReleases` in `App.config`. The URL must be HTTPS and must return a feed in the same
schema as the default GitHub releases API.

#### `UpdateIntervalSeconds`

How often (in seconds) the service polls the release feed. The minimum enforced value is
`600` (10 minutes); any value below that is silently raised to `600`. For example:

- `600` — every 10 minutes (minimum)
- `3600` — every hour
- `86400` — every day

#### `InstallationReminderSeconds`

How often the service sends a "pending update" notification to the UI after an update is
first detected. Should generally be ≥ `UpdateIntervalSeconds`. If set lower, users will be
notified on every update-check cycle.

#### `InstallationCriticalSeconds`

The age of a detected release (in seconds, measured from its publish date) at which the
update is treated as critical and applied automatically without user interaction. The
auto-install is triggered within 30 seconds of the threshold being crossed.

- `0` — every update is immediately critical; installation begins within 30 seconds of
  detection. **No grace period is given to the user.**
- `604800` — 7 days (the compiled-in default)

> **Interaction with `DisableAutomaticUpdates`:** If `DisableAutomaticUpdates` is `1`, the
> critical threshold is also suppressed. No force-install will occur, regardless of age.

#### `AlivenessChecksBeforeAction`

The number of consecutive failed aliveness checks against `ziti-edge-tunnel` before the
monitor takes corrective action (currently: terminates and restarts the tunneler). Each check
runs approximately every 5 seconds, so the default of `12` equals roughly 60 seconds of
unresponsiveness before action is taken.

---

## Behavior

### At startup

`ZitiUpdateService` calls `apply_gpo_overrides()` immediately after loading `settings.json`
and reading `App.config`. Any values present in the registry overwrite the corresponding
in-memory settings. Values not present in the registry are left at their `settings.json` /
`App.config` values; GPO does not need to configure every setting.

### At runtime

If a `settings.json` file-change event would modify a GPO-locked setting, the new file value
is ignored for that setting. The in-memory value (from the registry) is kept. A `WARN`-level
log entry is emitted.

### After GPO removal

If a policy is removed (set to "Not Configured" in GPMC, or the registry value is deleted),
the value from `settings.json` or `App.config` takes effect on the next service restart.
The runtime lock is lifted on next restart.

---

## Deploying the ADMX/ADML Templates

### Option A — Local machine (single computer)

1. Copy `NetFoundry.ZitiMonitorService.admx` to:
   ```
   C:\Windows\PolicyDefinitions\
   ```
2. Copy `en-US\NetFoundry.ZitiMonitorService.adml` to:
   ```
   C:\Windows\PolicyDefinitions\en-US\
   ```
3. Open the **Local Group Policy Editor** (`gpedit.msc`).
4. Navigate to:
   ```
   Computer Configuration
     └─ Administrative Templates
          └─ NetFoundry
               └─ Ziti Desktop Edge for Windows
                    └─ ziti-monitor-service
   ```
5. Configure the desired policies and click **OK** / **Apply**.
6. Restart the `ZitiUpdateService` service for changes to take effect.

### Option B — Domain (all managed endpoints)

1. On a **domain controller** (or any machine with RSAT), open the
   **Group Policy Management Console** (`gpmc.msc`).
2. Copy the template files to the **Central Store** on SYSVOL:
   ```
   \\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\NetFoundry.ZitiMonitorService.admx
   \\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\en-US\NetFoundry.ZitiMonitorService.adml
   ```
3. Create or edit a **Group Policy Object** (GPO) linked to the target OU.
4. Navigate to the same path as in Option A and configure the policies.
5. Run `gpupdate /force` on endpoints, or wait for the next Group Policy refresh cycle
   (typically 90 minutes ± 30 minutes).

---

## Testing Without Group Policy

You can test GPO behavior on a single machine by writing registry values directly with
`reg.exe` or `regedit`, without installing the ADMX templates.

```bat
REM Create the policy key
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" /f

REM Disable automatic updates
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v DisableAutomaticUpdates /t REG_DWORD /d 1 /f

REM Pin the update stream to the stable release feed
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v UpdateStreamURL /t REG_SZ /d "https://github.com/openziti/desktop-edge-win/releases" /f

REM Check for updates every hour
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v UpdateIntervalSeconds /t REG_DWORD /d 3600 /f

REM Treat updates as critical after 3 days
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v InstallationCriticalSeconds /t REG_DWORD /d 259200 /f

REM Override the update stream URL (air-gapped mirror or specific channel)
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v UpdateStreamURL /t REG_SZ /d "https://updates.internal.example.com/ziti/releases" /f
```

To remove a lock (simulate "Not Configured"):

```bat
reg delete "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v DisableAutomaticUpdates /f
```

Restart the service after any registry change:

```bat
net stop "ZitiUpdateService"
net start "ZitiUpdateService"
```

---

## Files in This Directory

```
ZitiUpdateService/windows/gpo/
├── NetFoundry.ZitiMonitorService.admx   — policy definitions (all languages)
├── en-US/
│   └── NetFoundry.ZitiMonitorService.adml  — English strings and UI presentations
└── README.md                            — this file
```

## Related Source Files

| File | Purpose |
|---|---|
| `utils/Settings.cs` | `settings.json` read/write/watch; `AutomaticUpdatesDisabled`, `AutomaticUpdateURL`, `AlivenessChecksBeforeAction` |
| `App.config` | Compiled-in defaults for `UpdateTimer`, `InstallationReminder`, `InstallationCritical`, `UseBetaReleases` |
| `Utils.cs` | Service utility code and startup initialization |
