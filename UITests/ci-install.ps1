#requires -Version 7.0
<#
    Installs Appium, appium-windows-driver and WinAppDriver for the UI tests, skipping any already installed.
    Needs node and npm on PATH.
#>
[CmdletBinding()]
param(
    [string] $WadVersion = "1.2.1",
    # Pinned because the workflow caches the install keyed on this file's hash.
    [string] $AppiumVersion = "3.8.0",
    [string] $WindowsDriverVersion = "6.3.0",
    # npm prefix to install Appium under, put on this process's PATH, so CI can cache it. Empty installs globally.
    [string] $NpmPrefix,
    # Windows Server only (the GitHub runner), whose default display is too short for the window.
    [switch] $SetDisplayResolution
)

$ErrorActionPreference = "Stop"

function Have-Command([string]$name) {
    return (Get-Command $name -ErrorAction SilentlyContinue) -ne $null
}

if ($NpmPrefix) {
    $env:npm_config_prefix = $NpmPrefix
    $env:PATH = "$NpmPrefix;$env:PATH"
}

if (-not (Have-Command appium)) {
    Write-Host "==> installing appium $AppiumVersion"
    & npm install -g "appium@$AppiumVersion"
    if ($LASTEXITCODE -ne 0) { throw "npm install appium failed" }
} else {
    Write-Host "==> appium already on PATH ($(appium --version 2>$null))"
}

# Installing over an installed driver leaves it unloadable ("Cannot find package 'appium'"), so only when missing.
$installedDrivers = & appium driver list --installed --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "appium driver list failed" }
if ($installedDrivers.windows) {
    Write-Host "==> appium-windows-driver $($installedDrivers.windows.version) already installed"
} else {
    Write-Host "==> installing appium-windows-driver $WindowsDriverVersion"
    & appium driver install --source=npm "appium-windows-driver@$WindowsDriverVersion"
    if ($LASTEXITCODE -ne 0) { throw "appium driver install appium-windows-driver failed" }
}

# run-ui-tests.ps1 starts it from this path.
$winAppDriverExe = "${env:ProgramFiles(x86)}\Windows Application Driver\WinAppDriver.exe"
if (Test-Path -LiteralPath $winAppDriverExe) {
    Write-Host "==> WinAppDriver already installed"
} else {
    Write-Host "==> installing WinAppDriver v$WadVersion"
    $msi = Join-Path $env:TEMP "WindowsApplicationDriver_$WadVersion.msi"
    $url = "https://github.com/microsoft/WinAppDriver/releases/download/v$WadVersion/WindowsApplicationDriver_$WadVersion.msi"
    Invoke-WebRequest $url -OutFile $msi -UseBasicParsing
    Start-Process msiexec.exe -ArgumentList "/i", $msi, "/quiet", "/norestart" -Wait
    if (-not (Test-Path -LiteralPath $winAppDriverExe)) {
        throw "WinAppDriver install did not produce $winAppDriverExe"
    }
    Write-Host "==> WinAppDriver installed"
}

if ($SetDisplayResolution) {
    Write-Host "==> setting display resolution to 1920x1080"
    Set-DisplayResolution -Width 1920 -Height 1080 -Force
}

Write-Host ""
Write-Host "==> prereqs ready"
