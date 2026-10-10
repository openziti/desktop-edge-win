#requires -Version 7.0
<#
    write-ui-test-report.ps1

    Writes report.md, gallery.html and index.html into ResultsDir from one or more tiers' results, each a directory
    under ResultsDir laid out the way run-ui-tests.ps1 leaves TestResults (results.trx, screenshots\, baselines\).
    run-ui-tests.ps1 passes its own TestResults as the only tier. The workflow's report job passes every tier's
    downloaded artifact, so one report and one gallery cover the whole run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsDir,
    # Tier name to its directory relative to ResultsDir, in display order. A tier without results.trx is reported as
    # missing, since its job never got as far as uploading one.
    [Parameter(Mandatory)] [System.Collections.Specialized.OrderedDictionary] $Tiers,
    # Where the gallery will be served, absolute or relative to report.md. Each test row in report.md links to its
    # card there. Empty when nothing serves the gallery, and then the rows carry no links.
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $GalleryUrl
)

$ErrorActionPreference = "Stop"

class UiTestResult {
    [string] $Tier
    [string] $Dir
    [string] $Class
    [string] $Name
    [string] $Outcome
    [string] $Duration
    [string] $Message
    [string] $Stack
}

function Read-TierResults([string] $Tier, [string] $Dir, [string] $TrxPath) {
    [xml]$trx = Get-Content -LiteralPath $TrxPath
    $ns = New-Object System.Xml.XmlNamespaceManager $trx.NameTable
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    [UiTestResult[]]$results = foreach ($r in $trx.SelectNodes("//t:UnitTestResult", $ns)) {
        # testName like "ZitiDesktopEdge.UITests.Tests.SmokeTests.MainWindow_LaunchesAndRenders"
        [string[]]$parts = $r.testName -split '\.'
        $message = $r.SelectSingleNode("t:Output/t:ErrorInfo/t:Message", $ns)
        $stack = $r.SelectSingleNode("t:Output/t:ErrorInfo/t:StackTrace", $ns)
        [UiTestResult]@{
            Tier     = $Tier
            Dir      = $Dir
            Class    = $parts[-2]
            Name     = $parts[-1]
            Outcome  = $r.outcome
            Duration = $r.duration
            Message  = if ($message) { $message.InnerText.Trim() } else { "" }
            Stack    = if ($stack) { $stack.InnerText.Trim() } else { "" }
        }
    }
    return $results
}

function Get-CardId([UiTestResult] $Result) {
    return "$($Result.Tier)-$($Result.Class)-$($Result.Name)"
}

function Format-TestCell([UiTestResult] $Result, [string] $GalleryUrl) {
    $label = "``$($Result.Class).$($Result.Name)``"
    if (-not $GalleryUrl) { return $label }
    return "[$label]($GalleryUrl#$(Get-CardId $Result))"
}

function Format-Report([string[]] $TierNames, [string[]] $MissingTiers, [UiTestResult[]] $Results, [string] $GalleryUrl) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# UI Tests Report")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| Tier | Passed | Failed | Skipped | Total |")
    [void]$sb.AppendLine("| --- | --- | --- | --- | --- |")
    foreach ($tier in $TierNames) {
        if ($MissingTiers -contains $tier) {
            [void]$sb.AppendLine("| $tier | no results.trx | | | |")
            continue
        }
        [UiTestResult[]]$tierResults = $Results | Where-Object Tier -eq $tier
        $passed = @($tierResults | Where-Object Outcome -eq 'Passed').Count
        $failed = @($tierResults | Where-Object Outcome -eq 'Failed').Count
        [void]$sb.AppendLine("| $tier | $passed | $failed | $($tierResults.Count - $passed - $failed) | $($tierResults.Count) |")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| Test | Tier | Outcome | Duration |")
    [void]$sb.AppendLine("| --- | --- | --- | --- |")
    $sorted = $Results | Sort-Object @{ Expression = { if ($_.Outcome -eq 'Failed') { 0 } else { 1 } } },
        @{ Expression = { [array]::IndexOf($TierNames, $_.Tier) } }, Class, Name
    foreach ($r in $sorted) {
        $icon = switch ($r.Outcome) {
            "Passed"      { "PASS" }
            "Failed"      { "FAIL" }
            "NotExecuted" { "SKIP" }
            default       { $r.Outcome }
        }
        [void]$sb.AppendLine("| $(Format-TestCell $r $GalleryUrl) | $($r.Tier) | $icon | $($r.Duration) |")
    }

    [UiTestResult[]]$failures = $sorted | Where-Object Outcome -eq 'Failed'
    if ($failures.Count -gt 0) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("## Failures")
        foreach ($r in $failures) {
            [void]$sb.AppendLine()
            [void]$sb.AppendLine("### $($r.Tier): $($r.Class).$($r.Name)")
            if ($r.Message) {
                [void]$sb.AppendLine()
                [void]$sb.AppendLine('```')
                [void]$sb.AppendLine($r.Message)
                [void]$sb.AppendLine('```')
            }
            if ($r.Stack) {
                [void]$sb.AppendLine()
                [void]$sb.AppendLine('<details><summary>stack</summary>')
                [void]$sb.AppendLine()
                [void]$sb.AppendLine('```')
                [void]$sb.AppendLine($r.Stack)
                [void]$sb.AppendLine('```')
                [void]$sb.AppendLine('</details>')
            }
        }
    }
    return $sb.ToString()
}

