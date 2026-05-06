# ZitiUpdateService -- Automatic Update Configuration

## Config File Location

The primary runtime settings file is stored at:

```
%APPDATA%\NetFoundry\ZitiUpdateService\settings.json
```

When running as the SYSTEM account (normal service execution):

```
C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\ZitiUpdateService\settings.json
```

When running under a user account (e.g. in the debugger):

```
C:\Users\[USERNAME]\AppData\Roaming\NetFoundry\ZitiUpdateService\settings.json
```

Defined in [`utils/Settings.cs:50-53`](utils/Settings.cs).

---

## Three-Tier Configuration (Priority Order)

Settings are resolved from three sources. Higher tiers win.

```
  1. Managed policy registry  (highest -- admin-enforced, cannot be overridden at runtime)
  2. settings.json             (mid -- hot-reloadable, writable via IPC)
  3. ZitiUpdateService.exe.config  (lowest -- startup-only, requires service restart)
```

`PolicySettings.cs` implements the `Effective*()` helpers that apply this priority order.
`PolicySettings.IsLocked(name)` returns `true` when a value is present in the registry,
which is used to prevent IPC calls from overwriting policy-controlled values.

---

## Tier 1: Managed Policy (Registry Overrides)

Registry root:

```
HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
```

This key is the standard Windows managed-policy path.  Values are written by Group Policy
Objects (via the supplied ADMX/ADML templates), Microsoft Intune (ADMX ingestion or OMA-URI),
MECM Compliance Settings, or direct registry writes for testing.

Implemented in [`utils/PolicySettings.cs`](utils/PolicySettings.cs).

| Registry Value Name          | Type      | Overrides                    | Description                                                                   |
|------------------------------|-----------|------------------------------|-------------------------------------------------------------------------------|
| `AutomaticUpdatesDisabled`   | REG_DWORD | `settings.json`              | `1` = suppress all auto-installs (notify only). `0` = allow.                 |
| `AutomaticUpdateURL`         | REG_SZ    | `settings.json`              | Override the update feed URL.                                                 |
| `AlivenessChecksBeforeAction`| REG_DWORD | `settings.json`              | Consecutive failed health checks before the tunneler is restarted.            |
| `UpdateTimer`                | REG_DWORD | `ZitiUpdateService.exe.config` | Poll interval in seconds. Minimum enforced: 600.                            |
| `InstallationReminder`       | REG_DWORD | `ZitiUpdateService.exe.config` | Re-notification interval in seconds.                                        |
| `InstallationCritical`       | REG_DWORD | `ZitiUpdateService.exe.config` | Release age in seconds after which update is force-installed. `0` = immediate. |

### Live Change Detection

`PolicySettings.StartWatching()` subscribes to WMI `RegistryTreeChangeEvent` on the parent
key path. Changes (create, modify, delete) are debounced with a 500 ms trailing-edge timer
so rapid sequential writes (e.g. a script setting multiple values) coalesce into a single
reload. After reload, before/after field changes are logged and `OnConfigurationChange` is
fired, which triggers the same path as a `settings.json` change.

The watcher is started in `UpdateService.OnStart()` and stopped in `UpdateService.OnStop()`.

### Deploying GPO Templates

ADMX/ADML template files are in [`windows/gpo/`](windows/gpo/):

| File                                          | Purpose                             |
|-----------------------------------------------|-------------------------------------|
| `NetFoundry.ZitiMonitorService.admx`          | Policy definitions                  |
| `en-US/NetFoundry.ZitiMonitorService.adml`    | English display strings             |

Copy both files to `%SystemRoot%\PolicyDefinitions\` (and the `.adml` into the `en-US`
subdirectory) on each machine, or to the SYSVOL `PolicyDefinitions` share on a domain
controller. The policies then appear in Group Policy Editor under:

```
Computer Configuration -> Administrative Templates -> NetFoundry -> Ziti Desktop Edge for Windows -> ziti-monitor-service
```

### Microsoft Intune

Intune supports custom ADMX templates via **OMA-URI**. Upload the `.admx` and `.adml`
files as custom configuration profiles. The OMA-URI path follows the standard
`./Device/Vendor/MSFT/Policy/ConfigV2/...` schema. This allows the same registry values
to be enforced without a domain controller.

### PowerShell Helper Script

[`windows/gpo/Set-PolicyRegistryValues.ps1`](windows/gpo/Set-PolicyRegistryValues.ps1) writes
or previews the registry values directly. Useful for Intune script deployment or manual
testing. Run without parameters to see usage and the current key values.

```powershell
# Preview all values (no changes made)
.\Set-PolicyRegistryValues.ps1 -WhatIf

# Disable automatic updates, check every 30 minutes
.\Set-PolicyRegistryValues.ps1 -AutomaticUpdatesDisabled 1 -UpdateTimer 1800

# List current GPO values
Get-ItemProperty 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'

# Remove a single value (falls back to settings.json / exe.config)
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service' -Name 'AutomaticUpdateURL'

