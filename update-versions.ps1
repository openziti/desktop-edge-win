echo "Obtaining version information from .\version"
$v=(Get-Content -Path .\version)
echo "          version: $v"
echo ""

rm temp.cs -ErrorAction Ignore
rm temp.aip -ErrorAction Ignore

$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
$assemblyInfoReplaced="${assemblyInfo}.replaced"
$installer="./Installer/ZitiDesktopEdge.aip"
$installerReplaced="${installer}.replaced"

echo "Replacing version in $assemblyInfo into $assemblyInfoReplaced"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace 'Version\("[0-9]*.[0-9]*.[0-9]*.0', "Version\(""${v}.0" | Set-Content -Encoding UTF8 -Path "$assemblyInfoReplaced" -NoNewline
rm $assemblyInfo
mv $assemblyInfoReplaced $assemblyInfo

echo "Replacing version in $installer into $installerReplaced"
(Get-Content -Encoding UTF8 -path $installer -Raw) -replace '"ProductVersion" Value="[0-9]\.[0-9]\.[0-9]"', """ProductVersion"" Value=""${v}""" | Set-Content -Path "${installer}.replaced" -Encoding UTF8 -NoNewline
rm $installer
mv $installerReplaced $installer

