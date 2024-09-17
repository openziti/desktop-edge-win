$ErrorActionPreference = "Stop"

function verifyFile($path) {
    if (Test-Path -Path "$path") {
        "OK: $path exists!"
    } else {
        throw [System.IO.FileNotFoundException] "$path not found"
    }
}

echo "========================== build.ps1 begins =========================="
$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path
$checkoutRoot = (Resolve-Path '${scriptPath}\..')
$buildPath = "${scriptPath}\build"
$ADV_INST_VERSION = Get-Content -Path "${checkoutRoot}\adv-inst-version"
$ADV_INST_HOME = "C:\Program Files (x86)\Caphyon\Advanced Installer ${ADV_INST_VERSION}"
$SIGNTOOL="${ADV_INST_HOME}\third-party\winsdk\x64\signtool.exe"
$ADVINST = "${ADV_INST_HOME}\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"
$ZITI_EDGE_TUNNEL_VERSION="v2.0.0-alpha19"

echo "Cleaning previous build folder if it exists"
Remove-Item "${buildPath}" -r -ErrorAction Ignore
mkdir "${buildPath}" -ErrorAction Ignore > $null

$global:ProgressPreference = "SilentlyContinue"
$destination="${scriptPath}\zet.zip"

if($null -eq $env:ZITI_EDGE_TUNNEL_BUILD) {
    if($null -eq $env:ZITI_EDGE_TUNNEL_VERSION) {
        # use the default $ZITI_EDGE_TUNNEL_VERSION
    } else {
        $ZITI_EDGE_TUNNEL_VERSION=$env:ZITI_EDGE_TUNNEL_VERSION
    }
    echo "========================== fetching ziti-edge-tunnel =========================="
    $zet_dl="https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/${ZITI_EDGE_TUNNEL_VERSION}/ziti-edge-tunnel-Windows_x86_64.zip"
    echo "Beginning to download ziti-edge-tunnel from ${zet_dl}"
    echo ""
    $response = Invoke-WebRequest $zet_dl -OutFile "${destination}"
} else {
    echo "========================== using locally defined ziti-edge-tunnel =========================="
    $zet_dl="${env:ZITI_EDGE_TUNNEL_BUILD}"

    echo "Sourcing ziti-edge-tunnel from ${zet_dl}"
    echo ""
    if ($SourcePath -match "^https?://") {
        $response = Invoke-WebRequest -Uri "${zet_dl}" -OutFile "${destination}"
    } else {
        $response = Copy-Item -Path "${zet_dl}" -Destination "${destination}" -ErrorAction Stop
    }
}

verifyFile("${destination}")
echo "Expanding downloaded file..."
Expand-Archive -Path "${destination}" -Force -DestinationPath "${buildPath}\service"
echo "expanded ${destination} file to ${buildPath}\service"

echo "========================== building and moving the custom signing tool =========================="
dotnet build -c Release "${checkoutRoot}/AWSSigner.NET\AWSSigner.NET.csproj"
Remove-Item "${scriptPath}\AWSSigner.NET" -Recurse -ErrorAction SilentlyContinue
$signerTargetDir="${scriptPath}\AWSSigner.NET"
move "${checkoutRoot}/AWSSigner.NET\bin\Release\" "${signerTargetDir}\"
$env:SIGNING_CERT="${scriptPath}\GlobalSign-SigningCert-2024-2027.cert"
$env:SIGNTOOL_PATH="${SIGNTOOL}"

Push-Location ${checkoutRoot}

echo "Updating the version for UI and Installer"
.\update-versions.ps1

echo "Restoring the .NET project"
nuget restore .\ZitiDesktopEdge.sln

echo "Building the UI"
msbuild ZitiDesktopEdge.sln /property:Configuration=Release

Pop-Location

$installerVersion=(Get-Content -Path ${checkoutRoot}\version)
if($null -ne $env:ZITI_DESKTOP_EDGE_VERSION) {
    echo "ZITI_DESKTOP_EDGE_VERSION is set. Using that: ${env:ZITI_DESKTOP_EDGE_VERSION} instead of version found in file ${installerVersion}"
    $installerVersion=$env:ZITI_DESKTOP_EDGE_VERSION
    echo "Version set to: ${installerVersion}"
}
$action = '/SetVersion'

echo "issuing $ADVINST /edit $ADVPROJECT $action $installerVersion (service version: $serviceVersion) - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $installerVersion

$action = '/build'
echo "Assembling installer using AdvancedInstaller at: $ADVINST $action $ADVPROJECT"
& $ADVINST $action $ADVPROJECT

$gituser=$(git config user.name)
if($gituser -eq "ziti-ci") {
  echo "detected user [${gituser}]"
  git add DesktopEdge/Properties/AssemblyInfo.cs ZitiDesktopEdge.Client/Properties/AssemblyInfo.cs ZitiUpdateService/Properties/AssemblyInfo.cs Installer/ZitiDesktopEdge.aip
  git commit -m "committing any version changes via ziti-ci"
  git push
} else {
  echo "detected user [${gituser}] which is not ziti-ci - skipping installer commit"
}

$outputPath="${scriptPath}\Output"
$exeName="Ziti Desktop Edge Client-${installerVersion}.exe"
$exeAbsPath="${outputPath}\${exeName}"

if($null -eq $env:AWS_KEY_ID) {
    echo ""
	echo "AWS_KEY_ID not set. __THE BINARY WILL NOT BE SIGNED!__"
    echo ""
}

if($null -eq $env:OPENZITI_P12_PASS_2024) {
    echo ""
    echo "Not calling signtool - env:OPENZITI_P12_PASS_2024 is not set"
    echo ""
} else {
    echo "adding additional signature to executable with openziti.org signing certificate"
    echo "Using ${SIGNTOOL} to sign executable with the additional OpenZiti signature"
    & "$SIGNTOOL" sign /f "${scriptPath}\openziti_2024.p12" /p "${env:OPENZITI_P12_PASS_2024}" /tr http://ts.ssl.com /fd sha512 /td sha512 /as "${exeAbsPath}"
}

(Get-FileHash "${exeAbsPath}").Hash > "${scriptPath}\Output\Ziti Desktop Edge Client-${installerVersion}.exe.sha256"
echo "========================== build.ps1 completed =========================="

$defaultRootUrl = "https://github.com/openziti/desktop-edge-win/releases/download/"
$defaultStream = "beta"
$defaultPublishedAt = Get-Date
$updateJson = "${installerVersion}.json"
& .\Installer\output-build-json.ps1 -version $installerVersion -url $defaultRootUrl -stream $defaultStream -published_at $defaultPublishedAt -outputPath $updateJson
