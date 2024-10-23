param(
    [string]$version,
    [string]$url = "https://github.com/openziti/desktop-edge-win/releases/download/",
    [string]$stream = "beta",
    [datetime]$published_at = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"),
    [bool]$jsonOnly = $false,
    [bool]$revertGitAfter = $true,
    [string]$versionQualifier = ""
)

$ErrorActionPreference = "Stop"
function verifyFile($path) {
    if (Test-Path -Path "$path") {
        "OK: $path exists!"
    } else {
        throw [System.IO.FileNotFoundException] "$path not found"
    }
}

echo ""
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
$ZITI_EDGE_TUNNEL_VERSION="v1.2.5"

echo "Cleaning previous build folder if it exists"
Remove-Item "${buildPath}" -r -ErrorAction Ignore
mkdir "${buildPath}" -ErrorAction Ignore > $null

$global:ProgressPreference = "SilentlyContinue"
$zetDownloadLoc="${scriptPath}\build\zet"
mkdir "${zetDownloadLoc}" -ErrorAction Ignore > $null
$destination="${zetDownloadLoc}\${ZITI_EDGE_TUNNEL_VERSION}-zet.zip"
$unzip = $true
if($null -eq $env:ZITI_EDGE_TUNNEL_BUILD) {
    if($null -eq $env:ZITI_EDGE_TUNNEL_VERSION) {
        # use the default $ZITI_EDGE_TUNNEL_VERSION
    } else {
        $ZITI_EDGE_TUNNEL_VERSION=$env:ZITI_EDGE_TUNNEL_VERSION
    }
    if (Test-Path ${destination} -PathType Container) {
        Write-Host -ForegroundColor Yellow "ziti-edge-tunnel.zip exists and won't be downloaded again: ${destination}"
    } else {
        echo "========================== fetching ziti-edge-tunnel =========================="
        $zet_dl="https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/${ZITI_EDGE_TUNNEL_VERSION}/ziti-edge-tunnel-Windows_x86_64.zip"
        echo "Beginning to download ziti-edge-tunnel from ${zet_dl} to ${destination}"
        echo ""
        $response = Invoke-WebRequest $zet_dl -OutFile "${destination}"
    }
} else {
    echo "========================== using locally defined ziti-edge-tunnel =========================="
    $zet_dl="${env:ZITI_EDGE_TUNNEL_BUILD}"

    echo "Using ziti-edge-tunnel declared from ${zet_dl}"
    echo ""
    if ($SourcePath -match "^https?://") {
        $response = Invoke-WebRequest -Uri "${zet_dl}" -OutFile "${destination}"
    } else {
        echo "Determining if the location is a directory or zip file"
        if (Test-Path $zet_dl -PathType Container) {
            $unzip = $false
        } elseif ($zet_dl -match '\.zip$') {
            echo "Copying zip file to destination"
            echo "  FROM: ${zet_dl}"
            echo "    TO: ${destination}"
            $response = Copy-Item -Path "${zet_dl}" -Destination "${destination}" -ErrorAction Stop
        } else {
            Write-Host  -ForegroundColor Red "Unknown type. Expected either a .zip file or a directory:"
            Write-Host  -ForegroundColor Red "  - ${zet_dl}"
            exit 1
        }
    }
}

if($unzip) {
    verifyFile("${destination}")
    echo "Expanding downloaded file..."
    Expand-Archive -Path "${destination}" -Force -DestinationPath "${buildPath}\service"
    echo "expanded ${destination} file to ${buildPath}\service"
} else {
    if (Test-Path -Path "${buildPath}\service") {
        echo "removing old service folder at: ${buildPath}\service"
        Remove-Item -Path "${buildPath}\service" -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    echo "creating new service directory: ${buildPath}\service"
    New-Item -Path "${buildPath}\service" -ItemType Directory | Out-Null
    
    echo "Copying files from directory to destination"
    echo "  FROM: ${zet_dl}\*"
    echo "    TO: ${buildPath}\service\"
    $response = Copy-Item -Path "${zet_dl}\wintun.dll" -Destination "${buildPath}\service\wintun.dll" -ErrorAction Stop -Force
    $response = Copy-Item -Path "${zet_dl}\ziti-edge-tunnel.exe" -Destination "${buildPath}\service\ziti-edge-tunnel.exe" -ErrorAction Stop -Force
}

echo "========================== building and moving the custom signing tool =========================="
dotnet build -c Release "${checkoutRoot}/AWSSigner.NET\AWSSigner.NET.csproj"
Remove-Item "${scriptPath}\AWSSigner.NET" -Recurse -ErrorAction SilentlyContinue
$signerTargetDir="${scriptPath}\AWSSigner.NET"
move "${checkoutRoot}/AWSSigner.NET\bin\Release\" "${signerTargetDir}\"
$env:SIGNING_CERT="${scriptPath}\GlobalSign-SigningCert-2024-2027.cert"
$env:SIGNTOOL_PATH="${SIGNTOOL}"

Push-Location ${checkoutRoot}

if ($version -eq "") {
    $version=(Get-Content -Path ${checkoutRoot}\version)
}

echo "Updating the version for UI and Installer"
.\update-versions.ps1 $version

echo "Restoring the .NET project"
nuget restore .\ZitiDesktopEdge.sln

echo "Building the UI"
msbuild ZitiDesktopEdge.sln /property:Configuration=Release

Pop-Location

echo "Building VERSION $version"

if($null -ne $env:ZITI_DESKTOP_EDGE_VERSION) {
    echo "ZITI_DESKTOP_EDGE_VERSION is set. Using that: ${env:ZITI_DESKTOP_EDGE_VERSION} instead of version found in file ${version}"
    $version=$env:ZITI_DESKTOP_EDGE_VERSION
    echo "Version set to: ${version}"
}
$action = '/SetVersion'

echo "issuing $ADVINST /edit $ADVPROJECT $action $version (service version: $serviceVersion) - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $version

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
$exeName="Ziti Desktop Edge Client-${version}.exe"
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
    echo "Using ${SIGNTOOL} to sign executable with the additional OpenZiti signature: ${exeAbsPath}"
    & "$SIGNTOOL" sign /f "${scriptPath}\openziti_2024.p12" /p "${env:OPENZITI_P12_PASS_2024}" /tr http://ts.ssl.com /fd sha512 /td sha512 /as "${exeAbsPath}"
}

(Get-FileHash "${exeAbsPath}").Hash > "${scriptPath}\Output\Ziti Desktop Edge Client-${version}.exe.sha256"

$outputPath = "${scriptPath}\Output\Ziti Desktop Edge Client-${version}.exe.json"
& .\Installer\output-build-json.ps1 -version $version -url $url -stream $stream -published_at $published_at -outputPath $outputPath -versionQualifier $versionQualifier

echo "REMOVING .back files: ${scriptPath}\*back*"
Remove-Item "${scriptPath}\*back*" -Recurse -ErrorAction SilentlyContinue

echo "Copying json file to beta.json"
copy $outputPath "$checkoutRoot\release-streams\beta.json"


if($revertGitAfter) {
  git checkout DesktopEdge/Properties/AssemblyInfo.cs ZitiUpdateService/Properties/AssemblyInfo.cs Installer/ZitiDesktopEdge.aip
}


echo "========================== build.ps1 completed =========================="
