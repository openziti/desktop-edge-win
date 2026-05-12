#requires -Version 7.0
<#
    quick-run.ps1

    One-shot local test loop:
      1. Nuke .verified.png baselines so AutoVerify regenerates them.
      2. Run the UI tests (no full rebuild -- assumes you built with -SkipBuild path).
      3. Tee a *filtered* run log to TestResults\run-output.txt (skips wire-protocol JSON spam).
      4. Pop the gallery in the default browser.

    Pass -ResetBaselines @('Visual_*','LogLevel_*') to only nuke specific baselines.
    Default nukes ALL .verified.png so a fresh visual review is generated.
#>
[CmdletBinding()]
param(
    [string[]] $ResetBaselines = @('*'),
    [switch]   $NoOpenGallery,
    [switch]   $Build,
    # One or more categories to narrow the run. Known values:
    #   MainScreen, IdentityDetail, IdentityDetailServices, Mfa, Sort,
    #   TunnelSettings, LogLevel, AutomaticUpdate.
    # Examples:
    #   -Category Mfa                       (just MFA tests)
    #   -Category Mfa,Sort,TunnelSettings   (all three)
    # Empty -> run everything.
    [string[]] $Category
)

$ErrorActionPreference = "Continue"
$here = $PSScriptRoot
$resultsDir = Join-Path $here "TestResults"
$txt = Join-Path $resultsDir "run-output.txt"
$buffer = New-Object System.Collections.Generic.List[string]

# Wipe matching baselines so AutoVerify rewrites them fresh
$baselinesDir = Join-Path $here "UITests.Appium\Tests"
foreach ($pattern in $ResetBaselines) {
    $glob = Join-Path $baselinesDir "SmokeTests.$pattern.verified.png"
    Get-ChildItem $glob -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "removing baseline: $($_.Name)"
        Remove-Item -Force $_.FullName -ErrorAction SilentlyContinue
    }
    $glob2 = Join-Path $baselinesDir "LandingReadOnlyTests.$pattern.verified.png"
    Get-ChildItem $glob2 -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "removing baseline: $($_.Name)"
        Remove-Item -Force $_.FullName -ErrorAction SilentlyContinue
    }
}

# Run the tests, buffer a filtered transcript (TestResults dir gets wiped mid-run,
# so we accumulate in memory and write the file at the end).
$runArgs = @{ AutoVerify = $true }
if (-not $Build) { $runArgs.SkipBuild = $true }

if ($Category -and $Category.Count -gt 0) {
    # dotnet test filter syntax uses '|' for OR between expressions
    $runArgs.Filter = ($Category | ForEach-Object { "Category=$_" }) -join '|'
    Write-Host "==> filtering to: $($runArgs.Filter)"
}

Write-Host "==> running run-ui-tests.ps1 ($(($runArgs.Keys | ForEach-Object { '-' + $_ }) -join ' '))"
& (Join-Path $here "run-ui-tests.ps1") @runArgs *>&1 |
    ForEach-Object {
        $line = "$_"
        # filter out megabytes of wire-protocol JSON the trace logs emit
        if ($line -match 'UI-DataClient-(send|read)-' ) { return }
        if ($line -match 'ZitiDesktopEdge\.Models\.ZitiIdentity\s+Identity:') { return }
        Write-Host $line
        $buffer.Add($line) | Out-Null
    }

$rc = $LASTEXITCODE

# Write buffered output now that run-ui-tests.ps1 has finished managing TestResults
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$buffer -join "`r`n" | Set-Content -LiteralPath $txt -Encoding utf8

Write-Host ""
Write-Host "==> filtered output: $txt"
if (Test-Path (Join-Path $resultsDir "report.md")) {
    Write-Host "==> markdown report: $(Join-Path $resultsDir 'report.md')"
}
if (Test-Path (Join-Path $resultsDir "gallery.html")) {
    Write-Host "==> gallery:         $(Join-Path $resultsDir 'gallery.html')"
    if (-not $NoOpenGallery) {
        Start-Process (Join-Path $resultsDir "gallery.html")
    }
}

exit $rc