function Format-Gallery([string] $ResultsDir, [string[]] $TierNames, [string[]] $MissingTiers, [UiTestResult[]] $Results) {
    $cards = New-Object System.Text.StringBuilder
    foreach ($t in ($Results | Sort-Object @{ Expression = { [array]::IndexOf($TierNames, $_.Tier) } }, Class, Name)) {
        $tierDir = Join-Path $ResultsDir $t.Dir
        $baselineName = "$($t.Class).$($t.Name).verified.png"
        $receivedName = "$($t.Class).$($t.Name).received.png"
        $hasBaseline = Test-Path -LiteralPath (Join-Path $tierDir "baselines" $baselineName)
        $hasReceived = Test-Path -LiteralPath (Join-Path $tierDir "baselines" $receivedName)
        $hasLatest = Test-Path -LiteralPath (Join-Path $tierDir "screenshots" "$($t.Name).png")

        $outcomeBadge = if ($t.Outcome -eq 'Passed') { 'pass' } elseif ($t.Outcome -eq 'Failed') { 'fail' } else { 'other' }
        $kind = if ($hasBaseline) { "visual" } else { "assertion" }
        $visualBadge = if ($hasReceived) { "MISMATCH" } else { $kind }

        # Multi-step screenshots live under screenshots\<TestName>\*.png
        $stepDir = Join-Path $tierDir "screenshots" $t.Name
        $stepFiles = @()
        if (Test-Path -LiteralPath $stepDir) {
            $stepFiles = Get-ChildItem -LiteralPath $stepDir -Filter "*.png" -File | Sort-Object Name
        }

        [void]$cards.AppendLine("<section class=`"card`" id=`"$(Get-CardId $t)`">")
        [void]$cards.AppendLine("  <h2><span class=`"cls`">$($t.Tier) &raquo; $($t.Class)</span> &raquo; $($t.Name) <span class=`"badge $outcomeBadge`">$($t.Outcome)</span> <span class=`"badge kind-$visualBadge`">$visualBadge</span> <span class=`"dur`">$($t.Duration)</span></h2>")

        if ($stepFiles.Count -gt 0) {
            [void]$cards.AppendLine("  <div class=`"row strip`">")
            foreach ($sf in $stepFiles) {
                [void]$cards.AppendLine("    <figure><figcaption>$($sf.BaseName)</figcaption><img src=`"$($t.Dir)/screenshots/$($t.Name)/$($sf.Name)`" /></figure>")
            }
            [void]$cards.AppendLine("  </div>")
        } elseif ($hasBaseline -or $hasLatest) {
            [void]$cards.AppendLine("  <div class=`"row`">")
            if ($hasBaseline) {
                [void]$cards.AppendLine("    <figure><figcaption>baseline</figcaption><img src=`"$($t.Dir)/baselines/$baselineName`" /></figure>")
            }
            if ($hasLatest) {
                [void]$cards.AppendLine("    <figure><figcaption>latest run</figcaption><img src=`"$($t.Dir)/screenshots/$($t.Name).png`" /></figure>")
            }
            if ($hasReceived) {
                [void]$cards.AppendLine("    <figure><figcaption>.received.png (rejected)</figcaption><img src=`"$($t.Dir)/baselines/$receivedName`" /></figure>")
            }
            [void]$cards.AppendLine("  </div>")
        } else {
            [void]$cards.AppendLine("  <div class=`"none`">(assertion-only test, no screenshot)</div>")
        }
        [void]$cards.AppendLine("</section>")
    }

    $missing = if ($MissingTiers.Count -gt 0) { " No results.trx from: $($MissingTiers -join ', ')." } else { "" }
    return @"
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
<div class="meta">Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'). $($Results.Count) test(s) total.$missing Side-by-side: <b>baseline</b> is the accepted .verified.png; <b>latest run</b> is the screenshot from this run.</div>
$($cards.ToString())
</body></html>
"@
}

[string[]]$tierNames = @($Tiers.Keys)
[string[]]$missingTiers = @()
[UiTestResult[]]$results = @()
foreach ($tier in $tierNames) {
    $dir = [string]$Tiers[$tier]
    $trxPath = Join-Path $ResultsDir $dir "results.trx"
    if (Test-Path -LiteralPath $trxPath) {
        $results += Read-TierResults -Tier $tier -Dir $dir -TrxPath $trxPath
    } else {
        $missingTiers += $tier
    }
}

$reportPath = Join-Path $ResultsDir "report.md"
$galleryPath = Join-Path $ResultsDir "gallery.html"
Format-Report -TierNames $tierNames -MissingTiers $missingTiers -Results $results -GalleryUrl $GalleryUrl |
    Set-Content -LiteralPath $reportPath -Encoding utf8
Format-Gallery -ResultsDir $ResultsDir -TierNames $tierNames -MissingTiers $missingTiers -Results $results |
    Set-Content -LiteralPath $galleryPath -Encoding utf8
# index.html so GitHub Pages serves the gallery at the site root
Copy-Item -LiteralPath $galleryPath -Destination (Join-Path $ResultsDir "index.html") -Force
Write-Host "==> report:  $reportPath"
Write-Host "==> gallery: $galleryPath"
