# Verifies that every browser_download_url in the release-streams JSON files is reachable.
# Skips versioned snapshot files (e.g. 2.9.5.0.json) and local dev files.
#
# Usage:
#   .\verify-streams.ps1
#   .\verify-streams.ps1 -StreamsDir .\release-streams
param(
    [string]$StreamsDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "release-streams")
)

$failed = $false

$files = Get-ChildItem (Join-Path $StreamsDir "*.json") | Where-Object {
    $_.BaseName -notmatch '^\d' -and $_.BaseName -notlike 'local*'
}

foreach ($file in $files) {
    $json = Get-Content $file.FullName -Raw | ConvertFrom-Json

    foreach ($asset in $json.assets) {
        $url = $asset.browser_download_url
        if (-not $url) { continue }

        Write-Host "$($file.Name): $url ... " -NoNewline
        try {
            $resp = Invoke-WebRequest -Uri $url -Method Head -UseBasicParsing -ErrorAction Stop
            Write-Host "OK ($($resp.StatusCode))" -ForegroundColor Green
        } catch {
            $status = $_.Exception.Response.StatusCode.value__
            Write-Host "FAIL ($status)" -ForegroundColor Red
            $failed = $true
        }
    }
}

if ($failed) {
    Write-Error "One or more download URLs could not be reached."
    exit 1
}

Write-Host "All URLs OK." -ForegroundColor Green
