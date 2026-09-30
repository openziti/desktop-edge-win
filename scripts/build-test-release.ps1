# Sample invocations:
# .\build-test-release.ps1 -jsonOnly $true -version 1.1.1
# .\build-test-release.ps1 -jsonOnly $true -version 1.1.1 -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -version 2.2.5 -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -jsonOnly $true -version 2.2.5 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -revertGitAfter $true
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at (Get-Date)
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at "2023-11-02T14:30:00"
# .\build-test-release.ps1 -version 1.2.3 -url https://lnxiskqx49x4.share.zrok.io/local -stream "dev" -published_at "2023-11-02T14:30:00" -Win32Crypto:$true
# .\build-test-release.ps1 -version 1.2.3 -increment patch -url http://sg4.parkplace-via-dhcp:8000/release-streams/local
# .\build-test-release.ps1 -increment minor          # 2.11.7.0 -> 2.12.0.0, taking the current version from the version file
param(
    [string]$version,
    # Which tuple to bump before use. From 2.11.7.0: patch gives 2.11.8.0, minor gives 2.12.0.0, major gives
    # 3.0.0.0, build gives 2.11.7.1. Everything after the bumped tuple resets to zero. Defaults to patch,
    # which is how releases normally move; build exists for the occasional respin of the same release.
    [ValidateSet("major", "minor", "patch", "build")]
    [string]$increment = "patch",
    [string]$url = "http://localhost:8000/release-streams/local",
    [string]$stream = "local",
    [datetime]$published_at = (Get-Date).ToUniversalTime(),
    [bool]$jsonOnly = $false,
    [bool]$revertGitAfter = $true,
    [string]$versionQualifier = "",
    [switch]$promote = $false,  # New parameter for promotion
    [bool]$Win32Crypto = $false, #used to specify which ziti edge tunnel version to pull, openssl or win32crypto-based
    [int]$FastInterval = 0  # if > 0, bakes ALLOWFASTINTERVAL + <N>s UpdateTimer into the installer for dev testing. 0 = disabled.
)

$newTimestamp = $published_at.ToString("yyyy-MM-ddTHH:mm:ssZ")

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
    $betaJsonPath = "$repoRoot\release-streams\beta${versionQualifier}.json"
    $latestJsonPath = "$repoRoot\release-streams\latest${versionQualifier}.json"
    
    if (Test-Path -Path $betaJsonPath) {
        Copy-Item -Force $betaJsonPath $latestJsonPath
        Write-Host "Copied '$betaJsonPath' to '$latestJsonPath'."

        $latestJsonContent = Get-Content -Path $latestJsonPath -Raw

        # Replace the 'published_at' timestamp with the current time
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
$repoRoot = (Resolve-Path "$scriptDirectory\..").Path
$localDir = Join-Path $repoRoot "release-streams\local${versionQualifier}"

# If promote flag is set, invoke the promotion function
if ($promote) {
    Promote-Release
}

$version = $version.Trim()
# Bumps one tuple of a four-part version and resets everything after it, so a minor bump gives 2.12.0.0
# rather than 2.12.7.0. Versions here are major.minor.patch.build.
function Step-Version {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Part
    )

    $index = @{ major = 0; minor = 1; patch = 2; build = 3 }[$Part]
    $segments = ($Version -replace '^v', '') -split '\.'

    $segments[$index] = [int]$segments[$index] + 1
    for ($i = $index + 1; $i -lt $segments.Count; $i++) { $segments[$i] = 0 }

    return ($segments -join '.')
}

# $increment has a default, so an explicit -version is only bumped when -increment was actually passed.
# Without this a plain "-version 1.2.3" would silently build 1.2.4.0.
$incrementRequested = $PSBoundParameters.ContainsKey("increment")

