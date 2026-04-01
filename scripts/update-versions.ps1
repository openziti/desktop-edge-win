param(
    [string]$version
)

function  NormalizeVersion([System.Version] $v) {
    $major = $v.Major
    $minor = $v.Minor
    $build = $v.Build
    $rev = $v.Revision

    if ($major -lt 0) { $major = 0}
    if ($minor -lt 0) { $major = 0}
    if ($build -lt 0) { $build = 0}
    if ($rev -lt 0) { $rev = 0}

    $ver = "$major.$minor.$build.$rev"

    return [System.Version]($ver)
}

echo "==================================== update-versions.ps1 begins ===================================="
if(-not $version.Trim()) {
	echo "Obtaining version information from .\version"
	#$rawVersion=(Get-Content -Path .\version)
	$installerVersion=(Get-Content -Path ${scriptPath}\..\version)
	if($null -ne $env:ZITI_DESKTOP_EDGE_VERSION) {
		echo "ZITI_DESKTOP_EDGE_VERSION is set. Using that: ${env:ZITI_DESKTOP_EDGE_VERSION} instead of version found in file ${installerVersion}"
		$installerVersion=$env:ZITI_DESKTOP_EDGE_VERSION
		echo "Version set to: ${installerVersion}"
	}
} else {
	$installerVersion = $version
}

$v=NormalizeVersion($installerVersion)
echo "          version: $v"
echo ""

$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
$assemblyInfoReplaced="${assemblyInfo}.replaced"
echo "Replacing version in $assemblyInfo into $assemblyInfoReplaced"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace 'Version\("[0-9]*.[0-9]*.[0-9]*.[0-9]*', "Version(""${v}" | Set-Content -Encoding UTF8 -Path "$assemblyInfoReplaced" -NoNewline
Remove-Item $assemblyInfo
Move-Item $assemblyInfoReplaced $assemblyInfo

$assemblyInfo="./ZitiUpdateService/Properties/AssemblyInfo.cs"
$assemblyInfoReplaced="${assemblyInfo}.replaced"
echo "Replacing version in $assemblyInfo into $assemblyInfoReplaced"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace 'Version\("[0-9]*.[0-9]*.[0-9]*.[0-9]*', "Version(""${v}" | Set-Content -Encoding UTF8 -Path "$assemblyInfoReplaced" -NoNewline
Remove-Item $assemblyInfo
Move-Item $assemblyInfoReplaced $assemblyInfo

echo "==================================== update-versions.ps1 complete ===================================="
