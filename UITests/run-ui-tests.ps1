#requires -Version 7.0
<#
    run-ui-tests.ps1

    Builds the ZDEW WPF UI in Debug, ensures Appium is reachable, then runs the
    UITests.Appium xUnit suite.

    Assumes `appium` is on PATH. The script will start a background `appium`
    process if one is not already listening on the chosen port, and stop it on
    exit.

    All logic lives here so a future GitHub Actions workflow can simply
    `pwsh -File UITests\run-ui-tests.ps1` after checkout.
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Debug",
    [int]    $AppiumPort    = 4723,
    [switch] $SkipBuild,
    [switch] $AutoVerify,   # passthrough: set ZDEW_AUTO_VERIFY=1 -- accepts new baselines
    [string] $Filter        # passthrough: --filter "<expr>" e.g. "Category=Mfa"
)

$ErrorActionPreference = "Stop"
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
    Write-Host "==> msbuild $Configuration ($msbuild)"
    & $msbuild $solution "/p:Configuration=$Configuration" /v:minimal /nologo
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

$resultsDir = Join-Path $uiTestsDir "TestResults"
if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
$trxPath = Join-Path $resultsDir "results.trx"
$reportPath = Join-Path $resultsDir "report.md"
$galleryPath = Join-Path $resultsDir "gallery.html"
$baselinesDir = Join-Path $uiTestsDir "UITests.Appium\Tests"
$screenshotsDir = Join-Path $resultsDir "screenshots"

try {
    if ($AutoVerify) { $env:ZDEW_AUTO_VERIFY = "1" }
    Write-Host "==> dotnet test"
    $dotnetTestArgs = @(
        $testCsproj,
        '--logger', 'console;verbosity=normal',
        '--logger', 'trx;LogFileName=results.trx',
        '--results-directory', $resultsDir
    )
    if ($Filter) {
        $dotnetTestArgs += @('--filter', $Filter)
    }
    & dotnet test @dotnetTestArgs
    $testExit = $LASTEXITCODE
} finally {
    if ($startedAppium -and $appiumProc -and -not $appiumProc.HasExited) {
        Write-Host "==> stopping appium (pid $($appiumProc.Id))"
        try { Stop-Process -Id $appiumProc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
}

# Generate markdown report from TRX
if (Test-Path $trxPath) {
    try {
        [xml]$trx = Get-Content -LiteralPath $trxPath
        $ns = New-Object System.Xml.XmlNamespaceManager $trx.NameTable
        $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

        $counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $ns)
        $total   = [int]$counters.total
        $passed  = [int]$counters.passed
        $failed  = [int]$counters.failed

        $sb = [System.Text.StringBuilder]::new()
        [void]$sb.AppendLine("# UI Tests Report")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("**Summary:** $passed / $total passed, $failed failed")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("| Outcome | Duration | Test |")
        [void]$sb.AppendLine("| --- | --- | --- |")

        $results = $trx.SelectNodes("//t:UnitTestResult", $ns) | `
            Sort-Object @{Expression={ if ($_.outcome -eq 'Failed') { 0 } else { 1 } }}, testName

        foreach ($r in $results) {
            $icon = switch ($r.outcome) {
                "Passed"     { "PASS" }
                "Failed"     { "FAIL" }
                "NotExecuted"{ "SKIP" }
                default      { $r.outcome }
            }
            $name = $r.testName -replace '^[^.]+\.', ''
            [void]$sb.AppendLine("| $icon | $($r.duration) | ``$name`` |")
        }

        $failedResults = $trx.SelectNodes("//t:UnitTestResult[@outcome='Failed']", $ns)
        if ($failedResults.Count -gt 0) {
            [void]$sb.AppendLine()
            [void]$sb.AppendLine("## Failures")
            foreach ($r in $failedResults) {
                [void]$sb.AppendLine()
                [void]$sb.AppendLine("### $($r.testName)")
                $msg = $r.SelectSingleNode("t:Output/t:ErrorInfo/t:Message", $ns)
                $stk = $r.SelectSingleNode("t:Output/t:ErrorInfo/t:StackTrace", $ns)
                if ($msg) {
                    [void]$sb.AppendLine()
                    [void]$sb.AppendLine('```')
                    [void]$sb.AppendLine($msg.InnerText.Trim())
                    [void]$sb.AppendLine('```')
                }
                if ($stk) {
                    [void]$sb.AppendLine()
                    [void]$sb.AppendLine('<details><summary>stack</summary>')
                    [void]$sb.AppendLine()
                    [void]$sb.AppendLine('```')
                    [void]$sb.AppendLine($stk.InnerText.Trim())
                    [void]$sb.AppendLine('```')
                    [void]$sb.AppendLine('</details>')
                }
            }
        }

        $sb.ToString() | Set-Content -LiteralPath $reportPath -Encoding utf8
        Write-Host ""
        Write-Host "==> report: $reportPath"
        Write-Host "==> trx:    $trxPath"
    } catch {
        Write-Warning "Failed to generate markdown report: $_"
    }
}

