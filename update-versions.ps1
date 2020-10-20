echo "==================================== update-versions.ps1 begins ===================================="
$invocation = (Get-Variable MyInvocation).Value
$scriptPath = Split-Path $invocation.MyCommand.Path
${scriptPath}

echo "Obtaining version information from ${scriptPath}\version"
$v=(Get-Content -Path ${scriptPath}\version)
echo "          version: $v"
echo ""

rm temp.cs -ErrorAction Ignore
rm temp.aip -ErrorAction Ignore

$assemblyInfo="./DesktopEdge/Properties/AssemblyInfo.cs"
$assemblyInfoReplaced="${assemblyInfo}.replaced"

echo "Replacing version in $assemblyInfo into $assemblyInfoReplaced"
(Get-Content -Encoding UTF8 -path $assemblyInfo -Raw) -replace 'Version\("[0-9]*.[0-9]*.[0-9]*.0', "Version(""${v}.0" | Set-Content -Encoding UTF8 -Path "$assemblyInfoReplaced" -NoNewline
rm $assemblyInfo
mv $assemblyInfoReplaced $assemblyInfo

$ADVINST = "C:\Program Files (x86)\Caphyon\Advanced Installer 17.5\bin\x86\AdvancedInstaller.com"
$ADVPROJECT = "${scriptPath}\Installer\ZitiDesktopEdge.aip"

$action = '/SetVersion'
echo "issuing $ADVINST /edit $ADVPROJECT $action $v - see https://www.advancedinstaller.com/user-guide/set-version.html"
& $ADVINST /edit $ADVPROJECT $action $v

#$installer="./Installer/ZitiDesktopEdge.aip"
#$installerReplaced="${installer}.replaced"
#echo "Replacing version in $installer into $installerReplaced"
#(Get-Content -Encoding UTF8 -path $installer -Raw) -replace '"ProductVersion" Value="[0-9]*\.[0-9]*\.[0-9]*"', """ProductVersion"" Value=""${v}""" | Set-Content -Path "${installer}.replaced" -Encoding UTF8 -NoNewline
#rm $installer
#mv $installerReplaced $installer

git status

echo "==================================== update-versions.ps1 complete ===================================="