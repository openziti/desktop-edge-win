# Enterprise Deployment Guide — `ziti-monitor` Policy Support

This document covers how to deploy and manage `ziti-monitor` (the `ZitiUpdateService` assembly) policy
settings across managed endpoints using enterprise tooling. The same registry values and ADMX templates are
used regardless of which tool writes them.

For a reference of the available policy settings (value names, types, ranges, fall-back behavior), see
[README.md](README.md).

---

## Supported deployment methods

| Tool                                            | Mechanism                                                       |
| ----------------------------------------------- | --------------------------------------------------------------- |
| Local Group Policy Editor (`gpedit.msc`)        | ADMX in `C:\Windows\PolicyDefinitions\`                         |
| Active Directory Group Policy (GPMC)            | ADMX in SYSVOL Central Store                                    |
| Microsoft Intune (ADMX ingestion)               | Upload via Intune portal                                        |
| Microsoft Intune (OMA-URI, no ADMX)             | Direct registry write via custom profile                        |
| Microsoft Endpoint Configuration Manager (MECM) | Via AD GPO or Compliance Settings                               |
| Direct registry write                           | `reg.exe`, PowerShell, `Set-PolicyRegistryValues.ps1`, anything |

All of these write to the same key:

```
HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
```

The service does not care which tool wrote the value — the WMI watcher reloads policy within ~500 ms of any
write to that subtree, and the UI banner appears within ~1 second after that. **No service restart is
required** for policy changes to take effect.

---

## Local Group Policy Editor (single machine)

Use for testing on an unmanaged machine, lab boxes, or kiosk-style deployments.

1. Copy the template files:

   ```
   NetFoundry.ZitiMonitorService.admx        →  C:\Windows\PolicyDefinitions\
   en-US\NetFoundry.ZitiMonitorService.adml  →  C:\Windows\PolicyDefinitions\en-US\
   ```

   If you also want the UI's `DefaultExtAuthProvider` policy, copy the templates from
   `DesktopEdge/windows/gpo/` to the same locations. The two ADMX files are independent — neither
   requires the other to be present.

2. Open `gpedit.msc`.

3. Navigate to:

   ```
   Computer Configuration
     └─ Administrative Templates
          └─ NetFoundry
               └─ Ziti Desktop Edge for Windows
                    └─ ziti-monitor-service
   ```

4. Configure the desired policies and click **OK**.

5. Verify:

   ```powershell
   Get-ItemProperty 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'
   ```

The service picks the change up live — within seconds, the log line
`Policy overrides loaded: ...` shows the new values, and any connected UI greys out the Automatic Upgrades
panel. No service restart needed.

---

## Active Directory Group Policy (domain)

### Deploy the ADMX templates (one-time)

Copy templates to the **Central Store** on SYSVOL:

```powershell
$domain  = (Get-WmiObject Win32_ComputerSystem).Domain
$central = "\\$domain\SYSVOL\$domain\Policies\PolicyDefinitions"

if (-not (Test-Path $central)) {
    Copy-Item C:\Windows\PolicyDefinitions $central -Recurse
}

Copy-Item NetFoundry.ZitiMonitorService.admx        "$central\"
Copy-Item en-US\NetFoundry.ZitiMonitorService.adml  "$central\en-US\"

