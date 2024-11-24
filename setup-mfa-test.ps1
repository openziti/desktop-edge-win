param (
    [switch]$ClearIdentitiesOk,
    [string]${ZitiHome}
)

if (-not $ClearIdentitiesOk) {
    Write-Host -ForegroundColor Red "CLEAR_IDENTITIES_OK parameter not set."
    Write-Host -ForegroundColor Red "  you MUST pass -ClearIdentitiesOk when running this script or it won't run."
    Write-Host -ForegroundColor Red "  This script deletes identities from C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry"
    Write-Host -ForegroundColor Red " "
    Write-Host -ForegroundColor Red "  YOU WERE WARNED"
    Write-Host -ForegroundColor Red "  Example: .\YourScript.ps1 -ClearIdentitiesOk"
    return
} else {
    Write-Host -ForegroundColor Green "-ClearIdentitiesOk detected. continuing..."
}

echo "starting reset"
taskkill /f /im ziti.exe

$prefix = "zitiquickstart"

if (-not ${ZitiHome}) {
    ${ZitiHome} = [System.IO.Path]::GetTempPath() + "zdew-" + ([System.Guid]::NewGuid().ToString())
    $zitiPkiRoot = "${ZitiHome}\pki"
    $identityDir = "${ZitiHome}\identities"
} else {
    $ZitiHome = $ZitiHome.TrimEnd("\")
    $zitiPkiRoot = "${ZitiHome}\pki"
    $identityDir = "${ZitiHome}\identities"
    echo "removing any .jwt/.json files at: ${ZitiHome}"
    Remove-Item "${ZitiHome}\*.json"
    Remove-Item "${ZitiHome}\*.jwt"
    if (Test-Path "${ZitiHome}\pki") {
        Remove-Item "${ZitiHome}\pki" -Recurse -Force -ErrorAction Continue > $null
    } else {
        Write-Host "Nothing found to remove: ${ZitiHome}\pki"
    }
    if (Test-Path "${ZitiHome}\identities") {
        Remove-Item "${ZitiHome}\identities" -Recurse -Force -ErrorAction Continue > $null
    } else {
        Write-Host "Nothing found to remove: ${ZitiHome}\identities"
    }
    if (Test-Path "${ZitiHome}\db") {
        Remove-Item "${ZitiHome}\db" -Recurse -Force -ErrorAction Continue > $null
    } else {
        Write-Host "Nothing found to remove: ${ZitiHome}\db"
    }
    

Write-Host "Press Enter to continue..."
[void][System.Console]::ReadLine()
    #echo "removing C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\config*.json"
    #Remove-Item "C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\config*.json"
   
    #echo "removing ${env:APPDATA}\NetFoundry\*.json"
    #Remove-Item "${env:APPDATA}\NetFoundry\*.json"
}

mkdir ${ZitiHome} -Force > $NULL
$logFile = "${ZitiHome}\quickstart.txt"
Write-Host -ForegroundColor Blue "TEMP DIR: ${ZitiHome}"
Write-Host -ForegroundColor Blue "ZITI LOG FILE: $logFile"

#Start-Process cmd.exe '/c ziti edge quickstart > NUL"' -NoNewWindow
#Start-Process "ziti" "edge quickstart" -NoNewWindow -RedirectStandardError $logFile -RedirectStandardInput $logFile
Start-Process "ziti" "edge quickstart --home ${ZitiHome}" -NoNewWindow *>&1 -RedirectStandardOutput $logFile

$hostname = "localhost"
$port = 1280
$delay = 1 # Delay in seconds
#Remove-Item "C:\temp\support\discourse\2790\pki" -Recurse -Force -ErrorAction SilentlyContinue
mkdir $identityDir -ErrorAction SilentlyContinue > $NULL

while ($true) {
    $socket = New-Object Net.Sockets.TcpClient
    try {
        $socket.Connect($hostname, $port)
        Write-Output "Port $port on $hostname is online."
        $socket.Close()
        break
    } catch {
        Write-Output "Port $port on $hostname is not online. Waiting..."
        Start-Sleep -Seconds $delay
    } finally {
        $socket.Dispose()
    }
}

$caName="my-third-party-ca"
$zitiUser="admin"
$zitiPwd="admin"
$zitiCtrl="localhost:1280"
$caName="my-third-party-ca"

ziti edge login $zitiCtrl -u $zitiUser -p $zitiPwd -y
ziti pki create ca --pki-root "${zitiPkiRoot}" --ca-file "$caName"
$rootCa=(Get-ChildItem -Path $zitiPkiRoot -Filter "$caName.cert" -Recurse).FullName
"root ca path: $rootCa"

ziti edge create ca "$caName" "$rootCa" --auth --ottca

$verificationToken=((ziti edge list cas -j | ConvertFrom-Json).data | Where-Object { $_.name -eq $caName }[0]).verificationToken
ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caName" --client-file "$verificationToken" --client-name "$verificationToken"

$verificationCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$verificationToken.cert" -Recurse).FullName
ziti edge verify ca $caName --cert $verificationCert
"verification cert path: $verificationCert"

$authPolicy=(ziti edge create auth-policy yubi-mfa --primary-cert-allowed --secondary-req-totp --primary-cert-expired-allowed)

$count = 0
$iterations = 5
for ($i = 0; $i -lt $iterations; $i++) {
    $id = "mfa-$count"
    ziti edge create identity "$id" --auth-policy "$authPolicy" -o "$identityDir\$id.jwt"
    $count++
    echo "$id"
}


function makeTestService {
    param (
        [string]$user,
        [string]$ordinal
    )
	$svc = "$user.svc.$ordinal.ziti"
    Write-host "Creating test service: $svc for user: $user"
	ziti edge create config "$svc.intercept.v1" intercept.v1 "{\""protocols\"":[\""tcp\""],\""addresses\"":[\""$svc\""], \""portRanges\"":[{\""low\"":80, \""high\"":443}]}"
    ziti edge create config "$svc.host.v1" host.v1 "{\""protocol\"":\""tcp\"", \""address\"":\""localhost\"",\""port\"":$port }"
	ziti edge create service "$svc" --configs "$svc.intercept.v1","$svc.host.v1"
	ziti edge create service-policy "$svc.dial" Dial --identity-roles "@$user" --service-roles "@$svc"
	ziti edge create service-policy "$svc.bind" Bind --identity-roles "@quickstart-router" --service-roles "@$svc"
}

