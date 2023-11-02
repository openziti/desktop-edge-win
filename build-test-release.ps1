param(
    [Parameter(Mandatory = $true)]
    [string]$version,

    [string]$url = "http://localhost:8000/ZitiDesktopEdgeClient"
)

#$env:ZITI_DESKTOP_EDGE_DOWNLOAD_URL="http://localhost:8000"
#$env:ZITI_DESKTOP_EDGE_VERSION="2.1.18"
$env:ZITI_DESKTOP_EDGE_DOWNLOAD_URL="$url"
$env:ZITI_DESKTOP_EDGE_VERSION="$version"

.\Installer\build.ps1
$exitCode = $LASTEXITCODE
if($exitCode -gt 0) {
  Write-Host "build.ps1 failed!"
  exit $exitCode
}

mkdir .\ZitiUpdateService\upgradeTesting\ZitiDesktopEdgeClient\${env:ZITI_DESKTOP_EDGE_VERSION} -ErrorAction Ignore > $null
move -Force "./Installer/Output/Ziti Desktop Edge Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe" ".\ZitiUpdateService\upgradeTesting\ZitiDesktopEdgeClient\${env:ZITI_DESKTOP_EDGE_VERSION}\Ziti.Desktop.Edge.Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe"
move -Force "./Installer/Output/Ziti Desktop Edge Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe.sha256" ".\ZitiUpdateService\upgradeTesting\ZitiDesktopEdgeClient\${env:ZITI_DESKTOP_EDGE_VERSION}\Ziti.Desktop.Edge.Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe.sha256"
move -Force "${env:ZITI_DESKTOP_EDGE_VERSION}.json" ".\ZitiUpdateService\upgradeTesting\version-check.json"

