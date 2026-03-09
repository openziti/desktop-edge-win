param (
    [string]$IdentityName    = "cert-mfa-test",
    [string]$PostureCheckName = "mfa-posture-check",
    [string]$ServiceName     = "",
    [string]$ServicePolicyName = ""
)

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "This script requires PowerShell 7+"
    exit 1
}

$envFile = ".env.ps1"
if (Test-Path $envFile) {
    . $envFile
} else {
    Write-Host "No .env.ps1 found - using params/environment variables"
}

$zitiUser = $env:ZITI_USER
$zitiPwd  = $env:ZITI_PASS
$zitiCtrl = $env:ZITI_CTRL

if (-not $zitiUser) { $zitiUser = "admin" }
if (-not $zitiPwd)  { $zitiPwd  = "admin" }

if (-not $zitiCtrl) {
    Write-Host -ForegroundColor Red "ZITI_CTRL environment variable not set"
    exit 1
}

if (-not $zitiCtrl.StartsWith("http")) {
    $zitiCtrl = "https://$zitiCtrl"
}
$zitiCtrl = $zitiCtrl.TrimEnd("/")

Write-Host -ForegroundColor Cyan "[login] authenticating to: $zitiCtrl as $zitiUser"
$loginOutput = ziti edge login $zitiCtrl -u $zitiUser -p $zitiPwd -y

if ($LASTEXITCODE -gt 0) {
    Write-Host -ForegroundColor Red "Could not authenticate! Check username/password/url"
    exit 1
}
Write-Host -ForegroundColor Green "[login] authenticated successfully"

# --- Identity ---
Write-Host -ForegroundColor Cyan "[identity] deleting existing identity: $IdentityName (if present)"
ziti edge delete identity where "name = `"$IdentityName`""

$outJwt = "C:\temp\$IdentityName.jwt"
New-Item -ItemType Directory -Force -Path "C:\temp" > $null

Write-Host -ForegroundColor Cyan "[identity] creating cert identity: $IdentityName (default auth policy)"
ziti edge create identity $IdentityName -o $outJwt

if ($LASTEXITCODE -gt 0) {
    Write-Host -ForegroundColor Red "[identity] failed to create identity"
    exit 1
}
Write-Host -ForegroundColor Green "[identity] done. JWT saved to: $outJwt"

# --- MFA Posture Check ---
$existingCheckOutput = ziti edge list posture-checks "name = `"$PostureCheckName`"" 2>$null
if ($existingCheckOutput -match $PostureCheckName) {
    Write-Host -ForegroundColor Yellow "[posture-check] '$PostureCheckName' already exists, skipping creation"
} else {
    Write-Host -ForegroundColor Cyan "[posture-check] creating MFA posture check: $PostureCheckName"
    ziti edge create posture-check mfa $PostureCheckName
    if ($LASTEXITCODE -gt 0) {
        Write-Host -ForegroundColor Red "[posture-check] failed to create MFA posture check"
        exit 1
    }
    Write-Host -ForegroundColor Green "[posture-check] created: $PostureCheckName"
}

# --- Service Policy (optional) ---
if ($ServiceName -and $ServicePolicyName) {
    Write-Host -ForegroundColor Cyan "[service-policy] creating dial policy: $ServicePolicyName"
    ziti edge create service-policy $ServicePolicyName Dial `
        --service-roles "@$ServiceName" `
        --identity-roles "@$IdentityName" `
        --posture-check-roles "@$PostureCheckName"

    if ($LASTEXITCODE -gt 0) {
        Write-Host -ForegroundColor Red "[service-policy] failed to create service policy"
        exit 1
    }
    Write-Host -ForegroundColor Green "[service-policy] created: $ServicePolicyName"
} else {
    Write-Host -ForegroundColor Yellow "[service-policy] skipped (pass -ServiceName and -ServicePolicyName to create)"
}

Write-Host ""
Write-Host -ForegroundColor Green "Done."
Write-Host "  Identity JWT : $outJwt"
Write-Host "  Posture Check: $PostureCheckName (MFA/TOTP required to access services)"
