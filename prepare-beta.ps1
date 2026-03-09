# Prepares a beta release branch with two local builds and opens a PR against release-next.
#
# Sample invocations:
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1 -DryRun
#
# Prerequisites:
#   - git configured with push access to the repo
#   - Advanced Installer, msbuild, nuget on PATH (same requirements as Installer\build.ps1)
#   - gh CLI installed and authenticated (https://cli.github.com) for PR creation
#
param(
    [Parameter(Mandatory = $true)]
    [string]$DesktopEdgeVersion,

    [Parameter(Mandatory = $true)]
    [string]$ZetVersion,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Log($msg)  { Write-Host $msg }
function Info($msg) { Write-Host -ForegroundColor Cyan   "  [info] $msg" }
function Ok($msg)   { Write-Host -ForegroundColor Green  "    [ok] $msg" }
function Die($msg)  { Write-Host -ForegroundColor Red    " [error] $msg"; exit 1 }

# ── Validate ──────────────────────────────────────────────────────────────────

Log ""
Log "========================================================"
Log "  prepare-beta.ps1"
Log "========================================================"
Info "Desktop Edge version : $DesktopEdgeVersion"
Info "ZET version          : $ZetVersion"
if ($DryRun) { Info "DRY RUN - no builds, git commits, or pushes will be made" }
Log "========================================================"
Log ""

if ($DesktopEdgeVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Die "DesktopEdgeVersion must be a 4-tuple (e.g. 2.9.6.0), got: $DesktopEdgeVersion"
}
if ($ZetVersion -notmatch '^v\d+\.\d+\.\d+$') {
    Die "ZetVersion must be in the form v1.2.3, got: $ZetVersion"
}

$ghAvailable = $null -ne (Get-Command gh -ErrorAction SilentlyContinue)
if (-not $ghAvailable) {
    Write-Host -ForegroundColor Yellow "  [warn] gh CLI not found - PR must be opened manually after pushing"
    Write-Host -ForegroundColor Yellow "         Install from https://cli.github.com or run: winget install GitHub.cli"
}

$branch   = "beta-release-$DesktopEdgeVersion"
$now      = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd") + "T12:00:00Z"
$zetExe   = "$scriptDir\Installer\build\service\ziti-edge-tunnel.exe"
$buildUrl = "https://github.com/openziti/desktop-edge-win/releases/download/"

Push-Location $scriptDir
try {

# ── Create branch ─────────────────────────────────────────────────────────────

Info "Creating branch: $branch"
if (-not $DryRun) {
    $branchExists = git branch --list $branch
    if ($branchExists) {
        Info "Branch already exists locally, switching to it"
        git checkout $branch
    } else {
        git checkout -b $branch
    }
    if ($LASTEXITCODE -ne 0) { Die "git checkout $branch failed" }
}
Ok "Branch ready"

# ── Update ZET version in Installer\build.ps1 ────────────────────────────────

Info "Updating Installer/build.ps1 -> ZITI_EDGE_TUNNEL_VERSION=$ZetVersion"
if (-not $DryRun) {
    $buildPs1 = "$scriptDir\Installer\build.ps1"
    (Get-Content $buildPs1) -replace '\$ZITI_EDGE_TUNNEL_VERSION="v\d+\.\d+\.\d+"', "`$ZITI_EDGE_TUNNEL_VERSION=`"$ZetVersion`"" |
        Set-Content $buildPs1
}
Ok "build.ps1 updated"

# ── Write version ─────────────────────────────────────────────────────────────

Info "Writing version -> $DesktopEdgeVersion"
if (-not $DryRun) {
    [System.IO.File]::WriteAllText("$scriptDir\version", $DesktopEdgeVersion)
}
Ok "version file written"

# ── Build standard installer ──────────────────────────────────────────────────

Log ""
Info "Running standard build..."
if (-not $DryRun) {
    & "$scriptDir\Installer\build.ps1" `
        -version $DesktopEdgeVersion `
        -url $buildUrl `
        -stream "beta" `
        -revertGitAfter $true
    if ($LASTEXITCODE -ne 0) { Die "Standard build failed" }
}
Ok "Standard build complete"

# ── Extract versions from standard binary (OpenSSL) ──────────────────────────

Log ""
$zetTunneler  = $ZetVersion
$zitiSdk      = "<fill in>"
$tlsuvOpenSsl = "<fill in>"
$tlsuvWin32   = "<fill in>"

if (-not $DryRun) {
    if (Test-Path $zetExe) {
        Info "Reading versions from standard ziti-edge-tunnel.exe (OpenSSL)"
        $versionLines = & $zetExe version -v 2>&1 | Where-Object { $_ -notmatch "StartServiceCtrlDispatcher" }
        foreach ($line in $versionLines) {
            Info "  $line"
            if ($line -match 'ziti-tunneler:\s*(.+)')  { $zetTunneler  = $Matches[1].Trim() }
            if ($line -match 'ziti-sdk:\s*(.+)')       { $zitiSdk      = $Matches[1].Trim() }
            if ($line -match 'tlsuv:\s*(.+)\[OpenSSL') { $tlsuvOpenSsl = $Matches[1].Trim() }
        }
        Ok "Standard versions extracted"
    } else {
        Write-Host -ForegroundColor Yellow "  [warn] ziti-edge-tunnel.exe not found after standard build, using placeholders"
    }
}

# ── Build win32crypto installer ───────────────────────────────────────────────

Log ""
Info "Running win32crypto build..."
if (-not $DryRun) {
    & "$scriptDir\Installer\build.ps1" `
        -version $DesktopEdgeVersion `
        -url $buildUrl `
        -stream "beta" `
        -Win32Crypto $true `
        -revertGitAfter $true
    if ($LASTEXITCODE -ne 0) { Die "Win32Crypto build failed" }
}
Ok "Win32Crypto build complete"

# ── Extract versions from win32crypto binary ──────────────────────────────────

Log ""
if (-not $DryRun) {
    if (Test-Path $zetExe) {
        Info "Reading versions from win32crypto ziti-edge-tunnel.exe"
        $versionLines = & $zetExe version -v 2>&1 | Where-Object { $_ -notmatch "StartServiceCtrlDispatcher" }
        foreach ($line in $versionLines) {
            Info "  $line"
            if ($line -match 'tlsuv:\s*(.+)\[win32')   { $tlsuvWin32   = $Matches[1].Trim() }
        }
        Ok "Win32crypto versions extracted"
    } else {
        Write-Host -ForegroundColor Yellow "  [warn] ziti-edge-tunnel.exe not found after win32crypto build, using placeholders"
    }
}

# ── Write beta.json ───────────────────────────────────────────────────────────
# Written after builds so our hardcoded 12:00:00Z timestamp isn't overwritten
# by output-build-json.ps1 which build.ps1 calls internally.

$betaJson = @"
{
  "name": "$DesktopEdgeVersion",
  "tag_name": "$DesktopEdgeVersion",
  "published_at": "$now",
  "installation_critical": false,
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe",
      "browser_download_url": "${buildUrl}$DesktopEdgeVersion/Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe"
    }
  ]
}
"@

