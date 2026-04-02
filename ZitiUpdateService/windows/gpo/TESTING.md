# Testing GPO / Policy Override Support — ZitiUpdateService

This document covers how to test the policy override feature locally without a domain
controller, Active Directory, or MDM server. All tests use direct registry manipulation
on Windows via PowerShell.

For a reference of the available policy settings, see [README.md](README.md).
For enterprise deployment, see [DEPLOYMENT.md](DEPLOYMENT.md).

---

## Prerequisites

### 1. Install Ziti Desktop Edge for Windows

The service must be installed so `ZitiUpdateService` is registered and can be started and
stopped. A standard installation is sufficient — no special build is required.

### 2. Verify the service name

```powershell
Get-Service -Name "Ziti Update Service"
```

This is the service name used in all restart commands throughout this document.

---

## Registry Helpers

Save these as a scratch script (`test-policy.ps1`) and dot-source it during testing.

```powershell
$PolicyKey = "HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service"

function New-PolicyKey {
    New-Item -Path $PolicyKey -Force | Out-Null
    Write-Host "Policy key created."
}

function Remove-PolicyKey {
    Remove-Item -Path $PolicyKey -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Policy key removed."
}

function Set-Policy([string]$Name, $Value, [string]$Type = "String") {
    New-PolicyKey
    Set-ItemProperty -Path $PolicyKey -Name $Name -Value $Value -Type $Type
    Write-Host "Set $Name = $Value ($Type)"
}

function Remove-Policy([string]$Name) {
    Remove-ItemProperty -Path $PolicyKey -Name $Name -ErrorAction SilentlyContinue
    Write-Host "Removed $Name"
}

function Show-Policy {
    if (Test-Path $PolicyKey) {
        Get-ItemProperty -Path $PolicyKey
    } else {
        Write-Host "(policy key not present)"
    }
}

function Restart-MonitorService {
    Restart-Service -Name "Ziti Update Service" -Force
    Write-Host "Service restarted."
}
```

---

## Test 1 — Startup: all six values overridden

**Goal:** verify that `GpoSettings.Load()` reads all six fields and logs them at startup,
and that the values take effect even if `settings.json` or `App.config` say something
different.

```powershell
Set-Policy "DisableAutomaticUpdates"    1                                             DWord
Set-Policy "UpdateStreamURL"            "https://updates.internal.example.com/ziti"  String
Set-Policy "UpdateIntervalSeconds"      3600                                          DWord
Set-Policy "InstallationReminderSeconds" 7200                                         DWord
Set-Policy "InstallationCriticalSeconds" 172800                                       DWord
Set-Policy "AlivenessChecksBeforeAction" 24                                           DWord
```

