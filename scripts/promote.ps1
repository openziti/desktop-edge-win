### Promotes a release to one or more target streams, updating published_at to noon UTC today.
### When no source JSON exists for the version, generates it from known URL patterns.
###
### Usage:
###   # by version (auto-finds or generates source, handles win32crypto twin automatically):
###   .\promote.ps1 -Version 2.9.6.1 -To beta
###   .\promote.ps1 -Version 2.9.6.1 -To latest, stable
###
###   # by file path (original behaviour):
###   .\promote.ps1 -From release-streams\beta.json -To release-streams\latest.json
[CmdletBinding()]
param (
    [Parameter(ParameterSetName = "ByPath",    Mandatory = $true)]  [string]   $From,
    [Parameter(ParameterSetName = "ByVersion", Mandatory = $true)]  [string]   $Version,
    [Parameter(Mandatory = $true)]                                   [string[]] $To
)

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

if ($PSCmdlet.ParameterSetName -eq "ByPath") {
    Invoke-Promote $From $To[0]
} else {
    $streamsDir = "$(Split-Path $PSScriptRoot -Parent)\release-streams"

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
