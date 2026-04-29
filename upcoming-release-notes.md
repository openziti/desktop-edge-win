# Release 2.11.0.0
## What's New
* Organizational policy enforcement via the Windows registry
    * IT administrators can enforce settings via Group Policy, Microsoft Intune (ADMX ingestion
      or OMA-URI), MDM, or any tool that writes to the managed-policy hive:
      `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\`
    * Registry values take precedence over `settings.json` and `App.config` defaults; if no
      registry value is present, behavior is unchanged from prior releases
    * Policy is enforced server-side by `ziti-monitor-service` — any IPC attempt to mutate a
      locked setting is rejected with a new `MANAGED_BY_POLICY` error code
    * The UI reads policy state from the monitor service at connect time and on every status
      event, disables locked controls, and shows a "Managed by your organization" banner on
      the Automatic Upgrades page
    * A WMI registry tree watcher detects policy writes, changes, and removals live — no
      service restart is required when an admin applies or revokes policy
    * A startup poll timer (5 s × up to 24 attempts, ~2 min total) handles the Group Policy
      boot-race where policy may not yet have applied by the time the service starts
    * Range-sensitive values are clamped on read (e.g. `UpdateTimer` ≥ 600 s,
      `AlivenessChecksBeforeAction` ≥ 1, `MaintenanceWindowStart/End` to 0–23);
      semantic correctness of other values is the administrator's responsibility
    * Settings controllable under `ziti-monitor-service`:
        * `AutomaticUpdatesDisabled` (DWORD) — enable/disable automatic update checks
        * `AutomaticUpdateURL` (String) — pin the update stream to a specific URL
        * `UpdateTimer` (DWORD, seconds) — update-check interval (minimum 600 s)
        * `InstallationReminder` (DWORD, seconds) — reminder countdown for pending updates
        * `InstallationCritical` (DWORD, seconds) — deadline after which a pending update is forced
        * `AlivenessChecksBeforeAction` (DWORD) — missed heartbeats before tunnel restart (min 1)
        * `DeferInstallToRestart` (DWORD) — force all updates to be staged until next restart
        * `MaintenanceWindowStart` (DWORD, 0–23) — start hour of the allowed install window
        * `MaintenanceWindowEnd` (DWORD, 0–23) — end hour (equal to start means "any time")
    * Settings controllable under `ui`:
        * `DefaultExtAuthProvider` (String) — pins the default external auth provider in the
          identity enrollment screen; the provider selector is disabled when set
* Maintenance window
    * Administrators (via policy) or users (when not policy-locked) can configure a daily hour
      range during which automatic installs are permitted
    * Updates that arrive outside the window are held and applied when the window next opens
    * Equal start/end hours means "any time" (no windowing)
    * Exposed in the Automatic Upgrades settings page with two hour-selector combo boxes
* Defer install until next system restart
    * Users (via the Automatic Upgrades page) or administrators (via the `DeferInstallToRestart`
      policy) can opt to stage an update rather than install immediately
    * When deferred, the installer is downloaded and validated in the background
    * Staged installs are registered as a Task Scheduler task under
      `Task Scheduler Library\NetFoundry\ZitiDesktopEdge-PendingUpdate`, running as SYSTEM at
      next startup with highest privileges
    * The task is removed automatically if policy later disables automatic updates,
      and at next service startup once the staged installer has run
* New `MonitorServiceStatusEvent` fields broadcast policy lock state and deferred-install state
  to the UI, including `*Locked` booleans for every policy-controllable setting,
  `MaintenanceWindowStart/End`, `DeferredInstallPending`, `DeferToRestartPending`, and
  `StagingDownloadPending`
* New IPC operations on the monitor service: `setmaintenancewindowstart`,
  `setmaintenancewindowend`; `TriggerUpdate` now accepts a `forceDefer` parameter so the UI
  can request a staged install explicitly
* ADMX/ADML Group Policy templates for `ziti-monitor-service` and the UI, ready for GPMC or
  Intune ADMX ingestion. Bundled as `ZDEW-AdminTemplates-<version>.zip` on each GitHub release.
* PowerShell helper script (`Set-GpoRegistryValues.ps1`) for setting policy registry values
  directly without a full GPO deployment — useful for standalone machines or test rigs
* Per-identity external authentication provider selection is policy-locked when
  `DefaultExtAuthProvider` is set under the `ui` key

## Bugs fixed:
* Sentinel "Updating" window no longer flashes when clicking Perform Update — progress UI now
  appears only when the install actually fires
* Deferred installs fire immediately when policy unblocks them instead of waiting up to one full
  poll interval
* Concurrent `CheckUpdate` invocations are dropped instead of producing duplicate log lines per tick
* WMI registry watcher re-arms after the policy key is deleted and recreated (handles GPO
  re-application at boot)
* Install times are displayed and triggered in local wall-clock time rather than being shifted by
  the machine's UTC offset
* Update badge / alert canvas suppressed when `AutomaticUpdatesDisabled` is set, so users aren't
  nagged about updates policy has blocked
* `MainWindow` tracks monitor connection state on connect / reconnect-failure to avoid stale UI

## Other changes
* Dependency bumps: NLog 5 → 6, Newtonsoft.Json 13.0.3 → 13.0.4, System.CodeDom 8 → 10,
  DnsClient 1.7 → 1.8, Microsoft.Windows.SDK.Contracts 26100 → 28000; added
  `Microsoft.Bcl.Cryptography` and `System.Formats.Asn1`
* Advanced Installer project upgraded 23.2 → 23.6; installer now emits an MSI SHA256 sidecar
  alongside the EXE
* New `DebugMock` solution configuration and `AllowFastInterval` build switch for local testing
  (production builds keep the 600 s minimum poll interval)

## Dependencies
* ziti-tunneler: v1.14.6
* ziti-sdk:      1.14.3
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.4.0
## What's New
* updated to ziti-edge-tunnel v1.14.6

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.14.6
* ziti-sdk:      1.14.3
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]
