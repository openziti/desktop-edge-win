#requires -Version 7.0
<#
    run-ui-tests.ps1

    Builds the ZDEW WPF UI in Debug, ensures Appium is reachable, runs the UITests.Appium xUnit suite, then writes
    TestResults\report.md, gallery.html and run-output.txt (the console log without the wire-protocol JSON).

    Assumes `appium` is on PATH. The script will start a background `appium`
    process if one is not already listening on the chosen port, and stop it on
    exit.

    All logic lives here, so .github/workflows/ui-tests.yml only calls this script after checkout.

    Integration tests take their binaries the way ziti-tunnel-sdk-c's tests/integration/scripts/run-ci.ps1 does:
      $env:ZET_BIN   Absolute path to a ziti-edge-tunnel.exe. CI downloads the release Installer\build.ps1 ships.
      $env:ZITI_BIN  Absolute path to a ziti.exe for the quickstart. CI resolves it with openziti's setup-cli action.
      $env:IDP_BIN   Absolute path to a dex.exe for the external auth tests. CI builds it with fetch-dex.ps1, a copy of ZET's.
#>
[CmdletBinding()]
param(
    [int]      $AppiumPort    = 4723,
    [switch]   $SkipBuild,
    [switch]   $AutoVerify,       # sets ZDEW_AUTO_VERIFY=1, which accepts new baselines
    [ValidateSet('Mock', 'IntegrationExtAuth', 'IntegrationMfa', 'Integration')]
    [string]   $Tier,             # a named slice of the suite, the same one CI runs per job, Mock when nothing is given
    [string]   $Filter,           # passthrough: --filter "<expr>" e.g. "FullyQualifiedName~Sort"
    [string[]] $Category,         # e.g. -Category MainScreen,Sort, ORed into the filter
    [string[]] $ResetBaselines,   # globs like 'Visual_*' deleted first, so -AutoVerify rewrites them
    [switch]   $OpenGallery
)

# Integration starts its own controller and ziti-edge-tunnel, which needs elevation, so Mock leaves it out. Two ZDEW
# windows can't share one desktop, so the integration tests split into tiers that each get a machine of their own.
$integrationExcludes = "FullyQualifiedName!~.ExternalAuth&FullyQualifiedName!~.LegacyAuth&FullyQualifiedName!~.Mfa&FullyQualifiedName!~.RemoveMfa"
$tierFilters = @{
    Mock               = "Category!=Integration"
    IntegrationExtAuth = "Category=Integration&FullyQualifiedName~.ExternalAuth"
    IntegrationMfa     = "Category=Integration&(FullyQualifiedName~.LegacyAuth|FullyQualifiedName~.Mfa|FullyQualifiedName~.RemoveMfa)"
    Integration        = "Category=Integration&$integrationExcludes"
}

if (@($Tier, $Filter, ($Category -join ',')).Where({ $_ }).Count -gt 1) {
    throw "Pass one of -Tier, -Filter or -Category."
}
if ($Category) { $Filter = ($Category | ForEach-Object { "Category=$_" }) -join '|' }
if (-not $Filter) {
    if (-not $Tier) { $Tier = "Mock" }
    $Filter = $tierFilters[$Tier]
}
# -Filter alone can reach an Integration test too, so only the Mock filter skips its binaries.
$runsIntegration = $Filter -ne $tierFilters.Mock

$ErrorActionPreference = "Stop"
if ($runsIntegration) {
    foreach ($variable in @("ZET_BIN", "ZITI_BIN", "IDP_BIN")) {
        $path = [Environment]::GetEnvironmentVariable($variable)
        if (-not $path) { throw "Integration tests need `$env:$variable" }
        if (-not (Test-Path -LiteralPath $path)) { throw "$variable=$path does not exist" }
    }
    Write-Host "==> ziti-edge-tunnel $(& $env:ZET_BIN version) at $($env:ZET_BIN)"
    Write-Host "==> ziti $(& $env:ZITI_BIN version) at $($env:ZITI_BIN)"
    Write-Host "==> $(& $env:IDP_BIN version | Select-Object -First 1) at $($env:IDP_BIN)"
}
$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..")
$uiTestsDir = $PSScriptRoot
$solution   = Join-Path $repoRoot "ZitiDesktopEdge.sln"
$testCsproj = Join-Path $uiTestsDir "UITests.Appium\UITests.Appium.csproj"

