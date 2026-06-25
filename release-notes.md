# Release 2.11.2.5
## What's New
n/a

## Bugs fixed
* [Issue 1018](https://github.com/openziti/desktop-edge-win/issues/1018) - Fix "Any time" maintenance window ignoring the configured day for automatic updates

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.18.0
* ziti-sdk:      1.18.0
* tlsuv:         v0.41.4[OpenSSL 3.6.2 7 Apr 2026]
* tlsuv:         v0.41.4[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.11.2.4
## What's New
- Clicking "enable MFA" on an identity in the main UI now opens the MFA setup view directly when MFA needs to be enabled.

## Bugs fixed
- https://github.com/openziti/desktop-edge-win/issues/947

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.18.0
* ziti-sdk:      1.18.0
* tlsuv:         v0.41.3[OpenSSL 3.6.2 7 Apr 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.11.2.3
## What's New
n/a

## Bugs fixed
n/a

## Other changes
n/a



## Dependencies
* ziti-tunneler: v1.17.1
* ziti-sdk:      1.17.1
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.11.2.2
## What's New
* Enroll to cert / token via external JWT signers
    * Joining a network by URL now discovers the controller's external JWT signers and routes
      enrollment through them when applicable
    * If the controller has no external signers, behavior is unchanged
    * If a single signer is offered with a single mode, the user is taken straight through
    * If a choice is required (multiple signers, or one signer that supports both cert and
      token enrollment), a new picker dialog appears
    * When the controller asks the user to authenticate in a browser, ZDEW launches it and
      the identity arrives once sign-in completes
* Welcome screen for first-run / no-identities state ([#1006](https://github.com/openziti/desktop-edge-win/issues/1006))
    * Appears automatically when the app has zero identities, dismissable per session
    * Inline "Add by JWT" / "Add by URL" links route straight into the existing add-identity flow
    * Pasting a URL from the clipboard pre-fills the Add-by-URL field and pre-selects the text
      so the first keystroke replaces it
    * NetFoundry logo doubles as a drag handle (left-click drag to detach, right-click to
      reattach) -- same gesture as the central Z
* Tray-icon context menu rebuilt ([#1007](https://github.com/openziti/desktop-edge-win/issues/1007))
    * "By NetFoundry vX.Y.Z" branding header with the NF mark
    * "Open OpenZiti Desktop Edge" brings the main window forward
    * Identities section (visible when at least one is enrolled) shows each identity with
      service count and status; clicking opens that identity's details panel
    * "Switch Tunneler" submenu (visible only when more than one ziti-edge-tunnel instance is
      running) mirrors the Ctrl+Shift+T dev picker
    * Add Identity by JWT / URL shortcuts -- same handlers as the in-app + button
    * Logging submenu: Set Log Level (Trace/Verbose/Debug/Info/Warn/Error with live
      checkmark) plus Open log folder
    * Help submenu: Show Welcome screen, Check for updates (with in-place spinner that keeps
      the menu open while running, and an "Update Now (vX.Y.Z)" entry that appears when an
      update is staged), Capture Feedback, Discourse Community, NetFoundry Support
    * "Close UI" preserved at the bottom
* Maintenance window can now run daily, weekly, or monthly
    * Weekly: pick a day of the week
    * Monthly: pick a specific day of the month, or pick an ordinal weekday like the third
      Tuesday or the last Friday
    * Hour-of-day start and end still applies within the qualifying day
    * Settings available via Group Policy, the bundled helper script, and the Automatic
      Upgrades panel in the app

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.16.1
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.11.1.0
## What's New
* Enroll to cert / token via external JWT signers
    * Joining a network by URL now discovers the controller's external JWT signers and routes
      enrollment through them when applicable
    * If the controller has no external signers, behavior is unchanged
    * If a single signer is offered with a single mode, the user is taken straight through
    * If a choice is required (multiple signers, or one signer that supports both cert and
      token enrollment), a new picker dialog appears
    * When the controller asks the user to authenticate in a browser, ZDEW launches it and
      the identity arrives once sign-in completes

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.16.1
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.11.0.0
## What's New
* [Issue 985](https://github.com/openziti/desktop-edge-win/issues/985) - Allow registry settings to override and lock ZDEW local/file settings
  * Organizational policy enforcement via the Windows registry
      * IT administrators can enforce settings via Group Policy, Microsoft Intune with the
        provided ADMX ingested, or any tool that writes directly to the managed-policy hive:
        `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\`
      * Registry values take precedence over `settings.json` and `App.config` defaults; if no
        registry value is present, behavior is unchanged from prior releases
      * Values live under `HKLM\SOFTWARE\Policies\...`, which the Group Policy Client Side
        Extension manages directly. When a GPO is unlinked or no longer applies to the
        machine, the corresponding values are removed automatically and the lock is released
      * Policy is enforced server-side by `ziti-monitor-service` -- any IPC attempt to mutate a
        locked setting is rejected with a new `MANAGED_BY_POLICY` error code
      * The UI reads policy state from the monitor service at connect time and on every status
        event, disables locked controls, and shows a "Managed by your organization" banner on
        the Automatic Upgrades page
      * A WMI registry tree watcher detects policy writes, changes, and removals live -- no
        service restart is required when an admin applies or revokes policy
      * A startup poll timer (5 s x up to 24 attempts, ~2 min total) handles the Group Policy
        boot-race where policy may not yet have applied by the time the service starts
      * Some values have enforced bounds (`UpdateTimer` >= 600 s,
        `AlivenessChecksBeforeAction` >= 1, `MaintenanceWindowStart`/`MaintenanceWindowEnd` 0-23).
        Values outside the bounds are adjusted to the nearest allowed value and the
        effective value is logged
      * Settings controllable under `ziti-monitor-service`:
          * `AutomaticUpdatesDisabled` (DWORD) -- enable/disable automatic update checks
          * `AutomaticUpdateURL` (String) -- pin the update stream to a specific URL
          * `UpdateTimer` (DWORD, seconds) -- update-check interval (minimum 600 s)
          * `InstallationReminder` (DWORD, seconds) -- reminder countdown for pending updates
          * `InstallationCritical` (DWORD, seconds) -- deadline after which a pending update is forced
          * `AlivenessChecksBeforeAction` (DWORD) -- missed heartbeats before tunnel restart (min 1)
          * `DeferInstallToRestart` (DWORD) -- force all updates to be staged until next restart
          * `MaintenanceWindowStart` (DWORD, 0-23) -- start hour of the allowed install window
          * `MaintenanceWindowEnd` (DWORD, 0-23) -- end hour (equal to start means "any time")
      * Settings controllable under `ui`:
          * `DefaultExtAuthProvider` (String) -- pins the default external auth provider in the
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
  * PowerShell helper script (`Set-PolicyRegistryValues.ps1`) for setting policy registry values
    directly without a full GPO deployment -- useful for standalone machines or test rigs
  * Per-identity external authentication provider selection is policy-locked when
    `DefaultExtAuthProvider` is set under the `ui` key
* [Issue 970](https://github.com/openziti/desktop-edge-win/issues/970) - Added toast notifications for external auth success/failure when the UI is minimized and auto-launch browser for single-provider external auth

## Bugs fixed
* [Issue 776](https://github.com/openziti/desktop-edge-win/issues/776) - Feedback collection no longer times out prematurely on large or verbose log bundles
    * Progress dialog shows the current phase (copy, collect, zip) and bundle size
    * Stall detection: if the service stops sending progress for 10 seconds the UI surfaces an error
    * A notice is shown when feedback is requested while a previous collection is still in progress
    * Error dialog reports the underlying error rather than always saying "monitor service is offline"
    * Symlinked log files are skipped, and a duplicate ZET log copy step was removed

## Other changes
* Dependency bumps: NLog 5 -> 6, Newtonsoft.Json 13.0.3 -> 13.0.4, System.CodeDom 8 -> 10,
  DnsClient 1.7 -> 1.8, Microsoft.Windows.SDK.Contracts 26100 -> 28000; added
  `Microsoft.Bcl.Cryptography` and `System.Formats.Asn1`
* Advanced Installer project upgraded 23.2 -> 23.6; installer now emits an MSI SHA256 sidecar
  alongside the EXE

## Dependencies
* ziti-tunneler: v1.16.1
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.7.0
## What's New
* updated to [ziti-edge-tunnel v1.16.1](https://github.com/openziti/ziti-tunnel-sdk-c/releases/tag/v1.16.1)

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.16.1
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.6.0
## What's New
* [Issue 818](https://github.com/openziti/desktop-edge-win/issues/818) - Added update progress UI
  * Update service sends progress and failure status to the UI during updates
  * Upgrade sentinel shows a dismissable progress dialog on user-triggered updates
  * Update failures are shown to the user instead of silently hanging
  * Automatic updates now relaunch the UI after the install completes if the UI was running prior to the update
* updated to [ziti-edge-tunnel v1.16.0](https://github.com/openziti/ziti-tunnel-sdk-c/releases/tag/v1.16.0)

## Bugs fixed
* [Issue 823](https://github.com/openziti/desktop-edge-win/issues/823) - Fixed a crash when rapidly scrolling the main identity list

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.16.0
* ziti-sdk:      1.16.0
* tlsuv:         v0.41.3[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.3[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.5.0
## What's New
* updated to ziti-edge-tunnel v1.15.1

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.15.1
* ziti-sdk:      1.15.0
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

# Release 2.10.3.0
## What's New
Support for L2 services.

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.14.4
* ziti-sdk:      1.14.3
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.1.0
## What's New
* updated to ziti-edge-tunnel v1.14.1

## Bugs fixed:
* Fixed forget identity confirmation modal not consistently filling the full UI area

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.14.1
* ziti-sdk:      1.14.1
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.10.0.0
## What's New
* updated to ziti-edge-tunnel v1.14.0
* Added L2 networking support to tunnel configuration
    * L2 Enabled toggle to enable/disable layer 2 mode
    * Pcap interface selection dropdown for choosing a network interface when L2 is enabled
    * Use Pcap checkbox to control whether a Pcap interface is sent to the tunneler
* Replaced `UpdateTunIpv4` IPC command with `UpdateInterfaceConfig` which sends L3 and L2 options in a single payload

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.14.0
* ziti-sdk:      1.14.0.2
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.8.0
## What's New
* Added column headers to allow ascending/descending sorting of identities based on:
    * Status (enabled/disabled)
    * Name
    * Number of services
* [Issue 887](https://github.com/openziti/desktop-edge-win/issues/887) - OS toast notifications when identities require authorization (ext-auth or MFA)
    * Single identity notifications include an Authenticate button; multiple identities are batched into a summary over a sliding 5-second window
    * Notifications are suppressed when the app is visible in the foreground
    * Disabled identities are excluded from the notification count

## Bugs fixed:
* Prevent identities from incorrectly persisting through disconnect/reconnect when identities no longer exist in the app data directory
* [Issue 924](https://github.com/openziti/desktop-edge-win/issues/924) - Fix identity details service list scaling up and down when sorting or filtering services

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.2
* ziti-sdk:      1.11.7
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.7.2
## What's New
* updated to ziti-edge-tunnel v1.11.5

## Bugs fixed
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.5
* ziti-sdk:      1.11.9
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.7.1
## What's New
* updated to ziti-edge-tunnel v1.11.4

## Bugs fixed:
* handle token update errors (via ziti-sdk-c)

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.4
* ziti-sdk:      1.11.8
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.7.0
## What's New
* updated to ziti-edge-tunnel v1.11.2

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.2
* ziti-sdk:      1.11.7
* tlsuv:         v0.41.1[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.41.1[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.6.1
## What's New
* updated to ziti-edge-tunnel v1.11.1

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.1
* ziti-sdk:      1.11.4
* tlsuv:         v0.40.13[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.40.13[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.6.0
## What's New
* updated to ziti-edge-tunnel v1.11.1

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.1
* ziti-sdk:      1.11.4
* tlsuv:         v0.40.13[OpenSSL 3.6.1 27 Jan 2026]
* tlsuv:         v0.40.13[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.5.0
## What's New
* uses 1.11.0 tunneler with fix for legacy authentication issues

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.11.0
* ziti-sdk:      1.11.2
* tlsuv:         v0.40.13[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.13[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.4.1
## What's New
* reverted to 1.9.6 tunneler for stable stream only to address legacy auth-related issues reported
  This version is only intended for the __stable__ stream.

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.9.6
* ziti-sdk:      1.9.17
* tlsuv:         v0.39.7[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.39.7[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.4.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.10
* ziti-sdk:      1.10.10
* tlsuv:         v0.40.10[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.10[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.3.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.9
* ziti-sdk:      1.10.9
* tlsuv:         v0.40.9[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.9[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.2.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.6
* ziti-sdk:      1.10.7
* tlsuv:         v0.40.5[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.5[win32crypto(CNG): ncrypt[1.0] ]

# Release 2.9.1.0
## What's New
* updated dependencies

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: v1.10.5
* ziti-sdk:      1.10.6
* tlsuv:         v0.40.4[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         v0.40.4[win32crypto(CNG): ncrypt[1.0] ]
