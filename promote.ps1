### Promotes a release stream to one or more target streams, updating published_at to noon UTC today.
###
### Usage:
###   # by file path (original behaviour):
###   .\promote.ps1 -From release-streams\beta.json -To release-streams\latest.json
###
###   # by version (auto-finds source, handles win32crypto twin automatically):
###   .\promote.ps1 -Version 2.9.6.1 -To latest
###   .\promote.ps1 -Version 2.9.6.1 -To latest, stable
[CmdletBinding()]
param (
    [Parameter(ParameterSetName = "ByPath",    Mandatory = $true)]  [string]   $From,
    [Parameter(ParameterSetName = "ByVersion", Mandatory = $true)]  [string]   $Version,
    [Parameter(Mandatory = $true)]                                   [string[]] $To
)

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

if ($PSCmdlet.ParameterSetName -eq "ByPath") {
    Invoke-Promote $From $To[0]
} else {
    $streamsDir = "$PSScriptRoot\release-streams"

    # Locate the regular source for this version.
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
    if (-not $regularSource) { Write-Error "No source JSON found for version $Version"; exit 1 }

    # Locate the win32crypto source for this version.
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
    if (-not $win32Source) { Write-Warning "No win32crypto source found for version $Version — skipping win32crypto targets" }

    foreach ($stream in $To) {
        Invoke-Promote $regularSource "$streamsDir\$stream.json"
        if ($win32Source) {
            Invoke-Promote $win32Source "$streamsDir\$stream-win32crypto.json"
        }
    }
}
