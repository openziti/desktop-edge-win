param (
    [switch]$ClearIdentitiesOk,
    [string]$ZitiHome,
    [string]$Url,
    [string]$Username,
    [string]$Password,
    [string]$RouterName,
    [string]$ExternalId
)

function waitForConfirm() {
    param (
        [string]$msg
    )
    Write-Host $msg
    [void][System.Console]::ReadLine()
}

function cleanService {
    param (
        [string]$svcName
    )
    ziti edge delete identities where ('name contains \"'+${svcName}+'\" limit none')
    ziti edge delete service where ('name contains \"'+${svcName}+'\" limit none')
    ziti edge delete service-policy where ('name contains \"'+${svcName}+'\" limit none')
    ziti edge delete config where ('name contains \"'+${svcName}+'\" limit none')
    ziti edge delete posture-check where ('name contains \"'+${svcName}+'\" limit none')
}

if (-not $ClearIdentitiesOk) {
    Write-Host -ForegroundColor Red "CLEAR_IDENTITIES_OK parameter not  set."
    Write-Host -ForegroundColor Red "  you MUST pass -ClearIdentitiesOk when running this script or it won't run."
    Write-Host -ForegroundColor Red "  This script deletes identities from C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry"
    Write-Host -ForegroundColor Red " "
    Write-Host -ForegroundColor Red "  YOU WERE WARNED"
    Write-Host -ForegroundColor Red "  Example: .\YourScript.ps1 -ClearIdentitiesOk"
    return
} else {
    Write-Host -ForegroundColor Green "-ClearIdentitiesOk detected. continuing..."
}

$envFile = ".env.ps1"
if (Test-Path $envFile) {
    . $envFile
} else {
    Write-Host "Add credentials to .env.ps1 to store Username/Password"
}

$startZiti = $true
$prefix = "zitiquickstart"
$zitiUser=""
$zitiPwd=""
$zitiCtrl="localhost:1280"
$caAutoId="tpca-test-autoId"
$caMappedId="tpca-test-mappedId"
$routerIdentity = ""
$autoCa="auto-ca"
$mappedCa="mapped-ca"
$zitiPkiRoot = "${ZitiHome}\pki"
$identityDir = "${ZitiHome}\identities"

if (${Url}) {
    if(-not $RouterName) {
        Write-Host -ForegroundColor Red "RouterName not set! -RouterName required when using -Url"
        return
    }
    $routerIdentity = $RouterName

    $startZiti = $false
    if (-not ${Url}.StartsWith("http")) {
        $Url = "https://${Url}"
    }
    $zitiCtrl = ${Url}
    $Url = $Url.TrimEnd("/")
}

# use params first...
if (${Username}) { $zitiUser = ${Username} }
if (${Password}) { $zitiPwd = ${Password} }

# use values read from file
if (-not ${zitiUser}) { $zitiUser = ${ZITI_USER} }
if (-not ${zitiPwd}) { $zitiPwd = ${ZITI_PASS} }

# use values in environment
if (-not ${zitiUser}) { $zitiUser = ${env:ZITI_USER} }
if (-not ${zitiPwd}) { $zitiPwd = ${env:ZITI_PASS} }

# fallback to defaults
if (-not ${zitiUser}) { $zitiUser="admin" }
if (-not ${zitiPwd}) { $zitiPwd="admin" }

if (${RouterName}) { $routerName = ${RouterName} }

if(-not $ExternalId) {
    Write-Host -ForegroundColor Yellow "ExternalId not set! using: testuser@test.com"
} else {
    Write-Host -ForegroundColor Blue "ExternalId set to: $ExternalId"
}

$loginOutput = ziti edge login $zitiCtrl -u $ZITI_USER -p $ZITI_PASS -y
$token = ($loginOutput | Select-String -Pattern "Token: (\S+)").Matches.Groups[1].Value


if ($LASTEXITCODE -gt 0) {
    Write-Host -ForegroundColor Red "Could not authenticate! Check username/password/url"
    return
}