# Copy baseline + received PNGs into TestResults\baselines\ so the gallery is
# self-contained and can be served from GitHub Pages (no relative ..\paths).
$galleryBaselineDir = Join-Path $resultsDir "baselines"
New-Item -ItemType Directory -Force -Path $galleryBaselineDir | Out-Null
if (Test-Path $baselinesDir) {
    Get-ChildItem $baselinesDir -Filter "*.verified.png" -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $galleryBaselineDir -Force }
    Get-ChildItem $baselinesDir -Filter "*.received.png" -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $galleryBaselineDir -Force }
}

# Generate visual gallery HTML
try {
    # Pull EVERY test from the TRX so assertion-only tests are visible too
    $allTests = @()
    if (Test-Path $trxPath) {
        [xml]$trx2 = Get-Content -LiteralPath $trxPath
        $ns2 = New-Object System.Xml.XmlNamespaceManager $trx2.NameTable
        $ns2.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        foreach ($r in $trx2.SelectNodes("//t:UnitTestResult", $ns2)) {
            # testName like "ZitiDesktopEdge.UITests.Tests.SmokeTests.MainWindow_LaunchesAndRenders"
            $full = $r.testName
            $short = ($full -split '\.')[-1]
            $cls = ($full -split '\.')[-2]
            $allTests += [pscustomobject]@{
                FullName = $full
                Name     = $short
                Class    = $cls
                Outcome  = $r.outcome
                Duration = $r.duration
            }
        }
    }

    $cards = New-Object System.Text.StringBuilder
    foreach ($t in ($allTests | Sort-Object Class, Name)) {
        $baselineFile = Join-Path $baselinesDir "$($t.Class).$($t.Name).verified.png"
        $receivedFile = Join-Path $baselinesDir "$($t.Class).$($t.Name).received.png"
        $latestFile   = Join-Path $screenshotsDir "$($t.Name).png"
        $hasBaseline = Test-Path -LiteralPath $baselineFile
        $hasReceived = Test-Path -LiteralPath $receivedFile
        $hasLatest   = Test-Path -LiteralPath $latestFile

        $outcomeBadge = if ($t.Outcome -eq 'Passed') { 'pass' } elseif ($t.Outcome -eq 'Failed') { 'fail' } else { 'other' }
        $kind = if ($hasBaseline) { "visual" } else { "assertion" }
        $visualBadge = if ($hasReceived) { "MISMATCH" } else { $kind }

        # Multi-step screenshots live under screenshots\<TestName>\*.png
        $stepDir = Join-Path $screenshotsDir $t.Name
        $stepFiles = @()
        if (Test-Path -LiteralPath $stepDir) {
            $stepFiles = Get-ChildItem -LiteralPath $stepDir -Filter "*.png" -File | Sort-Object Name
        }

        [void]$cards.AppendLine("<section class=`"card`">")
        [void]$cards.AppendLine("  <h2><span class=`"cls`">$($t.Class)</span> &raquo; $($t.Name) <span class=`"badge $outcomeBadge`">$($t.Outcome)</span> <span class=`"badge kind-$visualBadge`">$visualBadge</span> <span class=`"dur`">$($t.Duration)</span></h2>")

        if ($stepFiles.Count -gt 0) {
            [void]$cards.AppendLine("  <div class=`"row strip`">")
            foreach ($sf in $stepFiles) {
                $stepRel = "screenshots/$($t.Name)/$($sf.Name)"
                $stepCap = $sf.BaseName
                [void]$cards.AppendLine("    <figure><figcaption>$stepCap</figcaption><img src=`"$stepRel`" /></figure>")
            }
            [void]$cards.AppendLine("  </div>")
        } elseif ($hasBaseline -or $hasLatest) {
            [void]$cards.AppendLine("  <div class=`"row`">")
            if ($hasBaseline) {
                $brel = "baselines/$([System.IO.Path]::GetFileName($baselineFile))"
                [void]$cards.AppendLine("    <figure><figcaption>baseline</figcaption><img src=`"$brel`" /></figure>")
            }
            if ($hasLatest) {
                [void]$cards.AppendLine("    <figure><figcaption>latest run</figcaption><img src=`"screenshots/$($t.Name).png`" /></figure>")
            }
            if ($hasReceived) {
                $rrel = "baselines/$([System.IO.Path]::GetFileName($receivedFile))"
                [void]$cards.AppendLine("    <figure><figcaption>.received.png (rejected)</figcaption><img src=`"$rrel`" /></figure>")
            }
            [void]$cards.AppendLine("  </div>")
        } else {
            [void]$cards.AppendLine("  <div class=`"none`">(assertion-only test, no screenshot)</div>")
        }
        [void]$cards.AppendLine("</section>")
    }

    $html = @"
<!doctype html><html><head><meta charset="utf-8"><title>ZDEW UI Tests Gallery</title>
<style>
  body { font-family: -apple-system, Segoe UI, system-ui, sans-serif; background:#1e1e2e; color:#e6e6f0; margin:0; padding:24px; }
  h1 { margin:0 0 8px 0; }
  .meta { color:#9899ad; margin-bottom:24px; font-size:13px; }
  .card { background:#27293b; border:1px solid #3a3c54; border-radius:10px; padding:16px; margin-bottom:20px; }
  .card h2 { margin:0 0 12px 0; font-size:16px; font-weight:600; }
  .row { display:flex; gap:16px; flex-wrap:wrap; }
  .row.strip img { max-height:380px; }
  figure { margin:0; }
  figcaption { font-size:12px; color:#9899ad; margin-bottom:6px; }
  img { max-width:100%; max-height:520px; border:1px solid #3a3c54; border-radius:6px; background:#000; }
  .badge { font-size:11px; padding:2px 8px; border-radius:10px; margin-left:8px; vertical-align:middle; }
  .badge.pass { background:#163a1f; color:#7ee787; }
  .badge.fail { background:#4a1c1c; color:#ff7b7b; }
  .badge.other { background:#3a3a4a; color:#bdbdcc; }
  .badge.kind-visual { background:#1a2a4a; color:#9fc4ff; }
  .badge.kind-assertion { background:#2a2a3a; color:#bdbdcc; }
  .badge.kind-MISMATCH { background:#4a1c1c; color:#ff7b7b; }
  .cls { color:#9899ad; font-weight:400; font-size:13px; }
  .dur { color:#7a7b8e; font-weight:400; font-size:12px; float:right; }
  .none { color:#7a7b8e; font-style:italic; font-size:13px; padding:8px 0; }
</style></head><body>
<h1>ZDEW UI Tests Gallery</h1>
<div class="meta">Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'). $($allTests.Count) test(s) total. Side-by-side: <b>baseline</b> is the accepted .verified.png; <b>latest run</b> is the screenshot from this run.</div>
$($cards.ToString())
</body></html>
"@
    $html | Set-Content -LiteralPath $galleryPath -Encoding utf8
    # Also write an index.html alias so GitHub Pages serves the gallery as the
    # site root without an explicit ?file=gallery.html
    Copy-Item -LiteralPath $galleryPath -Destination (Join-Path $resultsDir "index.html") -Force
    Write-Host "==> gallery: $galleryPath"
} catch {
    Write-Warning "Failed to generate gallery: $_"
}

exit $testExit
