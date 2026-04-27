param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\Installer\Output")
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

$sources = @(
    @{ Src = Join-Path $repoRoot "ZitiUpdateService\windows\gpo\NetFoundry.ZitiMonitorService.admx";       Dest = "admx\NetFoundry.ZitiMonitorService.admx" },
    @{ Src = Join-Path $repoRoot "ZitiUpdateService\windows\gpo\en-US\NetFoundry.ZitiMonitorService.adml"; Dest = "admx\en-US\NetFoundry.ZitiMonitorService.adml" },
    @{ Src = Join-Path $repoRoot "DesktopEdge\windows\gpo\NetFoundry.ZitiDesktopEdgeUI.admx";              Dest = "admx\NetFoundry.ZitiDesktopEdgeUI.admx" },
    @{ Src = Join-Path $repoRoot "DesktopEdge\windows\gpo\en-US\NetFoundry.ZitiDesktopEdgeUI.adml";        Dest = "admx\en-US\NetFoundry.ZitiDesktopEdgeUI.adml" }
)

foreach ($s in $sources) {
    if (-not (Test-Path -LiteralPath $s.Src)) {
        throw "Required ADMX/ADML source not found: $($s.Src)"
    }
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("ZDEW-AdminTemplates-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    foreach ($s in $sources) {
        $destFull = Join-Path $staging $s.Dest
        $destDir = Split-Path -Parent $destFull
        if (-not (Test-Path -LiteralPath $destDir)) {
            New-Item -ItemType Directory -Path $destDir | Out-Null
        }
        Copy-Item -LiteralPath $s.Src -Destination $destFull
    }

    $zipName = "ZDEW-AdminTemplates-$Version.zip"
    $zipPath = Join-Path $OutputDir $zipName
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $staging "admx") -DestinationPath $zipPath -CompressionLevel Optimal

    $sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Set-Content -LiteralPath ($zipPath + ".sha256") -Value $sha -NoNewline

    Write-Host "Built admin templates zip: $zipPath"
    Write-Host "SHA256: $sha"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
