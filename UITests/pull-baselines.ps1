#requires -Version 7.0
<#
    Replaces every *.verified.png in UITests.Appium/Tests with the zip that baselines-zip names on the repo's
    uitest-baselines release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Repo    # owner/name, e.g. openziti/desktop-edge-win
)

$ErrorActionPreference = 'Stop'

[string] $baselinesDir = Join-Path $PSScriptRoot "UITests.Appium/Tests"
[string] $zipName = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "baselines-zip") -Raw).Trim()
[string] $url = "https://github.com/$Repo/releases/download/uitest-baselines/$zipName"
[string] $zip = Join-Path ([IO.Path]::GetTempPath()) $zipName

Invoke-WebRequest -Uri $url -OutFile $zip
Get-ChildItem -LiteralPath $baselinesDir -Filter *.verified.png | Remove-Item
Expand-Archive -LiteralPath $zip -DestinationPath $baselinesDir
Remove-Item -LiteralPath $zip
[int] $count = (Get-ChildItem -LiteralPath $baselinesDir -Filter *.verified.png).Count
Write-Host "==> restored $count baselines from $url"
