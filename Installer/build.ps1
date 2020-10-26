echo "========================== build.ps1 begins =========================="
echo "Cleaning previous build folder if it exists"
rm .\build -r -fo -ErrorAction Ignore

$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path
${scriptPath}

$x=[xml] @"
$((Invoke-WebRequest https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml).Content)
"@

echo "the branch is $env:GIT_BRANCH"

$branch = git rev-parse --abbrev-ref HEAD

if ("$branch" -ne 'master') {
    #if the git command to get the branch fails - this is 'not master' use the 'latest' - else fetch the 'release'
    $serviceVersion = $x.metadata.versioning.latest
}
else {
    $serviceVersion = $x.metadata.versioning.release
}
echo "service version is: $serviceVersion"
$installerVersion=(Get-Content -Path .\version)

$zipUrl = "https://netfoundry.jfrog.io/artifactory/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${serviceVersion}/ziti-tunnel-win-${serviceVersion}.zip"

echo "Downloading zip file "
echo "      from: $zipUrl"

tree ${scriptPath}..

$ProgressPreference = 'SilentlyContinue'
$timeTaken = Measure-Command -Expression {
    Invoke-WebRequest $zipUrl -OutFile ziti-tunnel-service.zip
}

$zipLocal = "ziti-tunnel-service.zip"
$zipLocal = "${scriptPath}/../service/ziti-tunnel-win.zip"

$milliseconds = $timeTaken.TotalMilliseconds
echo "      time to download: $milliseconds"
echo ""
echo "unzipping $zipLocal to ${scriptPath}\build\service\"

$ProgressPreference = 'Continue'

Expand-Archive -Verbose -Force -LiteralPath $zipLocal "${scriptPath}\build\service\"

Push-Location ${scriptPath}\..
echo "Updating the version for UI and Installer"
.\update-versions.ps1

echo "Building the UI"
#msbuild DesktopEdge\ZitiDesktopEdge.csproj /property:Configuration=Release
msbuild ZitiDesktopEdge.sln /property:Configuration=Release

echo "Building the Wintun installer"
msbuild ZitiWintunInstaller.sln /p:configuration=Release

Pop-Location

$ADVINST = "C:\Program Files (x86)\Caphyon\Advanced Installer 17.6\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"

$action = '/SetVersion'
echo "issuing $ADVINST /edit $ADVPROJECT $action $installerVersion (service version: $serviceVersion) - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $installerVersion

$action = '/build'
echo "Assembling installer using AdvancedInstaller at: $ADVINST $action $ADVPROJECT"
& $ADVINST $action $ADVPROJECT

echo "========================== build.ps1 competed =========================="