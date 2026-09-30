<#
.SYNOPSIS
    Builds the FIPS 140-3 validated OpenSSL FIPS Provider (fips.dll) and openssl.exe for Windows x64.

.DESCRIPTION
    Downloads the validated OpenSSL source tarball, verifies its SHA-256, imports the MSVC x64 build
    environment, configures with enable-fips, builds, runs the test suite, installs, and writes an evidence
    manifest describing exactly what was built and with what.

    Only the OpenSSL release named by -Version is validated. See fips/doc/build-fips-provider.md before changing it.

.PARAMETER Version
    OpenSSL release to build. Defaults to the currently validated 3.1.2 (CMVP certificate #4985).

.PARAMETER ExpectedSha256
    Published SHA-256 of the source tarball. The build aborts on a mismatch.

.PARAMETER WorkRoot
    Scratch directory for download, extraction and build.

.PARAMETER Prefix
    OpenSSL install prefix. fips.dll lands in <Prefix>\lib\ossl-modules, openssl.exe in <Prefix>\bin.

.PARAMETER EvidenceRoot
    Directory that receives the evidence package for this build.

.PARAMETER StaticCrt
    Link the static C runtime (/MT) so fips.dll has no vcruntime140.dll dependency. OFF by default, and it
    should stay off: CMVP Management Manual §7.9.2 footnote 5 permits a user to recompile a validated module
    only where "the methods in the Security Policy must be followed without modification", and the #4985
    security policy's Windows method is the bare 'perl Configure enable-fips / nmake / nmake install'. Adding
    /MT modifies that method and forfeits the cover. See build-fips-provider.md, "C runtime linkage".

.PARAMETER SkipTests
    Skip 'nmake test'. For iterating on the script only. A production build must run the tests.

.PARAMETER EnsureToolchain
    Install missing build prerequisites (Strawberry Perl, NASM) via Chocolatey. This performs machine-level
    package installation, so use it on a dedicated build VM, not on a workstation. Visual Studio is never
    installed automatically; it must already be present.

.PARAMETER AllowAnyVisualStudio
    Accept a Visual Studio other than 2019. The build normally pins to VS 2019 because 140sp4985 section 5.3
    records it as the compiler used for the tested Windows environment. Throwaway test builds only.

.EXAMPLE
    .\Build-FipsProvider.ps1 -EvidenceRoot C:\fips-evidence

.EXAMPLE
    .\Build-FipsProvider.ps1 -EvidenceRoot $env:RUNNER_TEMP\evidence -EnsureToolchain
#>
[CmdletBinding()]
param(
    [string]$Version = "3.1.2",
    [string]$ExpectedSha256 = "a0ce69b8b97ea6a35b96875235aa453b966ba3cba8af2de23657d8b6767d6539",
    [string]$WorkRoot = "C:\fips-build",
    [string]$Prefix = "C:\openssl-install",
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [bool]$StaticCrt = $false,
    [switch]$SkipTests,
    [switch]$EnsureToolchain,
    [switch]$AllowAnyVisualStudio
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "========== $Message ==========" -ForegroundColor Cyan
}

function Remove-BuildDirectory([string]$Path, [string]$Purpose) {
    <#
        This script recursively deletes its work tree and its install prefix. Those paths come from
        parameters, so a typo or a stray -Prefix C:\ would be catastrophic. Refuse anything that is not
        unmistakably a scratch directory, and say why.
    #>
    if (-not $Path) { return }
    if (-not (Test-Path $Path)) { return }

    $full = (Resolve-Path -LiteralPath $Path).ProviderPath.TrimEnd('\')
    $problems = @()

    if ($full -notmatch '^[A-Za-z]:\\') { $problems += "not a rooted local path" }

    # Require real depth: 'C:\openssl-install' is two segments and fine, 'C:\' and 'C:\Windows' are not.
    $segments = @($full -split '\\' | Where-Object { $_ })
    if ($segments.Count -lt 2) { $problems += "too shallow; refusing to delete a drive root" }

    $protected = @(
        $env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramData,
        $env:USERPROFILE, (Split-Path -Parent $env:USERPROFILE), "$env:SystemDrive\"
    ) | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }

    foreach ($p in $protected) {
        if ($full -ieq $p) { $problems += "equals protected path '$p'" }
        # Deleting something that CONTAINS a protected path means we are above it.
        if ($p -ilike "$full\*") { $problems += "is a parent of protected path '$p'" }
    }

    if ($problems.Count -gt 0) {
        $detail = ($problems | ForEach-Object { "  - $_" }) -join "`n"
        throw @"
SAFETY ABORT. Refusing to recursively delete the $Purpose directory:
  $full
Reasons:
$detail
Nothing has been deleted. Pass a dedicated scratch path, for example S:\fips-build.
"@
    }

    Write-Host "Removing previous $Purpose directory: $full"
    Remove-Item -LiteralPath $full -Recurse -Force
}

function Install-Toolchain {
    <#
        OpenSSL needs Perl to configure and NASM to assemble the x86-64 crypto primitives. GitHub's Windows
        runners ship both, but a fresh VM does not, and a missing NASM surfaces as an obscure Configure failure.
    #>
    if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
        throw "Chocolatey is not installed, so -EnsureToolchain cannot install anything."
    }
    foreach ($needed in @(
            @{ Command = "perl"; Package = "strawberryperl" },
            @{ Command = "nasm"; Package = "nasm" }
        )) {
        if (Get-Command $needed.Command -ErrorAction SilentlyContinue) {
            Write-Host "$($needed.Command) already present."
            continue
        }
        Write-Host "Installing $($needed.Package) via Chocolatey."
        choco install -y --no-progress $needed.Package
        if ($LASTEXITCODE -ne 0) { throw "choco install $($needed.Package) failed with exit code $LASTEXITCODE" }
    }

    # Chocolatey edits the machine PATH; the current process does not see it until we re-read it.
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machinePath;$userPath"

    # NASM's package does not always land on PATH.
    $nasmDir = Join-Path $env:ProgramFiles "NASM"
    if ((Test-Path $nasmDir) -and ($env:Path -notlike "*$nasmDir*")) {
        $env:Path = "$env:Path;$nasmDir"
    }
}

