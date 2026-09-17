<#
.SYNOPSIS
    Downloads the pinned OpenSSL FIPS Provider artifacts and verifies their SHA-256 hashes.

.DESCRIPTION
    Reads the manifest, fetches each asset from its pinned GitHub release URL, and verifies the hash. A
    mismatch throws. Called from Installer/build.ps1 to stage the FIPS files for the MSI.

    The hash check is not belt-and-braces. It is a link in the evidence chain: it ties the bytes in the installer
    to the bytes in the published release, which are in turn tied to the verified validated source by the
    release's evidence package.

.PARAMETER Destination
    Directory to place fips.dll and openssl.exe into.

.PARAMETER Manifest
    Full path to fips/provider.json, which names the artifacts, their download URLs and their expected
    SHA-256 hashes.

.PARAMETER CacheDir
    Where downloads are cached between builds. Cached files are re-verified, so a poisoned cache cannot slip
    through. Defaults to %LOCALAPPDATA%\NetFoundry\build-cache\fips, which is deliberately outside the checkout:
    Installer/build.ps1 deletes its whole build directory on every run, so a cache under there would never
    survive to be used.

.PARAMETER Force
    Re-download even when a verified cached copy exists.

.EXAMPLE
    .\Installer\Get-FipsProvider.ps1 -Manifest .\fips\provider.json -Destination .\Installer\build\service
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [string]$CacheDir,
    [switch]$SkipCrt,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-BinaryFileVersion {
    <#
        FileVersion is a free-form display string. Microsoft ships build-lab suffixes in it -- the GitHub
        Actions runner's vcruntime140.dll reports "14.29.30157.0 built by: cloudtest" -- and casting that
        to [version] throws. The numeric fields carry the same four parts without the prose.
    #>
    param([System.IO.FileInfo]$File)

    $v = $File.VersionInfo
    [version]::new($v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart)
}

if (-not (Test-Path $Manifest)) {
    throw ("FIPS provider manifest not found at $Manifest. " +
        "Publish a provider release first: fips/scripts/Publish-FipsProvider.ps1")
}
$pin = Get-Content $Manifest -Raw | ConvertFrom-Json

if (-not $CacheDir) {
    $localAppData = $env:LOCALAPPDATA
    if (-not $localAppData) { $localAppData = [Environment]::GetFolderPath("LocalApplicationData") }
    $CacheDir = Join-Path $localAppData "NetFoundry\build-cache\fips"
}
$cache = Join-Path $CacheDir $pin.releaseTag
New-Item -ItemType Directory -Force -Path $Destination, $cache | Out-Null

Write-Host ("FIPS provider pin: $($pin.moduleVersion) from $($pin.repo)@$($pin.releaseTag) " +
    "(CMVP #$($pin.cmvpCertificate))")
Write-Host "cache: $cache"

foreach ($asset in $pin.assets) {
    $cached = Join-Path $cache $asset.name
    $expected = $asset.sha256.ToLowerInvariant()

    if ($Force -and (Test-Path $cached)) { Remove-Item $cached -Force }

    if (-not (Test-Path $cached)) {
        Write-Host "Downloading $($asset.name) from $($asset.url)"
        try {
            Invoke-WebRequest -Uri $asset.url -OutFile $cached -ErrorAction Stop
        } catch {
            throw "Failed to download $($asset.name) from $($asset.url): $($_.Exception.Message)"
        }
    } else {
        Write-Host "Using cached $($asset.name)"
    }

    $actual = (Get-FileHash $cached -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item $cached -Force -ErrorAction SilentlyContinue
        throw @"
SHA-256 mismatch for $($asset.name). Refusing to build an installer around an unverified FIPS module.
  expected: $expected
    actual: $actual
       url: $($asset.url)
"@
    }
    Write-Host "  verified $($asset.name) $actual"

    Copy-Item $cached -Destination (Join-Path $Destination $asset.name) -Force
}

#--------------------------------------------------------------------------------------------------------------
# C runtime
#--------------------------------------------------------------------------------------------------------------

if (-not $SkipCrt) {
    <#
        fips.dll and openssl.exe are built with the default (dynamic) C runtime, so they import
        VCRUNTIME140.dll, which Windows does not ship. The UCRT imports (api-ms-win-crt-*) are part of
        Windows 10 and later and need nothing.

        The DLL is taken app-local rather than by installing the VC++ redistributable machine-wide, and it is
        sourced HERE rather than from the FIPS build host. It sits outside the cryptographic module boundary,
        so refreshing it has no bearing on the FIPS claim -- and tying it to the module build would mean
        rebuilding a validated module just to pick up a CRT fix.

        Microsoft's redistributable-files terms cover deploying these DLLs from a Visual Studio install's
        redist folder alongside an application.
    #>
    Write-Host ""
    Write-Host "Resolving vcruntime140.dll"

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    $crt = $null

    if (Test-Path $vswhere) {
        $roots = & $vswhere -products * -property installationPath
        foreach ($root in $roots) {
            $redist = Join-Path $root "VC\Redist\MSVC"
            if (-not (Test-Path $redist)) { continue }

            # A VS install carries several variants of the same DLL. Only the desktop one is wanted:
            #   <ver>\x64\Microsoft.VCnnn.CRT          <- this one
            #   <ver>\onecore\x64\Microsoft.VCnnn.CRT  <- OneCore editions, not desktop Win32
            #   <ver>\debug_nonredist\...              <- debug CRT, not redistributable
            #   <ver>\spectre\...                      <- Spectre-mitigated, mismatched with our build
            $found = Get-ChildItem $redist -Recurse -Filter "vcruntime140.dll" -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.DirectoryName -match '\\Redist\\MSVC\\[\d.]+\\x64\\Microsoft\.VC\d+\.CRT$'
                } |
                Sort-Object { Get-BinaryFileVersion $_ } -Descending |
                Select-Object -First 1

            if ($found -and (-not $crt -or
                (Get-BinaryFileVersion $found) -gt (Get-BinaryFileVersion $crt))) {
                $crt = $found
            }
        }
    }

    if (-not $crt) {
        throw @"
vcruntime140.dll not found in any Visual Studio redist folder on this machine.
fips.dll and openssl.exe import it and will not load without it.
Install Visual Studio or the VS Build Tools with the C++ workload, or pass -SkipCrt and supply the DLL
another way. Do not install the redistributable machine-wide on end-user machines; ship it app-local.
"@
    }

    Copy-Item $crt.FullName -Destination (Join-Path $Destination "vcruntime140.dll") -Force
    $crtHash = (Get-FileHash $crt.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "  version $($crt.VersionInfo.FileVersion) from $($crt.DirectoryName)"
    Write-Host "  sha256  $crtHash"

    # The CRT is normally newer than the compiler that built the module, which is the supported direction:
    # the VC runtime is binary compatible forward across v140/141/142/143/145, so a VS 2019-compiled
    # fips.dll runs against it. The reverse is not true, so flag an older one.
    if ((Get-BinaryFileVersion $crt) -lt [version]"14.29") {
        Write-Warning ("vcruntime140.dll $($crt.VersionInfo.FileVersion) is older than the toolset that " +
            "built fips.dll (14.29, VS 2019). Install a newer VC++ toolset on this machine.")
    }
}

Write-Host ""
Write-Host "Staged FIPS provider artifacts into $Destination"
