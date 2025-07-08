# Sample invocations:
# .\build-test-release.ps1 -jsonOnly $true -version 1.1.1
# .\build-test-release.ps1 -jsonOnly $true -version 1.1.1 -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -version 2.2.5 -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -jsonOnly $true -version 2.2.5 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at (Get-Date)
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at "2023-11-02T14:30:00"
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at "2023-11-02T14:30:00" -Win32Crypto:$true
param(
    [string]$version,
    [string]$url = "http://localhost:8000/release-streams/local",
    [string]$stream = "local",
    [datetime]$published_at = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"),
    [bool]$jsonOnly = $false,
    [bool]$revertGitAfter = $true,
    [string]$versionQualifier = "",
    [switch]$promote = $false,  # New parameter for promotion
    [bool]$Win32Crypto = $false #used to specify which ziti edge tunnel version to pull, openssl or win32crypto-based
)

if ([string]::IsNullOrEmpty($versionQualifier)) {
    if($Win32Crypto) {
        $versionQualifier = "-win32crypto"
    } else {
        $versionQualifier = ""
    }
    echo "Using versionQualifier: $versionQualifier"
}

# Promote function that copies 'beta.json' to 'latest.json' and updates timestamp
function Promote-Release {
    $betaJsonPath = "$scriptDirectory\release-streams\beta${versionQualifier}.json"
    $latestJsonPath = "$scriptDirectory\release-streams\latest${versionQualifier}.json"
    
    if (Test-Path -Path $betaJsonPath) {
        Copy-Item -Force $betaJsonPath $latestJsonPath
        Write-Host "Copied '$betaJsonPath' to '$latestJsonPath'."

        $latestJsonContent = Get-Content -Path $latestJsonPath -Raw

        # Replace the 'published_at' timestamp with the current time
        $newTimestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
        $latestJsonContent = $latestJsonContent -replace '"published_at": "(.*?)"', ('"published_at": "' + $newTimestamp + '"')

        # Write the updated content back to the latest.json
        Set-Content -Path $latestJsonPath -Value $latestJsonContent
        Write-Host "Updated the 'published_at' field in '$latestJsonPath'."
    } else {
        Write-Host "'$betaJsonPath' not found. Promotion failed." -ForegroundColor Red
    }
}

echo ""
$scriptDirectory = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
$localDir = Join-Path $scriptDirectory "release-streams\local${versionQualifier}"

# If promote flag is set, invoke the promotion function
if ($promote) {
    Promote-Release
}

$version = $version.Trim()
if (-not $version) {
    if (Test-Path -Path "version") {
        $version = (Get-Content -Path "version" -Raw).Trim()
        Write-Host -NoNewline "Version not supplied. Using version from file and incrementing: "
        Write-Host -ForegroundColor Yellow "${version}"

        # Increment the last tuple
        $versionWithoutPrefix = $version -replace '^v', ''
        $segments = $versionWithoutPrefix -split '\.'
        $segments[-1] = [int]$segments[-1] + 1
        $version = ($segments -join '.')

        Write-Host -NoNewline "New Version: "
        Write-Host -ForegroundColor Green "$version"
        # Check if the 'version' file has changes in git
        $gitStatus = git status --porcelain "version"

        if (-not $gitStatus) {
            # File has not been modified in git, proceed to update it
            Set-Content -Path "version" -Value $version -NoNewline
        } else {
            Write-Host "The version file has changes in git. Update skipped." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Version file not found" -ForegroundColor Red
        exit 1
    }
} else {
    # Regex to match semantic versioning pattern
    if ($version -notmatch '^v?\d+(\.\d+){0,3}$') {
        Write-Host -ForegroundColor Red "Invalid version format [${version}]. Expected a semantic version (e.g., 1.0.0)."
        exit 1
    }
	Write-Host -NoNewline "Version: "
    Write-Host -ForegroundColor Green "$version"
}

Set-Content -Path "version" -Value $version -NoNewline
Write-Host "Updating version file to version: $version" -ForegroundColor Yellow

$outputPath = "$scriptDirectory\release-streams\${version}.json"
& .\Installer\output-build-json.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -outputPath:$outputPath

Copy-Item -Force "$scriptDirectory\release-streams\${version}.json" "$scriptDirectory\release-streams\${stream}.json"
echo "json file written to: $scriptDirectory\release-streams\${stream}.json"

if(! $jsonOnly) {
  & .\Installer\build.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -jsonOnly:$jsonOnly -revertGitAfter:$revertGitAfter -versionQualifier:$versionQualifier -Win32Crypto:$Win32Crypto
  $exitCode = $LASTEXITCODE
  if($exitCode -gt 0) {
    Write-Host -ForegroundColor Red "ERROR:"
    Write-Host -ForegroundColor Red "  - build.ps1 failed!"
    exit $exitCode
  }
  
  mkdir "${localDir}\${version}" -ErrorAction Ignore > $null
  Move-Item -Force "$scriptDirectory/Installer/Output/Ziti Desktop Edge Client-${version}.exe" "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe"
  Move-Item -Force "$scriptDirectory/Installer/Output/Ziti Desktop Edge Client-${version}.exe.sha256" "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe.sha256"
  Write-Host ""
  Write-Host "done."
  Write-Host "installer exists at $localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe"
}

if($revertGitAfter) {
  git checkout DesktopEdge/Properties/AssemblyInfo.cs ZitiUpdateService/Properties/AssemblyInfo.cs Installer/ZitiDesktopEdge.aip
}

$localUrl="http://localhost:8000/release-streams/local${versionQualifier}"
& .\Installer\output-build-json.ps1 -version:$version -url:$localUrl -stream:$stream -published_at:$published_at -outputPath:"${localDir}\local.json"

Write-Host "Start a python server in this location with:"
Write-Host "" 
Write-Host "  python -m http.server 8000"
Write-Host "" 
Write-Host "Set the automatic upgrade url to http://localhost:8000/release-streams/local${versionQualifier}.json"
Write-Host "" 