# Optional — only if you want the UI's DefaultExtAuthProvider policy to appear in GPMC too
Copy-Item NetFoundry.ZitiDesktopEdgeUI.admx        "$central\"
Copy-Item en-US\NetFoundry.ZitiDesktopEdgeUI.adml  "$central\en-US\"
```

### Configure the GPO

1. Open `gpmc.msc` on the domain controller or an RSAT workstation.
2. Create or edit a GPO and link it to the target OU.
3. Navigate to:

   ```
   Computer Configuration
     └─ Policies
          └─ Administrative Templates
               └─ NetFoundry
                    └─ Ziti Desktop Edge for Windows
                         └─ ziti-monitor-service
   ```

4. Configure the desired policies. Click **OK**.

### Apply on endpoints

```powershell
gpupdate /force
```

…or wait for the standard refresh (90 ± 30 minutes for computer policy). Endpoints pick the change up live;
no service restart required.

---

## Microsoft Intune — ADMX ingestion

Intune supports importing custom ADMX files and exposing them as configurable policies in the portal. No
domain controller or Central Store required.

1. In the [Intune portal](https://intune.microsoft.com), go to:
   **Devices → Windows → Configuration profiles**.
2. **Create → New policy**:
   - Platform: **Windows 10 and later**
   - Profile type: **Templates → Administrative Templates**.
3. Click **Import ADMX**.
4. Upload `NetFoundry.ZitiMonitorService.admx` **first**, then its `.adml`. If you also want the UI's
   `DefaultExtAuthProvider` policy, upload `NetFoundry.ZitiDesktopEdgeUI.admx` + `.adml` **after** the
   monitor-service ADMX is already ingested.

   > **Order matters.** The UI ADMX imports parent categories from the monitor-service ADMX via a
   > `<using>` declaration. Intune validates referenced namespaces at upload time, so the UI ADMX
   > will be rejected with a vague namespace-resolution error if the monitor-service ADMX is not
   > already present in the tenant.
5. The policies appear under the same path as in GPMC:

   ```
   NetFoundry > Ziti Desktop Edge for Windows > ziti-monitor-service
   ```

6. Set each desired policy to **Enabled** with a value, and assign the profile to a device group.

---

## Microsoft Intune — Custom OMA-URI (no ADMX)

If you don't want to ingest the ADMX, write the registry values directly via Custom OMA-URI. One row per
setting:

| Setting                          | OMA-URI                                                                                                                            | Type    | Example       |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- | ------- | ------------- |
| Disable Automatic Updates        | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/AutomaticUpdatesDisabled`     | Integer | `1`           |
| Update Stream URL                | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/AutomaticUpdateURL`           | String  | `https://updates.example.com/ziti.json` |
| Update Check Interval (seconds)  | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/UpdateTimer`                  | Integer | `3600`        |
| Installation Reminder (seconds)  | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/InstallationReminder`         | Integer | `86400`       |
| Critical Threshold (seconds)     | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/InstallationCritical`         | Integer | `604800`      |
| Aliveness Check Threshold        | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/AlivenessChecksBeforeAction`  | Integer | `12`          |
| Defer Install Until Restart      | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/DeferInstallToRestart`        | Integer | `1`           |
| Maintenance Window Start (hour)  | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/MaintenanceWindowStart`       | Integer | `22`          |
| Maintenance Window End (hour)    | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti%20Desktop%20Edge%20for%20Windows/ziti-monitor-service/MaintenanceWindowEnd`         | Integer | `6`           |

> Spaces in the path must be URL-encoded as `%20` in the OMA-URI. Value names themselves contain no spaces.

---

## Microsoft Endpoint Configuration Manager (MECM / SCCM)

### Option A — via Active Directory Group Policy

If your endpoints are domain-joined, deploy the ADMX templates to the SYSVOL Central Store as in the
[Active Directory Group Policy](#active-directory-group-policy-domain) section above. MECM-managed devices
pick up the policies on their normal GPO refresh cycle. Simplest option for hybrid environments.

### Option B — Compliance Settings (no domain required)

MECM Compliance Settings can write the registry values directly, without GPO or ADMX. Works for workgroup
machines.

1. **Assets and Compliance → Compliance Settings → Configuration Items → Create new**.
   - Platform: **Windows Desktops and Servers**.
   - Type: **Windows Desktops and Servers (custom)**.

2. Add one **Setting** per policy value:

   | Field         | Value                                                                                |
   | ------------- | ------------------------------------------------------------------------------------ |
   | Setting type  | Registry value                                                                       |
   | Hive          | `HKEY_LOCAL_MACHINE`                                                                 |
   | Key           | `SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service`    |
   | Value name    | `AutomaticUpdatesDisabled`, `AutomaticUpdateURL`, `UpdateTimer`, etc. — see [README.md](README.md) for the full list |
   | Data type     | `Integer` for all `REG_DWORD` policies; `String` only for `AutomaticUpdateURL`       |

3. Set **Compliance Rules** to remediate: if the value isn't equal to the required value, set it.

4. Bundle the Configuration Items into a **Configuration Baseline** and deploy to the target collection.

MECM writes/maintains the registry values on each check-in (default every 8 hours, or on demand via
**Machine Policy Retrieval & Evaluation Cycle**).

> **Decommissioning gotcha.** Compliance Settings only *write* values; they do not delete them.
> Removing the Configuration Item or un-deploying the Baseline leaves the previously-written values
> in `HKLM\SOFTWARE\Policies\NetFoundry\…` and they will continue to be enforced by the service
> indefinitely. To fully revert, deploy a follow-up CI (or a one-shot script) that runs
> `Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry'` on the targeted devices.
> This is the same caveat that applies to all `\Policies\` keys written by direct-registry tools
> (Intune OMA-URI behaves similarly).

---

## Direct registry write (testing or scripted rollout)

For testing, or for fleets managed by tools other than the ones above, write the registry values directly.
The repo ships a helper at `Set-PolicyRegistryValues.ps1` that takes one parameter per policy:

```powershell
.\Set-PolicyRegistryValues.ps1 `
    -AutomaticUpdatesDisabled 0 `
    -AutomaticUpdateURL 'https://updates.internal.example.com/ziti.json' `
    -UpdateTimer 3600 `
    -InstallationCritical 259200 `
    -DeferInstallToRestart 1 `
    -MaintenanceWindowStart 22 `
    -MaintenanceWindowEnd 6
```

