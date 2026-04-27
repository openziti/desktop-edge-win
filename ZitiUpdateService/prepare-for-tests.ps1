# prepare-for-tests.ps1
#
# Prepares a machine for the manual-test runbook in
# ZitiUpdateService\manually-testing-automatic-updates.md / test-run.md.
#
#  1. Verifies PowerShell 7+
#  2. Verifies elevation
#  3. Uninstalls any existing ZDEW
#  4. Optionally wipes leftover identities/settings at
#     C:\Windows\system32\config\systemprofile\AppData\Roaming\NetFoundry
#  5. Removes any policy registry keys at
#     HKLM\SOFTWARE\Policies\NetFoundry
#  6. Installs the current stable ZDEW from https://get.openziti.io/zdew/stable.json
#
# Usage:
#   prepare-for-tests.ps1
#   prepare-for-tests.ps1 -PurgeData            # delete data dir (with DELETE confirmation)
#   prepare-for-tests.ps1 -PurgeData -Force     # delete data dir, no confirmation (scripts)
#   prepare-for-tests.ps1 -KeepData             # keep data dir, no prompt
#   prepare-for-tests.ps1 -SkipInstall          # uninstall only
#
# Must be run **elevated** from `pwsh` (PowerShell 7+).

[CmdletBinding()]
param(
    [switch]$PurgeData,
    [switch]$KeepData,
    [switch]$Force,
    [switch]$SkipInstall,
    [string]$StreamUrl = 'https://get.openziti.io/zdew/stable.json'
)

$ErrorActionPreference = 'Stop'

function Write-Ok   ($m) { Write-Host $m -ForegroundColor Green }
function Write-Warn ($m) { Write-Host $m -ForegroundColor Yellow }
function Write-Err  ($m) { Write-Host $m -ForegroundColor Red }

# ---- 1. PowerShell 7+ ----------------------------------------------------------

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Err "NOT ON 7+: running $($PSVersionTable.PSVersion)."
    Write-Err "Launch pwsh.exe elevated and re-run this script."
    exit 1
}
Write-Ok "PowerShell $($PSVersionTable.PSVersion) OK"

# ---- 2. Elevation --------------------------------------------------------------

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Err "Not elevated. Right-click pwsh.exe → Run as Administrator, then re-run."
    exit 1
}
Write-Ok "Running elevated OK"

# ---- 3. Uninstall existing ZDEW -----------------------------------------------

Write-Host ""
Write-Host "--- Checking for existing ZDEW install ---"

$uninstall = Get-ChildItem `
    HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall,
    HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall |
    ForEach-Object { Get-ItemProperty $_.PSPath } |
    Where-Object { $_.DisplayName -like 'Ziti Desktop Edge*' } |
    Select-Object -First 1

if (-not $uninstall) {
    Write-Warn "No existing ZDEW install found — skipping uninstall."
} else {
    Write-Host "Found: $($uninstall.DisplayName) $($uninstall.DisplayVersion)"
    Write-Host "UninstallString: $($uninstall.UninstallString)"
    $s = $uninstall.UninstallString
    if ($s -match 'MsiExec\.exe.*\{[0-9A-Fa-f-]+\}') {
        $guid = [regex]::Match($s,'\{[0-9A-Fa-f-]+\}').Value
        Write-Host "Running: MsiExec.exe /X$guid /passive /norestart"
        Start-Process MsiExec.exe -ArgumentList "/X$guid","/passive","/norestart" -Wait
    } else {
        Write-Warn "Non-MSI uninstaller; running as-is with /passive"
        Start-Process -FilePath $s -ArgumentList '/passive' -Wait
    }
    Write-Ok "Uninstall complete."
}

# ---- 4. Leftover data dir -----------------------------------------------------

Write-Host ""
Write-Host "--- Checking for leftover identities/settings ---"

$dataDir = 'C:\Windows\system32\config\systemprofile\AppData\Roaming\NetFoundry'
if (-not (Test-Path $dataDir)) {
    Write-Ok "No leftover data dir at $dataDir — nothing to clean up."
} elseif ($PurgeData) {
    if ($Force) {
        Remove-Item -Recurse -Force $dataDir
        Write-Ok "Purged $dataDir (because -PurgeData -Force)."
    } else {
        Write-Host ""
        Write-Warn "WARNING: -PurgeData will DELETE ALL identities and ALL settings under:"
        Write-Warn "  $dataDir"
        Write-Warn "This cannot be undone."
        Write-Host ""
        $ans = Read-Host "Proceed? (y/N)"
        if ($ans -match '^[Yy]') {
            Remove-Item -Recurse -Force $dataDir
            Write-Ok "Purged $dataDir."
        } else {
            Write-Warn "Aborted purge. Kept $dataDir."
            Write-Warn "(Re-run with -PurgeData -Force to skip this prompt in automated runs.)"
        }
    }
} elseif ($KeepData) {
    Write-Warn "Kept $dataDir (because -KeepData)."
} else {
    Write-Warn "Found leftover data dir: $dataDir"
    $ans = Read-Host "Delete identities and settings too? (y/N)"
    if ($ans -match '^[Yy]') {
        Remove-Item -Recurse -Force $dataDir
        Write-Ok "Deleted $dataDir."
    } else {
        Write-Warn "Kept $dataDir."
    }
}

# ---- 5. Policy registry keys --------------------------------------------------

Write-Host ""
Write-Host "--- Checking for leftover policy registry keys ---"

$policyKey = 'HKLM:\SOFTWARE\Policies\NetFoundry'
if (-not (Test-Path $policyKey)) {
    Write-Ok "No leftover policy keys at $policyKey — nothing to clean up."
} else {
    Remove-Item -Recurse -Force $policyKey
    Write-Ok "Removed $policyKey."
}

# ---- 6. Install baseline stable ----------------------------------------------

if ($SkipInstall) {
    Write-Warn "Skipping install (because -SkipInstall). Done."
    exit 0
}

Write-Host ""
Write-Host "--- Installing baseline stable from $StreamUrl ---"

$stream = Invoke-RestMethod $StreamUrl
$exeUrl = $stream.assets |
    Where-Object { $_.name -like '*.exe' -and $_.name -notlike '*.sha256*' } |
    Select-Object -First 1 -ExpandProperty browser_download_url

if (-not $exeUrl) {
    Write-Err "Could not find an .exe asset in $StreamUrl"
    exit 1
}

$exePath = Join-Path $env:TEMP ($exeUrl -split '/' | Select-Object -Last 1)
Write-Host "Downloading $exeUrl"
Invoke-WebRequest $exeUrl -OutFile $exePath
Write-Ok "Downloaded: $exePath"

Write-Host "Running installer (passive)..."
Start-Process $exePath -ArgumentList '/passive' -Wait
Write-Ok "Install finished."

Write-Host ""
Write-Host "--- Verifying installed version ---"
$installed = Get-ChildItem `
    HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall,
    HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall |
    ForEach-Object { Get-ItemProperty $_.PSPath } |
    Where-Object { $_.DisplayName -like 'Ziti Desktop Edge*' } |
    Select-Object -First 1

if ($installed) {
    Write-Ok "Installed: $($installed.DisplayName) $($installed.DisplayVersion)"
    Write-Host "That is your 'N' for the rest of the test run."
} else {
    Write-Warn "Could not verify install in the registry — check Control Panel manually."
}