# Remove all GPO overrides for this service
Remove-Item -Path 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service' -Recurse
```

> **Important:** Setting ANY registry value under this key causes the Ziti Desktop Edge
> UI to display a "Managed by your organization" banner on the Automatic Upgrades screen
> and disables all update-related controls so end users cannot override policy at runtime.

---

## Tier 2: `settings.json` -- Runtime / User-Modifiable

Hot-reloaded at runtime via a `FileSystemWatcher` ([`Settings.cs:55-65`](utils/Settings.cs)).
Safe to edit while the service is running.

| Property                     | Type    | Description                                                              |
|------------------------------|---------|--------------------------------------------------------------------------|
| `AutomaticUpdatesDisabled`   | bool    | Set to `true` to disable all automatic update installs.                  |
| `AutomaticUpdateURL`         | string  | Override the default update feed URL. Leave null/empty for default.      |
| `AlivenessChecksBeforeAction`| int?    | Number of consecutive failed health checks before the tunneler is killed.|

**Read/write implementation:**
- Load: [`Settings.cs:69-83`](utils/Settings.cs) -- reads file, deserializes via Newtonsoft.Json
- Write: [`Settings.cs:84-99`](utils/Settings.cs) -- pauses watcher, serializes with indentation, re-enables watcher
- Hot-reload: [`Settings.cs:118-144`](utils/Settings.cs) -- `OnChanged` callback with 500 ms retry for file-lock

**Methods that write `settings.json`** (blocked silently when the corresponding GPO value is set):
- `SetAutomaticUpdateDisabled()` -- [`UpdateService.cs:178-187`](UpdateService.cs)
- `SetAutomaticUpdateURL()` -- [`UpdateService.cs:124-156`](UpdateService.cs)

---

## Tier 3: `ZitiUpdateService.exe.config` -- App Config (next to the `.exe`)

Read at startup via `ConfigurationManager.AppSettings.Get(...)`. Requires a service
restart to take effect. All three values can be overridden at runtime via GPO (Tier 1).

| Key                    | Default       | Format              | Description                                                                         |
|------------------------|---------------|---------------------|-------------------------------------------------------------------------------------|
| `UpdateTimer`          | `0:0:10:0`    | `days:hours:min:sec`| How often the service polls for a new release. Minimum enforced: 10 minutes.        |
| `InstallationReminder` | `1:0:0:0`     | `days:hours:min:sec`| How often the user is prompted about a pending update. Should be ≥ `UpdateTimer`.  |
| `InstallationCritical` | `7:0:0:0`     | `days:hours:min:sec`| Age of a release after which it is treated as critical and auto-installed forcibly. |

**Where these are read in [`UpdateService.cs`](UpdateService.cs):**
- `UpdateTimer` -- line 671
- `InstallationReminder` -- line 1079
- `InstallationCritical` -- line 1088

When a release is deemed critical, installation is triggered within 30 seconds
([`UpdateService.cs:784`](UpdateService.cs)).

---

## Release Channel Marker File

| File                  | Location              | Effect                          |
|-----------------------|-----------------------|---------------------------------|
| `use-beta-stream.txt` | Same dir as `.exe`    | Present -> beta stream; absent -> stable stream |

Managed by `SetReleaseStream()` in [`UpdateService.cs:253-277`](UpdateService.cs).

The `AutomaticUpdateURL` GPO value (or `settings.json` value) supersedes the marker file --
if a URL is explicitly set, the marker file is irrelevant.

---

## Update Check Flow

```
Service Startup (UpdateService constructor, lines 87-92)
  ├─ Load settings.json (or initialize if missing)
  ├─ Load PolicySettings (registry read)
  ├─ Start PolicySettings WMI watcher
  └─ Subscribe to OnConfigurationChange event (settings.json + policy)

Periodic Poll (SetupServiceWatchers, line 671+)
  ├─ Read UpdateTimer: policy registry -> else exe.config
  └─ On each tick -> CheckUpdate()

CheckUpdate() (lines 760-804)
  ├─ Resolve effective URL:   policy -> settings.json -> compiled-in default
  ├─ If AutomaticUpdatesDisabled (policy or settings.json) -> notify only, skip install
  ├─ If release age ≥ InstallationCritical -> force-install within 30s
  └─ Otherwise -> notify user, respect InstallationReminder cadence

settings.json Change (any time)
  ├─ FileSystemWatcher fires OnChanged
  ├─ Load() -> Update() -> OnConfigurationChange event
  └─ All consumers receive updated settings immediately

Policy Registry Change (any time)
  ├─ WMI RegistryTreeChangeEvent fires
  ├─ 500 ms debounce timer resets on each event
  ├─ After silence: PolicySettings.Load() -> log before/after -> OnConfigurationChange
  └─ All consumers receive updated settings immediately
```

---

## UI Behavior When Policy Is Active

When any registry value is present under the managed-policy key, `MonitorServiceStatusEvent`
carries `*Locked` boolean fields for each setting. The UI client reads these and sets
`GpoPolicyViewModel.AutomaticUpgradesPolicyControlled = true`, which:

- Shows a "Managed by your organization" banner at the top of the Automatic Upgrades screen
- Dims all section headings and controls to 30% opacity
- Disables the Enabled/Disabled toggle and the URL text box
- Hides the Set URL and Reset buttons
- Keeps "Check for Updates Now" active (read-only action, not a settings change)

Removing all registry values (or the key itself) causes the service to emit a new event
with all locked fields `false`, which clears the banner and re-enables all controls
without requiring a UI restart.

---

## Known Gaps

| Gap | Impact | Notes |
|-----|--------|-------|
| No "install on next restart" mode | High -- critical for 911 dispatch and other always-on environments | Users need a way to stage an update that applies only at the next planned reboot, not mid-session |
| No maintenance window / scheduled install time | Medium | Grace period is age-based, not time-of-day-based |
| No Intune OMA-URI documentation | Medium | ADMX templates work with Intune but the configuration steps are undocumented |
