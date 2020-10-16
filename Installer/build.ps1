echo "---------------------"
pwd
pwd
echo "---------------------"

echo "Cleaning previous build folder if it exists"
rm .\build -r -fo -ErrorAction Ignore

$x=[xml] @"
$((Invoke-WebRequest https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml).Content)
"@

$v = $x.metadata.versioning.release

$zipUrl = "https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${v}/ziti-tunnel-win-${v}.zip"

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

Expand-Archive -Force -LiteralPath ziti-tunnel-service.zip build\service\


Push-Location ..
echo "Updating the version for UI and Installer"
.\update-versions.ps1
Pop-Location

echo "Building the UI"
msbuild ..\DesktopEdge\ZitiDesktopEdge.csproj /property:Configuration=Release

echo "Building the Wintun installer"
msbuild ..\ZitiWintunInstaller.sln /p:configuration=Release

$CMD = "$ENV:ADVINST_EXE"
$arg1 = '/build'
$arg2 = '.\ZitiDesktopEdge.aip'

echo "Assembling installer using AdvancedInstaller at: $CMD $arg1 $arg2"
& $CMD $arg1 $arg2

$ProgressPreference = 'Continue'