function Import-MsvcEnvironment {
    <#
        vcvars64.bat only configures the cmd.exe that runs it. Launching it from PowerShell leaves the calling
        session without nmake or cl. Run it in a child cmd, dump the resulting environment, and copy it back.

        The Visual Studio version is PINNED, not "latest". Section 5.3 of the #4985 security policy records
        "Windows 10: Visual Studio 2019" as the compiler used for the tested Windows environment. A bare
        'vswhere -latest' silently picks VS 2022 on any machine that has both, which would quietly move the
        artifact outside the configuration we are claiming to match.
    #>
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found at $vswhere. Install Visual Studio with the Native Desktop workload."
    }

    $range = "[16.0,17.0)"   # Visual Studio 2019 only
    if ($AllowAnyVisualStudio) {
        $range = "[16.0,99.0)"
        Write-Warning ("-AllowAnyVisualStudio was passed. The compiler may not be VS 2019, which deviates " +
            "from 140sp4985 section 5.3. Do not use this for a shipping artifact.")
    }

    $vsRoot = & $vswhere -products * -version $range -latest `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if (-not $vsRoot) {
        $found = & $vswhere -products * -property catalog_productDisplayVersion
        throw @"
Visual Studio 2019 with the x64 C++ toolset was not found (looked for version $range).
Installed versions: $($found -join ', ')
140sp4985 section 5.3 records 'Windows 10: Visual Studio 2019' for the tested Windows environment.
Install VS 2019, or pass -AllowAnyVisualStudio for a throwaway test build.
"@
    }
    Write-Host "Using Visual Studio at: $vsRoot"

    $vcvars = Join-Path $vsRoot "VC\Auxiliary\Build\vcvars64.bat"
    if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found at $vcvars" }

    Write-Host "Importing MSVC environment from $vcvars"
    cmd /c "`"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') {
            Set-Item -Path "env:$($Matches[1])" -Value $Matches[2]
        }
    }

    foreach ($tool in @("nmake.exe", "cl.exe")) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "$tool is still not on PATH after importing the MSVC environment."
        }
    }
    return $vsRoot
}

function Invoke-Logged([string]$Exe, [string[]]$Arguments, [string]$LogPath) {
    <#
        Success is judged by exit code, never by whether the tool wrote to stderr. With
        $ErrorActionPreference = 'Stop', redirecting a native command's stderr into the pipeline turns its
        first stderr line into a terminating error, and compilers write plenty of non-fatal output there.
        nmake fails on its own banner otherwise.
    #>
    Write-Host "> $Exe $($Arguments -join ' ')"

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments 2>&1 | Tee-Object -FilePath $LogPath
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }

    if ($code -ne 0) {
        throw "$Exe $($Arguments -join ' ') failed with exit code $code. Log: $LogPath"
    }
}

