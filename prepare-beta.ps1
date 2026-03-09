# Prepares a beta release branch and opens a PR against release-next.
#
# Sample invocations:
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1 -DryRun
#
# Prerequisites:
#   - git configured with push access to the repo
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

function Log($msg) { Write-Host $msg }
function Info($msg) { Write-Host -ForegroundColor Cyan  "  [info] $msg" }
function Ok($msg)   { Write-Host -ForegroundColor Green "    [ok] $msg" }
function Die($msg)  { Write-Host -ForegroundColor Red   " [error] $msg"; exit 1 }

# ── Validate ──────────────────────────────────────────────────────────────────

Log ""
Log "========================================================"
Log "  prepare-beta.ps1"
Log "========================================================"
Info "Desktop Edge version : $DesktopEdgeVersion"
Info "ZET version          : $ZetVersion"
if ($DryRun) { Info "DRY RUN - no git commits or pushes will be made" }
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
    Write-Host -ForegroundColor Yellow "  [warn] gh CLI not found - branch/commit/push will run but PR must be opened manually"
    Write-Host -ForegroundColor Yellow "         Install from https://cli.github.com or run: winget install GitHub.cli"
}

$branch = "beta-release-$DesktopEdgeVersion"
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd") + "T12:00:00Z"

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

# ── Update ZET version in build.ps1 ──────────────────────────────────────────

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

# ── Write beta.json ───────────────────────────────────────────────────────────

$betaJson = @"
{
  "name": "$DesktopEdgeVersion",
  "tag_name": "$DesktopEdgeVersion",
  "published_at": "$now",
  "installation_critical": false,
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe",
      "browser_download_url": "https://github.com/openziti/desktop-edge-win/releases/download/$DesktopEdgeVersion/Ziti.Desktop.Edge.Client-$DesktopEdgeVersion.exe"
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

# ── Summary of changes ────────────────────────────────────────────────────────

Log ""
Log "Files to commit:"
Info "Installer/build.ps1                -> ZET $ZetVersion"
Info "version                            -> $DesktopEdgeVersion"
Info "release-streams/beta.json          -> $DesktopEdgeVersion (github releases)"
Info "release-streams/beta-win32crypto.json -> $DesktopEdgeVersion (jfrog)"
Log ""

# ── Commit ────────────────────────────────────────────────────────────────────

$commitMsg = "chore: prepare beta $DesktopEdgeVersion with ZET $ZetVersion"
Info "Committing: $commitMsg"
if (-not $DryRun) {
    git add Installer/build.ps1 version release-streams/beta.json release-streams/beta-win32crypto.json
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Info "Nothing to commit - files already up to date"
    } else {
        git commit -m $commitMsg
        if ($LASTEXITCODE -ne 0) { Die "git commit failed" }
    }
}
Ok "Committed"

# ── Push ──────────────────────────────────────────────────────────────────────

Info "Pushing $branch to origin"
if (-not $DryRun) {
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { Die "git push failed" }
}
Ok "Pushed"

# ── Open PR ───────────────────────────────────────────────────────────────────

$prBody = @"
## Beta Release Preparation

| | |
|---|---|
| Desktop Edge version | ``$DesktopEdgeVersion`` |
| ziti-edge-tunnel version | ``$ZetVersion`` |

The installer build workflow will run automatically on this PR and produce signed artifacts.
Merge to publish the beta release to ``release-streams/beta.json``.
"@

Info "Opening PR: $branch -> release-next"
if (-not $DryRun) {
    if ($ghAvailable) {
        try {
            $existingPr = gh pr view $branch --json url --jq '.url' 2>$null
        } catch {
            $existingPr = $null
        }
        if ($existingPr) {
            Ok "PR already exists: $existingPr"
        } else {
            gh pr create `
                --base release-next `
                --head $branch `
                --title "Beta release $DesktopEdgeVersion" `
                --body $prBody
            if ($LASTEXITCODE -ne 0) { Die "gh pr create failed" }
            Ok "PR opened"
        }
    } else {
        $prUrl = "https://github.com/openziti/desktop-edge-win/pull/new/$branch"
        Info "gh CLI not available - open PR manually:"
        Info "  $prUrl"
    }
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
