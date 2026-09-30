<#
.SYNOPSIS
    Publishes a built OpenSSL FIPS Provider as a GitHub release, and writes the pin that Installer/build.ps1
    consumes.

.DESCRIPTION
    Takes the evidence directory produced by Build-FipsProvider.ps1, creates (or updates) a GitHub release whose
    assets are fips.dll, openssl.exe, the two OpenSSL DLLs openssl.exe links against, and the evidence package,
    then rewrites fips/provider.json with the release tag, asset URLs and SHA-256 hashes.

    The release is marked not-latest. This repository's releases are ZDEW product releases, and a provider
    build must not take the "Latest" badge from one.

    Publishing to a public GitHub release is deliberate. The whole compliance argument rests on being able to
    show what was built from what; a binary anyone can download and hash is stronger evidence than a binary in
    an artifact store, and it lets a customer verify our claim without asking us for anything.

    The release is created as a draft by default. Review the generated notes, attach the signed artifacts, then
    publish.

.PARAMETER EvidenceDir
    The per-build evidence directory created by Build-FipsProvider.ps1, e.g. C:\fips-evidence\openssl-3.1.2-<stamp>.

.PARAMETER ArtifactDir
    Directory holding the artifacts to publish. Defaults to <EvidenceDir>\artifacts, which is normally what you
    want: these are published UNSIGNED on purpose. ZDEW's installer build signs every binary it packages and
    these ride along in that pass, so signing is never special-cased for FIPS. The hashes pinned in
    provider.json are therefore the unsigned hashes, which is what ties the downloaded bytes to this build.

.PARAMETER Tag
    Release tag. Defaults to fips-provider-<version>-<yyyyMMdd>. Never reuse a tag; the pin is by tag.

.PARAMETER Repo
    owner/name of the repository to publish into.

.PARAMETER PinPath
    Path to the pin file rewritten on success.

.PARAMETER Publish
    Publish the release immediately instead of leaving it as a draft.

.PARAMETER DryRun
    Validate, hash and render everything, then print what would happen without creating a release, uploading
    anything, or writing provider.json.

