echo "Cleaning previous build folder if it exists"
rm .\build -r -fo -ErrorAction Ignore

$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path 
${scriptPath}

$x=[xml] @"
$((Invoke-WebRequest https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml).Content)
"@

$serviceVersion = $x.metadata.versioning.release
$installerVersion=(Get-Content -Path .\version)

$zipUrl = "https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${serviceVersion}/ziti-tunnel-win-${serviceVersion}.zip"

echo "Downloading zip file "
echo "      from: $zipUrl"

$ProgressPreference = 'SilentlyContinue'
$timeTaken = Measure-Command -Expression {
    Invoke-WebRequest $zipUrl -OutFile ziti-tunnel-service.zip
}

$milliseconds = $timeTaken.TotalMilliseconds
echo "      time to download: $milliseconds"
echo ""
echo "unzipping ziti-tunnel-service.zip to build\service\"

Expand-Archive -Verbose -Force -LiteralPath ziti-tunnel-service.zip "${scriptPath}\build\service\"

Push-Location ${scriptPath}\..
echo "Updating the version for UI and Installer"
.\update-versions.ps1

echo "Building the UI"
#msbuild DesktopEdge\ZitiDesktopEdge.csproj /property:Configuration=Release
msbuild ZitiDesktopEdge.sln /property:Configuration=Release

echo "Building the Wintun installer"
msbuild ZitiWintunInstaller.sln /p:configuration=Release

Pop-Location

$ADVINST = "C:\Program Files (x86)\Caphyon\Advanced Installer 17.5\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"

$action = '/SetVersion'
echo "issuing $ADVINST /edit $ADVPROJECT $action $installerVersion (service version: $serviceVersion) - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $installerVersion

$action = '/build'
echo "Assembling installer using AdvancedInstaller at: $ADVINST $action $ADVPROJECT"
& $ADVINST $arg1 $ADVPROJECT

$ProgressPreference = 'Continue'