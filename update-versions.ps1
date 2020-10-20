echo "Obtaining version information from .\version"
$v=(Get-Content -Path .\version)
echo "          version: $v"
echo ""

rm temp.cs -ErrorAction Ignore
rm temp.aip -ErrorAction Ignore

$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
echo "Replacing version in $assemblyInfo"
foreach($line in Get-Content $assemblyInfo) {
  $l = $line -replace '\d*.\d*.\d*.0', "${v}.0"
  echo $l >> temp.cs
}

echo "copying temp.cs into $assemblyInfo"
rm $assemblyInfo
mv temp.cs $assemblyInfo
#(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace '\d*.\d*.\d*.0', "${v}.0" | Set-Content -Path $assemblyInfo -Encoding UTF8 -NoNewline

$installer="./Installer/ZitiDesktopEdge.aip"
echo "Replacing version in $installer into temp.aip"
foreach($line in Get-Content $installer) {
  $l = $line -replace '"ProductVersion" Value="\d*\.\d*\.\d*"', """ProductVersion"" Value=""${v}"""
  echo $l >> temp.aip
}

echo "copying temp.aip into $installer"
rm $installer
mv temp.aip $installer
#(Get-Content -Encoding UTF8 -path $installer -Raw) -replace '"ProductVersion" Value="\d\.\d\.\d"', """ProductVersion"" Value=""${v}""" | Set-Content -Path $installer -Encoding UTF8 -NoNewline
