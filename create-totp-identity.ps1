param (
    [string]$IdentityName = "totp-test",
    [string]$AuthPolicy = "cert-primary-totp-auth-policy"
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

Write-Host -ForegroundColor Cyan "[identity] deleting existing identity: $IdentityName (if present)"
ziti edge delete identity where "name = `"$IdentityName`""

$outJwt = "C:\temp\$IdentityName.jwt"
New-Item -ItemType Directory -Force -Path "C:\temp" > $null

Write-Host -ForegroundColor Cyan "[identity] creating identity: $IdentityName with auth-policy: $AuthPolicy"
ziti edge create identity $IdentityName --auth-policy "$AuthPolicy" -o $outJwt

if ($LASTEXITCODE -gt 0) {
    Write-Host -ForegroundColor Red "[identity] failed to create identity"
    exit 1
}

Write-Host -ForegroundColor Green "[identity] done. JWT saved to: $outJwt"
