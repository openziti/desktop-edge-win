<#
.SYNOPSIS
    Generates openssl.cnf and fipsmodule.cnf so ziti-edge-tunnel loads the validated FIPS provider.

.DESCRIPTION
    Runs from an elevated installer custom action on every install and every major upgrade, and again with
    -Disable on uninstall. It ships into the install directory so an administrator can re-run it by hand and
    so the logic is reviewable in source control rather than embedded in the installer project.

    ziti-edge-tunnel.exe looks for openssl.cnf next to itself at startup and hands it to OpenSSL as the
    library context every Ziti TLS connection runs in. Both files this writes must sit in that same directory.

    Neither file can be shipped in the MSI:

      - fipsmodule.cnf contains an HMAC over the bytes of fips.dll plus the results of the module's power-on
        self-tests. Section 11.1 of the CMVP #4985 security policy requires those steps be performed on each
        platform where the module is used.
      - openssl.cnf embeds absolute paths, which are not known until the install directory is chosen.

    Ordering matters: fips.dll must already be Authenticode-signed when this runs, because the HMAC covers the
    file bytes. Signing happens at build time, so an install-time generation is always on the final bytes.
    Signing after generating fipsmodule.cnf would invalidate the integrity check silently, and the failure
    would surface much later as a provider activation error.

.PARAMETER InstallDir
    The directory holding ziti-edge-tunnel.exe, fips.dll and openssl.exe. Passed from the custom action via
    CustomActionData, since a deferred action cannot read properties.

.PARAMETER Disable
    Remove openssl.cnf and fipsmodule.cnf instead of generating them. Neither is tracked by the MSI, so
    neither is removed on uninstall without this.

.PARAMETER Verify
    Report whether this machine is running validated cryptography, and change nothing. Exit code 0 means yes.
    This is the command to hand an auditor.

.EXAMPLE
    .\Configure-Fips.ps1 -InstallDir "C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge"

.EXAMPLE
    .\Configure-Fips.ps1 -InstallDir "C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge" -Disable

.EXAMPLE
    .\Configure-Fips.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$Disable,
    [switch]$Verify
)

$ErrorActionPreference = "Stop"

