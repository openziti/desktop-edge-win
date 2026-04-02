# Enterprise Deployment Guide — ZitiUpdateService Policy Support

This document covers how to deploy and manage `ZitiUpdateService` (ziti-monitor-service)
policy settings across managed endpoints using enterprise tooling. The same registry values
and ADMX templates are used regardless of which tool writes them.

For a reference of the available policy settings, see [README.md](README.md).
For testing without enterprise tooling, see [TESTING.md](TESTING.md).

---

## Supported Deployment Methods

| Tool | Mechanism |
|---|---|
| Local Group Policy Editor (`gpedit.msc`) | ADMX in `C:\Windows\PolicyDefinitions\` |
| Active Directory Group Policy (GPMC) | ADMX in SYSVOL Central Store |
| Microsoft Intune (ADMX ingestion) | Upload via Intune portal |
| Microsoft Intune (OMA-URI, no ADMX) | Direct registry write via custom profile |
| Microsoft Endpoint Configuration Manager (MECM) | Via AD GPO or Compliance Settings |
| Direct registry write | `reg.exe`, PowerShell, or any configuration management tool |

---

## Windows: Local Group Policy Editor

Use this for testing on a single unmanaged machine or for kiosk/standalone deployments.

1. Copy the template files:

   ```
   NetFoundry.ZitiMonitorService.admx       →   C:\Windows\PolicyDefinitions\
   en-US\NetFoundry.ZitiMonitorService.adml  →   C:\Windows\PolicyDefinitions\en-US\
   ```

   > If you are also deploying `ziti-edge-tunnel` policies, copy
   > `NetFoundry.ZitiEdgeTunnel.admx` and its `.adml` to the same locations.
   > Both files must be present because `NetFoundry.ZitiMonitorService.admx` references
   > the shared parent categories defined in `NetFoundry.ZitiEdgeTunnel.admx`.

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

5. Restart the service for changes to take effect:

   ```powershell
   Restart-Service -Name "Ziti Update Service" -Force
   ```

---

## Windows: Active Directory Group Policy

Use this for domain-joined machines managed through GPMC.

### Deploy the ADMX templates (one-time)

Copy the templates to the **Central Store** on your domain controller (or any machine with
RSAT). The Central Store is authoritative for all DCs in the domain.

```powershell
$domain  = (Get-WmiObject Win32_ComputerSystem).Domain
$central = "\\$domain\SYSVOL\$domain\Policies\PolicyDefinitions"

# Create the Central Store if it does not exist yet
if (-not (Test-Path $central)) {
    Copy-Item "C:\Windows\PolicyDefinitions" $central -Recurse
}

