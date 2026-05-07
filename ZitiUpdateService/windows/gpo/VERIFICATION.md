# GPO / Automatic Upgrades — Comprehensive Test Plan

This plan covers the full surface area of the automatic-upgrade feature and its GPO policy
override layer. Tests are organized by scenario category. Each test lists preconditions,
steps, and unambiguous pass criteria.

For registry helper functions (`Set-Policy`, `Remove-PolicyKey`, etc.) see [TESTING.md](TESTING.md).
For deployment background see [DEPLOYMENT.md](DEPLOYMENT.md).

---

## Terminology

| Term | Meaning |
|---|---|
| **Settings** | `%APPDATA%\NetFoundry\ZitiUpdateService\settings.json` |
| **GPO key** | `HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service` |
| **Stable URL** | `https://get.openziti.io/zdew/stable.json` (ProdUrl default) |
| **Beta URL** | `https://get.openziti.io/zdew/beta.json` |
| **Locked** | A value is present in the GPO key; the UI shows "Managed by your organization" |
| **Unlocked** | The GPO value is absent; the UI is editable |
| **UI** | Ziti Desktop Edge tray app → Automatic Upgrades screen |
| **Blurb** | The brief "Settings Saved." confirmation that fades in after a successful save |

---

## Phase 1 — Baseline (no GPO, clean slate)

These tests establish that the feature works correctly with no policy applied. Run these
first. If any fail, stop — GPO tests will be meaningless.

### B-1: Fresh install, no settings.json, no GPO

**Setup:** Uninstall ZDE. Delete `%APPDATA%\NetFoundry\ZitiUpdateService\settings.json` if
it exists. Confirm the GPO key is absent. Reinstall.

**Steps:**
1. Open the UI → Automatic Upgrades screen.
2. Note the URL shown in the text box.
3. Note the Enabled/Disabled toggle state.
4. Check settings.json.

**Pass criteria:**
- URL shows the stable URL (default).
- Toggle shows "Enabled".
- No "Managed by your organization" banner.
- All fields are editable (full opacity, no dimming).
- settings.json exists and contains `"AutomaticUpdatesDisabled": false`.

---

### B-2: Disable automatic updates via UI, verify persistence

**Setup:** B-1 complete (service running, no GPO, updates enabled).

**Steps:**
1. Open UI → Automatic Upgrades. Click "Disabled".
2. Confirm the blurb "Settings Saved." appears.
3. Confirm URL, maintenance window, and Save Settings button are all dimmed/hidden.
4. Restart the service: `Restart-Service "Ziti Update Service" -Force`.
5. Reopen the UI → Automatic Upgrades.

**Pass criteria:**
- After clicking Disabled: URL field, maintenance window combos, and Save Settings are
  visually unavailable (dimmed or hidden).
- After service restart: toggle still shows "Disabled".
- settings.json shows `"AutomaticUpdatesDisabled": true`.

---

### B-3: Re-enable automatic updates via UI

**Setup:** B-2 complete (updates disabled, no GPO).

**Steps:**
1. Open UI → Automatic Upgrades. Click "Enabled".
2. Confirm blurb appears.
3. Confirm URL, maintenance window, and Save Settings become available.

**Pass criteria:**
- After clicking Enabled: URL, window combos, and Save Settings button are fully visible
  and editable.
- settings.json shows `"AutomaticUpdatesDisabled": false`.

---

### B-4: Change URL stable → beta → stable

**Setup:** B-3 complete (updates enabled, URL = stable, no GPO).

**Steps:**
1. Open UI → Automatic Upgrades. Note URL (should be stable).
2. Paste the beta URL into the text box. Click Save Settings.
3. Confirm blurb. Check settings.json.
4. Restart service. Reopen UI.
5. Note URL shown. Click Reset URL button.
6. Click Save Settings. Confirm blurb. Check settings.json.
7. Restart service. Reopen UI.

**Pass criteria:**
- After step 2: settings.json shows beta URL. UI shows beta URL (does not revert to stable
  after service fires its config-change event).
- After step 4 (restart): UI still shows beta URL — confirms persistence through restart.
- After step 5: URL text box immediately shows stable URL (Reset button works locally).
- After step 6: settings.json shows stable URL. UI still shows stable URL (does not revert
  to beta after the service processes the save).
- After step 7 (restart): UI shows stable URL.

