<#
.SYNOPSIS
    Proves that the built FIPS provider actually loads inside ziti-edge-tunnel, and that the tunneler reports
    itself as running in FIPS mode.

.DESCRIPTION
    This is the test that decides whether the whole approach works. Our fips.dll is built with MSVC. The
    OpenSSL core that has to load it is 3.6.3, built with mingw-w64 and statically linked into
    ziti-edge-tunnel.exe. Nothing has ever exercised that combination.

    It should work: a provider talks to its core through a plain C dispatch table, and a FIPS provider takes
    its allocator and file I/O from core upcalls precisely so it can be built independently. A cross-toolchain
    load failing at the integrity check is also exactly the kind of problem that surfaces weeks later as an
    unexplained TLS error, so it gets verified before any installer work.

    Run this on any Windows x64 machine. It is a technical compatibility test, not a compliance test, so the
    host OS does not have to match the certificate's tested environment.

    Nothing is installed and no service is touched. Everything happens in a staging directory.

.PARAMETER ArtifactDir
    Directory holding fips.dll, openssl.exe, libcrypto-3-x64.dll and libssl-3-x64.dll from
    Build-FipsProvider.ps1.

.PARAMETER ZetVersion
    ziti-edge-tunnel release to test against. Downloaded if not already cached.

.PARAMETER StageDir
    Where the test assembles its copy of the tunneler and the provider.

.EXAMPLE
    .\fips\scripts\Test-FipsWithTunneler.ps1 `
        -ArtifactDir D:\fips-buildhost\azure\openssl-3.1.2-20260916-133618\artifacts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactDir,
    [string]$ZetVersion = "v1.19.0",
    [string]$StageDir = "C:\temp\fips-stage"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$pass = @()
$fail = @()

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "========== $Message ==========" -ForegroundColor Cyan
}

function Assert-Result([bool]$Condition, [string]$Name, [string]$Detail = "") {
    if ($Condition) {
        $script:pass += $Name
        Write-Host "  PASS  $Name" -ForegroundColor Green
    } else {
        $script:fail += $Name
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor Red }
    }
}

#--------------------------------------------------------------------------------------------------------------
Write-Step "staging"

foreach ($f in @("fips.dll", "openssl.exe", "libcrypto-3-x64.dll", "libssl-3-x64.dll")) {
    if (-not (Test-Path (Join-Path $ArtifactDir $f))) {
        throw "$f not found in $ArtifactDir. Run Build-FipsProvider.ps1 and collect its artifacts first."
    }
}

if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

Copy-Item (Join-Path $ArtifactDir "*") -Destination $StageDir -Force

# fips.dll and openssl.exe are built against the dynamic C runtime, so the loader needs this next to them.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $crt = & $vswhere -products * -property installationPath | ForEach-Object {
        Get-ChildItem (Join-Path $_ "VC\Redist\MSVC") -Recurse -Filter "vcruntime140.dll" `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64\\Microsoft\.VC\d+\.CRT$' }
    } | Sort-Object {
        # Not FileVersion -- that display string can carry a build-lab suffix that [version] cannot parse.
        $v = $_.VersionInfo
        [version]::new($v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart)
    } -Descending | Select-Object -First 1
    if ($crt) {
        Copy-Item $crt.FullName -Destination $StageDir -Force
        Write-Host "  vcruntime140.dll $($crt.VersionInfo.FileVersion)"
    }
}

$zip = Join-Path $StageDir "zet.zip"
$url = "https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/$ZetVersion/" +
       "ziti-edge-tunnel-Windows_x86_64.zip"
Write-Host "  downloading ziti-edge-tunnel $ZetVersion"
Invoke-WebRequest -Uri $url -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $StageDir -Force
Remove-Item $zip -Force

$zet = Join-Path $StageDir "ziti-edge-tunnel.exe"
$openssl = Join-Path $StageDir "openssl.exe"
$fips = Join-Path $StageDir "fips.dll"
Write-Host "  staged in $StageDir"

#--------------------------------------------------------------------------------------------------------------
Write-Step "1. the tunneler's OpenSSL core"

$version = & $zet version -v 2>&1 | Out-String
Write-Host $version.Trim()
Assert-Result ($version -match "OpenSSL 3\.") "tunneler reports an OpenSSL 3.x core"
Assert-Result ($version -notmatch "\[FIPS\]") "tunneler is NOT in FIPS mode before configuration"

#--------------------------------------------------------------------------------------------------------------
Write-Step "2. fipsinstall (self-tests and integrity MAC)"

