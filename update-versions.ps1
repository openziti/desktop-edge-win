echo "Obtaining version information from .\version"
$v=(Get-Content -Path .\version)
echo "          version: $v"
echo ""
$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
echo "Replacing version in $assemblyInfo"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace '\d\.\d\.\d\.', "$v." | Set-Content -Path $assemblyInfo -Encoding UTF8 -NoNewline

$installer="./Installer/ZitiDesktopEdge.aip"
echo "Replacing version in $installer"
(Get-Content -Encoding UTF8 -path $installer -Raw) -replace '"ProductVersion" Value="\d\.\d\.\d"', """ProductVersion"" Value=""${v}""" | Set-Content -Path $installer -Encoding UTF8 -NoNewline
