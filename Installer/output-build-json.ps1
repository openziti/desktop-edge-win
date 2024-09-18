# Sample invocation:
# .\output-build-json.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "clintdev" -published_at (Get-Date)
# or if you want a particular date/time
# .\output-build-json.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "clintdev" -published_at "2023-11-02T14:30:00"
param(
    [Parameter(Mandatory = $true)]
    [string]$version,
    [string]$url = "http://localhost:8000/release-streams/local",
    [string]$stream = "local",
    [string]$outputPath = "${version}.json",
    [datetime]$published_at
)

echo "=========== emitting a json file that represents this build ============"
$url = $url.TrimEnd(" ", "/")
if ($published_at -eq $null) {
    $published_at_str = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
} else {
    $published_at_str = $published_at.ToString("yyyy-MM-ddTHH:mm:ssZ")
}
Write-Host "published_at resolved to: ${published_at_str}"

$jsonTemplate = @"
{
  "name": "${version}",
  "tag_name": "${version}",
  "published_at": "${published_at_str}",
  "installation_critical": false,
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-${version}.exe",
      "browser_download_url": "${url}/${version}/Ziti.Desktop.Edge.Client-${version}.exe"
    }
  ]
}
"@

$jsonTemplate = $jsonTemplate -replace '\$\{version\}', $version
$jsonTemplate = $jsonTemplate -replace '\$\{published_at\}', $published_at
$jsonTemplate | Set-Content -Path $outputPath