> **This is the most historically bug-prone flow. If the URL reverts in the UI after
> clicking Save, there is a regression in `SetAutomaticUpdateURL` or the event routing.**

---

### B-5: Invalid URL is rejected

**Setup:** Updates enabled, no GPO.

**Steps:**
1. Open UI → Automatic Upgrades.
2. Clear the URL field and type `not-a-url`. Click Save Settings.
3. Note what happens. Check settings.json.
4. Type `http://` (no host). Click Save Settings.
5. Type a valid-looking but unreachable URL (e.g., `https://does-not-exist.example.com/ziti.json`).
   Click Save Settings.

**Pass criteria:**
- Step 2: error shown or save silently rejected; settings.json URL unchanged.
- Step 4: same.
- Step 5: rejected with an error; settings.json URL unchanged. (The service validates the
  URL by fetching it before accepting it.)

---

### B-6: Maintenance window — Any time

**Setup:** Updates enabled, no GPO.

**Steps:**
1. Open UI → Automatic Upgrades.
2. Check that "Any time" checkbox is checked (default when both combos are 00:00).
3. Confirm both From/To combos show 00:00 and are disabled.
4. Click Save Settings. Check settings.json.

**Pass criteria:**
- `MaintenanceWindowStart: 0`, `MaintenanceWindowEnd: 0` in settings.json.
- After service restart, UI still shows Any time checked.

---

### B-7: Maintenance window — Set specific window, save, persist

**Setup:** B-6 complete (window = any time).

**Steps:**
1. Open UI → Automatic Upgrades. Uncheck "Any time".
2. Set From = 02:00, To = 04:00. Click Save Settings. Confirm blurb.
3. Check settings.json. Restart service. Reopen UI.

**Pass criteria:**
- settings.json: `MaintenanceWindowStart: 2`, `MaintenanceWindowEnd: 4`.
- After restart: From shows 02:00, To shows 04:00, "Any time" is unchecked.

---

### B-8: Maintenance window — Check "Any time" zeroes combos

**Setup:** B-7 complete (window 02:00–04:00).

**Steps:**
1. Open UI → Automatic Upgrades. Check "Any time".
2. Observe From/To combos. Click Save Settings. Confirm blurb.
3. Check settings.json.

**Pass criteria:**
- Immediately on checking "Any time": both combos jump to 00:00 and become disabled.
- After save: `MaintenanceWindowStart: 0`, `MaintenanceWindowEnd: 0`.

---

## Phase 2 — GPO at Startup

These tests verify that GPO values applied before service start are read correctly and
take precedence over settings.json.

### G-1: URL locked at startup — UI reflects lock

**Setup:** Set URL in settings.json to beta. Then set GPO:
```powershell
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.

**Pass criteria:**
- "Managed by your organization" banner visible.
- URL field shows `https://internal.example.com/ziti.json` (GPO value, not beta from settings.json).
- URL field is disabled (greyed out). Reset button hidden. Save Settings hidden.
- URL in settings.json is unchanged (still beta) — GPO does not write to settings.json.

---

### G-2: AutomaticUpdatesDisabled locked at startup

**Setup:**
```powershell
Remove-PolicyKey
Set-Policy "AutomaticUpdatesDisabled" 1 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.

**Pass criteria:**
- Banner visible. Toggle shows "Disabled" and is greyed out (not clickable).
- URL, maintenance window, and Save Settings are all greyed out / hidden (because updates
  are disabled).
- Attempting to click the toggle does nothing.

---

### G-3: Both URL and disable-flag locked

**Setup:**
```powershell
Remove-PolicyKey
Set-Policy "AutomaticUpdatesDisabled" 0 DWord
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.

**Pass criteria:**
- Banner visible.
- Toggle shows "Enabled" and is greyed out (locked to enabled).
- URL shows GPO URL, is greyed out.
- Maintenance window combos are greyed out (because URL is locked — Save Settings is hidden).

---

### G-4: Maintenance window locked

**Setup:**
```powershell
Remove-PolicyKey
Set-Policy "MaintenanceWindowStart" 2 DWord
Set-Policy "MaintenanceWindowEnd"   6 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades (updates enabled, URL unlocked).

**Pass criteria:**
- Banner visible (any locked field triggers it).
- From combo shows 02:00, To shows 06:00, both greyed out.
- "Any time" checkbox is greyed out (cannot check it).
- URL field is still editable (URL is not locked).
- Save Settings is hidden (because the maintenance window is locked).

---

### G-5: Partial lock — only one value locked

**Setup:**
```powershell
Remove-PolicyKey
Set-Policy "AlivenessChecksBeforeAction" 24 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.

