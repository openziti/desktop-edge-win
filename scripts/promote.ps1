### Promotes a release to one or more target streams, updating published_at to noon UTC today.
### When no source JSON exists for the version, generates it from known URL patterns.
###
### -From and -To must be one of: beta, latest, stable. Both regular and -win32crypto twins
### are promoted together.
###
### Usage:
###   # by version (auto-finds or generates source):
###   .\promote.ps1 -Version 2.9.6.1 -To beta
###   .\promote.ps1 -Version 2.9.6.1 -To latest, stable
###
###   # by stream name:
###   .\promote.ps1 -From beta -To latest
[CmdletBinding(DefaultParameterSetName = "Help")]
param (
    [Parameter(ParameterSetName = "ByStream",  Mandatory = $true)]
        [ValidateSet("beta", "latest", "stable")] [string]   $From,
    [Parameter(ParameterSetName = "ByVersion", Mandatory = $true)] [string]   $Version,
    [Parameter(ParameterSetName = "ByStream",  Mandatory = $true)]
    [Parameter(ParameterSetName = "ByVersion", Mandatory = $true)]
        [ValidateSet("beta", "latest", "stable")] [string[]] $To,
    [Parameter(ParameterSetName = "Help", Position = 0)]
        [ValidateSet("help", "-h", "--help", "/?")] [string] $HelpCmd
)

if ($PSCmdlet.ParameterSetName -eq "Help") {
    Write-Host @"
Usage:
  promote.ps1 -From <stream> -To <stream>[,<stream>...]
  promote.ps1 -Version <version> -To <stream>[,<stream>...]

  <stream> must be one of: beta, latest, stable
  Both the regular and -win32crypto twins are promoted together.

Examples:
  .\scripts\promote.ps1 -From beta -To latest
  .\scripts\promote.ps1 -Version 2.11.1.0 -To beta
  .\scripts\promote.ps1 -Version 2.9.6.1 -To latest, stable
"@
    exit 0
}

$githubBaseUrl = "https://github.com/openziti/desktop-edge-win/releases/download"
$jfrogBaseUrl  = "https://netfoundry.jfrog.io/artifactory/downloads/desktop-edge-win-win32crypto"

# thanks to https://jonathancrozier.com/blog/formatting-json-with-proper-indentation-using-powershell
function Format-Json
{
    param
    (
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [String]$Json,

        [ValidateRange(1, 1024)]
        [Int]$Indentation = 2
    )

    $lines = $Json -split '\r?\n'

    $indentLevel = 0

    $result = $lines | ForEach-Object {
        if ($_ -match "[\}\]]") { $indentLevel-- }
        $line = (' ' * $indentLevel * $Indentation) + $_.TrimStart().Replace(":  ", ": ")
        if ($_ -match "[\{\[]") { $indentLevel++ }
        return $line
    }

    return $result -join "`n"
}

function Invoke-Promote($sourcePath, $targetPath) {
    $json = Get-Content -Path $sourcePath | ConvertFrom-Json
    $json.published_at = (Get-Date).ToUniversalTime().Date.AddHours(12).ToString("yyyy-MM-ddTHH:mm:ssZ")
    $json | ConvertTo-Json -Depth 10 | Format-Json | Set-Content -Path $targetPath -Encoding UTF8
    Write-Host "promoted: $sourcePath -> $targetPath"
}

function New-VersionJson([string]$version, [string]$downloadUrl) {
    $now = (Get-Date).ToUniversalTime().Date.AddHours(12).ToString("yyyy-MM-ddTHH:mm:ssZ")
    return @"
{
  "name": "$version",
  "tag_name": "$version",
  "published_at": "$now",
  "installation_critical": false,
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-$version.exe",
      "browser_download_url": "$downloadUrl"
    }
  ]
}
"@
}

$streamsDir = "$(Split-Path $PSScriptRoot -Parent)\release-streams"

if ($PSCmdlet.ParameterSetName -eq "ByStream") {
    $sourcePath     = Join-Path $streamsDir "$From.json"
    $sourceWin32    = Join-Path $streamsDir "$From-win32crypto.json"

    foreach ($t in $To) {
        Invoke-Promote $sourcePath  (Join-Path $streamsDir "$t.json")
        Invoke-Promote $sourceWin32 (Join-Path $streamsDir "$t-win32crypto.json")
    }
} else {

    # Locate or generate the regular source for this version.
    $regularSource = $null
    $versionFile = "$streamsDir\$Version.json"
    if (Test-Path $versionFile) {
        $regularSource = $versionFile
    } else {
        foreach ($f in Get-ChildItem "$streamsDir\*.json" | Where-Object { $_.Name -notmatch "win32crypto" }) {
            $j = Get-Content $f.FullName | ConvertFrom-Json
            if ($j.tag_name -eq $Version -or $j.name -eq $Version) { $regularSource = $f.FullName; break }
        }
    }
    if (-not $regularSource) {
        Write-Host "No existing source found for $Version - generating $versionFile"
        $json = New-VersionJson $Version "$githubBaseUrl/$Version/Ziti.Desktop.Edge.Client-$Version.exe"
        $json | Set-Content -Path $versionFile -NoNewline
        $regularSource = $versionFile
    }

    # Locate or generate the win32crypto source for this version.
    $win32Source = $null
    $win32VersionFile = "$streamsDir\$Version-win32crypto.json"
    if (Test-Path $win32VersionFile) {
        $win32Source = $win32VersionFile
    } else {
        foreach ($f in Get-ChildItem "$streamsDir\*-win32crypto.json") {
            $j = Get-Content $f.FullName | ConvertFrom-Json
            if ($j.tag_name -eq $Version -or $j.name -eq $Version) { $win32Source = $f.FullName; break }
        }
    }
    if (-not $win32Source) {
        Write-Host "No existing win32crypto source found for $Version - generating $win32VersionFile"
        $json = New-VersionJson $Version "$jfrogBaseUrl/$Version/Ziti.Desktop.Edge.Client-$Version.exe"
        $json | Set-Content -Path $win32VersionFile -NoNewline
        $win32Source = $win32VersionFile
    }

    foreach ($stream in $To) {
        Invoke-Promote $regularSource "$streamsDir\$stream.json"
        Invoke-Promote $win32Source "$streamsDir\$stream-win32crypto.json"
    }
}
