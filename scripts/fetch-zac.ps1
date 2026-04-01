# Parameters
param (
    [string]$repo = "openziti/ziti-console", # Replace with the target GitHub repository
    [string]$version # Version number in #.#.# format, or "latest" for the newest release
)

# Variables
$apiUrl = "https://api.github.com/repos/$repo/releases"
$zacDir = $zacDir = Join-Path -Path (Get-Location) -ChildPath "zac"

# Function to download artifact
function Download-Artifact {
    param (
        [string]$tag,
        [string]$artifactName
    )

    $url = "https://github.com/$repo/releases/download/$tag/$artifactName"
    $outputPath = Join-Path -Path $zacDir -ChildPath "$tag.zip"

    if (-not (Test-Path $zacDir)) {
        New-Item -ItemType Directory -Path $zacDir | Out-Null
    }

    Write-Host "Downloading artifact from: $url"
	$ProgressPreference = 'SilentlyContinue' 
    Invoke-WebRequest -Uri $url -OutFile $outputPath
    Write-Host "Artifact saved to: $outputPath"
    return $outputPath
}

# Function to unzip artifact
function Unzip-Artifact {
    param (
        [string]$zipPath,
        [string]$extractTo
    )

    if (-not (Test-Path $zipPath)) {
        Write-Host "Zip file not found: $zipPath"
        exit 1
    }

    if (-not (Test-Path $extractTo)) {	
        New-Item -ItemType Directory -Path $extractTo | Out-Null
    }

    Write-Host "Extracting $zipPath to $extractTo"
	$global:ProgressPreference = "SilentlyContinue"
    Expand-Archive -Path $zipPath -DestinationPath $extractTo -Force | Out-Null
    Write-Host "Extraction complete."
}

# Fetch releases
try {
    $releases = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "PowerShell" }
} catch {
    Write-Host "Error fetching releases: $_"
    exit 1
}

if (-not $version -or $version -eq 'latest') {
    # Find the latest release that matches "app-ziti-console-"
    $latestRelease = $releases | Where-Object { $_.tag_name -match "app-ziti-console-" } | Select-Object -First 1
    if ($latestRelease) {
        Write-Host "Latest release found:"
        Write-Host "Name: $($latestRelease.name)"
        Write-Host "Tag: $($latestRelease.tag_name)"
        Write-Host "URL: $($latestRelease.html_url)"

        $artifactPath = (Download-Artifact -tag $latestRelease.tag_name -artifactName "ziti-console.zip")
		echo "Artfiact downloaded to : $artifactPath"
		$where = (Join-Path -Path $zacDir -ChildPath $latestRelease.tag_name.Replace("app-", ""))
		Unzip-Artifact -zipPath $artifactPath -extractTo $where
	
		Write-Host "      - binding: zac"
		Write-Host "        options:"
		Write-Host "          location: ""$where"""
		Write-Host "          indexFile: index.html"
    } else {
        Write-Host "No releases matching 'app-ziti-console-' found."
    }
    exit 0
}

# Validate version input
if (-not ($version -match '^(\\d+)\\.(\\d+)\\.(\\d+)$')) {
    Write-Host "Invalid version format. Use #.#.# (e.g., 3.7.0)."
    exit 1
}

# Find matching release
$pattern = "app-ziti-console-v$version"
$matchingRelease = $releases | Where-Object { $_.tag_name -match $pattern }

if ($matchingRelease) {
    Write-Host "Matching release found:"
    Write-Host "Name: $($matchingRelease.name)"
    Write-Host "Tag: $($matchingRelease.tag_name)"
    Write-Host "URL: $($matchingRelease.html_url)"

    $artifactPath = Download-Artifact -tag $matchingRelease.tag_name -artifactName "ziti-console.zip"
	$where = (Join-Path -Path $zacDir -ChildPath $matchingRelease.tag_name.Replace("app-", ""))
    Unzip-Artifact -zipPath $artifactPath -extractTo $where
	
	Write-Host "      - binding: zac"
	Write-Host "        options:"
	Write-Host "          location: ""$where"""
	Write-Host "          indexFile: index.html"

} else {
    Write-Host "No matching release found for pattern: $pattern"
}