**Pass criteria:**
- Banner visible (any lock triggers it).
- URL, toggle, and maintenance window are all editable.
- Save Settings is visible and functional.
- Log shows `AlivenessChecksBeforeAction = 24` in "GPO overrides loaded" line.
- settings.json `AlivenessChecksBeforeAction` value is overridden to 24 in practice even
  though it is not written to the file.

---

### G-6: GPO value takes precedence over settings.json conflict

**Setup:** Set `"AutomaticUpdatesDisabled": true` in settings.json directly. Then:
```powershell
Set-Policy "AutomaticUpdatesDisabled" 0 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.
2. Check the service log for startup entries.

**Pass criteria:**
- Toggle shows "Enabled" (GPO wins over settings.json `true`).
- Log shows `AutomaticUpdatesDisabled = 0` from GPO.
- settings.json still contains `"AutomaticUpdatesDisabled": true` (GPO does not overwrite it).

---

### G-7: settings.json absent at startup with GPO set

**Setup:** Delete settings.json. Set a GPO value:
```powershell
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Confirm service starts successfully.
2. Open UI → Automatic Upgrades.
3. Check that settings.json was recreated.

**Pass criteria:**
- Service starts without error.
- UI shows GPO URL, locked.
- A new settings.json was created with defaults. GPO URL is NOT written to settings.json.

---

## Phase 3 — Runtime GPO Changes (WMI Watcher)

These tests verify that policy changes are detected while the service is running, without
requiring a restart. The WMI watcher has a 500 ms debounce.

### R-1: Apply URL lock at runtime

**Setup:** No GPO. Service running. UI open on Automatic Upgrades screen.

**Steps:**
1. Confirm URL field is editable.
2. Run:
   ```powershell
   Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
   ```
3. Wait 2–3 seconds. Observe the UI.

**Pass criteria:**
- Without restarting the service or closing the UI:
  - Banner appears.
  - URL field updates to show the GPO URL and becomes greyed out.
  - Save Settings button disappears.
- Service log shows "GPO registry key changed — reloading policy overrides".

---

### R-2: Remove URL lock at runtime

**Setup:** URL locked via GPO (from R-1 or fresh setup). Service running. UI open.

**Steps:**
1. Confirm URL field is greyed out and shows GPO URL.
2. Run:
   ```powershell
   Remove-Policy "AutomaticUpdateURL"
   ```
3. Wait 2–3 seconds. Observe the UI.

**Pass criteria:**
- Without restart:
  - URL field becomes editable.
  - URL shown is whatever is in settings.json (not the removed GPO value).
  - If no other fields are locked, banner disappears.
  - Save Settings button reappears.

---

### R-3: Apply disable-updates lock at runtime

**Setup:** No GPO. Updates enabled. UI open.

**Steps:**
1. Run:
   ```powershell
   Set-Policy "AutomaticUpdatesDisabled" 1 DWord
   ```
2. Wait 2–3 seconds. Observe the UI.

**Pass criteria:**
- Toggle flips to "Disabled" and becomes greyed out.
- URL, maintenance window, Save Settings become greyed out / hidden.
- Banner appears.

---

### R-4: Rapid registry changes (debounce)

**Setup:** No GPO. Service running.

**Steps:**
1. Run in quick succession (within 1 second):
   ```powershell
   Set-Policy "AutomaticUpdateURL" "https://a.example.com/ziti.json" String
   Set-Policy "AutomaticUpdateURL" "https://b.example.com/ziti.json" String
   Set-Policy "AutomaticUpdateURL" "https://c.example.com/ziti.json" String
   ```
2. Wait 2 seconds. Check service log and UI.

**Pass criteria:**
- Service log shows "GPO registry key changed" fires multiple times but "reloading policy
  overrides" fires only once (or at most twice) — the debounce collapsed the rapid events.
- UI and effective URL settle on `https://c.example.com/ziti.json` (the final value).

---

### R-5: GPO removal requires restart to fully lift

**Setup:** `AutomaticUpdateURL` locked via GPO. Service running.

**Steps:**
1. Remove the lock:
   ```powershell
   Remove-Policy "AutomaticUpdateURL"
   ```
