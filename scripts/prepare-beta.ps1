# Prepares a beta release branch and opens a PR against release-next.
# Downloads ziti-edge-tunnel binaries to extract dependency versions for the
# release notes. The actual signed build happens automatically via
# installer.build.yml when the PR is opened.
#
# Sample invocations:
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.9.6.0 -ZetVersion v1.11.1 -DryRun
#   .\prepare-beta.ps1 -DesktopEdgeVersion 2.10.2.0
#
# Prerequisites:
#   - git configured with push access to the repo
#   - gh CLI installed and authenticated (https://cli.github.com) for PR creation
#
param(
    [Parameter(Mandatory = $true)]
    [string]$DesktopEdgeVersion,

    [string]$ZetVersion,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = (Resolve-Path "$scriptDir\..").Path

function Log($msg)  { Write-Host $msg }
function Info($msg) { Write-Host -ForegroundColor Cyan   "  [info] $msg" }
function Ok($msg)   { Write-Host -ForegroundColor Green  "    [ok] $msg" }
function Die($msg)  { Write-Host -ForegroundColor Red    " [error] $msg"; exit 1 }

# ── Validate ──────────────────────────────────────────────────────────────────

Log ""
Log "========================================================"
Log "  prepare-beta.ps1"
Log "========================================================"
$isZetBump = [bool]$ZetVersion

if (-not $ZetVersion) {
    $buildPs1Content = Get-Content "$repoRoot\Installer\build.ps1" -Raw
    if ($buildPs1Content -match '\$ZITI_EDGE_TUNNEL_VERSION="(v\d+\.\d+\.\d+)"') {
        $ZetVersion = $Matches[1]
    } else {
        Die "Could not read current ZITI_EDGE_TUNNEL_VERSION from Installer/build.ps1"
    }
}

Info "Desktop Edge version : $DesktopEdgeVersion"
Info "ZET version          : $ZetVersion"
if ($DryRun) { Info "DRY RUN - no git commits or pushes will be made" }
Log "========================================================"
Log ""

if ($DesktopEdgeVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Die "DesktopEdgeVersion must be a 4-tuple (e.g. 2.9.6.0), got: $DesktopEdgeVersion"
}
if ($ZetVersion -and $ZetVersion -notmatch '^v\d+\.\d+\.\d+$') {
    Die "ZetVersion must be in the form v1.2.3, got: $ZetVersion"
}

$ghAvailable = $null -ne (Get-Command gh -ErrorAction SilentlyContinue)
if (-not $ghAvailable) {
    Write-Host -ForegroundColor Yellow "  [warn] gh CLI not found - PR must be opened manually after pushing"
    Write-Host -ForegroundColor Yellow "         Install from https://cli.github.com or run: winget install GitHub.cli"
}

$branch   = "beta-release-$DesktopEdgeVersion"
$zetBase  = "https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/$ZetVersion"

Push-Location $repoRoot
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
$zitiSdk      = ""
$tlsuvOpenSsl = ""
$tlsuvWin32   = ""

$zetDownloadDir = "$repoRoot\Installer\build\zet"
New-Item -ItemType Directory -Path "$zetDownloadDir\standard"    -Force | Out-Null
New-Item -ItemType Directory -Path "$zetDownloadDir\win32crypto" -Force | Out-Null

foreach ($variant in @("", "-win32crypto")) {
    $zipName    = "ziti-edge-tunnel-Windows_x86_64${variant}.zip"
    $subDir     = if ($variant -eq "") { "standard" } else { "win32crypto" }
    $extractDir = "$zetDownloadDir\$subDir"
    $zipPath    = "$zetDownloadDir\$zipName"
    $url        = "$zetBase/$zipName"

    Info "Downloading $zipName -> Installer\build\zet\$subDir\"
    Invoke-WebRequest -Uri $url -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
    $exe = "$extractDir\ziti-edge-tunnel.exe"

    Info "Running version -v on $subDir binary"
    $lines = & $exe version -v 2>&1 | Where-Object { $_ -notmatch "StartServiceCtrlDispatcher" }
    foreach ($line in $lines) {
        Info "  $line"
        if ($variant -eq "") {
            if ($line -match 'ziti-tunneler:\s*(.+)')  { $zetTunneler  = $Matches[1].Trim() }
            if ($line -match 'ziti-sdk:\s*(.+)')       { $zitiSdk      = $Matches[1].Trim() }
            if ($line -match 'tlsuv:\s*(.+)')            { $tlsuvOpenSsl = $Matches[1].Trim() }
        } else {
            if ($line -match 'tlsuv:\s*(.+)')            { $tlsuvWin32   = $Matches[1].Trim() }
        }
    }
    Ok "Versions extracted from $subDir binary"
}

# ── Update ZET version in Installer\build.ps1 ────────────────────────────────

if ($isZetBump) {
    Info "Updating Installer/build.ps1 -> ZITI_EDGE_TUNNEL_VERSION=$ZetVersion"
    if (-not $DryRun) {
        $buildPs1 = "$repoRoot\Installer\build.ps1"
        (Get-Content $buildPs1) -replace '\$ZITI_EDGE_TUNNEL_VERSION="v\d+\.\d+\.\d+"', "`$ZITI_EDGE_TUNNEL_VERSION=`"$ZetVersion`"" |
            Set-Content $buildPs1
    }
    Ok "build.ps1 updated"
} else {
    Info "ZET version unchanged - skipping build.ps1 update"
}

# ── Write version ─────────────────────────────────────────────────────────────

Info "Writing version -> $DesktopEdgeVersion"
if (-not $DryRun) {
    [System.IO.File]::WriteAllText("$repoRoot\version", $DesktopEdgeVersion)
}
Ok "version file written"

# ── Build release notes ──────────────────────────────────────────────────────

$releaseNotesPath = "$repoRoot\upcoming-release-notes.md"

$currentNotes = ([System.IO.File]::ReadAllText($releaseNotesPath) -replace "`r`n", "`n").Trim()

# Strip the top-level header block (e.g. "# Next Release" or "# Release X.Y.Z.W"),
# keep only the ## section content. Also strip any trailing ## Dependencies block
# since it will be regenerated from the downloaded binaries.
$currentNotes = $currentNotes -replace "(?s)^# [^\n]*\n.*?(?=## )", ""
$currentNotes = $currentNotes -replace "(?s)\n## Dependencies.*$", ""

# Inject "updated to ziti-edge-tunnel" into What's New if this is a ZET bump
if ($isZetBump) {
    $currentNotes = $currentNotes -replace "(## What's New\n)", "`$1* updated to ziti-edge-tunnel $ZetVersion`n"
}

# Append dependencies
$depsSection = @"

## Dependencies
* ziti-tunneler: $zetTunneler
* ziti-sdk:      $zitiSdk
* tlsuv:         $tlsuvOpenSsl
* tlsuv:         $tlsuvWin32
"@

$releaseEntry = "# Release $DesktopEdgeVersion`n$currentNotes`n$depsSection`n" -replace "`r`n", "`n"

Info "Updating upcoming-release-notes.md with version header and dependencies"
if (-not $DryRun) {
    [System.IO.File]::WriteAllText($releaseNotesPath, $releaseEntry, [System.Text.UTF8Encoding]::new($false))
}
Ok "upcoming-release-notes.md updated"

if ($DryRun) {
    Log ""
    Log "Release notes preview:"
    Log "------------------------------------------------------------------------"
    Log $releaseEntry
    Log "------------------------------------------------------------------------"
}

# ── Summary of changes ────────────────────────────────────────────────────────

Log ""
Log "Files to commit:"
if ($isZetBump) { Info "Installer/build.ps1                   -> ZET $ZetVersion" }
Info "version                               -> $DesktopEdgeVersion"
Info "upcoming-release-notes.md             -> $DesktopEdgeVersion entry"
Log ""

# ── Commit ────────────────────────────────────────────────────────────────────

if ($isZetBump) {
    $commitMsg = "chore: prepare beta $DesktopEdgeVersion with ZET $ZetVersion"
} else {
    $commitMsg = "chore: prepare beta $DesktopEdgeVersion"
}
Info "Committing: $commitMsg"
if (-not $DryRun) {
    $filesToAdd = @("version", "upcoming-release-notes.md")
    if ($isZetBump) { $filesToAdd += "Installer/build.ps1" }
    git add @filesToAdd
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

if ($isZetBump) {
    $prBody = @"
## Beta Release Preparation

| | |
|---|---|
| Desktop Edge version | ``$DesktopEdgeVersion`` |
| ziti-edge-tunnel version | ``$ZetVersion`` |

The installer build workflow will run automatically on this PR and produce signed artifacts.
After merging and publishing the release, run ``promote.ps1`` to write release stream JSONs.
"@
} else {
    $prBody = @"
## Beta Release Preparation

| | |
|---|---|
| Desktop Edge version | ``$DesktopEdgeVersion`` |

The installer build workflow will run automatically on this PR and produce signed artifacts.
After merging and publishing the release, run ``promote.ps1`` to write release stream JSONs.
"@
}

Info "Opening PR: $branch -> main"
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
                --base main `
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
