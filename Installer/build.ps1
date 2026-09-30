param(
    [string]$version,
    [string]$url = "https://github.com/openziti/desktop-edge-win/releases/download/",
    [string]$stream = "beta",
    [datetime]$published_at = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"),
    [bool]$jsonOnly = $false,
    [bool]$revertGitAfter = $true,
    [string]$versionQualifier = "",
    [bool]$Win32Crypto = $false, #used to specify which ziti edge tunnel version to pull, openssl or win32crypto-based
    [int]$FastInterval = 0  # if > 0, bakes ALLOWFASTINTERVAL into the binary and sets UpdateTimer to this many seconds. use when dev/testing auto updating. 0 = disabled (production 10-min floor applies).
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
$checkoutRoot = (Resolve-Path "${scriptPath}\..")
$buildPath = "${scriptPath}\build"

# Downloads of third-party artifacts live outside ${buildPath}, which is wiped on every run. Putting them
# under LOCALAPPDATA also means several worktrees of this repo share one copy. Everything here is keyed by
# version and hash-checked or version-named, so a stale entry cannot be served as a newer one.
$buildCache = Join-Path $env:LOCALAPPDATA "NetFoundry\build-cache"

$ADV_INST_VERSION = Get-Content -Path "${checkoutRoot}\adv-inst-version"
$ADV_INST_HOME = "C:\Program Files (x86)\Caphyon\Advanced Installer ${ADV_INST_VERSION}"
$SIGNTOOL="${ADV_INST_HOME}\third-party\winsdk\x64\signtool.exe"
$ADVINST = "${ADV_INST_HOME}\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\ZitiDesktopEdge.aip"
$ZITI_EDGE_TUNNEL_VERSION="v1.19.0"

#--------------------------------------------------------------------------------------------------------------
# Preflight. Everything below takes minutes and downloads a lot. Fail now, not at minute six, if a tool the
# build needs is not here. msbuild in particular is only on PATH inside a Developer PowerShell, and the build
# gets all the way through NuGet restore before it notices.
#--------------------------------------------------------------------------------------------------------------
echo "========================== preflight =========================="
$missing = @()

foreach ($tool in "msbuild", "nuget", "dotnet", "git") {
    $found = Get-Command $tool -ErrorAction SilentlyContinue
    if ($null -eq $found) {
        $missing += "  ${tool}: not on PATH"
    } else {
        echo "  ${tool}: $($found.Source)"
    }
}

if (Test-Path $ADVINST) {
    echo "  AdvancedInstaller: ${ADVINST}"
} else {
    $missing += "  AdvancedInstaller ${ADV_INST_VERSION}: not found at ${ADVINST}"
}

if ($missing.Count -gt 0) {
    # PowerShell's exception formatter folds newlines into one wrapped line, so the detail is written to the
    # host and the throw is kept to a single sentence.
    Write-Host ""
    Write-Host "build prerequisites missing:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host $_ -ForegroundColor Red }

    if ($missing -match "msbuild") {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        $vsPath = "<your Visual Studio install>"
        if (Test-Path $vswhere) {
            $vsFound = & $vswhere -latest -products * -property installationPath | Select-Object -First 1
            if ($vsFound) { $vsPath = $vsFound }
        }
        Write-Host ""
        Write-Host "msbuild is only on PATH in a Developer PowerShell. Start one, or import it here:"
        Write-Host "  Import-Module `"${vsPath}\Common7\Tools\Microsoft.VisualStudio.DevShell.dll`""
        Write-Host "  Enter-VsDevShell -VsInstallPath `"${vsPath}`" -SkipAutomaticLocation -DevCmdArguments `"-arch=x64`""
    }
    # Enter-VsDevShell / vcvars set these. If none are present, this shell was never developer-initialized,
    # which is the cause of nearly every miss above.
    if ($null -eq $env:VSINSTALLDIR -and $null -eq $env:DevEnvDir -and $null -eq $env:VCToolsInstallDir) {
        Write-Host ""
        Write-Host "This shell does not look like a Developer PowerShell (VSINSTALLDIR, DevEnvDir and" -ForegroundColor Yellow
        Write-Host "VCToolsInstallDir are all unset). That is most likely what is wrong." -ForegroundColor Yellow
    }
    Write-Host ""

    # exit rather than throw: a throw here reprints the whole message inside PowerShell's exception
    # formatter, which is noise. The non-zero code is what CI cares about.
    exit 1
}

echo "Cleaning previous build folder if it exists"
Remove-Item "${buildPath}" -r -ErrorAction Ignore
mkdir "${buildPath}" -ErrorAction Ignore > $null

if ([string]::IsNullOrEmpty($versionQualifier)) {
    if($Win32Crypto) {
        $versionQualifier = "-win32crypto"
    } else {
        $versionQualifier = ""
    }
    echo "Using versionQualifier: $versionQualifier"
}

$global:ProgressPreference = "SilentlyContinue"
$unzip = $true
if($null -eq $env:ZITI_EDGE_TUNNEL_BUILD) {
    if($null -eq $env:ZITI_EDGE_TUNNEL_VERSION) {
        # use the default $ZITI_EDGE_TUNNEL_VERSION
    } else {
        $ZITI_EDGE_TUNNEL_VERSION=$env:ZITI_EDGE_TUNNEL_VERSION
    }

    # The cached name carries both the version and the qualifier. The -win32crypto build is a different
    # binary from the same tag, so the two must not share a file name. The version is resolved above,
    # after any env override, so an overridden version is never cached under the default name.
    $zetDownloadLoc="${buildCache}\zet"
    mkdir "${zetDownloadLoc}" -ErrorAction Ignore > $null
    $destination="${zetDownloadLoc}\${ZITI_EDGE_TUNNEL_VERSION}${versionQualifier}-zet.zip"

    if (Test-Path ${destination} -PathType Leaf) {
        Write-Host -ForegroundColor Yellow "ziti-edge-tunnel.zip is cached and won't be downloaded again: ${destination}"
    } else {
        echo "========================== fetching ziti-edge-tunnel =========================="
        $zet_dl="https://github.com/openziti/ziti-tunnel-sdk-c/releases/download/${ZITI_EDGE_TUNNEL_VERSION}/ziti-edge-tunnel-Windows_x86_64${versionQualifier}.zip"
        echo "Beginning to download ziti-edge-tunnel from ${zet_dl} to ${destination}"
        echo ""
        try {
            $response = Invoke-WebRequest $zet_dl -OutFile "${destination}" -ErrorAction Stop
        }
        catch {
            # A partial file left by a failed transfer would be served as a cache hit on the next run.
            Remove-Item "${destination}" -Force -ErrorAction SilentlyContinue
            Write-Host -ForegroundColor Red "❌ Download failed: $zet_dl $($_.Exception.Message)"
            exit 1
        }
    }
} else {
    echo "========================== using locally defined ziti-edge-tunnel =========================="
    # A locally supplied build is never cached: it has no version to key on and changes under the same path.
    $zetDownloadLoc="${buildPath}\zet"
    mkdir "${zetDownloadLoc}" -ErrorAction Ignore > $null
    $destination="${zetDownloadLoc}\local-zet.zip"
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

echo "========================== fetching the FIPS provider =========================="
# Staged beside ziti-edge-tunnel.exe because that is where it lands in APPDIR: the tunneler resolves
# openssl.cnf from its own directory. Hashes come from fips\provider.json and a mismatch fails the build.
# See fips\README.md.
& "${scriptPath}\Get-FipsProvider.ps1" `
    -Manifest "${checkoutRoot}\fips\provider.json" `
    -Destination "${buildPath}\service" `
    -CacheDir "${buildCache}\fips"

echo "========================== building and moving the custom signing tool =========================="
# AWSSigner.NET is a non-SDK-style project, so packages aren't auto-restored by
# `dotnet build`. Restore explicitly so a fresh checkout / worktree works.
nuget restore "${checkoutRoot}/AWSSigner.NET\AWSSigner.NET.csproj" -PackagesDirectory "${checkoutRoot}\packages"
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
.\scripts\update-versions.ps1 $version

echo "Restoring the .NET project"
nuget restore .\ZitiDesktopEdge.sln

if ($FastInterval -gt 0) {
    $ts = [TimeSpan]::FromSeconds($FastInterval)
    $tsValue = "{0}:{1}:{2}:{3}" -f $ts.Days, $ts.Hours, $ts.Minutes, $ts.Seconds
    echo "FastInterval: patching UpdateTimer to $FastInterval seconds ($tsValue) in App.config"
    $appConfig = "$checkoutRoot\ZitiUpdateService\App.config"
    (Get-Content $appConfig) -replace 'key="UpdateTimer" value="[^"]*"', "key=`"UpdateTimer`" value=`"$tsValue`"" | Set-Content $appConfig
}

echo "Building the UI"
$msbuildExtra = if ($FastInterval -gt 0) { "/p:AllowFastInterval=true" } else { "" }
msbuild ZitiDesktopEdge.sln /property:Configuration=Release /p:EnableWin32Crypto=$Win32Crypto $msbuildExtra

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

# Clear this version's artifacts first, leaving other versions alone. A build that stops producing one of
# its outputs -- a misconfigured Advanced Installer build type will do it -- otherwise leaves the previous
# file in place, and everything downstream treats it as current: it gets hashed, listed in the release JSON
# and installed during testing. The only symptom is a file date nobody looks at.
$stale = Get-ChildItem "${scriptPath}\Output" -Filter "Ziti Desktop Edge Client-${version}.*" -ErrorAction Ignore
if ($stale) {
    echo "Removing previous artifacts for ${version}"
    $stale | ForEach-Object {
        echo "  $($_.Name)"
        Remove-Item $_.FullName -Force -ErrorAction Ignore
    }
}

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

$timeout = 1  # Set timeout in seconds
Write-Host ""
Write-Host "============================================"
Write-Host "Waiting $timeout seconds for Adv Inst to finish writing the file...."
Write-Host "  if you see errors indicating the file is not found, increment"
Write-Host "  the `$timeout by one"
Write-Host "============================================"
Start-Sleep -Seconds $timeout

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

echo "Generating SHA256 for EXE: ${exeAbsPath}"
(Get-FileHash "${exeAbsPath}").Hash > "${scriptPath}\Output\Ziti Desktop Edge Client-${version}.exe.sha256"

$msiAbsPath="${outputPath}\Ziti Desktop Edge Client-${version}.msi"
if (Test-Path "${msiAbsPath}") {
    echo "Generating SHA256 for MSI: ${msiAbsPath}"
    (Get-FileHash "${msiAbsPath}").Hash > "${scriptPath}\Output\Ziti Desktop Edge Client-${version}.msi.sha256"
} else {
    # Reaching here means the BuildMSI build produced nothing. The usual cause is its package type having
    # been switched to an EXE, which makes it a duplicate of BuildEXE and silently stops producing an MSI.
    Write-Warning "MSI not found at ${msiAbsPath} - the BuildMSI build produced no .msi file"
    Write-Warning "  check Package Definition -> Builds -> BuildMSI is still an MSI package, not an EXE"
}

$outputPath = "${scriptPath}\Output\Ziti Desktop Edge Client-${version}.exe.json"
& .\Installer\output-build-json.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -outputPath:$outputPath -versionQualifier:$versionQualifier

echo "REMOVING .back files: ${scriptPath}\*back*"
Remove-Item "${scriptPath}\*back*" -Recurse -ErrorAction SilentlyContinue

if($revertGitAfter) {
  # An array, splatted. As a single space-joined string git sees one pathspec and matches nothing, leaving
  # the version-stamped files dirty after every build.
  $revertFiles = @(
    "DesktopEdge/Properties/AssemblyInfo.cs",
    "ZitiUpdateService/Properties/AssemblyInfo.cs",
    "Installer/ZitiDesktopEdge.aip"
  )
  if ($FastInterval -gt 0) { $revertFiles += "ZitiUpdateService/App.config" }
  git checkout @revertFiles
}

$log = ".\deps-info${versionQualifier}.txt"

"" | Out-File $log
"Dependencies from ziti-edge-tunnel:" | Out-File $log -Append
"---------------------------------------------" | Out-File $log -Append

& '.\Installer\build\service\ziti-edge-tunnel.exe' version -v | ForEach-Object {
    if ($_ -notmatch "StartServiceCtrlDispatcher failed") {
        "* $_" | Out-File $log -Append
    }
}

"" | Out-File $log -Append

Get-Content $log

# What this build actually produced. Worth printing because a missing artifact is otherwise invisible: the
# build reports success, and the only clue is a file date nobody reads. Expect an .exe and an .msi, each
# with a .sha256, plus the .exe.json.
echo ""
echo "========================== artifacts =========================="
Get-ChildItem "${scriptPath}\Output" -Filter "Ziti Desktop Edge Client-${version}.*" |
    Format-Table Name, Length, LastWriteTime -AutoSize | Out-String | Write-Host

echo "========================== build.ps1 completed =========================="
