#requires -Version 7.0
<#
    ci-install.ps1

    Idempotent one-time install of the prereqs the UI tests need on a Windows
    runner: Appium 2.x + appium-windows-driver + WinAppDriver.

    Designed to be runnable locally as well as from GitHub Actions. Assumes:
      - Node.js + npm are already on PATH (the workflow uses actions/setup-node).
      - .NET 9 SDK is already installed (actions/setup-dotnet).
      - MSBuild + Visual Studio Build Tools are already installed on the runner image.

    Skips work that's already done so you can re-run safely.
#>
[CmdletBinding()]
param(
    [string] $WadVersion = "1.2.1"
)

$ErrorActionPreference = "Stop"

function Have-Command([string]$name) {
    return (Get-Command $name -ErrorAction SilentlyContinue) -ne $null
}

# 1. Appium 2.x (global npm)
if (-not (Have-Command appium)) {
    Write-Host "==> installing appium"
    & npm install -g appium
    if ($LASTEXITCODE -ne 0) { throw "npm install appium failed" }
} else {
    Write-Host "==> appium already on PATH ($(appium --version 2>$null))"
}

# 2. appium-windows-driver (idempotent; appium prints a warning if already installed)
Write-Host "==> ensuring appium-windows-driver"
& appium driver install --source=npm appium-windows-driver 2>&1 |
    Where-Object { $_ -notmatch 'already installed' } |
    Write-Host
# Non-zero exit when already installed is expected; ignore.

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

Write-Host ""
Write-Host "==> prereqs ready"