if($startZiti) {
    echo "starting reset"
    taskkill /f /im ziti.exe
}

if (-not ${ZitiHome}) {
    ${ZitiHome} = [System.IO.Path]::GetTempPath() + "zdew-" + ([System.Guid]::NewGuid().ToString())
} else {
    $ZitiHome = $ZitiHome.TrimEnd("\")
    $identityDir = "${ZitiHome}\identities"
    echo "removing any .jwt/.json files at: ${ZitiHome}"
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
    
    Write-Host -ForegroundColor Blue "TEMP DIR: ${ZitiHome}"
    waitForConfirm("Ready to proceed? Press Enter to continue...")
}

function cleanController {
    # ziti edge delete identities where 'name contains \"mfa\"' limit none
    # ziti edge delete service where 'name contains \"mfa\"' limit none
    # ziti edge delete service-policy where 'name contains \"mfa\"' limit none
    # ziti edge delete config where 'name contains \"mfa\"' limit none
    # ziti edge delete posture-check where 'name contains \"mfa\"' limit none
    cleanService "mfa"

    # ziti edge delete identities where 'name contains \"normal\"' limit none
    # ziti edge delete service where 'name contains \"normal\"' limit none
    # ziti edge delete service-policy where 'name contains \"normal\"' limit none
    # ziti edge delete config where 'name contains \"normal\"' limit none
    cleanService "normal"
    cleanService "${autoCa}"
    
    ziti edge delete identities where 'name contains \"ejs\"'
    ziti edge delete ext-jwt-signer where 'name contains \"ejs\"'

    ziti edge delete identity where ('name contains \"' + $caAutoId + '\"')
    ziti edge delete ca "$caAutoId"

    ziti edge delete identity where ('name contains \"' + $caMappedId + '\"')
    ziti edge delete ca "$caMappedId"

    ziti edge delete auth-policy "cert-primary-totp-auth-policy"
    ziti edge delete auth-policy where 'name contains \"ejs\"'

    waitForConfirm("Delete complete. Press Enter to continue...")
}

mkdir ${ZitiHome} -Force > $NULL
if($startZiti) {
    $logFile = "${ZitiHome}\quickstart.txt"
    Write-Host -ForegroundColor Blue "ZITI LOG FILE: $logFile"
    Start-Process "ziti" "edge quickstart --home ${ZitiHome}" -NoNewWindow *>&1 -RedirectStandardOutput $logFile
    $routerIdentity = "quickstart-router"
} else {
    cleanController
}

Write-Host "URL: $zitiCtrl"
$uri = [System.Uri]::new($zitiCtrl)
$hostname = $uri.Host
$port = $uri.Port

$delay = 1 # Delay in seconds
mkdir $identityDir -ErrorAction SilentlyContinue > $NULL

while ($true) {
    $socket = New-Object Net.Sockets.TcpClient
    try {
        $socket.Connect($hostname, $port)
        Write-Output "Controller at ${hostname}:${port} is online."
        $socket.Close()
        break
    } catch {
        Write-Output "Waiting for ${hostname}:${port}..."
        Start-Sleep -Seconds $delay
    } finally {
        $socket.Dispose()
    }
}

function routerOffloadPolicy {
    param (
        [string]$router
    )
    ziti edge delete service-policy "${router}.offload"
	ziti edge create service-policy "${router}.offload" Bind --identity-roles "@${router}" --service-roles "#router-offloaded"
}

function makeTestService {
    param (
        [string]$user,
        [string]$ordinal,
        [string[]]$attrs = @(),
        [string]$binder = "@${user}.svc.${ordinal}.ziti",
        [string]$dialer = "@${user}"
        )
	    $svc = "${user}.svc.${ordinal}.ziti"
    Write-host "Creating test service: ${svc} for user: ${user}"
    $allAttrs = @("router-offloaded") + $attrs
    $attrString = ($allAttrs | ForEach-Object { "`"$_`"" }) -join ","

	ziti edge create config "${svc}.intercept.v1" intercept.v1 "{\""protocols\"":[\""tcp\""],\""addresses\"":[\""${svc}\""], \""portRanges\"":[{\""low\"":80, \""high\"":443}]}"
    ziti edge create config "${svc}.host.v1" host.v1 "{\""protocol\"":\""tcp\"", \""address\"":\""localhost\"",\""port\"":${port} }"
    ziti edge create service "${svc}" --configs "${svc}.intercept.v1","${svc}.host.v1" --role-attributes "${attrString}"
	ziti edge create service-policy "${svc}.dial" Dial --identity-roles "${dialer}" --service-roles "@${svc}"
	#ziti edge create service-policy "${svc}.binder" Dial --identity-roles "${binder}" --service-roles "@${svc}"
	# replaced withrouterOffloadPolicy: ziti edge create service-policy "$svc.bind" Bind --identity-roles "@${routerName}" --service-roles "@$svc"
}

$authPolicy=(ziti edge create auth-policy "cert-primary-totp-auth-policy" `
    --primary-cert-allowed `
    --secondary-req-totp `
    --primary-cert-expired-allowed)

routerOffloadPolicy "${routerName}"

function createMfaRelatedIdentities {
    $count = 0
    $iterations = 3
    for ($i = 0; $i -lt $iterations; $i++) {
        $id = "mfa-$count"
        ziti edge create identity "$id" --auth-policy "$authPolicy" -o "$identityDir\$id.jwt"
        $count++
        echo "$id"
    }

    $param1Range = 0..2
    foreach ($i in $param1Range) {
        foreach ($j in 1..$i) {
            makeTestService "mfa-$i" "$(if ($j -lt 10) {"0$j"} else {$j})"
        }
    }

    # make a user that needs mfa for a posture check and the posture check times out quickly
    $name="mfa-with-imeout"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --seconds 60
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    # make a user that needs mfa for a posture check and the posture check triggers on lock
    $name="mfa-onunlock"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --unlock 
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    # make a user that needs mfa for a posture check and the posture check triggers on wake
    $name="mfa-onwake"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --wake
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"
}

function createNormalUsers {
    # make a few regular ol users, nothing special...
    $name="normal-user-01"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create service-policy "normal.dial" Dial `
        --identity-roles "#all" `
        --service-roles "@${name}.svc.0.ziti"

    $name="normal-user-02"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"

    $name="normal-user-03"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
}

function createExternalJwtEntities {
    $extJwtSignerRoot = "https://keycloak.clint.demo.openziti.org:8446/realms/zitirealm"
    $extJwtDiscoveryEndpoint = "$extJwtSignerRoot/.well-known/openid-configuration"
    $extJwtClaimsProp = "email"
    $extJwtAudience = "openziti"
    $extJwtClientId = "openziti-client"
    $extJwtAuthUrl = "$extJwtSignerRoot"
    $extJwtScopes = "openid,profile,email"

    $extJwtSigner = curl -s $extJwtDiscoveryEndpoint `
        | ConvertFrom-Json
    ziti edge create ext-jwt-signer ejs-zdew-test $extJwtSigner.issuer `
        --jwks-endpoint $extJwtSigner.jwks_uri `
        --audience $extJwtAudience `
        --claims-property $extJwtClaimsProp `
        --client-id $extJwtClientId `
        --external-auth-url $extJwtAuthUrl `
        --scopes $extJwtScopes

    ziti edge create auth-policy ejs-auth-policy-primary `
        --primary-ext-jwt-allowed

    ziti edge create identity ejs-test-id --external-id $ExternalId --role-attributes "ejwt-svcs"
    
    cleanService "ext-jwt-svc"
    makeTestService -user "ext-jwt-svc" -ordinal "0" -dialer "#ejwt-svcs"

    
    # get the network jwt for use with ext-auth
    $network_jwt="${identityDir}\${hostname}_${port}.jwt"
    $json = curl -sk "${Url}/edge/management/v1/network-jwts"
    Set-Content -Path $network_jwt -Value ($json | ConvertFrom-Json).data.token 
}

function createCaRelatedEntities {
    makeTestService "${autoCa}" "0" "${autoCa}"
    ziti edge create service-policy "${autoCa}.svc.dial" Dial --identity-roles "#${autoCa}" --service-roles "@${autoCa}.svc.0.ziti"

    ziti pki create ca --pki-root "${zitiPkiRoot}" --ca-file "$caAutoId"
    $rootCa=(Get-ChildItem -Path $zitiPkiRoot -Filter "$caAutoId.cert" -Recurse).FullName
    "root ca path: $rootCa"

    $CA_ID = ziti edge create ca "$caAutoId" "$rootCa" --auth --ottca --autoca --role-attributes "${autoCa}"

    $verificationToken=((ziti edge list cas ('name = \"' + $caAutoId + '\"') -j | ConvertFrom-Json).data | Where-Object { $_.name -eq $caAutoId }[0]).verificationToken
    ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caAutoId" --client-file "$verificationToken" --client-name "$verificationToken"

    $verificationCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$verificationToken.cert" -Recurse).FullName
    ziti edge verify ca $caAutoId --cert $verificationCert
    "verification cert path: $verificationCert"

    # using the ziti CLI - make a client cert for the verificationToken
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user1" --client-file="${caAutoId}-user1"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user2" --client-file="${caAutoId}-user2"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user3" --client-file="${caAutoId}-user3"

    curl -sk -X GET `
        -H "Content-Type: text/plain" `
        -H "zt-session: ${token}" `
        "${Url}/edge/management/v1/cas/${CA_ID}/jwt" > "${identityDir}\${caAutoId}.jwt"

    ziti pki create ca --pki-root "${zitiPkiRoot}" --ca-file "$caMappedId"
    $rootCa=(Get-ChildItem -Path $zitiPkiRoot -Filter "$caMappedId.cert" -Recurse).FullName
    "root ca path: $rootCa"

    $CA_ID = ziti edge create ca "$caMappedId" "$rootCa" --auth --ottca --role-attributes "ott-ca-attrs"

    $verificationToken=((ziti edge list cas ('name = \"' + $caMappedId + '\"') -j | ConvertFrom-Json).data | Where-Object { $_.name -eq $caMappedId }[0]).verificationToken
    ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caMappedId" --client-file "$verificationToken" --client-name "$verificationToken"

    $verificationCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$verificationToken.cert" -Recurse).FullName
    ziti edge verify ca $caMappedId --cert $verificationCert
    "verification cert path: $verificationCert"

    # using the ziti CLI - make a client cert for the verificationToken
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user1" --client-file="${caMappedId}-user1"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user2" --client-file="${caMappedId}-user2"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user3" --client-file="${caMappedId}-user3"

    $idName="${caMappedId}-user1"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"
    
    $idName="${caMappedId}-user2"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"
    
    $idName="${caMappedId}-user3"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"

    curl -sk -X GET `
        -H "Content-Type: text/plain" `
        -H "zt-session: ${token}" `
        "${Url}/edge/management/v1/cas/${CA_ID}/jwt" > "${identityDir}\${caMappedId}.jwt"

    Write-Host -ForegroundColor Blue "IDENTITIES AT: ${identityDir}"
    Write-Host -ForegroundColor Blue " - network-jwts at : ${identityDir}\${hostname}_${port}.jwt"
    Write-Host -ForegroundColor Blue " - CA JWT at       : ${identityDir}\${caAutoId}.jwt"
    Write-Host -ForegroundColor Blue " - CA JWT at       : ${identityDir}\${caMappedId}.jwt"
}

createMfaRelatedIdentities
createNormalUsers
createExternalJwtEntities
createCaRelatedEntities