# ---------------------------------------------------------------------------------------------------------------
Write-Step "preparing directories"

$tarball = "openssl-$Version.tar.gz"
$sourceUrl = "https://github.com/openssl/openssl/releases/download/openssl-$Version/$tarball"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDir = Join-Path $EvidenceRoot "openssl-$Version-$stamp"
$srcDir = Join-Path $WorkRoot "openssl-$Version"

New-Item -ItemType Directory -Force -Path $WorkRoot, $evidenceDir | Out-Null

# Configure must run against a pristine extraction, and the prefix must not carry artifacts from an earlier
# build. Both deletes are gated; see Remove-BuildDirectory.
Remove-BuildDirectory -Path $srcDir -Purpose "source tree"
Remove-BuildDirectory -Path $Prefix -Purpose "install prefix"

# ---------------------------------------------------------------------------------------------------------------
Write-Step "fetching and verifying validated source"

$tarballPath = Join-Path $WorkRoot $tarball
if (-not (Test-Path $tarballPath)) {
    Write-Host "Downloading $sourceUrl"
    Invoke-WebRequest -Uri $sourceUrl -OutFile $tarballPath
}

$actualSha = (Get-FileHash -Path $tarballPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = $ExpectedSha256.ToLowerInvariant()
Write-Host "expected SHA-256: $expected"
Write-Host "  actual SHA-256: $actualSha"
if ($actualSha -ne $expected) {
    throw "Source tarball hash mismatch. Refusing to build a module from unverified source."
}

Push-Location $WorkRoot
try {
    Invoke-Logged "tar.exe" @("-xzf", $tarball) (Join-Path $evidenceDir "extract.log")
} finally {
    Pop-Location
}

# ---------------------------------------------------------------------------------------------------------------
Write-Step "importing build environment"

if ($EnsureToolchain) { Install-Toolchain }
$vsRoot = Import-MsvcEnvironment

# ---------------------------------------------------------------------------------------------------------------
Write-Step "configuring with enable-fips"

Push-Location $srcDir
try {
    # The #4985 security policy's Windows method is 'perl Configure enable-fips'. VC-WIN64A is what that
    # auto-selects on an x64 MSVC host, and --prefix/--openssldir only relocate the install tree, which the
    # policy itself contemplates by documenting 'openssl fipsinstall' for non-default locations. Anything
    # beyond this is a modification of the documented method -- see MM 7.9.2 footnote 5.
    $configureArgs = @(
        "Configure",
        "enable-fips",
        "VC-WIN64A",
        "--prefix=$Prefix",
        "--openssldir=$Prefix\ssl"
    )
    if ($StaticCrt) {
        Write-Warning ("Building with the static C runtime (/MT). This MODIFIES the security policy's " +
            "documented build method and forfeits the CMVP MM 7.9.2 footnote 5 recompilation cover. " +
            "Only do this as a deliberate, documented decision.")
        $configureArgs += "/MT"
    }
    Invoke-Logged "perl" $configureArgs (Join-Path $evidenceDir "configure.log")

    Invoke-Logged "perl" @("configdata.pm", "--dump") (Join-Path $evidenceDir "configdata.txt")

    Write-Step "building"
    Invoke-Logged "nmake" @() (Join-Path $evidenceDir "nmake.log")

    if ($SkipTests) {
        Write-Warning "Tests skipped. This build is NOT suitable for release."
    } else {
        Write-Step "running the test suite"
        Invoke-Logged "nmake" @("test") (Join-Path $evidenceDir "nmake-test.log")
    }

    Write-Step "installing"
    Invoke-Logged "nmake" @("install") (Join-Path $evidenceDir "nmake-install.log")
} finally {
    Pop-Location
}

# ---------------------------------------------------------------------------------------------------------------
Write-Step "collecting artifacts and evidence"

$fipsDll = Join-Path $Prefix "lib\ossl-modules\fips.dll"
$opensslExe = Join-Path $Prefix "bin\openssl.exe"
foreach ($artifact in @($fipsDll, $opensslExe)) {
    if (-not (Test-Path $artifact)) { throw "Expected artifact not produced: $artifact" }
}

$artifactDir = Join-Path $evidenceDir "artifacts"
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Copy-Item $fipsDll, $opensslExe -Destination $artifactDir -Force

# openssl.exe from a default (shared) build links against libcrypto and libssl, so it cannot run
# 'fipsinstall' on a target machine without them. These are the 3.1.2 command-line tool's own dependencies,
# not the cryptographic module: ziti-edge-tunnel never loads them, it has its own OpenSSL.
$runtimeDlls = Get-ChildItem (Join-Path $Prefix "bin") -Filter "lib*-3-x64.dll" -ErrorAction SilentlyContinue
if ($runtimeDlls) {
    Copy-Item $runtimeDlls.FullName -Destination $artifactDir -Force
    Write-Host "Collected openssl.exe dependencies: $(($runtimeDlls.Name) -join ', ')"
} else {
    Write-Warning ("No libcrypto/libssl DLLs found beside openssl.exe. If openssl.exe is dynamically " +
        "linked it will fail to start on a machine that lacks them.")
}
Copy-Item $tarballPath -Destination $evidenceDir -Force

# The fipsmodule.cnf that 'nmake install' wrote is keyed to this build machine and is deliberately not kept.
$builtCnf = Join-Path $Prefix "ssl\fipsmodule.cnf"
if (Test-Path $builtCnf) {
    Write-Host "Discarding build-machine fipsmodule.cnf; it is regenerated per installation."
    Remove-Item $builtCnf -Force
}

$clBanner = (cmd /c "cl.exe 2>&1" | Select-Object -First 2) -join " "

# Resolve these before the literal. Windows PowerShell 5.1 will not accept try/catch as an expression, and
# this script runs in the build guest, which is 5.1 unless someone installed PowerShell 7 there.
$perlVersion = "not found"
try { $perlVersion = ((perl -v) -join " ").Trim() } catch { }

$nasmVersion = "not found"
try { $nasmVersion = ((nasm -v) -join " ").Trim() } catch { }

$deviations = @("none")
if ($StaticCrt) { $deviations = @("/MT static C runtime added to Configure") }

$manifest = [ordered]@{
    module                = "OpenSSL FIPS Provider"
    moduleVersion         = $Version
    cmvpCertificate       = "4985"
    cmvpStandard          = "FIPS 140-3"
    cmvpSunset            = "2030-03-10"
    builtAtUtc            = (Get-Date).ToUniversalTime().ToString("o")
    builtBy               = "$env:USERDOMAIN\$env:USERNAME"
    buildHost             = $env:COMPUTERNAME
    sourceUrl             = $sourceUrl
    sourceSha256Expected  = $expected
    sourceSha256Actual    = $actualSha
    configureArguments    = ($configureArgs -join " ")
    staticCrt             = $StaticCrt
    testsRun              = (-not $SkipTests.IsPresent)
    fipsDllSha256         = (Get-FileHash $fipsDll -Algorithm SHA256).Hash.ToLowerInvariant()
    fipsDllBytes          = (Get-Item $fipsDll).Length
    opensslExeSha256      = (Get-FileHash $opensslExe -Algorithm SHA256).Hash.ToLowerInvariant()
    opensslExeBytes       = (Get-Item $opensslExe).Length
    toolchain             = [ordered]@{
        visualStudioRoot = $vsRoot
        compiler         = $clBanner
        perl             = $perlVersion
        nasm             = $nasmVersion
        os               = (Get-CimInstance Win32_OperatingSystem).Caption
        osBuild          = [string][System.Environment]::OSVersion.Version
    }
    securityPolicyMethod  = "perl Configure enable-fips / nmake / nmake install (140sp4985 section 11.1)"
    methodDeviations      = $deviations
    notes                 = @(
        "fipsmodule.cnf is generated on each target machine by 'openssl fipsinstall' and is not part of this package.",
        "fips.dll must be Authenticode-signed BEFORE fipsinstall runs; the integrity HMAC covers the file bytes.",
        "140sp4985 section 5.3 records Visual Studio 2019 as the compiler used for the Windows 10 tested OE."
    )
}

$manifestPath = Join-Path $evidenceDir "manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Step "done"
Write-Host "fips.dll      : $fipsDll"
Write-Host "openssl.exe   : $opensslExe"
Write-Host "evidence      : $evidenceDir"
Write-Host ""
Write-Host "Next: verify the module on the bench before wiring it into the installer." -ForegroundColor Yellow
Write-Host "See fips\doc\verify-fips-provider.md"
