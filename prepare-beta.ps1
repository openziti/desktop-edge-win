# Prepares a beta release branch and opens a PR against release-next.
# Downloads ziti-edge-tunnel binaries to extract dependency versions for the
# release notes. The actual signed build happens automatically via
# installer.build.yml when the PR is opened.
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
    Write-Host -ForegroundColor Yellow "  [warn] gh CLI not found - PR must be opened manually after pushing"
    Write-Host -ForegroundColor Yellow "         Install from https://cli.github.com or run: winget install GitHub.cli"
}

$branch   = "beta-release-$DesktopEdgeVersion"
$now      = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd") + "T12:00:00Z"
$zetBase  = "https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/$ZetVersion"
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

# ── Download ZET binaries and extract dependency versions ─────────────────────

$zetTunneler  = $ZetVersion
$zitiSdk      = "<fill in>"
$tlsuvOpenSsl = "<fill in>"
$tlsuvWin32   = "<fill in>"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    foreach ($variant in @("", "-win32crypto")) {
        $zipName = "ziti-edge-tunnel-Windows_x86_64${variant}.zip"
        $zipPath = Join-Path $tempDir $zipName
        $extractDir = Join-Path $tempDir "zet${variant}"
        $url = "$zetBase/$zipName"

        Info "Downloading $zipName"
        if (-not $DryRun) {
            Invoke-WebRequest -Uri $url -OutFile $zipPath
            Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
            $exe = Join-Path $extractDir "ziti-edge-tunnel.exe"

            Info "Running version -v on $zipName"
            $lines = & $exe version -v 2>&1 | Where-Object { $_ -notmatch "StartServiceCtrlDispatcher" }
            foreach ($line in $lines) {
                Info "  $line"
                if ($variant -eq "") {
                    if ($line -match 'ziti-tunneler:\s*(.+)')  { $zetTunneler  = $Matches[1].Trim() }
                    if ($line -match 'ziti-sdk:\s*(.+)')       { $zitiSdk      = $Matches[1].Trim() }
                    if ($line -match 'tlsuv:\s*(.+)\[OpenSSL') { $tlsuvOpenSsl = $Matches[1].Trim() }
                } else {
                    if ($line -match 'tlsuv:\s*(.+)\[win32')   { $tlsuvWin32   = $Matches[1].Trim() }
                }
            }
            Ok "Versions extracted from $zipName"
        }
    }
} finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

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
        Info "Entry for $DesktopEdgeVersion already exists - overwriting it"
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