$cnfModule = Join-Path $StageDir "fipsmodule.cnf"
$out = & $openssl fipsinstall -pedantic -out $cnfModule -module $fips 2>&1 | Out-String
Write-Host $out.Trim()
Assert-Result ($LASTEXITCODE -eq 0 -and (Test-Path $cnfModule)) "openssl fipsinstall succeeded" $out

#--------------------------------------------------------------------------------------------------------------
Write-Step "3. provider list under a FIPS-only configuration"

# base + fips only. Activating 'default' would leave every non-approved algorithm reachable, which is not a
# FIPS configuration even though the provider loaded.
$appdir = $StageDir -replace '\\', '/'
$cnf = Join-Path $StageDir "openssl.cnf"
@"
openssl_conf = openssl_init

.include "$appdir/fipsmodule.cnf"

[openssl_init]
providers = provider_sect
alg_section = algorithm_sect

[provider_sect]
base = base_sect
fips = fips_sect

[base_sect]
activate = 1

[fips_sect]
module = $appdir/fips.dll

[algorithm_sect]
default_properties = fips=yes
"@ | Set-Content -Path $cnf -Encoding ASCII

# 'openssl list' has no -config option; the config comes from OPENSSL_CONF.
$env:OPENSSL_CONF = $cnf
$providers = & $openssl list -providers 2>&1 | Out-String
Write-Host $providers.Trim()
Assert-Result ($providers -match "(?s)fips.*?status:\s*active") "fips provider is active"
Assert-Result ($providers -match "3\.1\.2") "fips provider reports version 3.1.2"
Assert-Result ($providers -notmatch "OpenSSL Default Provider") "default provider is NOT active"

#--------------------------------------------------------------------------------------------------------------
Write-Step "4. THE TEST: MSVC-built fips.dll inside the mingw-built tunneler"

<#
    'version -v' calls default_tls_context() and prints straight away. The openssl.cnf resolution and
    tlsuv_set_config_path() live in the run path, after command dispatch, so the config sitting beside the
    exe is not consulted by this subcommand. Production uses that path; this test cannot.

    What still works: tls_lib_version() reports [FIPS] from
    EVP_default_properties_is_fips_enabled(global_ctx), and with global_ctx unset that queries the DEFAULT
    library context, which OpenSSL auto-configures from OPENSSL_CONF. Pointing OPENSSL_CONF at the same
    config therefore exercises the same thing we care about: the tunneler's mingw-built OpenSSL 3.6.3 core
    loading our MSVC-built 3.1.2 fips.dll and running its self-tests.
#>
$env:OPENSSL_CONF = $cnf
$fipsVersion = & $zet version -v 2>&1 | Out-String
Write-Host $fipsVersion.Trim()
Assert-Result ($fipsVersion -match "\[FIPS\]") `
    "tunneler core loads the provider and reports [FIPS]" `
    "The cross-toolchain load did not take. This is the blocking result."

#--------------------------------------------------------------------------------------------------------------
Write-Step "5. negative control"

# A test that cannot fail proves nothing. Point the config at a module that is not there and confirm the
# tunneler notices, rather than silently carrying on with its built-in implementations.
$broken = Join-Path $StageDir "openssl.cnf"
(Get-Content $broken) -replace 'module = .*', "module = $appdir/does-not-exist.dll" |
    Set-Content -Path $broken -Encoding ASCII

$env:OPENSSL_CONF = $broken
$brokenOut = & $zet version -v 2>&1 | Out-String
Assert-Result ($brokenOut -notmatch "\[FIPS\]") `
    "tunneler does NOT claim FIPS when the module is missing" `
    "It reported [FIPS] with no module present, so the marker cannot be trusted as evidence."

Remove-Item Env:\OPENSSL_CONF -ErrorAction SilentlyContinue

#--------------------------------------------------------------------------------------------------------------
Write-Step "result"

Write-Host ""
Write-Host "passed: $($pass.Count)" -ForegroundColor Green
if ($fail.Count -gt 0) {
    Write-Host "failed: $($fail.Count)" -ForegroundColor Red
    $fail | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Do not start installer work until these pass." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "The MSVC-built provider loads into the tunneler's mingw-built OpenSSL core."
Write-Host ""
Write-Host "Still unproven by this test, because 'version -v' does not use it: the production path, where"
Write-Host "the tunneler resolves openssl.cnf from its own directory during 'run'. Confirm that against a"
Write-Host "real installation with fips\doc\test-plan.md, which checks the startup log line as well as [FIPS]."
Write-Host ""
Write-Host "Next: sign the artifacts, then fips\scripts\Publish-FipsProvider.ps1."
Write-Host "Staging directory left at $StageDir for inspection."
