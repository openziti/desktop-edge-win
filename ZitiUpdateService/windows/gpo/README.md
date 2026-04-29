# Group Policy Support — `ziti-monitor` service

This directory contains Windows Group Policy administrative templates (ADMX/ADML) for the `ziti-monitor`
Windows service (assembly: `ZitiUpdateService`), the automatic-update and aliveness-monitor component of
**Ziti Desktop Edge for Windows**.

Group Policy lets administrators lock specific update settings across a fleet of managed endpoints. When a
setting is controlled by policy, the service applies the policy value and rejects any IPC, settings.json, or
UI mutation of that field. The UI shows a "Managed by your organization" banner and greys out locked
controls.

Policy is enforced **server-side** in the monitor service. The UI greys out locked controls proactively, but
even if a non-compliant client (or a hand-crafted IPC message) attempts to change a locked field, the service
rejects the mutation with `MANAGED_BY_POLICY`.

---

## Registry path

All policies live under (machine-wide; no user-level policies):

```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
```

The presence of a named value under this key is the authoritative signal that the setting is policy-controlled.
If the value is absent (policy "Not Configured"), the setting is read from `settings.json` or `App.config` as
normal.

> The parent key `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\` is shared with other
> components of the product (e.g. the Desktop Edge UI uses the sibling `\ui` subkey — see
> `DesktopEdge/windows/gpo/`). Each component has its own ADMX file and subkey.

---

## Live policy reload

The service runs a WMI `RegistryTreeChangeEvent` watcher rooted at
`HKLM\SOFTWARE\Policies\NetFoundry`. Any value write, delete, or subkey create/destroy under that tree
triggers a reload within ~500 ms (debounced) — **no service restart required**. This means GPO changes pushed
via `gpupdate /force`, Intune deltas, or direct registry edits all take effect live, including locking the
Automatic Upgrades panel in any connected UI within ~1 second.

There is one exception: at *first* service startup after install, the service polls every 5 seconds for up to
2 minutes waiting for Group Policy to apply, so that the very first update check uses policy values instead
of `settings.json`/`App.config` defaults. After that two-minute window (or as soon as policy is detected,
whichever is first), the WMI watcher takes over and the poll-loop stops.

---

## Policies

| Display name                           | Registry value                | Type       | Range / format                     |
| -------------------------------------- | ----------------------------- | ---------- | ---------------------------------- |
| Disable Automatic Updates              | `AutomaticUpdatesDisabled`    | `REG_DWORD`| `0` or `1`                         |
| Update Stream URL                      | `AutomaticUpdateURL`          | `REG_SZ`   | HTTP(S) URL                        |
| Update Check Interval                  | `UpdateTimer`                 | `REG_DWORD`| seconds; minimum 600 (10 min)      |
| Installation Reminder Interval         | `InstallationReminder`        | `REG_DWORD`| seconds; ≥ 1                       |
| Critical Installation Threshold        | `InstallationCritical`        | `REG_DWORD`| seconds; ≥ 0 (`0` = force-now)     |
| Aliveness Check Threshold              | `AlivenessChecksBeforeAction` | `REG_DWORD`| count; 1–720                       |
| Defer Install Until Restart            | `DeferInstallToRestart`       | `REG_DWORD`| `0` or `1`                         |
| Maintenance Window Start Hour          | `MaintenanceWindowStart`      | `REG_DWORD`| hour; 0–23                         |
| Maintenance Window End Hour            | `MaintenanceWindowEnd`        | `REG_DWORD`| hour; 0–23                         |

> **Format note.** Policy values that represent durations (`UpdateTimer`, `InstallationReminder`,
> `InstallationCritical`) are stored as **seconds** in the registry. The same settings in `App.config` use
> the .NET TimeSpan format `days:hours:minutes:seconds` (e.g. `0:0:10:0` for ten minutes). Don't paste an
> `App.config` TimeSpan string into the registry — use the seconds equivalent.

### Per-policy details

#### `AutomaticUpdatesDisabled`

Master switch. When set to `1`, the service detects available updates but never installs them — including
when `InstallationCritical` would otherwise force an install. Toasts and badges in the UI are also suppressed.
Setting this to `1` while a deferred install is queued **cancels** the queued install immediately. Set to `0`
to explicitly permit auto-installs (equivalent to "Not Configured" but explicit and locked-in).

Falls back to `AutomaticUpdatesDisabled` in `settings.json` (default: `false`) when not set.

#### `AutomaticUpdateURL`

Full URL of the release feed. Replaces the `AutomaticUpdateURL` value in `settings.json`. Must return a feed
in the same JSON schema as the GitHub releases API. Pointing this at an internal mirror is the supported way
to operate in air-gapped environments.

Changing the URL while the service is running causes the next update check to fetch from the new URL. The WMI
watcher re-binds the registry subscription after every reload, so subsequent value writes continue to fire
events even if the policy key is deleted and recreated (e.g. by an admin re-applying GPO at boot).

Falls back to `AutomaticUpdateURL` in `settings.json` when not set, then to the compiled-in default feed.

#### `UpdateTimer`

How often (seconds) the service polls the release feed. The minimum **at the policy layer** is 600 (10 min);
values below that are silently raised to 600. Common choices: `600`, `3600` (hourly), `86400` (daily).

Falls back to `UpdateTimer` in `App.config` (TimeSpan; default `0:0:10:0` = 10 minutes) when not set.

#### `InstallationReminder`

How often (seconds) the service re-notifies the UI about a pending update after first detection. Should be
≥ `UpdateTimer`. Common: `86400` (daily reminder), `604800` (weekly).

Falls back to `InstallationReminder` in `App.config` (TimeSpan; default `1:0:0:0` = 1 day) when not set.

#### `InstallationCritical`

Age of a release (seconds, from `published_at`) at which the update is treated as critical and auto-installed
without user interaction. The auto-install fires within 30 seconds of the threshold being crossed (a
`Thread.Sleep(30s)` between detection and execution gives the user time to see the WARN toast).

- `0` — every update is immediately critical. **No grace period.**
- `604800` — 7 days (`App.config` default).

Suppressed entirely when `AutomaticUpdatesDisabled=1`.

Falls back to `InstallationCritical` in `App.config` (TimeSpan; default `7:0:0:0` = 7 days) when not set.

#### `AlivenessChecksBeforeAction`

How many consecutive failed `ziti-edge-tunnel` aliveness probes (~5 s apart) trigger a tunnel restart. Range
1–720. Default 12 ≈ 60 seconds of unresponsiveness.

Falls back to `AlivenessChecksBeforeAction` in `settings.json` (default 12) when not set.

#### `DeferInstallToRestart`

When set to `1`, ready-to-install updates are staged but not applied until the next system reboot. The
service registers a Task Scheduler entry at `Task Scheduler Library\NetFoundry\ZitiDesktopEdge-PendingUpdate`
that runs the staged installer at next boot as `SYSTEM`. The task is auto-removed after the install runs.

Suppressed by `AutomaticUpdatesDisabled=1` (no install at all). Suppressed by `InstallationCritical` once the
critical threshold is crossed (force-install fires regardless).

Falls back to `DeferInstallToRestart` in `settings.json` (default `false`) when not set.

#### `MaintenanceWindowStart` / `MaintenanceWindowEnd`

Hour-of-day (0–23, local-clock, 24-hour) bounds for click-time installs. When a user clicks **Perform Update**
outside the window, the install is queued and fires when the window next opens. Setting `Start == End`
disables the window (installs allowed any time). When `End < Start`, the window crosses midnight (e.g.
`Start=22, End=6` ⇒ 10 PM – 6 AM).

Clearing the window keys (or `DeferInstallToRestart`) while a deferred install is queued causes the install
to fire **immediately** rather than waiting for the next poll tick.

Both fall back to the corresponding fields in `settings.json` (default unset = no window) when not set.

---

## Behavior summary

| Event                                      | Effect                                                                          |
| ------------------------------------------ | ------------------------------------------------------------------------------- |
| Policy value written                       | Loaded within ~500 ms (WMI debounce). Field is locked; `settings.json`/IPC writes rejected. |
| Policy value deleted                       | Field unlocks within ~500 ms. Falls back to `settings.json` / `App.config`.     |
| Whole policy key deleted                   | All fields unlock. UI banner disappears within ~1 s.                            |
| `settings.json` mutation of locked field   | Rejected with `MANAGED_BY_POLICY`. In-memory policy value is kept.              |
| IPC `setautomaticupgradedisabled` etc. on locked field | Rejected with `MANAGED_BY_POLICY`. Caller sees an error code.       |
| Service starts with policy already in registry | Policy is loaded *before* the first update check, so initial behavior matches policy. |
| Service starts with no policy (fresh install) | Polls registry every 5 s for up to 2 min waiting for Group Policy to apply. |

---

## Files in this directory

```
ZitiUpdateService/windows/gpo/
├── NetFoundry.ZitiMonitorService.admx       — policy definitions (all languages)
├── en-US/
│   └── NetFoundry.ZitiMonitorService.adml   — English strings + presentation
├── Set-PolicyRegistryValues.ps1             — helper to write/clear policy values without GPMC
├── README.md                                — this file
└── DEPLOYMENT.md                            — GPMC / Intune / MECM deployment recipes
```

The UI's `DefaultExtAuthProvider` policy lives in a separate ADMX/ADML pair under `DesktopEdge/windows/gpo/`
because it writes to a different subkey (`...\ui\`) and ships with the UI binary, not the monitor service.

## Related source files

| File                                       | Role                                                                            |
| ------------------------------------------ | ------------------------------------------------------------------------------- |
| `ZitiUpdateService/utils/PolicySettings.cs`| Reads the registry, exposes `IsLocked()` / `Effective*()`, runs WMI watcher.    |
| `ZitiUpdateService/utils/Settings.cs`      | `settings.json` read/write; locked setters reject with `MANAGED_BY_POLICY`.     |
| `ZitiUpdateService/UpdateService.cs`       | Wires policy into update flow, maintenance windows, deferred-install staging.   |
| `ZitiUpdateService/App.config`             | Compiled-in defaults for `UpdateTimer`, `InstallationReminder`, `InstallationCritical`. |
