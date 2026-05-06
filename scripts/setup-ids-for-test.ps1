param (
    [switch]$ClearIdentitiesOk,
    [string]$ZitiHome,
    [string]$Url,
    [string]$Username,
    [string]$Password,
    [string]$RouterName,
    [string]$ExternalId,
    [switch]$Mfa,
    [switch]$Normal,
    [switch]$Ejs,
    [switch]$Ca
)

$ProgressPreference = 'SilentlyContinue'
$transcriptFile = "$PSScriptRoot\setup-ids-for-test.log"
Start-Transcript -Path $transcriptFile -Force
Write-Host -ForegroundColor Cyan "[transcript] logging to: $transcriptFile"

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
    Write-Host -ForegroundColor Cyan "  [clean] deleting identities/service/policies/configs/posture-checks for: ${svcName}"
    ziti edge delete identities where "name contains `"${svcName}`" limit none"
    ziti edge delete service where "name contains `"${svcName}`" limit none"
    ziti edge delete service-policy where "name contains `"${svcName}`" limit none"
    ziti edge delete config where "name contains `"${svcName}`" limit none"
    ziti edge delete posture-check where "name contains `"${svcName}`" limit none"
    Write-Host -ForegroundColor Cyan "  [clean] done: ${svcName}"
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "This script requires PowerShell 7+"
    exit 1
}

$runAll = -not ($Mfa -or $Normal -or $Ejs -or $Ca)

if (-not $ClearIdentitiesOk -and $runAll) {
    Write-Host -ForegroundColor Red "CLEAR_IDENTITIES_OK parameter not  set."
    Write-Host -ForegroundColor Red "  you MUST pass -ClearIdentitiesOk when running this script or it won't run."
    Write-Host -ForegroundColor Red "  This script deletes identities from C:\Windows\System32\config\systemprofile\AppData\Roaming\NetFoundry"
    Write-Host -ForegroundColor Red " "
    Write-Host -ForegroundColor Red "  YOU WERE WARNED"
    Write-Host -ForegroundColor Red "  Example: .\YourScript.ps1 -ClearIdentitiesOk"
    return
} elseif ($ClearIdentitiesOk) {
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
$zitiCtrl=""
$caAutoId="tpca-test-autoId"
$caMappedId="tpca-test-mappedId"
$routerIdentity = ""
$autoCa="auto-ca"
$mappedCa="mapped-ca"
$zitiPkiRoot = ""
$identityDir = ""

if (${Url}) {
    if(-not $RouterName) {
        Write-Host -ForegroundColor Red "RouterName not set! -RouterName required when using -Url"
        return
    }
    $routerIdentity = $RouterName

    $startZiti = $false
    $zitiCtrl = ${Url}
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
if (-not ${zitiCtrl}) {
    if (${env:ZITI_CTRL}) {
        $zitiCtrl = ${env:ZITI_CTRL}
        $startZiti = $false
    }
}

# fallback to defaults
if (-not ${zitiUser}) { $zitiUser="admin" }
if (-not ${zitiPwd}) { $zitiPwd="admin" }
if (-not ${zitiCtrl}) { $zitiCtrl="localhost:1280" }

# ensure https prefix
if (-not $zitiCtrl.StartsWith("http")) { $zitiCtrl = "https://$zitiCtrl" }
$zitiCtrl = $zitiCtrl.TrimEnd("/")

if (${RouterName}) { $routerName = ${RouterName} }
if (-not $routerName) { $routerName = $env:ZITI_ROUTER }
if (-not $ZitiHome)   { $ZitiHome  = $env:ZITI_HOME }

$zitiPkiRoot = "${ZitiHome}\pki"
$identityDir = "${ZitiHome}\identities"

if (-not $ExternalId) { $ExternalId = $env:EXT_ID }
if(-not $ExternalId) {
    Write-Host -ForegroundColor Yellow "ExternalId not set! using: testuser@test.com"
} else {
    Write-Host -ForegroundColor Blue "ExternalId set to: $ExternalId"
}

Write-Host -ForegroundColor Cyan "[login] authenticating to: $zitiCtrl as $zitiUser"
$loginOutput = ziti edge login $zitiCtrl -u $zitiUser -p $zitiPwd -y 2>&1

if ($LASTEXITCODE -gt 0) {
    Write-Host -ForegroundColor Red "Could not authenticate! Check username/password/url"
    return
}
Write-Host -ForegroundColor Green "[login] authenticated successfully"

$tokenMatch = ($loginOutput | Select-String -Pattern "Token: (\S+)")
if ($tokenMatch) {
    $token = $tokenMatch.Matches.Groups[1].Value
} elseif ($env:ZITI_TOKEN) {
    $token = $env:ZITI_TOKEN
} else {
    Write-Host -ForegroundColor Yellow "[login] warning: could not extract token from login output"
}

if($startZiti) {
    Write-Host -ForegroundColor Cyan "[quickstart] killing any existing ziti.exe and starting fresh"
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
    Write-Host -ForegroundColor Cyan "[cleanController] cleaning existing test entities from controller"
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

    Write-Host -ForegroundColor Cyan "  [clean] deleting ejs identities and ext-jwt-signers"
    ziti edge delete identities where 'name contains "ejs"'
    ziti edge delete identity "ejs-test-id"
    if ($ExternalId) {
        ziti edge delete identity where "externalId = `"$ExternalId`""
    }
    ziti edge delete auth-policy where 'name contains "ejs"'
    ziti edge delete ext-jwt-signer where 'name contains "ejs"'
    $keycloakData = (ziti edge list ext-jwt-signers 'name = "keycloak"' -j | ConvertFrom-Json).data
    $keycloakId = if ($keycloakData -and $keycloakData.Count -gt 0) { $keycloakData[0].id } else { $null }
    if ($keycloakId) {
        Write-Host -ForegroundColor Cyan "  [clean] deleting auth policies referencing keycloak signer ($keycloakId)"
        $allPolicies = (ziti edge list auth-policies 'limit none' -j | ConvertFrom-Json).data
        foreach ($policy in $allPolicies) {
            $referencesKeycloak = ($policy.primary.extJwt.allowedSigners -contains $keycloakId) -or
                                  ($policy.secondary.requireExtJwtSigner -eq $keycloakId)
            if ($referencesKeycloak) {
                Write-Host -ForegroundColor Cyan "    [clean] deleting identities referencing auth policy: $($policy.name)"
                ziti edge delete identities where "authPolicyId = `"$($policy.id)`" limit none"
                Write-Host -ForegroundColor Cyan "    [clean] deleting auth policy: $($policy.name) ($($policy.id))"
                ziti edge delete auth-policy $policy.id
            }
        }
        ziti edge delete ext-jwt-signer "keycloak"
    }
    ziti edge delete identity where "name contains `"$caAutoId`""
    ziti edge delete ca "$caAutoId"

    ziti edge delete identity where "name contains `"$caMappedId`""
    ziti edge delete ca "$caMappedId"

    Write-Host -ForegroundColor Cyan "  [clean] deleting auth policies"
    ziti edge delete auth-policy "cert-primary-totp-auth-policy"

    Write-Host -ForegroundColor Green "[cleanController] done"
    waitForConfirm("Delete complete. Press Enter to continue...")
}

Write-Host -ForegroundColor Cyan "[setup] creating home directory: ${ZitiHome}"
mkdir ${ZitiHome} -Force > $NULL
if($startZiti) {
    $logFile = "${ZitiHome}\quickstart.txt"
    Write-Host -ForegroundColor Blue "ZITI LOG FILE: $logFile"
    Write-Host -ForegroundColor Cyan "[quickstart] starting ziti edge quickstart"
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

Write-Host -ForegroundColor Cyan "[setup] creating auth policy: cert-primary-totp-auth-policy"
$authPolicy=(ziti edge create auth-policy "cert-primary-totp-auth-policy" `
    --primary-cert-allowed `
    --secondary-req-totp `
    --primary-cert-expired-allowed)
echo "Auth policy created: cert-primary-totp-auth-policy. --primary-cert-allowed, --secondary-req-totp, --primary-cert-expired-allowed"

function routerOffloadPolicy {
    param (
        [string]$router
    )
    ziti edge delete service-policy "${router}.offload"
	ziti edge create service-policy "${router}.offload" Bind --identity-roles "@${router}" --service-roles "#router-offloaded"
}

Write-Host -ForegroundColor Cyan "[setup] creating router offload policy for: ${routerName}"
if ($routerName) {
    routerOffloadPolicy "${routerName}"
} else {
    Write-Host -ForegroundColor Yellow "  [setup] skipping router offload policy: ZITI_ROUTER not set"
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
     
    ziti edge create config "${svc}.intercept.v1" intercept.v1 "{`"protocols`":[`"tcp`"],`"addresses`":[`"${svc}`"], `"portRanges`":[{`"low`":80, `"high`":443}]}"
    ziti edge create config "${svc}.host.v1" host.v1 "{`"protocol`":`"tcp`", `"address`":`"localhost`",`"port`":${port} }"
     
    ziti edge create service "${svc}" --configs "${svc}.intercept.v1,${svc}.host.v1" --role-attributes "${attrString}"

	ziti edge create service-policy "${svc}.dial" Dial --identity-roles "${dialer}" --service-roles "@${svc}"
	#ziti edge create service-policy "${svc}.binder" Dial --identity-roles "${binder}" --service-roles "@${svc}"
	# replaced withrouterOffloadPolicy: ziti edge create service-policy "$svc.bind" Bind --identity-roles "@${routerName}" --service-roles "@$svc"
}

function createMfaRelatedIdentities {
    Write-Host -ForegroundColor Cyan "[mfa] creating MFA-related identities and services"
    $count = 0
    $iterations = 3
    for ($i = 0; $i -lt $iterations; $i++) {
        $id = "mfa-$count"
        Write-Host -ForegroundColor Cyan "  [mfa] creating identity: $id"
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


    $name="mfa-needed"
    Write-Host -ForegroundColor Cyan "  [mfa] creating identity with MFA posture check: $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    # make a user that needs mfa for a posture check and the posture check times out quickly
    $name="mfa-with-timeout"
    Write-Host -ForegroundColor Cyan "  [mfa] creating identity with MFA posture check (timeout 60s): $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --seconds 60
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    # make a user that needs mfa for a posture check and the posture check triggers on lock
    $name="mfa-onunlock"
    Write-Host -ForegroundColor Cyan "  [mfa] creating identity with MFA posture check (on unlock): $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --unlock
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    # make a user that needs mfa for a posture check and the posture check triggers on wake
    $name="mfa-onwake"
    Write-Host -ForegroundColor Cyan "  [mfa] creating identity with MFA posture check (on wake): $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create posture-check mfa $name --wake
    ziti edge update service-policy "$name.svc.0.ziti.dial" --posture-check-roles "@$name"

    Write-Host -ForegroundColor Green "[mfa] done"
}

function createNormalUsers {
    Write-Host -ForegroundColor Cyan "[normal] creating normal user identities and services"
    # make a few regular ol users, nothing special...
    $name="normal-user-01"
    Write-Host -ForegroundColor Cyan "  [normal] creating identity: $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"
    ziti edge create service-policy "normal.dial" Dial `
        --identity-roles "#all" `
        --service-roles "@${name}.svc.0.ziti"

    $name="normal-user-02"
    Write-Host -ForegroundColor Cyan "  [normal] creating identity: $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"

    $name="normal-user-03"
    Write-Host -ForegroundColor Cyan "  [normal] creating identity: $name"
    ziti edge create identity $name -o "$identityDir\$name.jwt"
    makeTestService $name "0"

    Write-Host -ForegroundColor Green "[normal] done"
}

function createExternalJwtEntities {
    Write-Host -ForegroundColor Cyan "[ejs] creating external JWT signer entities"
    $extJwtSignerRoot = "https://keycloak.zrok.clint.demo.openziti.org:8446/realms/zitirealm"
    $extJwtDiscoveryEndpoint = "$extJwtSignerRoot/.well-known/openid-configuration"
    $extJwtClaimsProp = "email"
    $extJwtAudience = "openziti"
    $extJwtClientId = "openziti-client"
    $extJwtAuthUrl = "$extJwtSignerRoot"
    $extJwtScopes = "openid,profile,email"

    Write-Host -ForegroundColor Cyan "  [ejs] fetching OIDC discovery document from: $extJwtDiscoveryEndpoint"
    $extJwtSigner = curl.exe -s $extJwtDiscoveryEndpoint | ConvertFrom-Json
    if (-not $extJwtSigner -or -not $extJwtSigner.issuer -or -not $extJwtSigner.jwks_uri) {
        Write-Host -ForegroundColor Red "ERROR: Failed to retrieve or parse JWT discovery document: $extJwtDiscoveryEndpoint"
        Write-Host -ForegroundColor Red "  skipping ext-jwt-signer creation"
        return
    }

    $existingKeycloak = (ziti edge list ext-jwt-signers 'name = "keycloak"' -j | ConvertFrom-Json).data
    if ($existingKeycloak -and $existingKeycloak.Count -gt 0) {
        Write-Host -ForegroundColor Yellow "  [ejs] keycloak ext-jwt-signer already exists, skipping create"
    } else {
        Write-Host -ForegroundColor Cyan "  [ejs] creating ext-jwt-signer: keycloak (issuer: $($extJwtSigner.issuer))"
        ziti edge create ext-jwt-signer keycloak $($extJwtSigner.issuer) `
            --jwks-endpoint $($extJwtSigner.jwks_uri) `
            --audience $extJwtAudience `
            --claims-property $extJwtClaimsProp `
            --client-id $extJwtClientId `
            --external-auth-url $extJwtAuthUrl `
            --scopes $extJwtScopes `
            --verbose
    }

    Write-Host -ForegroundColor Cyan "  [ejs] creating auth policy: ejs-auth-policy-primary"
    ziti edge create auth-policy ejs-auth-policy-primary --primary-ext-jwt-allowed

    Write-Host -ForegroundColor Cyan "  [ejs] creating identity: ejs-test-id (external-id: $ExternalId)"
    ziti edge create identity ejs-test-id --external-id $ExternalId --role-attributes "ejwt-svcs"

    cleanService "ext-jwt-svc"
    makeTestService -user "ext-jwt-svc" -ordinal "0" -dialer "#ejwt-svcs"

    # get the network jwt for use with ext-auth
    Write-Host -ForegroundColor Cyan "  [ejs] fetching network JWT"
    $network_jwt="${identityDir}\${hostname}_${port}.jwt"
    $json = curl.exe -sk "${Url}/edge/management/v1/network-jwts"
    Set-Content -Path $network_jwt -Value ($json | ConvertFrom-Json).data.token
    Write-Host -ForegroundColor Green "[ejs] done"
}

function createCaRelatedEntities {
    Write-Host -ForegroundColor Cyan "[ca] creating CA-related entities"

    Write-Host -ForegroundColor Cyan "  [ca] creating auto-CA service and policy"
    makeTestService "${autoCa}" "0" @("${autoCa}") -dialer "#${autoCa}"
    ziti edge create service-policy "${autoCa}.svc.dial" Dial --identity-roles "#${autoCa}" --service-roles "@${autoCa}.svc.0.ziti"

    Write-Host -ForegroundColor Cyan "  [ca] creating PKI root CA: $caAutoId"
    if (Test-Path "${zitiPkiRoot}\${caAutoId}") {
        Remove-Item "${zitiPkiRoot}\${caAutoId}" -Recurse -Force
    }
    ziti pki create ca --pki-root "${zitiPkiRoot}" --ca-file "$caAutoId"
    $rootCa=(Get-ChildItem -Path $zitiPkiRoot -Filter "$caAutoId.cert" -Recurse).FullName
    "root ca path: $rootCa"

    Write-Host -ForegroundColor Cyan "  [ca] registering auto-enroll CA: $caAutoId"
    $CA_ID = ziti edge create ca "$caAutoId" "$rootCa" --auth --ottca --autoca --role-attributes "${autoCa}"

    Write-Host -ForegroundColor Cyan "  [ca] verifying CA: $caAutoId"
    $verificationToken=((ziti edge list cas "name = `"$caAutoId`"" -j | ConvertFrom-Json).data | Where-Object { $_.name -eq $caAutoId }[0]).verificationToken
    ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caAutoId" --client-file "$verificationToken" --client-name "$verificationToken"

    $verificationCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$verificationToken.cert" -Recurse).FullName
    ziti edge verify ca $caAutoId --cert $verificationCert
    "verification cert path: $verificationCert"

    Write-Host -ForegroundColor Cyan "  [ca] creating client certs for $caAutoId users"
    # using the ziti CLI - make a client cert for the verificationToken
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user1" --client-file="${caAutoId}-user1"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user2" --client-file="${caAutoId}-user2"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caAutoId}" --client-name="${caAutoId}-user3" --client-file="${caAutoId}-user3"

    Write-Host -ForegroundColor Cyan "  [ca] fetching CA JWT for: $caAutoId"
    curl.exe -sk -X GET `
        -H "Content-Type: text/plain" `
        -H "zt-session: ${token}" `
        "${Url}/edge/management/v1/cas/${CA_ID}/jwt" > "${identityDir}\${caAutoId}.jwt"

    Write-Host -ForegroundColor Cyan "  [ca] creating PKI root CA: $caMappedId"
    if (Test-Path "${zitiPkiRoot}\${caMappedId}") {
        Remove-Item "${zitiPkiRoot}\${caMappedId}" -Recurse -Force
    }
    ziti pki create ca --pki-root "${zitiPkiRoot}" --ca-file "$caMappedId"
    $rootCa=(Get-ChildItem -Path $zitiPkiRoot -Filter "$caMappedId.cert" -Recurse).FullName
    "root ca path: $rootCa"

    Write-Host -ForegroundColor Cyan "  [ca] registering mapped CA: $caMappedId"
    $CA_ID = ziti edge create ca "$caMappedId" "$rootCa" --auth --ottca --role-attributes "ott-ca-attrs"

    Write-Host -ForegroundColor Cyan "  [ca] verifying CA: $caMappedId"
    $verificationToken=((ziti edge list cas "name = `"$caMappedId`"" -j | ConvertFrom-Json).data | Where-Object { $_.name -eq $caMappedId }[0]).verificationToken
    ziti pki create client --pki-root "${zitiPkiRoot}" --ca-name "$caMappedId" --client-file "$verificationToken" --client-name "$verificationToken"

    $verificationCert=(Get-ChildItem -Path $zitiPkiRoot -Filter "$verificationToken.cert" -Recurse).FullName
    ziti edge verify ca $caMappedId --cert $verificationCert
    "verification cert path: $verificationCert"

    Write-Host -ForegroundColor Cyan "  [ca] creating client certs for $caMappedId users"
    # using the ziti CLI - make a client cert for the verificationToken
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user1" --client-file="${caMappedId}-user1"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user2" --client-file="${caMappedId}-user2"
    ziti pki create client --pki-root="${zitiPkiRoot}" --ca-name="${caMappedId}" --client-name="${caMappedId}-user3" --client-file="${caMappedId}-user3"

    Write-Host -ForegroundColor Cyan "  [ca] creating mapped-CA identities"
    $idName="${caMappedId}-user1"
    Write-Host -ForegroundColor Cyan "    [ca] creating identity: $idName"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"

    $idName="${caMappedId}-user2"
    Write-Host -ForegroundColor Cyan "    [ca] creating identity: $idName"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"

    $idName="${caMappedId}-user3"
    Write-Host -ForegroundColor Cyan "    [ca] creating identity: $idName"
    ziti edge create identity "${idName}" `
        -o "${identityDir}\${idName}.jwt" `
        --auth-policy "$authPolicy" `
        --external-id "${idName}"

    Write-Host -ForegroundColor Cyan "  [ca] fetching CA JWT for: $caMappedId"
    curl.exe -sk -X GET `
        -H "Content-Type: text/plain" `
        -H "zt-session: ${token}" `
        "${Url}/edge/management/v1/cas/${CA_ID}/jwt" > "${identityDir}\${caMappedId}.jwt"

    Write-Host -ForegroundColor Green "[ca] done"
    Write-Host -ForegroundColor Blue "IDENTITIES AT: ${identityDir}"
    Write-Host -ForegroundColor Blue " - network-jwts at : ${identityDir}\${hostname}_${port}.jwt"
    Write-Host -ForegroundColor Blue " - CA JWT at       : ${identityDir}\${caAutoId}.jwt"
    Write-Host -ForegroundColor Blue " - CA JWT at       : ${identityDir}\${caMappedId}.jwt"
}

Write-Host -ForegroundColor Cyan "[main] === starting test identity setup ==="
if ($runAll -or $Mfa)    { createMfaRelatedIdentities }
if ($runAll -or $Normal) { createNormalUsers }
if ($runAll -or $Ejs)    { createExternalJwtEntities }
if ($runAll -or $Ca)     { createCaRelatedEntities }
Write-Host -ForegroundColor Green "[main] === test identity setup complete ==="
try { Stop-Transcript } catch { }