2. Wait 2 seconds. Observe UI — URL field should become editable (R-2 confirmed this).
3. Change URL in UI to beta URL. Click Save Settings.
4. Restart service. Reopen UI.

**Pass criteria:**
- After step 2: URL field becomes editable, shows settings.json value.
- After step 3: blurb appears, settings.json updated to beta URL.
- After step 4: UI still shows beta URL — the removal of the GPO lock and the subsequent
  manual change both survived the restart.

---

## Phase 4 — IPC Rejection When Locked

These tests verify that the service correctly rejects mutation commands for locked fields
and that the rejection code is correct.

### I-1: SetAutomaticUpgradeURL rejected when URL locked

**Setup:**
```powershell
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Attempt to change the URL via UI (should be impossible — field is greyed out).
2. Verify via service log that any programmatic attempt returns `Code: 3` (MANAGED_BY_GPO).

**Pass criteria:**
- UI does not allow URL changes when field is greyed.
- Log contains "UpdateStreamURL is managed by Group Policy — change rejected" if a direct
  IPC call is attempted.

---

### I-2: SetAutomaticUpgradeDisabled rejected when locked

**Setup:**
```powershell
Set-Policy "AutomaticUpdatesDisabled" 0 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Attempt to click the "Disabled" toggle in the UI.

**Pass criteria:**
- Toggle does not change state (locked to Enabled, click does nothing).
- Log contains "DisableAutomaticUpdates is managed by Group Policy — change rejected".

---

### I-3: SetMaintenanceWindow rejected when locked

