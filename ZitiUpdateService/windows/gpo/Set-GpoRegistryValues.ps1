<#
.SYNOPSIS
    Creates the GPO registry key and values read by ZitiUpdateService's GpoSettings.

.DESCRIPTION
    Writes policy overrides to:
      HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service

    Any value left at its default ($null) is skipped — only explicitly supplied values
    are written.  Run with -WhatIf to preview changes without applying them.

    NOTE: Setting ANY of these values causes the Ziti Desktop Edge UI to display a
    "Managed by your organization" banner on the Automatic Upgrades screen and locks
    all controls so end users cannot change update settings at runtime.

.PARAMETER AutomaticUpdatesDisabled
    Overrides settings.json: AutomaticUpdatesDisabled (default: false)
    Set to 1 to prevent ZitiUpdateService from auto-installing updates.

.PARAMETER AutomaticUpdateURL
    Overrides settings.json: AutomaticUpdateURL
    Override the releases URL used to check for updates.

.PARAMETER UpdateTimer
    Overrides ZitiUpdateService.exe.config: UpdateTimer (default: 0:0:10:0)
    How often (in seconds) to poll for updates. Minimum enforced by the service: 600.

.PARAMETER InstallationReminder
    Overrides ZitiUpdateService.exe.config: InstallationReminder (default: 1:0:0:0)
    How often (in seconds) to re-notify the user about a pending update.

.PARAMETER InstallationCritical
    Overrides ZitiUpdateService.exe.config: InstallationCritical (default: 7:0:0:0)
    Age of a release (in seconds) after which the update is force-installed.

.PARAMETER AlivenessChecksBeforeAction
    Overrides settings.json: AlivenessChecksBeforeAction (default: 12)
    Consecutive failed health checks before the tunneler process is restarted.

.PARAMETER DeferInstallToRestart
    Overrides settings.json: DeferInstallToRestart (default: false)
    Set to 1 to stage updates and apply them on the next service/system restart instead
    of installing immediately. Overridden by AutomaticUpdatesDisabled (if that is 1,
    no install occurs at all). Overridden by InstallationCritical (if the release age
    threshold is crossed, install is forced immediately regardless).

.PARAMETER MaintenanceWindowStart
    Overrides settings.json: MaintenanceWindowStart
    Hour of day (0-23) when the installation maintenance window begins.
    Set equal to MaintenanceWindowEnd to disable the window (installs allowed any time).
    If greater than MaintenanceWindowEnd, the window crosses midnight.

.PARAMETER MaintenanceWindowEnd
    Overrides settings.json: MaintenanceWindowEnd
    Hour of day (0-23) when the installation maintenance window ends.
    Set equal to MaintenanceWindowStart to disable the window (installs allowed any time).
    If less than MaintenanceWindowStart, the window crosses midnight.
    Example: Start=22, End=6 means installations are allowed from 10 PM to 6 AM.

.EXAMPLE
    # Disable automatic updates and check every 30 minutes
    .\Set-GpoRegistryValues.ps1 -AutomaticUpdatesDisabled 1 -UpdateTimer 1800