$param1Range = 1..3

# Loop through the ranges and call the function
foreach ($i in $param1Range) {
    foreach ($j in 1..$i) {
        makeTestService "mfa-$i" "$(if ($j -lt 10) {"0$j"} else {$j})"
    }
}

# make a user that has NO mfa requirement
ziti edge create identity mfa-not-needed -o "$identityDir\mfa-not-needed.jwt"
#ziti edge create service "mfa-not-needed-svc"
#ziti edge create service-policy "mfa-not-needed.dial" Dial --identity-roles "@mfa-not-needed" --service-roles "@mfa-not-needed-svc"
makeTestService "mfa-not-needed" "0"

# make a user that needs mfa for a posture check
$name="mfa-normal"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge create posture-check mfa $name
ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

# make a user that needs mfa for a posture check and the posture check times out quickly
$name="mfa-to"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge create posture-check mfa $name --seconds 60
ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

# make a user that needs mfa for a posture check and the posture check triggers on lock
$name="mfa-unlock"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge create posture-check mfa $name --unlock 
ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

# make a user that needs mfa for a posture check and the posture check triggers on wake
$name="mfa-wake"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge create posture-check mfa $name --wake
ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"


# make a regular ol users, nothing special...
$name="normal-user-01"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge update service-policy "$name.svc.0.ziti.dial"

$name="normal-user-02"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge update service-policy "$name.svc.0.ziti.dial"

$name="normal-user-03"
ziti edge create identity $name -o "$identityDir\$name.jwt"
makeTestService $name "0"
ziti edge update service-policy "$name.svc.0.ziti.dial"







Write-Host -ForegroundColor Blue "IDENTITIES AT: ${identityDir}"