**Setup:**
```powershell
Set-Policy "MaintenanceWindowStart" 3 DWord
Set-Policy "MaintenanceWindowEnd"   5 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Open UI → Automatic Upgrades.
2. Attempt to interact with From/To combos or "Any time" checkbox.

**Pass criteria:**
- Combos and checkbox are disabled — cannot be changed.
- Save Settings button is hidden.

---

## Phase 5 — URL Switching (the most fragile area)

These tests specifically target the URL save → event → UI refresh cycle that has historically
caused the URL to revert to a stale value in the text box.

### U-1: Reset URL → Save → UI does not revert

**Setup:** URL set to beta in settings.json. No GPO. Updates enabled. Open UI.

**Steps:**
1. Open Automatic Upgrades. Confirm URL shows beta.
2. Click Reset URL button. Confirm text box immediately shows stable URL.
3. Click Save Settings. Watch the URL text box closely for 3–5 seconds after the blurb.
4. Check settings.json.

**Pass criteria:**
- After step 3: URL text box stays on stable URL. It must NOT flicker back to beta at
  any point after clicking Save, even briefly.
- settings.json: stable URL.
- After service restart: UI still shows stable URL.

---

### U-2: Manual edit → Save → UI does not revert

**Setup:** URL = stable in settings.json. No GPO.

**Steps:**
1. Open Automatic Upgrades. Manually type the beta URL into the text box.
2. Click Save Settings. Watch text box for 3–5 seconds.
3. Check settings.json.

**Pass criteria:**
- Text box stays on beta URL after save (does not revert to stable).
- settings.json: beta URL.

---

### U-3: URL change followed immediately by maintenance window change

**Setup:** URL = stable. Maintenance window 00:00/00:00. No GPO.

**Steps:**
1. Open Automatic Upgrades. Enter beta URL in text box.
2. Uncheck "Any time". Set From = 01:00, To = 05:00.
3. Click Save Settings. Watch URL text box and combo boxes for 3–5 seconds.
4. Check settings.json.

**Pass criteria:**
- URL stays on beta throughout (does not revert).
- Combo boxes stay on 01:00/05:00 (do not revert to 00:00/00:00).
- settings.json: beta URL, `MaintenanceWindowStart: 1`, `MaintenanceWindowEnd: 5`.

> **This tests the bug where an incoming service event during the Save await chain reset
> the UI controls to stale viewmodel values before all IPC calls completed.**

---

### U-4: Any time checkbox → Save → UI does not revert

**Setup:** Maintenance window = 19:00/21:00 (non-zero). URL = stable. No GPO.

**Steps:**
1. Open Automatic Upgrades. Check "Any time".
2. Confirm both combos jump to 00:00.
3. Click Save Settings.
4. Watch combos for 3–5 seconds. Check settings.json.

**Pass criteria:**
- Combos stay at 00:00 after save (do not revert to 19/21).
- settings.json: `MaintenanceWindowStart: 0`, `MaintenanceWindowEnd: 0`.

---

### U-5: Toggle Disabled → Enabled → Save URL

**Setup:** Updates disabled in settings.json. No GPO.

**Steps:**
1. Open Automatic Upgrades. Confirm toggle shows Disabled, fields greyed.
2. Click Enabled. Confirm fields become editable, blurb shows.
3. Change URL to beta. Set From = 02:00, To = 04:00.
4. Click Save Settings. Watch all fields for 3–5 seconds.
5. Check settings.json.

**Pass criteria:**
- After step 2: fields immediately editable (no lag, no revert to Disabled state).
- After step 4: URL = beta, window = 02:00/04:00. No revert.
- settings.json: `AutomaticUpdatesDisabled: false`, beta URL, start=2, end=4.

---

## Phase 6 — First Install Scenarios

### F-1: Fresh install, GPO already applied

**Setup:** Apply GPO before installing ZDE:
```powershell
Set-Policy "AutomaticUpdatesDisabled" 1 DWord
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
```
Install ZDE for the first time (no existing settings.json).

**Steps:**
1. Open UI → Automatic Upgrades immediately after install.

**Pass criteria:**
- Banner visible. Toggle shows Disabled (locked). URL shows GPO value (locked).
- Service log shows GPO values loaded at startup.
- settings.json was created with defaults; GPO values are NOT written into it.

---

### F-2: Fresh install, no GPO, first-run defaults

**Setup:** Remove GPO key. Install ZDE fresh (no settings.json).

**Steps:**
1. Open UI → Automatic Upgrades.

**Pass criteria:**
- No banner. Toggle = Enabled. URL = stable. All editable.
- settings.json created with `AutomaticUpdatesDisabled: false`.

---

### F-3: Upgrade from version without GPO support

**Setup:** Install an older version of ZDE (pre-GPO). Set the registry key manually:
```powershell
Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
```
Upgrade to the current version (without uninstalling first).

**Steps:**
1. After upgrade, open UI → Automatic Upgrades.
2. Check service log for GPO load lines.

**Pass criteria:**
- Upgrade completes without error.
- Service reads GPO on first start of the new version.
- UI shows the GPO URL as locked.
- Existing settings.json from the old version is preserved; GPO URL is not written to it.

---

## Phase 7 — Long-Running Tunneler / Update In Progress

These tests simulate real-world conditions: the service has been running for an extended
period, and things change while it is running.

### L-1: GPO applied while update notification is showing

**Setup:** An update is available and the service has sent an update notification to the UI
(the update banner is visible). No GPO is set.

**Steps:**
1. While the update notification is visible, apply a URL GPO lock:
   ```powershell
   Set-Policy "AutomaticUpdateURL" "https://internal.example.com/ziti.json" String
   ```
2. Wait 2–3 seconds. Open Automatic Upgrades screen.

**Pass criteria:**
- "Managed by your organization" banner appears.
- URL switches to GPO value and becomes locked.
- The update notification (if still visible) is not corrupted or cleared by the GPO event.
- No crash or exception in service log.

---

### L-2: URL changed while a long update check is in progress

**Setup:** The service is mid-check (e.g., slow network, the HTTP request to the release
feed has not returned yet). Simulate by monitoring logs during an update check interval.

**Steps:**
1. While the service is performing an update check (visible in DEBUG logs), change the URL:
   ```powershell
   Set-Policy "AutomaticUpdateURL" "https://b.example.com/ziti.json" String
   ```
   Or via UI if not locked.
2. Allow the in-progress check to complete.
3. Wait for the next check cycle to start.

**Pass criteria:**
- The in-progress check completes without crashing, using whichever URL it started with.
- The next check cycle uses the new URL.
- settings.json shows the new URL (if not GPO-locked) or is unchanged (if GPO-locked).

---

### L-3: Service running for days — GPO applied cold (no recent restart)

**Setup:** Leave the service running with no GPO for 24+ hours (or simulate by leaving it
running overnight). Then apply a GPO:
```powershell
Set-Policy "AutomaticUpdatesDisabled" 1 DWord
```

**Steps:**
1. Apply GPO. Wait 2–3 seconds. Open UI.

**Pass criteria:**
- WMI watcher still fires (it was not lost after days of uptime).
- Banner appears. Toggle becomes locked/disabled.
- Service log shows "GPO registry key changed — reloading policy overrides".

---

### L-4: Service restarts mid-download

**Setup:** An update is available and the service has begun downloading the installer
package. Restart the service while the download is in progress.

**Steps:**
1. Monitor the update folder for a partial download.
2. Restart the service: `Restart-Service "Ziti Update Service" -Force`.
3. Wait for the next update check cycle.

**Pass criteria:**
- Service restarts cleanly without exception.
- On next check, the service either resumes the download or re-downloads cleanly.
- No leftover partial file corrupts the install.
- `scanForStaleDownloads` cleans up if the file is stale.

---

### L-5: Toggle Disabled during an active update countdown

**Setup:** An update is available with a countdown to install (the "will be automatically
installed by [time]" banner is visible).

**Steps:**
1. Click "Disabled" to toggle automatic updates off.
2. Observe the countdown banner.

**Pass criteria:**
- Countdown banner either disappears or its text updates to reflect that auto-install is
  now off.
- The service does not auto-install when the countdown expires.
- settings.json: `AutomaticUpdatesDisabled: true`.

---

## Phase 8 — Maintenance Window Behavior

### M-1: Update available outside the window — no install

**Setup:** Set a maintenance window that does not include the current time. For example, if
it is currently 14:00, set From = 02:00, To = 04:00. Ensure an update is available.

```powershell
# Set window (adjust hours to be outside current local time)
$PolicyKey = "HKLM:\SOFTWARE\Policies\NetFoundry\..."
Set-Policy "MaintenanceWindowStart" 2 DWord
Set-Policy "MaintenanceWindowEnd"   4 DWord
Restart-Service "Ziti Update Service" -Force
```

**Steps:**
1. Wait for an update check cycle.
2. Observe service log and installer behavior.

**Pass criteria:**
- Service detects the update but does not download or install it.
- Log shows the update is deferred to the maintenance window install time.
- UI shows the update notification and scheduled install time falls within 02:00–04:00.

---

### M-2: Midnight-crossing window

**Setup:** Set window that crosses midnight: From = 22:00, To = 02:00. Test at a time that
falls inside the window (e.g., 23:00 or 01:00).

**Steps:**
1. Set window and restart service.
2. If current time is in the window: confirm installs are allowed.
3. If current time is outside: confirm installs are deferred.

**Pass criteria:**
- The window crossing midnight is handled correctly — hours between 22:00 and 23:59 AND
  between 00:00 and 02:00 are considered inside the window.
- No off-by-one error at midnight.

---

### M-3: Window = 0/0 means any time (no restriction)

**Setup:** Ensure settings.json has `MaintenanceWindowStart: 0`, `MaintenanceWindowEnd: 0`.
An update is available.

**Pass criteria:**
- Update installs at any time of day — the 0/0 window is not interpreted as a
  "midnight only" window.

---

## Phase 9 — Settings.json Interaction and Persistence

### P-1: Non-locked field changes survive restart

**Setup:** URL locked via GPO. Aliveness checks NOT locked. Service running.

**Steps:**
1. Manually edit settings.json: change `AlivenessChecksBeforeAction` to `6`.
2. Restart service. Check log.

**Pass criteria:**
- Service picks up `AlivenessChecksBeforeAction = 6` from settings.json.
- URL is still the GPO value (not the settings.json value).
- Log shows `AlivenessChecksBeforeAction = (not set)` for GPO (confirming it is not locked).

---

### P-2: settings.json write during GPO-locked run does not stomp locked values

**Setup:** URL locked via GPO to `https://internal.example.com/ziti.json`. Service running.