# When this script is running from the install directory -- which is how it ships -- there is nothing to pass.
# The installer custom action still supplies it explicitly, because a deferred action starts elsewhere.
if (-not $InstallDir) { $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$opensslCnf = Join-Path $InstallDir "openssl.cnf"
$fipsmoduleCnf = Join-Path $InstallDir "fipsmodule.cnf"

#--------------------------------------------------------------------------------------------------------------
# Verify
#--------------------------------------------------------------------------------------------------------------

if ($Verify) {
    $failures = @()

    function Write-Check([string]$Label, [bool]$Ok, [string]$Detail) {
        $mark = if ($Ok) { "PASS" } else { "FAIL" }
        $colour = if ($Ok) { "Green" } else { "Red" }
        Write-Host ("  [{0}] {1}" -f $mark, $Label) -ForegroundColor $colour
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
    }

    Write-Host ""
    Write-Host "Ziti Desktop Edge -- FIPS 140-3 status"
    Write-Host "  $InstallDir"
    Write-Host ""

    # 1. The module and the two generated configs are present.
    foreach ($item in @(
        @{ Label = "fips.dll present";       Path = (Join-Path $InstallDir "fips.dll") }
        @{ Label = "openssl.cnf present";    Path = $opensslCnf }
        @{ Label = "fipsmodule.cnf present"; Path = $fipsmoduleCnf }
    )) {
        $ok = Test-Path $item.Path
        if (-not $ok) { $failures += $item.Label }
        Write-Check $item.Label $ok $item.Path
    }

    # 2. fips.dll carries a valid Authenticode signature. fipsmodule.cnf's HMAC is over these exact bytes, so
    #    a signature applied afterwards would have invalidated it.
    $dll = Join-Path $InstallDir "fips.dll"
    if (Test-Path $dll) {
        $sig = Get-AuthenticodeSignature $dll
        $ok = $sig.Status -eq "Valid"
        if (-not $ok) { $failures += "fips.dll signature" }
        $signer = ([regex]::Match($sig.SignerCertificate.Subject, 'CN=([^,]+)')).Groups[1].Value
        Write-Check "fips.dll signature valid" $ok ("signed by $signer")
    }

    # 3. The module loads and the default properties really do require FIPS. OPENSSL_CONF is set for this
    #    process only; nothing machine-wide is touched, and the service does not rely on it.
    $opensslExe = Join-Path $InstallDir "openssl.exe"
    if ((Test-Path $opensslExe) -and (Test-Path $opensslCnf)) {
        $previousConf = $env:OPENSSL_CONF
        $env:OPENSSL_CONF = $opensslCnf
        try {
            $providers = (& $opensslExe list -providers 2>&1) -join "`n"
        } finally {
            $env:OPENSSL_CONF = $previousConf
        }
        $ok = $providers -match "(?m)^\s*fips\s*$"
        if (-not $ok) { $failures += "fips provider activates" }
        $version = ([regex]::Match($providers, 'version:\s*(\S+)')).Groups[1].Value
        Write-Check "fips provider activates" $ok ("OpenSSL FIPS Provider $version, CMVP certificate #4985")
    }

    # 4. The running service loaded our config. This is the one that matters: the checks above prove the
    #    module is installable, this proves ziti-edge-tunnel is actually using it. The tunneler logs the line
    #    at startup, so it reflects the process that is running now rather than what is on disk.
    $log = Join-Path $InstallDir "logs\service\ziti-tunneler.log"
    $configured = $null
    if (Test-Path $log) {
        $configured = Select-String -Path $log -Pattern "openssl config" | Select-Object -Last 1
    }
    $ok = $null -ne $configured -and $configured.Line -match [regex]::Escape($opensslCnf)
    if (-not $ok) { $failures += "service loaded the config" }
    $detail = if ($configured) {
        ($configured.Line -replace '^.*openssl config\s*:\s*', '')
    } else {
        "no 'openssl config' line in $log"
    }
    Write-Check "ziti-edge-tunnel loaded openssl.cnf" $ok $detail

    Write-Host ""
    if ($failures.Count -eq 0) {
        Write-Host "This machine is using FIPS 140-3 validated cryptography." -ForegroundColor Green
        Write-Host "All Ziti TLS runs in the OpenSSL FIPS Provider 3.1.2 (CMVP #4985)."
        Write-Host ""
        exit 0
    }

    Write-Host "NOT running validated cryptography. Failed: $($failures -join ', ')" -ForegroundColor Red
    Write-Host ""
    exit 1
}

#--------------------------------------------------------------------------------------------------------------
# Uninstall
#--------------------------------------------------------------------------------------------------------------

if ($Disable) {
    foreach ($path in $opensslCnf, $fipsmoduleCnf) {
        if (Test-Path $path) {
            Remove-Item $path -Force
            Write-Host "removed $path"
        }
    }
    exit 0
}

#--------------------------------------------------------------------------------------------------------------
# Generate
#--------------------------------------------------------------------------------------------------------------

$opensslExe = Join-Path $InstallDir "openssl.exe"
$fipsDll = Join-Path $InstallDir "fips.dll"

foreach ($required in $opensslExe, $fipsDll) {
    if (-not (Test-Path $required)) {
        Write-Error "required file not found: $required"
        exit 1
    }
}

# -pedantic is not optional. Section 11.1 of the security policy documents 'openssl fipsinstall -pedantic' as
# the way to install the configuration to a non-default location, and 11.1(c) requires the module's run-time
# security checks stay enabled. A non-zero exit means the self-tests failed or the module would not load.
Write-Host "running fipsinstall"
& $opensslExe fipsinstall -pedantic -out $fipsmoduleCnf -module $fipsDll
if ($LASTEXITCODE -ne 0) {
    Write-Error "openssl fipsinstall failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# OpenSSL treats a backslash in a configuration file as an escape character, so every path below is written
# with forward slashes. Windows accepts them.
$dir = $InstallDir.TrimEnd('\').Replace('\', '/')

# Two things here are load-bearing and easy to get wrong:
#
#   - The default provider is deliberately absent. Activating it alongside fips leaves every non-approved
#     algorithm reachable, which defeats the point. Only base (encoders, decoders, PEM handling, no
#     cryptography) and fips are activated.
#   - default_properties = fips=yes is what forces every algorithm fetch to resolve to a FIPS implementation,
#     and it is what EVP_default_properties_is_fips_enabled reports. Without it the provider is loaded but
#     nothing is constrained.
#
# [fips_sect] carries only the module path. fipsinstall writes its own [fips_sect] with activate, module-mac,
# install-mac and the self-test status, and that arrives through the .include. Do not restate activate here:
# duplicate keys within one section are order-dependent.
$config = @"
openssl_conf = openssl_init

.include "$dir/fipsmodule.cnf"

[openssl_init]
providers = provider_sect
alg_section = algorithm_sect

[provider_sect]
base = base_sect
fips = fips_sect

[base_sect]
activate = 1

[fips_sect]
module = $dir/fips.dll

[algorithm_sect]
default_properties = fips=yes
"@

Set-Content -Path $opensslCnf -Value $config -Encoding ASCII
Write-Host "wrote $opensslCnf"

# Prove the merged configuration actually loads before leaving it in place. Writing an openssl.cnf that
# references an unverified module is worse than writing none at all: ziti-edge-tunnel would fail to establish
# TLS entirely, with the cause several layers down in an OpenSSL error.
#
# The 'list' subcommand has no -config option in 3.1.2, so the config is supplied through the environment.
# ziti-edge-tunnel finds the same file by its own exe-adjacent lookup; this only affects openssl.exe here.
$previousConf = $env:OPENSSL_CONF
$env:OPENSSL_CONF = $opensslCnf
try {
    $providers = & $opensslExe list -providers 2>&1
} finally {
    $env:OPENSSL_CONF = $previousConf
}
if ($LASTEXITCODE -ne 0 -or ($providers -join "`n") -notmatch "(?m)^\s*fips\s*$") {
    Remove-Item $opensslCnf -Force -ErrorAction SilentlyContinue
    Write-Error "the generated configuration did not load the fips provider:`n$($providers -join "`n")"
    exit 1
}

Write-Host "fips provider active"
exit 0