if ($incrementRequested -and $version) {
    $version = Step-Version -Version $version -Part $increment
    Write-Host -NoNewline "Incremented version to: "
    Write-Host -ForegroundColor Green "$version"
}
if (-not $version) {
    if (Test-Path -Path "version") {
        $version = (Get-Content -Path "version" -Raw).Trim()
        Write-Host -NoNewline "Version not supplied. Using version from file and incrementing: "
        Write-Host -ForegroundColor Yellow "${version}"

        $version = Step-Version -Version $version -Part $increment

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

$outputPath = "$repoRoot\release-streams\${version}.json"
& .\Installer\output-build-json.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -outputPath:$outputPath

Copy-Item -Force "$repoRoot\release-streams\${version}.json" "$repoRoot\release-streams\${stream}.json"
echo "json file written to: $repoRoot\release-streams\${stream}.json"

$buildSucceeded = $false

if(! $jsonOnly) {
  & .\Installer\build.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -jsonOnly:$jsonOnly -revertGitAfter:$revertGitAfter -versionQualifier:$versionQualifier -Win32Crypto:$Win32Crypto -FastInterval:$FastInterval
  $exitCode = $LASTEXITCODE
  if($exitCode -gt 0) {
    Write-Host -ForegroundColor Red "ERROR:"
    Write-Host -ForegroundColor Red "  - build.ps1 failed!"
    Write-Host -ForegroundColor Red "  - NOT updating ${localDir}\local.json — the served JSON still points at the previous build."
    exit $exitCode
  }

  mkdir "${localDir}\${version}" -ErrorAction Ignore > $null
  Move-Item -Force "$repoRoot/Installer/Output/Ziti Desktop Edge Client-${version}.exe" "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe"
  Move-Item -Force "$repoRoot/Installer/Output/Ziti Desktop Edge Client-${version}.exe.sha256" "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe.sha256"

  $msiSrc = "$repoRoot/Installer/Output/Ziti Desktop Edge Client-${version}.msi"
  if (Test-Path $msiSrc) {
    Move-Item -Force $msiSrc "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.msi"
    Move-Item -Force "$repoRoot/Installer/Output/Ziti Desktop Edge Client-${version}.msi.sha256" "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.msi.sha256"
  }

  # Confirm the moved EXE actually exists before updating the served JSON.
  $producedExe = "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.exe"
  if (-not (Test-Path $producedExe)) {
    Write-Host -ForegroundColor Red "ERROR: expected installer missing after move: $producedExe"
    Write-Host -ForegroundColor Red "  - NOT updating ${localDir}\local.json — the served JSON still points at the previous build."
    exit 1
  }
  $buildSucceeded = $true

  Write-Host ""
  Write-Host "done."
  Write-Host "installer (EXE) exists at $producedExe"
  if (Test-Path "$localDir\${version}\Ziti.Desktop.Edge.Client-${version}.msi") {
    Write-Host "installer (MSI) exists at $localDir\${version}\Ziti.Desktop.Edge.Client-${version}.msi"
  }
}

if($revertGitAfter) {
  git checkout DesktopEdge/Properties/AssemblyInfo.cs ZitiUpdateService/Properties/AssemblyInfo.cs Installer/ZitiDesktopEdge.aip
}

# Only update the served local.json when either:
#   * this was a jsonOnly run (no build attempted, caller explicitly asked for JSON), or
#   * the build + moves completed and the installer EXE is sitting in its expected place.
# Updating unconditionally would leave the test machine fetching a local.json that
# points to a non-existent installer after any build failure.
if ($jsonOnly -or $buildSucceeded) {
  & .\Installer\output-build-json.ps1 -version:$version -url:$url -stream:$stream -published_at:$published_at -outputPath:"${localDir}\local.json"
}

$builtConfig = "$repoRoot\ZitiUpdateService\bin\Release\ZitiUpdateService.exe.config"
$builtUpdateTimer = if (Test-Path $builtConfig) {
    ((Get-Content $builtConfig) | Select-String 'key="UpdateTimer"' | Select-Object -Last 1).ToString().Trim()
} else { "(ZitiUpdateService.exe.config not found at $builtConfig)" }

$summary = @"
============================================================
  BUILD SUMMARY
============================================================
  version            = $version
  url                = $url
  stream             = $stream
  published_at       = $newTimestamp
  jsonOnly           = $jsonOnly
  revertGitAfter     = $revertGitAfter
  FastInterval       = $(if ($FastInterval -gt 0) { "$FastInterval seconds" } else { '(disabled)' })
  Win32Crypto        = $Win32Crypto
  versionQualifier   = $(if ($versionQualifier) { $versionQualifier } else { '(none)' })
  localDir           = $localDir
  builtUpdateTimer   = $builtUpdateTimer
============================================================
"@

$summaryFile = "$scriptDirectory\build-summary-${version}.txt"
$summary | Set-Content $summaryFile
Write-Host $summary -ForegroundColor Cyan

Write-Host "Start a python server in this location with:"
Write-Host ""
Write-Host "  python -m http.server 8000"
Write-Host ""
Write-Host "Set the automatic upgrade url to ${url}/local.json"
Write-Host ""