**Steps:**
1. Change the maintenance window via UI (this writes settings.json).
2. Inspect settings.json.

**Pass criteria:**
- settings.json's `AutomaticUpdateURL` is NOT updated to the GPO URL.
- The field the UI actually changed (maintenance window) IS updated.

---

### P-3: settings.json deleted at runtime

**Setup:** No GPO. Service running.

**Steps:**
1. Delete settings.json while the service is running.
2. Wait 2–3 seconds. Check whether the service recreates it.
3. Check the UI.

**Pass criteria:**
- Service recreates settings.json with defaults (the file watcher's `OnDeleted` handler
  resets settings to defaults in memory).
- UI reflects the reset state (URL = stable, updates enabled).
- No crash.

---

## Phase 10 — Edge Cases and Adversarial Inputs

### E-1: Registry key exists but is empty (no values)

**Setup:**
```powershell
New-Item -Path "HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service" -Force
Restart-Service "Ziti Update Service" -Force
```

**Pass criteria:**
- Service starts normally.
- Log shows "GPO registry key absent" or loads with all `(not set)`.
- No fields locked. UI is fully editable.

---

### E-2: Out-of-range registry values

**Setup:**
```powershell
Set-Policy "MaintenanceWindowStart" 25 DWord   # out of range (max is 23)
Set-Policy "MaintenanceWindowEnd"   99 DWord
Restart-Service "Ziti Update Service" -Force
```