Or with `reg.exe`:

```bat
REM Disable automatic updates
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v AutomaticUpdatesDisabled /t REG_DWORD /d 1 /f

REM Pin the update stream
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v AutomaticUpdateURL /t REG_SZ /d "https://updates.internal.example.com/ziti.json" /f

REM Hourly poll
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v UpdateTimer /t REG_DWORD /d 3600 /f

REM Treat updates as critical after 3 days
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v InstallationCritical /t REG_DWORD /d 259200 /f

REM Maintenance window 10 PM - 6 AM
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v MaintenanceWindowStart /t REG_DWORD /d 22 /f
reg add "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v MaintenanceWindowEnd /t REG_DWORD /d 6 /f
```

To remove a single policy lock (revert that field to settings.json/App.config):

```bat
reg delete "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" ^
    /v AutomaticUpdatesDisabled /f
```

To remove the whole policy state at once:

```powershell
Remove-Item -Recurse -Force 'HKLM:\SOFTWARE\Policies\NetFoundry'
```

The WMI watcher fires on these writes and the policy is reloaded within ~500 ms — **no service restart
needed**.

---

## Verifying on an endpoint

Regardless of which tool deployed the policy, verify the values are present:

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service' `
    -ErrorAction SilentlyContinue
```

Or check the service log for the `Policy overrides loaded:` line, which lists every effective value (or
`(not set)`) on every reload:

```
C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\logs\ZitiMonitorService\ZitiUpdateService.log
```

If the registry values are present and the log line lists them as expected, policy is in effect. The UI's
Automatic Upgrades panel will show the "Managed by your organization" banner with all controls greyed out.

---

## Policy precedence

When all sources go through the Group Policy engine (domain GPO, local GP, Intune ADMX-ingested
profiles), standard Windows precedence applies:

```
Domain GPO (highest)
  └─ Local Group Policy
       └─ Intune ADMX-ingested profile (lowest)
```

**There is no enforced precedence between direct-registry writers** (Intune Custom OMA-URI,
MECM Compliance Settings, the `Set-PolicyRegistryValues.ps1` helper, `reg.exe`) and Group Policy.
They all target the same `HKLM\SOFTWARE\Policies\NetFoundry\…` key, and the outcome of a conflict
is **last-writer-wins**. The Group Policy engine will overwrite a direct-registry value the next
time the GPO refreshes (`gpupdate /force` or the 90-min cycle); a subsequent MECM check-in will
overwrite the GPO value back; etc. The result is policy ping-pong.

**Choose one authoritative source per environment and stick to it.** If you must mix (e.g. domain
GPO for most settings, OMA-URI for one extra value not yet in your GPO baseline), make sure the
two sources never write the same value name.