function Find-MSBuild {
    if (Get-Command msbuild -ErrorAction SilentlyContinue) { return "msbuild" }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        $vswhere = "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
    }
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found; install Visual Studio Build Tools" }
    $found = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $found) {
        $found = & $vswhere -latest -prerelease -products * `
            -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if (-not $found) { throw "MSBuild.exe not found via vswhere" }
    return $found
}

function Test-PortListening([int]$port) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $iar = $tcp.BeginConnect("127.0.0.1", $port, $null, $null)
        $ok  = $iar.AsyncWaitHandle.WaitOne(500)
        if ($ok -and $tcp.Connected) { $tcp.Close(); return $true }
        $tcp.Close()
    } catch {}
    return $false
}

if (-not $SkipBuild) {
    Write-Host "==> nuget restore"
    & nuget restore $solution
    if ($LASTEXITCODE -ne 0) { throw "nuget restore failed" }

    $msbuild = Find-MSBuild
    # Debug only: Release enforces single-instance (App.xaml.cs), so each test's launch would hand off to the last one.
    Write-Host "==> msbuild Debug ($msbuild)"
    & $msbuild $solution "/p:Configuration=Debug" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "msbuild failed" }
}

$appiumProc = $null
$startedAppium = $false
if (-not (Test-PortListening $AppiumPort)) {
    $appiumCmd = Get-Command appium.cmd -ErrorAction SilentlyContinue
    if (-not $appiumCmd) { $appiumCmd = Get-Command appium.exe -ErrorAction SilentlyContinue }
    if (-not $appiumCmd) {
        $any = Get-Command appium -ErrorAction SilentlyContinue
        if ($any) {
            $cmdSibling = Join-Path (Split-Path $any.Source) "appium.cmd"
            if (Test-Path $cmdSibling) {
                $appiumCmd = [pscustomobject]@{ Source = $cmdSibling }
            }
        }
    }
    if (-not $appiumCmd) {
        throw @"
'appium' not found on PATH. Install it once with:
    npm install -g appium
    appium driver install --source=npm appium-windows-driver
Then re-run this script.
"@
    }
    Write-Host "==> starting appium on port $AppiumPort ($($appiumCmd.Source))"
    $appiumProc = Start-Process -FilePath $appiumCmd.Source `
        -ArgumentList @("--base-path=/", "--port=$AppiumPort") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $uiTestsDir "appium.stdout.log") `
        -RedirectStandardError  (Join-Path $uiTestsDir "appium.stderr.log")
    $startedAppium = $true

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and -not (Test-PortListening $AppiumPort)) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-PortListening $AppiumPort)) {
        throw "Appium did not start listening on port $AppiumPort within 30s. See appium.stdout.log / appium.stderr.log."
    }
} else {
    Write-Host "==> appium already listening on $AppiumPort -- reusing"
}

# Started here and handed to every session as appium:wadUrl, because the windows driver kills a WinAppDriver it spawns
# itself when /status does not answer within a fixed 10s, which a slow first launch exceeds. Must match
# AppiumSession.WinAppDriverUrl.
$winAppDriverPort = 4724
$winAppDriverStatusUrl = "http://127.0.0.1:$winAppDriverPort/wd/hub/status"
$winAppDriverProc = $null
if (-not (Test-PortListening $winAppDriverPort)) {
    $winAppDriverExe = "${env:ProgramFiles(x86)}\Windows Application Driver\WinAppDriver.exe"
    if (-not (Test-Path -LiteralPath $winAppDriverExe)) { throw "WinAppDriver not found at $winAppDriverExe" }
    Write-Host "==> starting WinAppDriver on port $winAppDriverPort"
    $winAppDriverProc = Start-Process -FilePath $winAppDriverExe `
        -ArgumentList @("$winAppDriverPort/wd/hub") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $uiTestsDir "winappdriver.stdout.log") `
        -RedirectStandardError  (Join-Path $uiTestsDir "winappdriver.stderr.log")

    $deadline = (Get-Date).AddSeconds(60)
    $lastStatusError = $null
    while ((Get-Date) -lt $deadline) {
        if ($winAppDriverProc.HasExited) {
            throw "WinAppDriver exited with code $($winAppDriverProc.ExitCode) before answering $winAppDriverStatusUrl. See winappdriver.stdout.log / winappdriver.stderr.log."
        }
        try {
            Invoke-RestMethod -Uri $winAppDriverStatusUrl -TimeoutSec 5 | Out-Null
            $lastStatusError = $null
            break
        } catch {
            $lastStatusError = $_
            Start-Sleep -Milliseconds 500
        }
    }
    if ($lastStatusError) {
        throw "WinAppDriver did not answer $winAppDriverStatusUrl within 60s: $lastStatusError"
    }
    Write-Host "==> WinAppDriver answered $winAppDriverStatusUrl"
} else {
    Write-Host "==> WinAppDriver already listening on $winAppDriverPort -- reusing"
}

$resultsDir = Join-Path $uiTestsDir "TestResults"
if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
$trxPath = Join-Path $resultsDir "results.trx"
$galleryPath = Join-Path $resultsDir "gallery.html"
$runLogPath = Join-Path $resultsDir "run-output.txt"
$baselinesDir = Join-Path $uiTestsDir "UITests.Appium\Tests"

foreach ($pattern in $ResetBaselines) {
    Get-ChildItem (Join-Path $baselinesDir "*.$pattern.verified.png") | ForEach-Object {
        Write-Host "==> removing baseline $($_.Name)"
        Remove-Item -LiteralPath $_.FullName
    }
}

$runLog = [System.Collections.Generic.List[string]]::new()
try {
    if ($AutoVerify) { $env:ZDEW_AUTO_VERIFY = "1" }
    Write-Host "==> dotnet test $(if ($Filter) { "--filter $Filter" })"
    $dotnetTestArgs = @(
        $testCsproj,
        '--logger', 'console;verbosity=normal',
        '--logger', 'trx;LogFileName=results.trx',
        '--results-directory', $resultsDir
    )
    if ($Filter) {
        $dotnetTestArgs += @('--filter', $Filter)
    }
    & dotnet test @dotnetTestArgs 2>&1 | ForEach-Object {
        $line = "$_"
        Write-Host $line
        # The console keeps the wire-protocol JSON for diagnosis, run-output.txt drops it.
        if ($line -notmatch 'UI-DataClient-(send|read)-|ZitiDesktopEdge\.Models\.ZitiIdentity\s+Identity:') {
            $runLog.Add($line)
        }
    }
    $testExit = $LASTEXITCODE
} finally {
    # the script runs in the caller's session, so a leaked value auto-accepts baselines on every later run
    Remove-Item Env:ZDEW_AUTO_VERIFY -ErrorAction SilentlyContinue
    if ($startedAppium -and $appiumProc -and -not $appiumProc.HasExited) {
        Write-Host "==> stopping appium (pid $($appiumProc.Id))"
        try { Stop-Process -Id $appiumProc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    if ($winAppDriverProc -and -not $winAppDriverProc.HasExited) {
        Write-Host "==> stopping WinAppDriver (pid $($winAppDriverProc.Id))"
        Stop-Process -Id $winAppDriverProc.Id -Force
    }
}

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$runLog -join "`n" | Set-Content -LiteralPath $runLogPath -Encoding utf8
# Appium starts before TestResults is wiped, so its log is copied in afterwards. It holds WinAppDriver's own error
# text, which Selenium reduces to "An unknown error occurred in the remote end".
if ($startedAppium) {
    $appiumLogDir = Join-Path $resultsDir "logs"
    New-Item -ItemType Directory -Force -Path $appiumLogDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $uiTestsDir "appium.stdout.log"), (Join-Path $uiTestsDir "appium.stderr.log") -Destination $appiumLogDir
}
if ($winAppDriverProc) {
    $winAppDriverLogDir = Join-Path $resultsDir "logs"
    New-Item -ItemType Directory -Force -Path $winAppDriverLogDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $uiTestsDir "winappdriver.stdout.log"), (Join-Path $uiTestsDir "winappdriver.stderr.log") -Destination $winAppDriverLogDir
}

# Copied into TestResults so the gallery has no ..\ paths and works from GitHub Pages.
$galleryBaselineDir = Join-Path $resultsDir "baselines"
New-Item -ItemType Directory -Force -Path $galleryBaselineDir | Out-Null
if (Test-Path $baselinesDir) {
    Get-ChildItem $baselinesDir -Filter "*.verified.png" -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $galleryBaselineDir -Force }
    Get-ChildItem $baselinesDir -Filter "*.received.png" -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $galleryBaselineDir -Force }
}

$tierLabel = if ($Tier) { $Tier } else { $Filter }
& (Join-Path $uiTestsDir "write-ui-test-report.ps1") -ResultsDir $resultsDir -Tiers ([ordered]@{ $tierLabel = "." }) `
    -GalleryUrl "gallery.html"
Write-Host "==> trx:     $trxPath"
Write-Host "==> log:     $runLogPath"
if ($OpenGallery) { Start-Process $galleryPath }

exit $testExit