Restart the service and look for these lines in the log
(`%APPDATA%\NetFoundry\ZitiUpdateService\` or the installation log directory):

```
[INFO] GPO overrides loaded — DisableAutomaticUpdates=True, UpdateStreamURL=https://...,
       UpdateIntervalSeconds=3600, InstallationReminderSeconds=7200,
       InstallationCriticalSeconds=172800, AlivenessChecksBeforeAction=24
```

**Pass criteria:** all six values appear in the `GPO overrides loaded` log line; none of
the `settings.json` or `App.config` defaults are used in their place.

---

## Test 2 — Startup: partial override

**Goal:** verify that setting only some values overrides only those; others fall back to
`settings.json` / `App.config` as normal.

```powershell
Remove-PolicyKey
Set-Policy "DisableAutomaticUpdates" 1 DWord
```

Restart the service. Expected log:

```
[INFO] GPO overrides loaded — DisableAutomaticUpdates=True, UpdateStreamURL=(not set),
       UpdateIntervalSeconds=(not set), ...
```

**Pass criteria:**
- `DisableAutomaticUpdates` is `True` in the log
- All other values show `(not set)` — they are taken from `settings.json` / `App.config`

---

## Test 3 — Runtime block: SetAutomaticUpdateDisabled

**Goal:** verify that sending a `SetAutomaticUpdateDisabled` IPC command while
`DisableAutomaticUpdates` is GPO-locked returns an error and does not change the setting.

```powershell
Set-Policy "DisableAutomaticUpdates" 0 DWord   # locked to "enabled"
Restart-MonitorService
```

Now use the Ziti Desktop Edge UI (Settings → toggle automatic updates) or any IPC client
to attempt to disable automatic updates. Expected IPC response:

```json
{ "Message": "Failure", "Code": 3, "Error": "DisableAutomaticUpdates is managed by Group Policy" }
```

And in the service log:

```
[WARN] DisableAutomaticUpdates is managed by Group Policy — change rejected
```

**Pass criteria:** response `Code` is `3` (`MANAGED_BY_GPO`), setting does not change.

---

## Test 4 — Runtime block: SetAutomaticUpdateURL

**Goal:** verify that attempting to change the update URL while `UpdateStreamURL` is
GPO-locked is rejected.

```powershell
Set-Policy "UpdateStreamURL" "https://gpo.example.com/ziti" String
Restart-MonitorService
```

Attempt to change the URL via the UI or IPC. Expected response:

```json
{ "Message": "Failure", "Code": 3, "Error": "UpdateStreamURL is managed by Group Policy" }
```

**Pass criteria:** response `Code` is `3`, URL does not change.

---

## Test 5 — Runtime block: SetReleaseStream

**Goal:** verify that switching between beta/stable release streams is silently blocked
when `UpdateStreamURL` is GPO-locked (since GPO owns the stream URL entirely).

```powershell
Set-Policy "UpdateStreamURL" "https://gpo.example.com/ziti" String
Restart-MonitorService
```

Attempt to switch the release stream via the UI. Expected service log:

```
[WARN] UpdateStreamURL is managed by Group Policy — SetReleaseStream rejected
```

No error is returned to the caller (the command is a fire-and-forget void call), but the
release stream does not change.

**Pass criteria:** `SetReleaseStream rejected` WARN line in the log; stream URL unchanged.

---

## Test 6 — No policy key present

**Goal:** verify baseline behaviour is unchanged when no policy key exists at all.

```powershell
Remove-PolicyKey
Restart-MonitorService
```

Expected log:

```
[DEBUG] GPO registry key absent — no policy overrides in effect
```

Confirm:
- No `GPO overrides loaded` line appears
- `SetAutomaticUpdateDisabled` and `SetAutomaticUpdateURL` IPC commands succeed normally
- The release stream switch works normally

**Pass criteria:** only the "key absent" debug line; all IPC mutations succeed.

---

## Test 7 — Policy removal (restart required)

**Goal:** verify that removing a policy value and restarting the service restores normal
behaviour.

```powershell
# Start with updates locked disabled
Set-Policy "DisableAutomaticUpdates" 1 DWord
Restart-MonitorService
# (verify IPC block from Test 3) ...

# Remove the lock
Remove-Policy "DisableAutomaticUpdates"
Restart-MonitorService
```

After restart, `SetAutomaticUpdateDisabled` IPC commands should succeed again.

**Pass criteria:** after restart with the value removed, the setting can be changed via IPC.

---

## Test 8 — InstallationCriticalSeconds = 0 (force-install immediately)

**Goal:** verify that setting the critical threshold to `0` causes the service to treat
any detected update as immediately critical and schedule installation within 30 seconds.

```powershell
Set-Policy "InstallationCriticalSeconds" 0 DWord
Restart-MonitorService
```

If an update is available, the service log should show:

```
[WARN] Installation is critical! ... approximate install time: <~30 seconds from now>
```

**Pass criteria:** `Installation is critical!` log line appears shortly after service start
(assuming an update is available).

---

## Test Matrix

| Test | Policy state | Action | Expected outcome |
|---|---|---|---|
| 1 | All six values set | Startup | All six values in `GPO overrides loaded` log |
| 2 | DisableAutomaticUpdates only | Startup | Only that field non-null in log |
| 3 | DisableAutomaticUpdates set | SetAutomaticUpdateDisabled IPC | `Code: 3`, WARN logged |
| 4 | UpdateStreamURL set | SetAutomaticUpdateURL IPC | `Code: 3`, WARN logged |
| 5 | UpdateStreamURL set | SetReleaseStream IPC | WARN logged, no error returned |
| 6 | No policy key | Any IPC mutation | Normal success |
| 7 | Value removed, restart | SetAutomaticUpdateDisabled IPC | Success |
| 8 | InstallationCriticalSeconds = 0 | Update available | Force-install within 30 s |

---

## Checking the Registry Manually

```powershell
# View current policy state
Show-Policy

# Quick one-liner
Get-ItemProperty `
    "HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" `
    -ErrorAction SilentlyContinue
```

```cmd
rem Or with reg.exe
reg query "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service"
```
