#requires -Version 7.0
<#
    ci-install.ps1

    Idempotent one-time install of the prereqs the UI tests need on a Windows
    runner: Appium + appium-windows-driver + WinAppDriver.

    Designed to be runnable locally as well as from GitHub Actions. Assumes:
      - Node.js + npm are already on PATH (the windows-latest image ships them).
      - .NET 9 SDK is already installed (the image ships it).
      - MSBuild + Visual Studio Build Tools are already installed on the runner image.

    Skips work that's already done so you can re-run safely.
#>
[CmdletBinding()]
param(
    [string] $WadVersion = "1.2.1",
    # Pinned because the workflow caches the install keyed on this file's hash.
    [string] $AppiumVersion = "3.8.0",
    [string] $WindowsDriverVersion = "6.3.0",
    # npm prefix to install Appium under, put on this process's PATH, so CI can cache it. Empty installs globally.
    [string] $NpmPrefix,
    # Windows Server only (the GitHub runner). Its default display is too short for the window,
    # which lands partly off-screen.
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

# 1. Appium (npm)
if (-not (Have-Command appium)) {
    Write-Host "==> installing appium $AppiumVersion"
    & npm install -g "appium@$AppiumVersion"
    if ($LASTEXITCODE -ne 0) { throw "npm install appium failed" }
} else {
    Write-Host "==> appium already on PATH ($(appium --version 2>$null))"
}

# 2. appium-windows-driver. Installing over an installed driver fails with "already installed" and leaves it unloadable
# ("Cannot find package 'appium'" on every session), so install only when it is missing.
$installedDrivers = & appium driver list --installed --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "appium driver list failed" }
if ($installedDrivers.windows) {
    Write-Host "==> appium-windows-driver $($installedDrivers.windows.version) already installed"
} else {
    Write-Host "==> installing appium-windows-driver $WindowsDriverVersion"
    & appium driver install --source=npm "appium-windows-driver@$WindowsDriverVersion"
    if ($LASTEXITCODE -ne 0) { throw "appium driver install appium-windows-driver failed" }
}

# 3. WinAppDriver
$wad1 = "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"
$wad2 = "C:\Program Files\Windows Application Driver\WinAppDriver.exe"
if ((Test-Path $wad1) -or (Test-Path $wad2)) {
    Write-Host "==> WinAppDriver already installed"
} else {
    Write-Host "==> installing WinAppDriver v$WadVersion"
    $msi = Join-Path $env:TEMP "WindowsApplicationDriver_$WadVersion.msi"
    $url = "https://github.com/microsoft/WinAppDriver/releases/download/v$WadVersion/WindowsApplicationDriver_$WadVersion.msi"
    Invoke-WebRequest $url -OutFile $msi -UseBasicParsing
    Start-Process msiexec.exe -ArgumentList "/i", $msi, "/quiet", "/norestart" -Wait
    if (-not ((Test-Path $wad1) -or (Test-Path $wad2))) {
        throw "WinAppDriver install did not produce WinAppDriver.exe at either Program Files location"
    }
    Write-Host "==> WinAppDriver installed"
}

# 4. Display resolution
if ($SetDisplayResolution) {
    Write-Host "==> setting display resolution to 1920x1080"
    Set-DisplayResolution -Width 1920 -Height 1080 -Force
}

Write-Host ""
Write-Host "==> prereqs ready"