.EXAMPLE
    .\Publish-FipsProvider.ps1 -EvidenceDir C:\fips-evidence\openssl-3.1.2-20260916-101500
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceDir,
    [string]$ArtifactDir,
    [string]$Tag,
    [string]$Repo = "openziti/desktop-edge-win",
    [string]$PinPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "provider.json"),
    [switch]$Publish,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue) -and -not $DryRun) {
    throw "The GitHub CLI (gh) is required. Install it, or set GH_TOKEN and run this where gh is available."
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY RUN -- no release is created, nothing is uploaded, provider.json is not written." `
        -ForegroundColor Yellow
    Write-Host "evidence.zip and release-notes.md are still produced inside the evidence directory, so the" `
        -ForegroundColor Yellow
    Write-Host "sizes and notes shown below are the real ones." -ForegroundColor Yellow
}

$manifestPath = Join-Path $EvidenceDir "manifest.json"
if (-not (Test-Path $manifestPath)) {
    throw "No manifest.json in $EvidenceDir. Run Build-FipsProvider.ps1 first."
}
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if (-not $ArtifactDir) { $ArtifactDir = Join-Path $EvidenceDir "artifacts" }

# All four ship. openssl.exe is dynamically linked, so it cannot run fipsinstall on a target machine without
# libcrypto and libssl beside it.
$required = @("fips.dll", "openssl.exe", "libcrypto-3-x64.dll", "libssl-3-x64.dll")
$artifacts = foreach ($name in $required) {
    $path = Join-Path $ArtifactDir $name
    if (-not (Test-Path $path)) { throw "Missing artifact: $path" }
    Get-Item $path
}
$fipsDll = (Join-Path $ArtifactDir "fips.dll")
$opensslExe = (Join-Path $ArtifactDir "openssl.exe")

if (-not $Tag) { $Tag = "fips-provider-$($manifest.moduleVersion)-$(Get-Date -Format 'yyyyMMdd')" }

$fipsSha = (Get-FileHash $fipsDll -Algorithm SHA256).Hash.ToLowerInvariant()
$opensslSha = (Get-FileHash $opensslExe -Algorithm SHA256).Hash.ToLowerInvariant()

$assetHashes = ($artifacts | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}) -join "`n"

# ConvertFrom-Json turns the ISO timestamp into a DateTime, which then renders in the local culture's format.
# An evidence document needs an unambiguous UTC value, not 09/16/2026.
$builtAt = ([datetime]$manifest.builtAtUtc).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# The compiler string carries Microsoft's copyright banner. Keep the version, drop the boilerplate.
$compiler = ($manifest.toolchain.compiler -split 'Copyright')[0].Trim()

$signed = @{}
foreach ($pair in @(@{ Name = "fips.dll"; Path = $fipsDll }, @{ Name = "openssl.exe"; Path = $opensslExe })) {
    # Recorded, not enforced. These are expected to be unsigned here; ZDEW's installer build signs them along
    # with everything else it packages.
    $sig = Get-AuthenticodeSignature $pair.Path
    $signed[$pair.Name] = $sig.Status.ToString()
}

if ($fipsSha -ne $manifest.fipsDllSha256) {
    Write-Host "fips.dll hash differs from the as-built manifest value." -ForegroundColor Yellow
    Write-Host "  as built : $($manifest.fipsDllSha256)"
    Write-Host "  publishing: $fipsSha"
    Write-Host "Expected when publishing signed artifacts. The pin records the published hash."
}

Write-Host "Packaging evidence"
$evidenceZip = Join-Path $EvidenceDir "evidence.zip"
if (Test-Path $evidenceZip) { Remove-Item $evidenceZip -Force }
$toZip = Get-ChildItem $EvidenceDir -File | Where-Object { $_.Name -ne "evidence.zip" }
Compress-Archive -Path $toZip.FullName -DestinationPath $evidenceZip -Force

$notes = @"
OpenSSL FIPS Provider $($manifest.moduleVersion) for Windows x64.

This is the FIPS 140-3 validated cryptographic module that Ziti Desktop Edge for Windows loads when installed
with FIPS mode enabled. It is published so that anyone can obtain it, hash it, and verify that the binary shipped
in the installer is the binary built from the validated source below.

| Property | Value |
| --- | --- |
| Module | OpenSSL FIPS Provider $($manifest.moduleVersion) |
| CMVP certificate | [#$($manifest.cmvpCertificate)](https://csrc.nist.gov/projects/cryptographic-module-validation-program/certificate/$($manifest.cmvpCertificate)) ($($manifest.cmvpStandard)) |
| Certificate sunset | $($manifest.cmvpSunset) |
| Source | ``openssl-$($manifest.moduleVersion).tar.gz`` |
| Source SHA-256 | ``$($manifest.sourceSha256Actual)`` |
| Build configuration | ``$($manifest.configureArguments)`` |
| Static C runtime | $($manifest.staticCrt) |
| Test suite run | $($manifest.testsRun) |
| Built on | $($manifest.toolchain.os) (build $($manifest.toolchain.osBuild)) |
| Compiler | $compiler |
| Built at (UTC) | $builtAt |

### Asset hashes (SHA-256)

``````
$assetHashes
``````

### Signing

These artifacts are **unsigned by design**, and the hashes above are the unsigned hashes.

Ziti Desktop Edge for Windows Authenticode-signs every binary it packages, and these are signed in that same
pass rather than separately, so a change to how signing works cannot leave them behind. The installer verifies
the hashes above on download, then signs. ``openssl fipsinstall`` runs at install time, after signing, so the
module's integrity HMAC covers exactly the bytes that ship.

Current status as published: fips.dll = $($signed['fips.dll']), openssl.exe = $($signed['openssl.exe']).

``evidence.zip`` contains the source tarball hash, the full build configuration dump, and the build and test logs.

### Notes

- ``fipsmodule.cnf`` is **not** published. It contains an HMAC over the bytes of ``fips.dll`` and must be
  generated on each machine with ``openssl fipsinstall``. See ``fips/`` in this repository.
- Only OpenSSL $($manifest.moduleVersion) holds an active OpenSSL-project FIPS certificate. Do not substitute a
  newer 3.1.x patch release; it is not validated.
- The certificate's only tested Windows operational environment is Windows 10 Pro on x86-64. Other Windows
  versions are vendor-affirmed.
"@

$notesPath = Join-Path $EvidenceDir "release-notes.md"
$notes | Set-Content -Path $notesPath -Encoding UTF8

if (-not $DryRun) {
    $existing = gh release view $Tag --repo $Repo --json tagName 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "Release $Tag already exists in $Repo. Tags are pinned by consumers; pick a new tag."
    }
}

$ghArgs = @("release", "create", $Tag)
$ghArgs += ($artifacts | ForEach-Object { $_.FullName })
$ghArgs += @(
    $evidenceZip,
    "--repo", $Repo,
    "--title", "OpenSSL FIPS Provider $($manifest.moduleVersion) (Windows x64)",
    "--notes-file", $notesPath,
    # This repository's releases are ZDEW product releases. Without this, GitHub would hand the "Latest"
    # badge to a provider build and bury the actual product release on the repo's front page.
    "--latest=false"
)
if (-not $Publish) { $ghArgs += "--draft" }

if ($DryRun) {
    Write-Host ""
    Write-Host "--- release notes that would be published ---" -ForegroundColor Cyan
    Write-Host $notes
    Write-Host "--- command that would run ---" -ForegroundColor Cyan
    # Quote anything with a space so the printed line is actually runnable. PowerShell splatting handles this
    # for the real invocation; the display form has to do it itself.
    $shown = $ghArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }
    Write-Host "gh $($shown -join ' ')"
} else {
    Write-Host "Creating release $Tag in $Repo"
    gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE" }
}

