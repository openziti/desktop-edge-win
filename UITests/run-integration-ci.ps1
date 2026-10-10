#requires -Version 7.0
<#
    run-integration-ci.ps1

    Gets the integration tier's binaries the way ziti-tunnel-sdk-c's integration-tests.yml does, then runs
    run-ui-tests.ps1 on the given integration tier. ZET is the ziti-edge-tunnel release Installer\build.ps1 ships. dex is
    built with fetch-dex.ps1, which needs go and git, unless ToolsDir already holds it (CI restores it from a cache).

    Locally: ./run-integration-ci.ps1 -ToolsDir C:\tools\zdew -ZitiBin (Get-Command ziti).Source -GhToken (gh auth token)
        -Tier IntegrationExtAuth
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ToolsDir,
    [Parameter(Mandatory)] [string] $ZitiBin,
    [Parameter(Mandatory)] [string] $GhToken,
    [Parameter(Mandatory)] [ValidateSet('IntegrationExtAuth', 'IntegrationMfa', 'Integration')] [string] $Tier
)

$ErrorActionPreference = "Stop"
$buildScript = Join-Path $PSScriptRoot "..\Installer\build.ps1"

$versionMatch = Select-String -LiteralPath $buildScript -Pattern '^\$ZITI_EDGE_TUNNEL_VERSION="(.*)"$' |
    Select-Object -First 1
if (-not $versionMatch) { throw "no `$ZITI_EDGE_TUNNEL_VERSION line in $buildScript" }
$zetVersion = $versionMatch.Matches[0].Groups[1].Value
$zetDir = Join-Path $ToolsDir "zet-release"
$zetZip = "ziti-edge-tunnel-Windows_x86_64.zip"
Write-Host "==> downloading ziti-edge-tunnel $zetVersion"
$env:GH_TOKEN = $GhToken
& gh release download --repo openziti/ziti-tunnel-sdk-c $zetVersion --pattern $zetZip --dir $zetDir --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release download of ziti-edge-tunnel $zetVersion failed (exit $LASTEXITCODE)" }
Expand-Archive -LiteralPath (Join-Path $zetDir $zetZip) -DestinationPath $zetDir -Force

# fetch-dex.ps1 is a byte copy of ZET's and throws without go even when dex is already built, so skip the call.
$dexDir = Join-Path $ToolsDir "dex"
if (-not (Test-Path -LiteralPath (Join-Path $dexDir "dex.exe"))) {
    & (Join-Path $PSScriptRoot "fetch-dex.ps1") -Dest $dexDir
}

$env:ZET_BIN = Join-Path $zetDir "ziti-edge-tunnel.exe"
$env:ZITI_BIN = $ZitiBin
$env:IDP_BIN = Join-Path $dexDir "dex.exe"
& (Join-Path $PSScriptRoot "run-ui-tests.ps1") -Tier $Tier
exit $LASTEXITCODE
