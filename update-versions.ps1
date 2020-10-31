echo "==================================== update-versions.ps1 begins ===================================="
echo "Obtaining version information from .\version"
$v=(Get-Content -Path .\version)
echo "          version: $v"
echo ""

$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
$assemblyInfoReplaced="${assemblyInfo}.replaced"
echo "Replacing version in $assemblyInfo into $assemblyInfoReplaced"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace 'Version\("[0-9]*.[0-9]*.[0-9]*.0', "Version(""${v}.0" | Set-Content -Encoding UTF8 -Path "$assemblyInfoReplaced" -NoNewline
rm $assemblyInfo
mv $assemblyInfoReplaced $assemblyInfo

echo "==================================== update-versions.ps1 complete ===================================="