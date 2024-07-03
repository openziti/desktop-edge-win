if ("$env:CLEAR_IDENTITIES_OK" -ne "yes") {
    Write-host -ForegroundColor red "CLEAR_IDENTITIES_OK env var not set."
    Write-host -ForegroundColor red "  you MUST set CLEAR_IDENTITIES_OK=\""yes\"" in the environment or this script won't run"
    Write-host -ForegroundColor red "  This script deletes identities from C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry"
    Write-host -ForegroundColor red " "
    Write-host -ForegroundColor red "  YOU WERE WARNED"
    Write-host -ForegroundColor red "  copy/paste: `$env:CLEAR_IDENTITIES_OK=""yes"""

    return
} else {
    Write-host -ForegroundColor Green "`$env:CLEAR_IDENTITIES_OK=""yes"" detected. continuing..."
}
Remove-Item C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\mfa*.json
Remove-Item C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry\config*.json
Remove-Item "$env:APPDATA\NetFoundry\*.json"
#copy C:\Users\clint\AppData\Roaming\NetFoundry\empty.config.json C:\Users\clint\AppData\Roaming\NetFoundry\config.json


echo "starting reset"

taskkill /f /im ziti.exe
$prefix = "zitiquickstart"
$tempDir = [System.IO.Path]::GetTempPath()
$logFile = "${tempDir}quickstart.txt"
Write-host -ForegroundColor BLUE "LOG FILE: $logFile"

#Start-Process cmd.exe '/c ziti edge quickstart > NUL"' -NoNewWindow
#Start-Process "ziti" "edge quickstart" -NoNewWindow -RedirectStandardError $logFile -RedirectStandardInput $logFile
Start-Process "ziti" "edge quickstart" -NoNewWindow *>&1 -RedirectStandardOutput $logFile

$hostname = "localhost"
$port = 1280
$delay = 1 # Delay in seconds
$zitiPkiRoot="C:\temp\support\discourse\2790\pki"
Remove-Item "C:\temp\support\discourse\2790\pki" -Recurse -Force -ErrorAction SilentlyContinue
$identityDir="c:\temp\mfa"
mkdir $identityDir -ErrorAction SilentlyContinue
Remove-Item "$identityDir\mfa-*.jwt"


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

$newUser="clint"
ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caName" --client-file "$newUser" --client-name "$newUser"
$newUserCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$newUser.cert" -Recurse).FullName
$newUserKey=(Get-ChildItem -Path $zitiPkiRoot -Filter "$newUser.key" -Recurse).FullName
ziti edge create identity $newUser --auth-policy "$authPolicy"
ziti edge create enrollment ottca $newUser $caName

$ottcajwt = (ziti edge list identities "name contains \""$newUser\""" -j | ConvertFrom-Json).data.enrollment.ottca.jwt
Set-Content -Path "$newUser.jwt" -Value $ottcajwt -NoNewline -Encoding ASCII

$count = 0
$iterations = 10
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

$param1Range = 1..5
$param2Range = 1..5

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

