**Pass criteria:**
- Service does not crash.
- Values are clamped to valid range (PolicySettings clamps 0–23).
- Log shows the clamped values.
- UI shows 23:00 for both (or 00:00 if clamp to 0).

---

### E-3: UpdateTimer below minimum

**Setup:**
```powershell
Set-Policy "UpdateTimer" 30 DWord   # below 600-second minimum
Restart-Service "Ziti Update Service" -Force
```

**Pass criteria:**
- Service log warns that the provided value is too small.
- Effective interval is 600 seconds (10 minutes), not 30 seconds.

---

### E-4: AutomaticUpdateURL set to an HTTP (non-HTTPS) URL

**Setup:**
```powershell
Set-Policy "AutomaticUpdateURL" "http://internal.example.com/ziti.json" String
Restart-Service "Ziti Update Service" -Force
```

**Pass criteria:**
- Service starts. Check whether the URL is accepted or rejected.
- If accepted: update checks attempt to use it (which may fail depending on the server).
- If rejected: service falls back to settings.json URL and logs a warning.
- No crash.

---

### E-5: Non-admin user cannot write the GPO key

**Setup:** Log in as a standard (non-admin) user.

**Steps:**
1. Attempt to write to `HKLM\SOFTWARE\Policies\...` via PowerShell or regedit.

**Pass criteria:**
- Write is denied by Windows (Access Denied). This is OS-level enforcement — the service
  does not need to handle this case, but the test confirms that non-admins cannot
  self-apply GPO overrides to bypass update policy.

---

### E-6: Service upgrade preserves GPO-unlocked settings

**Setup:** Set URL to beta via UI (no GPO). Set maintenance window to 03:00–05:00. Confirm
settings.json reflects both. Then upgrade ZDE to a newer version.

**Pass criteria:**
- After upgrade, settings.json is preserved (not reset).
- UI shows beta URL and 03:00–05:00 window.
- Service log shows the settings were loaded from the existing file, not recreated from
  defaults.

---

## Regression Tests (bugs fixed during development)

Run these after any change to `UpdateService.cs`, `PolicySettings.cs`, `Settings.cs`,
`ManagedSettingsViewModel.cs`, or `MainMenu.xaml.cs`.

| ID | Description | Regression test |
|---|---|---|
| RG-1 | URL reverts to old value after Reset + Save | U-1 |
| RG-2 | `InstallationNotificationEvent` (Type=Notification) routed to wrong handler, `gpoPolicyViewModel` not updated | U-1, U-2, L-1 |
| RG-3 | Maintenance window reverts to old values after Save (combos read after await) | U-3, U-4 |
| RG-4 | Toggle snaps back to old state after optimistic UI update | B-2, B-3, U-5 |
| RG-5 | "Disabled" written to settings.json even when only URL/window were saved | U-3 (check `AutomaticUpdatesDisabled` stays false) |

---

## Test Execution Order (recommended)

```
Phase 1 (B-1 through B-8)   — Baseline, no GPO
Phase 5 (U-1 through U-5)   — URL switching (run early; most likely to regress)
Phase 2 (G-1 through G-7)   — GPO at startup
Phase 3 (R-1 through R-5)   — Runtime GPO changes
Phase 4 (I-1 through I-3)   — IPC rejection
Phase 6 (F-1 through F-3)   — First install
Phase 8 (M-1 through M-3)   — Maintenance window behavior
Phase 7 (L-1 through L-5)   — Long-running / update in progress
Phase 9 (P-1 through P-3)   — settings.json interaction
Phase 10 (E-1 through E-6)  — Edge cases
```

Run the regression table (RG-1 through RG-5) after every relevant code change.
