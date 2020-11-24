function EndLogs {
echo "=====================================  collecting logs ends  ====================================="
echo ""
}
function ExitWithCode($exitcode) {
  #$host.SetShouldExit($exitcode)
  exit $exitcode
}

$now=(Get-Date).ToString('yyyy-MM-dd_HHmmss')
$logroot="${env:ProgramFiles(x86)}\NetFoundry, Inc\Ziti Desktop Edge\logs"
$ipinfo="${logroot}\ipconfig.info.txt"

$logdest="$logroot\ziti-service\$now"
$logdest
$logsrc="C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\*.log"
$destArchive="${logroot}\logs-${now}.zip"

echo ""
echo "===================================== collecting logs begins ====================================="
echo "    checking for admin privs"
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin=$currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$isAdmin) {
    echo ""
    echo "    ERROR: administrator rights not found"
    echo "           this script requires access to files on the administrator can access"
    echo ""
    EndLogs
    exit 11
}
echo "    ensure folder exists: $logdest"
mkdir $logdest -ErrorAction SilentlyContinue > $null

echo "    issuing ipconfig /all"
echo " "
ipconfig /all > "$ipinfo"

echo "    copying log files...."
echo "       source: $logsrc"
echo "           to: $logdest"
cp $logsrc $logdest

echo " "
echo "    creating archive: ${destArchive}"
compress-archive -Path "${ipinfo}","${logroot}\ziti-service","${logroot}\UI","${logroot}\ZitiMonitorService" -DestinationPath "${destArchive}"

echo " "
echo "    removing log folder: $logdest"
rm -r "$logdest"
echo "    removing ipinfo    : $ipinfo"
rm "$ipinfo"

$Acl = Get-ACL $logroot
$AccessRule= New-Object System.Security.AccessControl.FileSystemAccessRule("everyone","FullControl","ContainerInherit,Objectinherit","none","Allow")
$Acl.AddAccessRule($AccessRule)
Set-Acl $logroot $Acl

echo " "
echo "    Logs collected successfully"
echo "    Log location: ${destArchive}"

EndLogs