# Copy both ADMX files — ZitiMonitorService references ZitiEdgeTunnel for parent categories
Copy-Item "NetFoundry.ZitiEdgeTunnel.admx"          "$central\"
Copy-Item "en-US\NetFoundry.ZitiEdgeTunnel.adml"    "$central\en-US\"
Copy-Item "NetFoundry.ZitiMonitorService.admx"       "$central\"
Copy-Item "en-US\NetFoundry.ZitiMonitorService.adml" "$central\en-US\"
```

### Configure the GPO

1. Open `gpmc.msc` on the domain controller or an RSAT workstation.
2. Create a new GPO (or edit an existing one) and link it to the target OU.
3. Navigate to:

   ```
   Computer Configuration
     └─ Policies
          └─ Administrative Templates
               └─ NetFoundry
                    └─ Ziti Desktop Edge for Windows
                         └─ ziti-monitor-service
   ```

4. Configure the desired policies.

### Apply immediately on endpoints

```powershell
gpupdate /force
```

Or wait for the standard refresh cycle (90 ± 30 minutes for computer policy). The service
must be restarted after the registry values are written for them to take effect.

```powershell
Restart-Service -Name "Ziti Update Service" -Force
```

---

## Microsoft Intune — Windows (ADMX ingestion)

Intune supports importing custom ADMX files and exposing them as configurable policies in
the portal. No domain controller or Central Store is required.

### Import the ADMX templates

> Both ADMX files must be imported because `NetFoundry.ZitiMonitorService.admx` depends on
> the parent categories defined in `NetFoundry.ZitiEdgeTunnel.admx`. Import
> `NetFoundry.ZitiEdgeTunnel.admx` first.

1. In the [Intune portal](https://intune.microsoft.com), go to:
   **Devices → Windows → Configuration profiles**

2. Click **Create → New policy**.
   - Platform: **Windows 10 and later**
   - Profile type: **Templates → Administrative Templates**

3. On the Administrative Templates editor, click **Import ADMX**.

4. Upload `NetFoundry.ZitiEdgeTunnel.admx` and its `.adml` file first, then repeat for
   `NetFoundry.ZitiMonitorService.admx` and its `.adml`.

5. After import, the policies appear under **All Settings** or in the tree at:

   ```
   NetFoundry > Ziti Desktop Edge for Windows > ziti-monitor-service
   ```

### Configure and assign

1. Find the desired policy settings in the Administrative Templates editor.
2. Set each to **Enabled** and supply the value.
3. Assign the profile to a device group.

Intune writes the policy values to the same registry path that `ZitiUpdateService` reads:

```
HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
```

The service does not know or care whether values were written by Intune, Group Policy, or a
script — the behaviour is identical.

### Alternative: Custom OMA-URI (no ADMX required)

If you prefer not to ingest the ADMX, you can write the registry values directly via a
custom OMA-URI configuration profile. Create one OMA-URI entry per setting:

| Setting | OMA-URI | Data type | Example value |
|---|---|---|---|
| Disable automatic updates | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/DisableAutomaticUpdates` | Integer | `1` |
| Update stream URL | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/UpdateStreamURL` | String | `https://...` |
| Update interval (seconds) | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/UpdateIntervalSeconds` | Integer | `3600` |
| Installation reminder (seconds) | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/InstallationReminderSeconds` | Integer | `86400` |
| Critical threshold (seconds) | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/InstallationCriticalSeconds` | Integer | `604800` |
| Aliveness check threshold | `./Device/Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/NetFoundry/Ziti Desktop Edge for Windows/ziti-monitor-service/AlivenessChecksBeforeAction` | Integer | `12` |

---

## Microsoft Endpoint Configuration Manager (MECM / SCCM)

### Option A — Via Active Directory Group Policy

Deploy the ADMX templates to the SYSVOL Central Store as described in the
[AD Group Policy](#windows-active-directory-group-policy) section above. MECM-managed
devices that are also domain-joined will pick up the policies on their normal GPO refresh
cycle. This is the simplest option when all managed machines are domain-joined.

### Option B — Compliance Settings (no domain required)

MECM Compliance Settings can write the registry values directly, without Group Policy or
ADMX templates. This works for workgroup machines and hybrid environments.

1. In the MECM console, go to:
   **Assets and Compliance → Compliance Settings → Configuration Items**

2. Create a new Configuration Item:
   - Platform: **Windows Desktops and Servers**
   - Type: **Windows Desktops and Servers (custom)**

3. Add a **Setting** for each policy value:

   | Field | Value |
   |---|---|
   | Setting type | Registry value |
   | Hive | HKEY_LOCAL_MACHINE |
   | Key | `SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service` |
   | Value name | e.g. `DisableAutomaticUpdates` (repeat for each field) |
   | Data type | Integer (DWORD values) or String (`UpdateStreamURL`) |

4. Set **Compliance Rules** to remediate: if the value is not equal to the required value,
   set it.

5. Bundle the Configuration Items into a **Configuration Baseline** and deploy to the
   target collection.

MECM will write and maintain the registry values on each check-in (default every 8 hours,
or on demand via **Machine Policy Retrieval & Evaluation Cycle**).

---

## All Methods: Verifying the Registry on an Endpoint

Regardless of which tool deployed the policy, verify the values are present:

```powershell
Get-ItemProperty `
    "HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" `
    -ErrorAction SilentlyContinue
```

```cmd
reg query "HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service"
```

If the key exists and contains the expected values, `ZitiUpdateService` will apply them on
next startup and block runtime changes to those fields.

---

## Policy Precedence

When multiple tools are involved, standard Windows policy precedence applies:

```
Domain GPO (highest)
  └─ Local Group Policy
       └─ Intune / MECM Compliance Settings (lowest)
```

In practice, choose one authoritative source per environment and avoid overlap to prevent
unexpected behaviour when policies conflict.
