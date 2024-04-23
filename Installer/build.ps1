$ErrorActionPreference = "Stop"

function verifyFile($path) {
    if (Test-Path -Path "$path") {
        "OK: $path exists!"
    } else {
        throw [System.IO.FileNotFoundException] "$path not found"
    }
}
function signFile($loc, $name) {
	echo "Using signtool: '$SIGNTOOL'"
	$cert="${scriptPath}\GlobalSign-SigningCert-2024-2027.cert"
	$exeAbsPath="$loc\$name"
	echo "----- signFile: producing digest to send to AWS KMS -----"
	& "$SIGNTOOL" sign /dg $loc /fd sha256 /f $cert $exeAbsPath
	$tosign = Get-Content "${exeAbsPath}.dig" -Raw
	echo "----- signFile: sending digest to AWS KMS for signing -----"
	$signature = aws kms sign --key-id $env:AWS_KEY_ID `
		--message $tosign `
		--message-type DIGEST `
		--signing-algorithm "RSASSA_PKCS1_V1_5_SHA_256" `
		--output text `
		--query "Signature"
	Set-Content -Path "${exeAbsPath}.dig.signed" -Value $signature
	echo "----- signFile: adding signature -----"
	& "$SIGNTOOL" sign /di $loc $exeAbsPath
	echo "----- signFile: adding timestamp -----"
	& "$SIGNTOOL" timestamp /tr "http://timestamp.digicert.com" /td sha256 $exeAbsPath
	& "$SIGNTOOL" verify /pa $exeAbsPath
}

echo "========================== build.ps1 begins =========================="
$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path
$checkoutRoot = (Resolve-Path '${scriptPath}\..')
$buildPath = "${scriptPath}\build"
$ADV_INST_VERSION = Get-Content -Path "${checkoutRoot}\adv-inst-version"
$ADV_INST_HOME = "C:\Program Files (x86)\Caphyon\Advanced Installer ${ADV_INST_VERSION}"
$SIGNTOOL="${ADV_INST_HOME}\third-party\winsdk\x64\signtool"
$ADVINST = "${ADV_INST_HOME}\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"

echo "Cleaning previous build folder if it exists"
Remove-Item "${buildPath}" -r -ErrorAction Ignore
mkdir "${buildPath}" -ErrorAction Ignore > $null

$global:ProgressPreference = "SilentlyContinue"

if($null -eq $env:ZITI_EDGE_TUNNEL_BUILD) {
    if($null -eq $env:ZITI_EDGE_TUNNEL_VERSION) {
        $ZITI_EDGE_TUNNEL_VERSION="v0.22.28"
    } else {
        $ZITI_EDGE_TUNNEL_VERSION=$env:ZITI_EDGE_TUNNEL_VERSION
    }
    echo "========================== fetching ziti-edge-tunnel =========================="
    $zet_dl="https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/${ZITI_EDGE_TUNNEL_VERSION}/ziti-edge-tunnel-Windows_x86_64.zip"
    echo "Beginning to download ziti-edge-tunnel from ${zet_dl}"
    echo ""
    $response = Invoke-WebRequest $zet_dl -OutFile "${scriptPath}\zet.zip"
    verifyFile("${scriptPath}\zet.zip")

    echo "Expanding downloaded file..."
    Expand-Archive -Path "${scriptPath}\zet.zip" -Force -DestinationPath "${buildPath}\service"
    echo "expanded zet.zip file to ${buildPath}\service"
} else {
    echo "========================== using locally defined ziti-edge-tunnel =========================="
    $zet_folder=$env:ZITI_EDGE_TUNNEL_BUILD
    echo "local build folder set to: $zet_folder"
    echo ""
    verifyFile("$zet_folder\ziti-edge-tunnel.exe")
    verifyFile("$zet_folder\wintun.dll")
    echo ""
    mkdir "${buildPath}\service" -ErrorAction Ignore > $null
    copy "$zet_folder\ziti-edge-tunnel.exe" -Destination "$buildPath\service" -Force
    copy "$zet_folder\wintun.dll" -Destination "$buildPath\service" -Force
}

Push-Location ${checkoutRoot}

echo "========================== fetching vc++ redist =========================="
$VC_REDIST_URL="https://aka.ms/vs/17/release/vc_redist.x64.exe"
echo "Beginning to download vc++ redist from MS at ${VC_REDIST_URL}"
echo ""
Invoke-WebRequest $VC_REDIST_URL -OutFile "${buildPath}\VC_redist.x64.exe"
verifyFile("${buildPath}\VC_redist.x64.exe")
echo "========================== fetching vc++ redist complete =========================="

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

$exePath="${scriptPath}\Output"
$exeName="Ziti Desktop Edge Client-${installerVersion}.exe"
$exeAbsPath="${exePath}\${exeName}"

if($null -eq $env:AWS_KEY_ID) {
    echo ""
	echo "AWS_KEY_ID not set. __THE BINARY WILL NOT BE SIGNED!__"
    echo ""
} else {
    signFile -loc $exePath -name $exeName
}

if($null -eq $env:OPENZITI_P12_PASS_2024) {
    echo ""
    echo "Not calling signtool - env:OPENZITI_P12_PASS_2024 is not set"
    echo ""
} else {
    echo "adding additional signature to executable with openziti.org signing certificate"\
    echo "Using ${SIGNTOOL} to sign executable with the additional OpenZiti signature"
    & "$SIGNTOOL" sign /f "${scriptPath}\openziti_2024.p12" /p "${env:OPENZITI_P12_PASS_2024}" /tr http://ts.ssl.com /fd sha512 /td sha512 /as "${exeAbsPath}"
}

(Get-FileHash "${exeAbsPath}").Hash > "${scriptPath}\Output\Ziti Desktop Edge Client-${installerVersion}.exe.sha256"
echo "========================== build.ps1 completed =========================="

$defaultRootUrl = "https://github.com/openziti/desktop-edge-win/releases/download/"
$defaultStream = "beta"
$defaultPublishedAt = Get-Date
$outputPath = "${installerVersion}.json"
& .\Installer\output-build-json.ps1 -version $installerVersion -url $defaultRootUrl -stream $defaultStream -published_at $defaultPublishedAt -outputPath $outputPath
