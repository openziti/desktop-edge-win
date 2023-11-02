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

mkdir .\release-streams\local\${env:ZITI_DESKTOP_EDGE_VERSION} -ErrorAction Ignore > $null
Move-Item -Force "./Installer/Output/Ziti Desktop Edge Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe" ".\release-streams\local\${env:ZITI_DESKTOP_EDGE_VERSION}\Ziti.Desktop.Edge.Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe"
Move-Item -Force "./Installer/Output/Ziti Desktop Edge Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe.sha256" ".\release-streams\local\${env:ZITI_DESKTOP_EDGE_VERSION}\Ziti.Desktop.Edge.Client-${env:ZITI_DESKTOP_EDGE_VERSION}.exe.sha256"
Move-Item -Force "${env:ZITI_DESKTOP_EDGE_VERSION}.json" ".\release-streams\local.json"

