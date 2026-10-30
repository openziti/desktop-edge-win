#requires -Version 7.0
<#
    Run by the UI Tests workflow's publish-baselines job after pull-baselines.ps1, on a regenerate_baselines dispatch.
    Each tier's TestResults/baselines holds every pulled baseline plus the ones its -AutoVerify run rewrote or added,
    so this copies only the pngs that differ from the pulled ones into UITests.Appium/Tests, zips them all as
    baselines-<version file>-<Commit's first 7>.zip, uploads it to the repo's uitest-baselines release, and returns the
    zip's name for UITests/baselines-zip. Only a rerun on the same commit replaces a zip.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]   $Repo,               # owner/name, e.g. openziti/desktop-edge-win
    [Parameter(Mandatory)] [string]   $GhToken,            # needs contents: write on Repo
    [Parameter(Mandatory)] [string[]] $TierBaselinesDirs,
    [Parameter(Mandatory)] [string]   $Commit              # the sha the tiers tested
)

$ErrorActionPreference = 'Stop'

[string] $baselinesDir = Join-Path $PSScriptRoot "UITests.Appium/Tests"
[hashtable] $pulledHashByName = @{}
foreach ($pulledBaseline in [IO.FileInfo[]] (Get-ChildItem -LiteralPath $baselinesDir -Filter *.verified.png)) {
    $pulledHashByName[$pulledBaseline.Name] = (Get-FileHash -LiteralPath $pulledBaseline.FullName).Hash
}
[hashtable] $changedHashByName = @{}
foreach ($tierDir in $TierBaselinesDirs) {
    foreach ($tierBaseline in [IO.FileInfo[]] (Get-ChildItem -LiteralPath $tierDir -Filter *.verified.png)) {
        [string] $tierHash = (Get-FileHash -LiteralPath $tierBaseline.FullName).Hash
        if ($pulledHashByName[$tierBaseline.Name] -eq $tierHash) { continue }
        if ($changedHashByName.ContainsKey($tierBaseline.Name)) {
            if ($changedHashByName[$tierBaseline.Name] -ne $tierHash) { throw "two tiers wrote different $($tierBaseline.Name)" }
            continue
        }
        Copy-Item -LiteralPath $tierBaseline.FullName -Destination (Join-Path $baselinesDir $tierBaseline.Name)
        $changedHashByName[$tierBaseline.Name] = $tierHash
        Write-Host "changed or new: $($tierBaseline.Name)"
    }
}
if ($changedHashByName.Count -eq 0) { throw "no tier in $($TierBaselinesDirs -join ', ') changed or added a baseline" }

[IO.FileInfo[]] $baselines = Get-ChildItem -LiteralPath $baselinesDir -Filter *.verified.png
[string] $appVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "../version") -Raw).Trim()
[string] $zipName = "baselines-$appVersion-$($Commit.Substring(0, 7)).zip"
[string] $zip = Join-Path ([IO.Path]::GetTempPath()) $zipName
Compress-Archive -LiteralPath $baselines.FullName -DestinationPath $zip -Force

$env:GH_TOKEN = $GhToken
& gh release upload uitest-baselines $zip --repo $Repo --clobber | Out-Host
if ($LASTEXITCODE -ne 0) { throw "gh release upload uitest-baselines $zip --repo $Repo --clobber failed with exit code $LASTEXITCODE" }
Remove-Item -LiteralPath $zip
Write-Host "==> published $($baselines.Count) baselines, $($changedHashByName.Count) changed or new, as $zipName"
$zipName