Info "Writing release-streams/beta.json"
if (-not $DryRun) {
    $betaJson | Set-Content -Path "$scriptDir\release-streams\beta.json" -NoNewline
}
Ok "beta.json written"

# ── Write beta-win32crypto.json ───────────────────────────────────────────────

$betaWin32CryptoJson = @"
{
  "name": "$DesktopEdgeVersion",
  "tag_name": "$DesktopEdgeVersion",
  "published_at": "$now",
  "installation_critical": false,
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe",
      "browser_download_url": "https://netfoundry.jfrog.io/artifactory/downloads/desktop-edge-win-win32crypto/$DesktopEdgeVersion/Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe"
    }
  ]
}
"@

Info "Writing release-streams/beta-win32crypto.json"
if (-not $DryRun) {
    $betaWin32CryptoJson | Set-Content -Path "$scriptDir\release-streams\beta-win32crypto.json" -NoNewline
}
Ok "beta-win32crypto.json written"

# ── Prepend release-notes.md entry ───────────────────────────────────────────

$releaseNotesEntry = @"
# Release $DesktopEdgeVersion
## What's New
* updated to ziti-edge-tunnel $ZetVersion

## Bugs fixed:
n/a

## Other changes
n/a

## Dependencies
* ziti-tunneler: $zetTunneler
* ziti-sdk:      $zitiSdk
* tlsuv:         ${tlsuvOpenSsl}[OpenSSL 3.6.0 1 Oct 2025]
* tlsuv:         ${tlsuvWin32}[win32crypto(CNG): ncrypt[1.0] ]

"@

Info "Prepending entry to release-notes.md"
if (-not $DryRun) {
    $existing = Get-Content -Path "$scriptDir\release-notes.md" -Raw
    if ($existing -match "# Release $([regex]::Escape($DesktopEdgeVersion))") {
        Info "Entry for $DesktopEdgeVersion already exists in release-notes.md - overwriting it"
        $existing = $existing -replace "(?s)# Release $([regex]::Escape($DesktopEdgeVersion)).+?(?=# Release |\z)", ""
    }
    ($releaseNotesEntry + $existing) | Set-Content -Path "$scriptDir\release-notes.md" -NoNewline
}
Ok "release-notes.md updated"

# ── Summary of changes ────────────────────────────────────────────────────────

Log ""
Log "Files to commit:"
Info "Installer/build.ps1                   -> ZET $ZetVersion"
Info "version                               -> $DesktopEdgeVersion"
Info "release-notes.md                      -> prepended $DesktopEdgeVersion entry"
Info "release-streams/beta.json             -> $DesktopEdgeVersion (github releases)"
Info "release-streams/beta-win32crypto.json -> $DesktopEdgeVersion (jfrog)"
Log ""

# ── Commit ────────────────────────────────────────────────────────────────────

$commitMsg = "chore: prepare beta $DesktopEdgeVersion with ZET $ZetVersion"
Info "Committing: $commitMsg"
if (-not $DryRun) {
    git add Installer/build.ps1 version release-notes.md release-streams/beta.json release-streams/beta-win32crypto.json
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Info "Nothing to commit - files already up to date"
    } else {
        git commit -m $commitMsg
        if ($LASTEXITCODE -ne 0) { Die "git commit failed" }
    }
}
Ok "Committed"

# ── Push + PR ─────────────────────────────────────────────────────────────────

Log ""
Log "Branch is ready locally. When satisfied, push and open a PR:"
Log ""
Log "  git push origin $branch"
if ($ghAvailable) {
    Log "  gh pr create --base release-next --head $branch --title `"Beta release $DesktopEdgeVersion`""
} else {
    Log "  https://github.com/openziti/desktop-edge-win/pull/new/$branch"
}
Log ""

Log "========================================================"
Log "  Done."
if ($DryRun) { Log "  (dry run - no changes were made)" }
Log "========================================================"
Log ""

} finally {
    Pop-Location
}
