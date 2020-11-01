echo "========================== build.ps1 begins =========================="
echo "Cleaning previous build folder if it exists"
rm .\build -r -fo -ErrorAction Ignore

$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path

echo "the branch is $env:GIT_BRANCH"

$zipLocal = "ziti-tunnel-service.zip"
$zipLocal = "${scriptPath}/../service/ziti-tunnel-win.zip"

echo ""
echo "unzipping $zipLocal to ${scriptPath}\build\service\"

Expand-Archive -Verbose -Force -LiteralPath $zipLocal "${scriptPath}\build\service\"

Push-Location ${scriptPath}\..
echo "Updating the version for UI and Installer"
.\update-versions.ps1

echo "Building the UI"
msbuild ZitiDesktopEdge.sln /property:Configuration=Release

echo "Building the Wintun installer"
msbuild ZitiWintunInstaller.sln /p:configuration=Release

Pop-Location

$ADVINST = "C:\Program Files (x86)\Caphyon\Advanced Installer 17.6\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"
$installerVersion=(Get-Content -Path ${scriptPath}\..\version)
$action = '/SetVersion'

echo "issuing $ADVINST /edit $ADVPROJECT $action $installerVersion (service version: $serviceVersion) - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $installerVersion

$action = '/build'
echo "Assembling installer using AdvancedInstaller at: $ADVINST $action $ADVPROJECT"
& $ADVINST $action $ADVPROJECT

$gituser=$(git config user.name)
if($gituser -eq "ziti-ci") {
  echo "yes ziti-ci"
  git commit -m "[ci skip] committing updated installer file" 2>&1
} else {
  echo "detected user [${gituser}] which is not ziti-ci - skipping installer commit"
}

git add Installer/ZitiDesktopEdge.aip

echo "========================== build.ps1 competed =========================="