$baseUrl = "https://github.com/$Repo/releases/download/$Tag"
$pin = [ordered]@{
    '$comment'      = "This file is used by the installer build process where the FIPS files should be " +
                      "downloaded from and what each file should hash to. If a download does not match, " +
                      "the build must stop. Do not edit this file by hand - it is written by running " +
                      "fips/scripts/Publish-FipsProvider.ps1. See fips/doc/runbook.md."
    moduleVersion   = $manifest.moduleVersion
    cmvpCertificate = $manifest.cmvpCertificate
    releaseTag      = $Tag
    repo            = $Repo
    assets          = @(
        $artifacts | ForEach-Object {
            [ordered]@{
                name   = $_.Name
                url    = "$baseUrl/$($_.Name)"
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
}
$pinJson = $pin | ConvertTo-Json -Depth 5

if ($DryRun) {
    Write-Host ""
    Write-Host "--- $PinPath that would be written ---" -ForegroundColor Cyan
    Write-Host $pinJson
    Write-Host ""
    Write-Host "--- files that would be uploaded ---" -ForegroundColor Cyan
    $artifacts | ForEach-Object { "  {0,-24} {1,10:N0} bytes" -f $_.Name, $_.Length }
    "  {0,-24} {1,10:N0} bytes" -f (Split-Path -Leaf $evidenceZip), (Get-Item $evidenceZip).Length

    Write-Host ""
    Write-Host "DRY RUN complete. Nothing was created, uploaded or written." -ForegroundColor Yellow
    Write-Host "Re-run without -DryRun to create the draft release."
    return
}

$pinJson | Set-Content -Path $PinPath -Encoding UTF8

Write-Host ""
Write-Host "release : https://github.com/$Repo/releases/tag/$Tag"
Write-Host "pin     : $PinPath"
Write-Host ""
if (-not $Publish) {
    Write-Host "Release is a DRAFT. Review the notes and publish it before merging the pin." `
        -ForegroundColor Yellow
}
Write-Host "Commit the updated pin so Installer/build.ps1 picks up this module." -ForegroundColor Yellow