.EXAMPLE
    # Preview all values without making any changes
    .\Set-GpoRegistryValues.ps1 `
        -AutomaticUpdatesDisabled 1 `
        -AutomaticUpdateURL 'https://get.openziti.io/zdew/beta.json' `
        -UpdateTimer 1800 `
        -InstallationReminder 86400 `
        -InstallationCritical 604800 `
        -AlivenessChecksBeforeAction 12 `
        -WhatIf
#>
param(
    [ValidateSet(0, 1)]
    [Nullable[int]] $AutomaticUpdatesDisabled     = $null,

    [string]        $AutomaticUpdateURL             = $null,

    [ValidateRange(600, [int]::MaxValue)]
    [Nullable[int]] $UpdateTimer       = $null,

    [ValidateRange(0, [int]::MaxValue)]
    [Nullable[int]] $InstallationReminder = $null,

    [ValidateRange(0, [int]::MaxValue)]
    [Nullable[int]] $InstallationCritical = $null,

    [ValidateRange(1, [int]::MaxValue)]
    [Nullable[int]] $AlivenessChecksBeforeAction = $null,

    [ValidateSet(0, 1)]
    [Nullable[int]] $DeferInstallToRestart = $null,

    [ValidateRange(0, 23)]
    [Nullable[int]] $MaintenanceWindowStart = $null,

    [ValidateRange(0, 23)]
    [Nullable[int]] $MaintenanceWindowEnd = $null,

    [switch] $WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# If no parameters were supplied at all, default to -WhatIf so the script is safe to run bare
$noParams = (-not $PSBoundParameters.Count)
if ($noParams) { $WhatIf = $true }

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $WhatIf) {
    throw 'This script must be run as Administrator. Re-run from an elevated PowerShell prompt, or use -WhatIf to preview changes.'
}

$KeyPath = 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'

# Each entry carries the GPO key name/value/type plus the source config key it overrides
$entries = [System.Collections.Generic.List[object]]::new()

function Add-DwordValue([string]$Name, [Nullable[int]]$Value, [string]$Source, [string]$ConfigKey, [bool]$UIVisible) {
    if ($null -ne $Value) {
        $entries.Add([pscustomobject]@{ Name = $Name; Value = "$Value"; Type = 'DWORD'; Source = $Source; ConfigKey = $ConfigKey; UIVisible = $UIVisible })
    }
}

function Add-StringValue([string]$Name, [string]$Value, [string]$Source, [string]$ConfigKey, [bool]$UIVisible) {
    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $entries.Add([pscustomobject]@{ Name = $Name; Value = $Value; Type = 'String'; Source = $Source; ConfigKey = $ConfigKey; UIVisible = $UIVisible })
    }
}

#                  GPO key name                  value                        source                         config key it overrides       UI visible?
Add-DwordValue  'AutomaticUpdatesDisabled'     $AutomaticUpdatesDisabled     'settings.json'                  'AutomaticUpdatesDisabled'     $true
Add-StringValue 'AutomaticUpdateURL'           $AutomaticUpdateURL           'settings.json'                  'AutomaticUpdateURL'           $true
Add-DwordValue  'AlivenessChecksBeforeAction'  $AlivenessChecksBeforeAction  'settings.json'                  'AlivenessChecksBeforeAction'  $false
Add-DwordValue  'DeferInstallToRestart'        $DeferInstallToRestart        'settings.json'                  'DeferInstallToRestart'        $false
Add-DwordValue  'MaintenanceWindowStart'       $MaintenanceWindowStart       'settings.json'                  'MaintenanceWindowStart'       $false
Add-DwordValue  'MaintenanceWindowEnd'         $MaintenanceWindowEnd         'settings.json'                  'MaintenanceWindowEnd'         $false
Add-DwordValue  'UpdateTimer'                  $UpdateTimer                  'ZitiUpdateService.exe.config'   'UpdateTimer'                  $false
Add-DwordValue  'InstallationReminder'         $InstallationReminder         'ZitiUpdateService.exe.config'   'InstallationReminder'         $false
Add-DwordValue  'InstallationCritical'         $InstallationCritical         'ZitiUpdateService.exe.config'   'InstallationCritical'         $false

$prefix     = if ($WhatIf) { '[WhatIf] ' } else { '' }
$configCol  = if ($entries.Count) { ($entries | ForEach-Object { $_.ConfigKey.Length } | Measure-Object -Maximum).Maximum } else { 0 }
$nameCol    = if ($entries.Count) { ($entries | ForEach-Object { $_.Name.Length }      | Measure-Object -Maximum).Maximum } else { 0 }

Write-Host "${prefix}Writing GPO overrides to:"
Write-Host "  $KeyPath"
Write-Host ''

if (-not $WhatIf -and -not (Test-Path $KeyPath)) { New-Item -Path $KeyPath -Force | Out-Null }

foreach ($source in @('settings.json', 'ZitiUpdateService.exe.config')) {
    $group = $entries | Where-Object { $_.Source -eq $source }
    if (-not $group) { continue }

    Write-Host "  # $source"
    foreach ($e in $group) {
        $configPadded = $e.ConfigKey.PadRight($configCol)
        $namePadded   = $e.Name.PadRight($nameCol)
        $visibility   = if ($e.UIVisible) { '[ui] ' } else { '[svc]' }
        Write-Host "      $prefix$visibility  $configPadded  ->  $namePadded  =  $($e.Value)  ($($e.Type))"
        if (-not $WhatIf) {
            if ($e.Type -eq 'DWORD') {
                Set-ItemProperty -Path $KeyPath -Name $e.Name -Value ([int]$e.Value) -Type DWord
            } else {
                Set-ItemProperty -Path $KeyPath -Name $e.Name -Value $e.Value -Type String
            }
        }
    }
    Write-Host ''
}

if ($noParams) {
    Write-Host 'No parameters supplied. Example usage:'
    Write-Host ''
    Write-Host '  .\windows\gpo\Set-GpoRegistryValues.ps1 `'
    Write-Host "      -AutomaticUpdatesDisabled 1 ``"
    Write-Host "      -AutomaticUpdateURL 'https://get.openziti.io/zdew/beta.json' ``"
    Write-Host "      -AlivenessChecksBeforeAction 12 ``"
    Write-Host "      -DeferInstallToRestart 1 ``"
    Write-Host "      -MaintenanceWindowStart 22 ``"
    Write-Host "      -MaintenanceWindowEnd 6 ``"
    Write-Host "      -UpdateTimer 1800 ``"
    Write-Host "      -InstallationReminder 86400 ``"
    Write-Host "      -InstallationCritical 604800 ``"
    Write-Host '      -WhatIf'
    Write-Host ''
    Write-Host 'To list current values:'
    Write-Host "  Get-ItemProperty 'HKLM:\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service'"
    Write-Host ''
    Write-Host ''
} elseif ($WhatIf) {
    Write-Host '[WhatIf] Registry was not modified — remove -WhatIf to apply.'
    Write-Host ''
} else {
    Write-Host 'Done. Restart ZitiUpdateService for changes to take effect if the WMI watcher is not running.'
    Write-Host ''
}
