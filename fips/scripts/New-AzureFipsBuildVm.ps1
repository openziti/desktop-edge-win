<#
.SYNOPSIS
    Creates an Azure Windows 10 Pro VM, installs the pinned toolchain, builds the OpenSSL FIPS Provider on it
    over SSH, and copies the artifacts and evidence back to this machine.

.DESCRIPTION
    Azure CLI creates the resources. Everything inside the guest runs over SSH, sent as an encoded command
    so nothing depends on scp or on quoting surviving three layers of shell. Output streams live rather than
    arriving as a truncated buffer at the end, and a 45-minute Visual Studio install is not fighting a
    command timeout.

    'az vm run-command' is used exactly once, to bootstrap OpenSSH and authorise your key. After that the
    Azure agent is not involved.

    Safe to re-run at any point. Every step checks what already exists before acting, so an interrupted run
    is resumed by issuing the same command again.

    Why Windows 10 Pro and Visual Studio 2019: section 5.3 of the #4985 security policy records
    "Windows 10: Visual Studio 2019" as the compiler used for the tested Windows environment. The image SKU
    win10-22h2-pro-g2 and the VS 2019 pin in Build-FipsProvider.ps1 match it on both axes.

    Windows 10 client images in Azure are licensed for development and testing under a Visual Studio
    subscription. --license-type Windows_Client asserts that entitlement; creation fails without it.

.PARAMETER Action
    All       -- create, prep, provision, build, fetch. Resumable; re-run after any failure.
    Create    -- resource group, VM, network rules. Calls Prep when it is done.
    Prep      -- make the guest reachable: OpenSSH installed and running, your key authorised, the guest
                 firewall opened, then wait for ssh to answer. Safe to run against an existing VM.
    Provision -- Chocolatey, Visual Studio 2019 Community, Strawberry Perl, NASM.
    Build     -- send fips/scripts/Build-FipsProvider.ps1 to the guest and run it there.
    Fetch     -- copy the artifacts and evidence back.
    Rdp       -- write a .rdp file with local drives redirected, for driving the guest by hand.
    Destroy   -- delete the whole resource group.

.PARAMETER PublicKey
    SSH public key authorised on the VM. Prompted for if omitted. Accepts key text or a path to a .pub file.

.PARAMETER IdentityFile
    Private key used to connect. Defaults to the public key's path with .pub removed.

.PARAMETER AdminPassword
    Local administrator password, needed only while the VM is being created. Generated and printed once if
    omitted. Azure requires 12-123 characters with at least three of: lower, upper, digit, special.

.PARAMETER Force
    Redo steps that would otherwise be skipped. Does not delete the VM.

.EXAMPLE
    .\fips\scripts\New-AzureFipsBuildVm.ps1 -Action All

.EXAMPLE
    .\fips\scripts\New-AzureFipsBuildVm.ps1 -Action Build -Force

.EXAMPLE
    .\fips\scripts\New-AzureFipsBuildVm.ps1 -Action Destroy
#>
[CmdletBinding()]
param(
    [ValidateSet("All", "Create", "Prep", "Provision", "Build", "Fetch", "Rdp", "Destroy")][string]$Action = "All",
    [string]$ResourceGroup = "fips-build-rg",
    [string]$Location = "eastus",
    [string]$VmName = "fips-build",
    [string]$AdminUser = "fipsbuilder",
    [string]$AdminPassword,
    [string]$PublicKey,
    [string]$IdentityFile,
    [string]$Size = "Standard_D4s_v5",
    [string]$ImageUrn = "MicrosoftWindowsDesktop:Windows-10:win10-22h2-pro-g2:latest",
    [int]$OsDiskGB = 256,
    [string]$LocalOutput = "D:\fips-buildhost\azure",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

#------------------------------------------------------------------------------------------------------------
# Audit trail
#
# An auditor asking "what did this run actually do" should not have to infer it. Every decision is recorded,
# including the ones where nothing happened, because "we skipped it because it already existed" and "we never
# checked" look identical in a log that only records changes.
#------------------------------------------------------------------------------------------------------------

$script:RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$script:AuditLog = [System.Collections.Generic.List[object]]::new()
$script:CurrentStage = "startup"
$script:TranscriptPath = $null

function Write-Audit {
    <#
        Result is deliberately a closed set:
          Created   -- something now exists that did not before
          Updated   -- something existed and was changed
          Skipped   -- checked, already correct, nothing done
          Verified  -- checked, no change intended, result recorded
          Started / Completed -- stage boundaries
          Failed    -- attempted and did not succeed
    #>
    param(
        [Parameter(Mandatory)][ValidateSet("Created", "Updated", "Skipped", "Verified",
            "Started", "Completed", "Failed")][string]$Result,
        [Parameter(Mandatory)][string]$Action,
        [string]$Detail = ""
    )

    $entry = [ordered]@{
        timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        stage        = $script:CurrentStage
        action       = $Action
        result       = $Result
        detail       = $Detail
    }
    $script:AuditLog.Add([pscustomobject]$entry)

    $colour = switch ($Result) {
        "Skipped"  { "DarkGray" }
        "Verified" { "DarkGray" }
        "Failed"   { "Red" }
        default    { "Gray" }
    }
    $stamp = (Get-Date).ToString("HH:mm:ss")
    $line = "  [{0}] {1,-9} {2}" -f $stamp, $Result.ToUpper(), $Action
    if ($Detail) { $line += " -- $Detail" }
    Write-Host $line -ForegroundColor $colour
}

function Write-Step([string]$Message) {
    $script:CurrentStage = $Message
    Write-Host ""
    Write-Host "========== $Message ==========" -ForegroundColor Cyan
    Write-Audit -Result Started -Action $Message
}

function Write-Skip([string]$Message) {
    Write-Audit -Result Skipped -Action $Message -Detail "already in the desired state"
}

function Save-AuditLog {
    param([string]$Directory)
    if (-not $Directory) { $Directory = $LocalOutput }
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null

    $path = Join-Path $Directory "run-log-$($script:RunId).json"
    [ordered]@{
        runId          = $script:RunId
        action         = $Action
        startedUtc     = $script:AuditLog[0].timestampUtc
        finishedUtc    = (Get-Date).ToUniversalTime().ToString("o")
        invokedBy      = "$env:USERDOMAIN\$env:USERNAME"
        invokedFrom    = $env:COMPUTERNAME
        resourceGroup  = $ResourceGroup
        vmName         = $VmName
        imageUrn       = $ImageUrn
        builderScript  = (Join-Path $PSScriptRoot "Build-FipsProvider.ps1")
        transcript     = $script:TranscriptPath
        entries        = $script:AuditLog
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $path -Encoding UTF8

    Write-Host ""
    Write-Host "run log   : $path"
    if ($script:TranscriptPath) { Write-Host "transcript: $($script:TranscriptPath)" }
    return $path
}

#------------------------------------------------------------------------------------------------------------
# Environment
#------------------------------------------------------------------------------------------------------------

function Assert-Tools {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        # A shell opened before the CLI was installed, or installed from a different elevation context,
        # will not have it on PATH. Re-read the stored PATH, then fall back to the known install locations.
        $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                    [Environment]::GetEnvironmentVariable("Path", "User")

        if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
            $candidates = @(
                "$env:ProgramFiles\Microsoft SDKs\Azure\CLI2\wbin",
                "${env:ProgramFiles(x86)}\Microsoft SDKs\Azure\CLI2\wbin",
                "$env:LOCALAPPDATA\Programs\Microsoft SDKs\Azure\CLI2\wbin"
            ) | Where-Object { Test-Path (Join-Path $_ "az.cmd") }

            if ($candidates) {
                $env:Path = "$env:Path;$($candidates[0])"
                Write-Host "Found the Azure CLI at $($candidates[0])"
            }
        }
    }

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw ("Azure CLI not found on PATH or in the usual install locations. Install it with: " +
            "winget install --id Microsoft.AzureCLI -e, then open a new shell.")
    }
    # Resolve ssh and scp to the Windows OpenSSH binaries explicitly. Git for Windows and MSYS both put
    # POSIX builds on PATH, and those fail here with "/usr/bin/ssh: No such file or directory" because they
    # try to exec POSIX paths that only exist inside their own environment.
    $inbox = Join-Path $env:SystemRoot "System32\OpenSSH"
    foreach ($t in @("ssh", "scp")) {
        $native = Join-Path $inbox "$t.exe"
        if (Test-Path $native) {
            Set-Variable -Name "${t}Exe" -Scope Script -Value $native
        } else {
            $found = Get-Command "$t.exe" -ErrorAction SilentlyContinue |
                Where-Object { $_.Source -notmatch '\\(usr|mingw\d+|git)\\' } |
                Select-Object -First 1
            if (-not $found) {
                throw ("$t not found. Enable the Windows OpenSSH client with: " +
                    "Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0")
            }
            Set-Variable -Name "${t}Exe" -Scope Script -Value $found.Source
        }
    }
    Write-Host "ssh: $($script:sshExe)"
    Write-Host "scp: $($script:scpExe)"
    $account = az account show --output json 2>$null | ConvertFrom-Json
    if (-not $account) { throw "Not signed in. Run 'az login' first." }
    Write-Host "Subscription: $($account.name)"
}

function Test-ResourceGroup {
    return ((az group exists --name $ResourceGroup --output tsv) -eq "true")
}

function Test-Vm {
    if (-not (Test-ResourceGroup)) { return $false }
    $found = az vm show --resource-group $ResourceGroup --name $VmName --output tsv --query name 2>$null
    return [bool]$found
}

function Get-VmPublicIp {
    return (az vm show --resource-group $ResourceGroup --name $VmName --show-details `
        --query publicIps --output tsv).Trim()
}

function Resolve-Keys {
    <#
        Asks for the key rather than assuming one. Accepts either key text or a path to a .pub file, so both
        "paste it" and "point at it" work. The private half is inferred from the path when possible.
    #>
    if (-not $script:PublicKey) {
        Write-Host ""
        Write-Host "SSH public key to authorise on the VM (key text, or a path to a .pub file)."
        $default = Join-Path $HOME ".ssh\id_ed25519.pub"
        if (Test-Path $default) { Write-Host "  press Enter to use $default" }
        $answer = Read-Host "  key"
        if (-not $answer -and (Test-Path $default)) { $answer = $default }
        $script:PublicKey = $answer
    }
    if (-not $script:PublicKey) { throw "No SSH public key supplied." }

    if (Test-Path -LiteralPath $script:PublicKey -ErrorAction SilentlyContinue) {
        $pubPath = (Resolve-Path -LiteralPath $script:PublicKey).Path
        if (-not $script:IdentityFile) { $script:IdentityFile = $pubPath -replace '\.pub$', '' }
        $script:PublicKey = (Get-Content -LiteralPath $pubPath -Raw).Trim()
    }
    $script:PublicKey = $script:PublicKey.Trim()

    if ($script:PublicKey -notmatch '^(ssh-rsa|ssh-ed25519|ecdsa-sha2-\S+)\s+\S+') {
        throw "That does not look like an SSH public key."
    }
    if ($script:IdentityFile -and -not (Test-Path -LiteralPath $script:IdentityFile)) {
        Write-Warning "Private key not found at $($script:IdentityFile); ssh will fall back to its defaults."
        $script:IdentityFile = $null
    }
}

function Get-SshArgs {
    # accept-new records the host key on first contact without prompting, and still fails if it later
    # changes. A fresh VM has a host key nobody has seen before, so a strict setting would just block.
    # ServerAlive settings keep a long build alive through an idle connection; ConnectTimeout is overridden
    # per call, because a reachability probe should give up far sooner than a build should.
    $a = @("-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=10",
           "-o", "ServerAliveInterval=30", "-o", "ServerAliveCountMax=120")
    if ($script:IdentityFile) { $a += @("-i", $script:IdentityFile) }
    return $a
}

function New-CompliantPassword {
    # Azure rejects anything under 12 characters or missing three of the four character classes. The suffix
    # guarantees all four regardless of what the random draw produced.
    $pool = (65..90) + (97..122) + (48..57) + (33, 35, 36, 37, 42, 64)
    $body = -join ($pool | Get-Random -Count 20 | ForEach-Object { [char]$_ })
    return "$body`Aa1!"
}

#------------------------------------------------------------------------------------------------------------
# Guest access
#------------------------------------------------------------------------------------------------------------

function Wait-ForSsh([int]$TimeoutSeconds = 10) {
    <#
        SSH either answers within seconds or something is wrong with the path to it. A long silent poll just
        delays the diagnosis, so this gives up quickly and reports what ssh actually said.
    #>
    $target = "$AdminUser@$(Get-VmPublicIp)"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempt = 0
    $last = ""

    while ((Get-Date) -lt $deadline) {
        $attempt++
        $out = & $script:sshExe @(Get-SshArgs) -o ConnectTimeout=4 -o BatchMode=yes $target "hostname" 2>&1
        if ($LASTEXITCODE -eq 0 -and $out) {
            Write-Audit -Result Verified -Action "ssh reachable" `
                -Detail "$target responded as $(($out | Select-Object -First 1).ToString().Trim())"
            return
        }
        $last = ($out | Select-Object -Last 1)
        Write-Host "    attempt $attempt`: $last"
        Start-Sleep -Seconds 1
    }

    Write-Audit -Result Failed -Action "ssh reachable" `
        -Detail "no response in ${TimeoutSeconds}s; last error: $last"

    # The failure mode is usually obvious from the error, so say which one it is rather than making the
    # reader work it out.
    $hint = switch -Regex ("$last") {
        "Connection timed out|No route to host" {
            "TCP never completed. Either the NSG rule for port 22 is missing or the guest firewall is " +
            "blocking it. Run -Action Prep, which fixes both."
        }
        "Connection refused" {
            "Port reachable but nothing listening. sshd is not running in the guest. Run -Action Prep."
        }
        "Permission denied|publickey" {
            "TCP and sshd are fine; the key was rejected. Check that -PublicKey matches the private key " +
            "in -IdentityFile, and that the guest has it in administrators_authorized_keys."
        }
        default { "Run -Action Prep, then try again." }
    }
    throw "ssh to $target did not come up within ${TimeoutSeconds}s.`n  last error: $last`n  likely cause: $hint"
}

function Invoke-InGuest {
    <#
        Runs a PowerShell script in the guest over ssh, streaming output as it happens.
    #>
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$Description,
        [switch]$Capture
    )

    $started = Get-Date
    Write-Host "  guest: $Description"
    $target = "$AdminUser@$(Get-VmPublicIp)"

    # The script goes over as -EncodedCommand rather than as a copied file. That avoids depending on scp,
    # which needs a working sftp subsystem, and it sidesteps every layer of quote mangling between
    # PowerShell here, the ssh command line, and cmd.exe as the remote login shell.
    #
    # Progress records are suppressed because PowerShell serialises non-stdout streams as CLIXML when it is
    # not attached to a console host, and a Chocolatey install emits thousands of them.
    $wrapped = "`$ProgressPreference = 'SilentlyContinue'`n$Script"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapped))
    if ($encoded.Length -gt 24000) {
        throw "Script for '$Description' is too large to send as an encoded command ($($encoded.Length) chars)."
    }

    try {
        $cmd = "powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
        if ($Capture) {
            $out = & $script:sshExe @(Get-SshArgs) $target $cmd 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Audit -Result Failed -Action "guest: $Description" -Detail "exit code $LASTEXITCODE"
                throw "guest step failed: $Description"
            }
            Write-Audit -Result Verified -Action "guest: $Description" `
                -Detail ("completed in {0:N0}s" -f ((Get-Date) - $started).TotalSeconds)
            return ($out -join "`n")
        }

        & $script:sshExe @(Get-SshArgs) $target $cmd
        if ($LASTEXITCODE -ne 0) {
            Write-Audit -Result Failed -Action "guest: $Description" -Detail "exit code $LASTEXITCODE"
            throw "guest step failed: $Description"
        }
        Write-Audit -Result Completed -Action "guest: $Description" `
            -Detail ("exit 0 in {0:N0}s" -f ((Get-Date) - $started).TotalSeconds)
    } catch {
        Write-Audit -Result Failed -Action "guest: $Description" -Detail $_.Exception.Message
        throw
    }
}

function Invoke-ViaAgent {
    <#
        Runs a script in the guest through the Azure agent and RETURNS its output. Used only before ssh
        exists. Passing the script as a file avoids the quoting mangling that happens when a script with
        embedded quotes is handed to az.cmd on a command line.
    #>
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$Description
    )

    Write-Host "  agent: $Description"
    $temp = New-TemporaryFile
    try {
        Set-Content -Path $temp -Value $Script -Encoding UTF8

        # One invocation. Each round trip through the agent costs about a minute, so stdout and stderr come
        # from the same call rather than two.
        $raw = az vm run-command invoke --resource-group $ResourceGroup --name $VmName `
            --command-id RunPowerShellScript --scripts "@$temp" --output json
        if ($LASTEXITCODE -ne 0) {
            Write-Audit -Result Failed -Action $Description -Detail "run-command returned $LASTEXITCODE"
            throw "Azure agent command failed: $Description"
        }

        $result = $raw | ConvertFrom-Json
        $out = ($result.value | Where-Object { $_.code -like "*StdOut*" }).message
        $err = ($result.value | Where-Object { $_.code -like "*StdErr*" }).message
        if ($err -and $err.Trim()) {
            Write-Warning "guest stderr during '$Description':`n$err"
        }
        return $out
    } finally {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
    }
}

function Set-NsgRule {
    <#
        Creates an inbound rule if missing, or updates its source address if it exists. Home addresses
        change; this keeps a re-run working without piling up duplicate rules.
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][int]$Priority,
        [Parameter(Mandatory)][string]$SourceCidr
    )

    $nsg = "${VmName}NSG"
    $existing = az network nsg rule show --resource-group $ResourceGroup --nsg-name $nsg --name $Name `
        --output json 2>$null | ConvertFrom-Json

    if ($existing) {
        if ($existing.sourceAddressPrefix -eq $SourceCidr) {
            Write-Audit -Result Skipped -Action "NSG rule $Name" `
                -Detail "already allows tcp/$Port from $SourceCidr only"
            return
        }
        az network nsg rule update --resource-group $ResourceGroup --nsg-name $nsg --name $Name `
            --source-address-prefixes $SourceCidr --output none
        Write-Audit -Result Updated -Action "NSG rule $Name" `
            -Detail "source $($existing.sourceAddressPrefix) -> $SourceCidr, port $Port"
    } else {
        az network nsg rule create `
            --resource-group $ResourceGroup --nsg-name $nsg --name $Name `
            --priority $Priority --source-address-prefixes $SourceCidr `
            --destination-port-ranges $Port --protocol Tcp --access Allow --output none
        Write-Audit -Result Created -Action "NSG rule $Name" `
            -Detail "allow tcp/$Port inbound from $SourceCidr only"
    }
}

#------------------------------------------------------------------------------------------------------------
# Stages
#------------------------------------------------------------------------------------------------------------

function Invoke-Create {

    Write-Step "provisioning Azure resources"

    Resolve-Keys

    # Record the key by type and fingerprint-ish prefix, not in full; it identifies the run without
    # pasting a credential into the audit log.
    $keyParts = $script:PublicKey -split '\s+'
    $keyLabel = "$($keyParts[0]) $($keyParts[1].Substring(0, [Math]::Min(16, $keyParts[1].Length)))..."
    Write-Audit -Result Verified -Action "SSH public key resolved" -Detail $keyLabel

    if (Test-ResourceGroup) {
        Write-Skip "resource group $ResourceGroup"
    } else {
        az group create --name $ResourceGroup --location $Location --output none
        Write-Audit -Result Created -Action "resource group $ResourceGroup" -Detail "location $Location"
    }

    if (Test-Vm) {
        Write-Skip "VM $VmName"
    } else {
        if (-not $script:AdminPassword) {
            $script:AdminPassword = New-CompliantPassword
            Write-Host ""
            Write-Host "Generated administrator password. RECORD THIS -- nothing else recovers it:" `
                -ForegroundColor Yellow
            Write-Host "  $($script:AdminPassword)" -ForegroundColor Yellow
            Write-Host ""
        }

        az vm create `
            --resource-group $ResourceGroup --name $VmName `
            --image $ImageUrn `
            --size $Size `
            --admin-username $AdminUser --admin-password $script:AdminPassword `
            --os-disk-size-gb $OsDiskGB --storage-sku Premium_LRS `
            --public-ip-sku Standard --nsg-rule NONE `
            --license-type Windows_Client `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Write-Audit -Result Failed -Action "create VM $VmName" -Detail "az vm create returned $LASTEXITCODE"
            throw ("az vm create failed. A Windows 10 client image requires Visual Studio subscription " +
                "entitlement on this subscription.")
        }

        # "latest" resolves to a dated image at deployment time. Capture what was actually deployed.
        $img = (az vm show --resource-group $ResourceGroup --name $VmName --output json |
            ConvertFrom-Json).storageProfile.imageReference
        Write-Audit -Result Created -Action "VM $VmName" `
            -Detail "$Size, $($img.publisher):$($img.offer):$($img.sku):$($img.exactVersion)"
    }

    Invoke-Prep
}

function Invoke-Prep {

    Write-Step "preparing the guest for ssh"

    if (-not (Test-Vm)) { throw "VM $VmName does not exist. Run -Action Create first." }
    Resolve-Keys

    # Two separate filters sit between here and sshd, and both have to be open. The NSG is the Azure-side
    # one; the guest's own firewall is the other, and traffic that clears the NSG still dies silently there.
    $myIp = (Invoke-RestMethod https://api.ipify.org).Trim()
    Set-NsgRule -Name "allow-ssh-from-me" -Port 22   -Priority 1010 -SourceCidr "$myIp/32"
    Set-NsgRule -Name "allow-rdp-from-me" -Port 3389 -Priority 1000 -SourceCidr "$myIp/32"

    # The only use of the Azure agent: prepare SSH so everything after this can use it. The default shell
    # is deliberately left as cmd.exe, because Windows OpenSSH 8.x still uses the legacy scp protocol and
    # scp breaks when the login shell is powershell.
    $bootstrap = @"
`$ErrorActionPreference = 'Continue'

if ((Get-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0).State -ne 'Installed') {
    Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 | Out-Null
}
Set-Service sshd -StartupType Automatic -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path 'C:\ProgramData\ssh' | Out-Null

# sshd normally copies sshd_config_default into place on its first successful start. If the first start
# fails the config is never written, and the missing config is then why every later start fails too.
`$cfg = 'C:\ProgramData\ssh\sshd_config'
`$default = Join-Path `$env:SystemRoot 'System32\OpenSSH\sshd_config_default'
if (-not (Test-Path `$cfg) -and (Test-Path `$default)) {
    Copy-Item `$default `$cfg -Force
}

# sshd also refuses to start without host keys, and a freshly added capability does not always have them.
# ssh-keygen -A is idempotent: it creates only the key types that are missing.
`$keygen = Join-Path `$env:SystemRoot 'System32\OpenSSH\ssh-keygen.exe'
if (Test-Path `$keygen) { & `$keygen -A | Out-Null }

# Host keys must not be world-readable or sshd rejects them.
`$fixAcl = Join-Path `$env:SystemRoot 'System32\OpenSSH\FixHostFilePermissions.ps1'
if (Test-Path `$fixAcl) { & `$fixAcl -Confirm:`$false | Out-Null }

# Start, and if it will not come up, reinstall the capability. A partially unpacked OpenSSH install is the
# remaining cause of a service that exists but never signals the service controller.
for (`$try = 1; `$try -le 3; `$try++) {
    if ((Get-Service sshd -ErrorAction SilentlyContinue).Status -eq 'Running') { break }
    try {
        Start-Service sshd -ErrorAction Stop
    } catch {
        "start-attempt-`$try=`$(`$_.Exception.Message)"
        if (`$try -eq 2) {
            "reinstalling the OpenSSH.Server capability"
            Remove-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 -ErrorAction SilentlyContinue |
                Out-Null
            Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 -ErrorAction SilentlyContinue |
                Out-Null
            if (-not (Test-Path `$cfg) -and (Test-Path `$default)) { Copy-Item `$default `$cfg -Force }
            if (Test-Path `$keygen) { & `$keygen -A | Out-Null }
            if (Test-Path `$fixAcl) { & `$fixAcl -Confirm:`$false | Out-Null }
            Set-Service sshd -StartupType Automatic -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 3
}

# Adding the OpenSSH capability does not reliably create the inbound firewall rule, and without it the NSG
# allows traffic all the way to a guest that silently drops it. That looks identical to a missing NSG rule.
if (-not (Get-NetFirewallRule -Name 'sshd-in' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name 'sshd-in' -DisplayName 'OpenSSH Server (sshd)' -Enabled True ``
        -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 -Profile Any | Out-Null
} else {
    Enable-NetFirewallRule -Name 'sshd-in' -ErrorAction SilentlyContinue
}

`$f = 'C:\ProgramData\ssh\administrators_authorized_keys'
`$key = '$($script:PublicKey)'
`$current = if (Test-Path `$f) { @(Get-Content `$f | Where-Object { `$_ }) } else { @() }
if (`$current -notcontains `$key) {
    Set-Content -Path `$f -Value (`$current + `$key) -Encoding ascii
    icacls `$f /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
}

# Report the resulting state. The caller checks these rather than assuming the run-command exiting zero
# means the work inside it succeeded.
`$svc = (Get-Service sshd -ErrorAction SilentlyContinue).Status
`$listen = [bool](Get-NetTCPConnection -State Listen -LocalPort 22 -ErrorAction SilentlyContinue)
`$fw = (Get-NetFirewallRule -Name 'sshd-in' -ErrorAction SilentlyContinue).Enabled -eq 'True'
"sshd=`$svc"
"listening=`$listen"
"firewall=`$fw"
"keyfile=`$(Test-Path `$f)"
"config=`$(Test-Path `$cfg)"
"hostkeys=`$((Get-ChildItem 'C:\ProgramData\ssh\ssh_host_*_key' -ErrorAction SilentlyContinue).Count)"
"profiles=`$((Get-NetFirewallProfile | ForEach-Object { `"`$(`$_.Name)=`$(`$_.Enabled)`" }) -join ' ')"
"@
    $out = Invoke-ViaAgent -Script $bootstrap -Description "OpenSSH, guest firewall, authorised key"
    Write-Host ($out -split "`n" | ForEach-Object { "    $_" }) -Separator "`n"

    # Report what the guest actually said rather than assuming the run-command succeeding means the work
    # inside it did. Each of these is a separate thing that can be wrong on its own.
    $failures = @()
    foreach ($check in @(
            @{ Pattern = 'sshd=Running';   Action = 'sshd service running' },
            @{ Pattern = 'listening=True'; Action = 'sshd listening on tcp/22' },
            @{ Pattern = 'firewall=True';  Action = 'guest firewall allows tcp/22' },
            @{ Pattern = 'keyfile=True';   Action = 'authorised key file present' }
        )) {
        if ($out -match [regex]::Escape($check.Pattern)) {
            Write-Audit -Result Verified -Action $check.Action
        } else {
            Write-Audit -Result Failed -Action $check.Action -Detail "guest did not report $($check.Pattern)"
            $failures += $check.Action
        }
    }

    # No point probing a port the guest has already told us nothing is listening on. Diagnose, try the one
    # fix that reliably works for a freshly added capability, and only then give up.
    if ($failures) {

        $detail = Invoke-ViaAgent -Description "why sshd will not start" -Script @'
$ssh = Join-Path $env:SystemRoot 'System32\OpenSSH'
"binaries : $(Test-Path (Join-Path $ssh 'sshd.exe'))"
"config   : $(Test-Path 'C:\ProgramData\ssh\sshd_config')"
"hostkeys : $((Get-ChildItem 'C:\ProgramData\ssh\ssh_host_*_key' -ErrorAction SilentlyContinue).Count)"
"--- sshd -t (config test) ---"
& (Join-Path $ssh 'sshd.exe') -t 2>&1 | ForEach-Object { $_.ToString() }
"exit=$LASTEXITCODE"
$log = 'C:\ProgramData\ssh\logs\sshd.log'
if (Test-Path $log) { "--- sshd.log tail ---"; Get-Content $log -Tail 20 }
'@
        Write-Host ($detail -split "`n" | ForEach-Object { "    $_" }) -Separator "`n"

        # The in-box OpenSSH in some Windows images is broken: sshd.exe fails to load at all, which shows up
        # as exit code 0xC0000139 (STATUS_ENTRYPOINT_NOT_FOUND) from 'sshd -t' and as a service that never
        # signals the service controller. No amount of config repair fixes a binary that will not load.
        # The standalone Win32-OpenSSH build ships its own dependencies and does not have the problem.
        Write-Audit -Result Updated -Action "installing standalone Win32-OpenSSH" `
            -Detail "in-box sshd.exe fails to load; replacing it rather than repairing config"

        $after = Invoke-ViaAgent -Description "standalone OpenSSH install" -Script @'
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

Stop-Service sshd -Force -ErrorAction SilentlyContinue
Remove-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0 -ErrorAction SilentlyContinue | Out-Null

$zip = "$env:TEMP\OpenSSH-Win64.zip"
$url = 'https://github.com/PowerShell/Win32-OpenSSH/releases/latest/download/OpenSSH-Win64.zip'
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
"downloaded $((Get-Item $zip).Length) bytes"

$dest = "$env:ProgramFiles\OpenSSH"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue }
Expand-Archive -Path $zip -DestinationPath $env:ProgramFiles -Force
if (Test-Path "$env:ProgramFiles\OpenSSH-Win64") {
    Rename-Item "$env:ProgramFiles\OpenSSH-Win64" $dest -Force
}

& powershell -NoProfile -ExecutionPolicy Bypass -File "$dest\install-sshd.ps1" | Out-Null
& "$dest\ssh-keygen.exe" -A | Out-Null
if (Test-Path "$dest\FixHostFilePermissions.ps1") {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$dest\FixHostFilePermissions.ps1" -Confirm:$false |
        Out-Null
}

# The config left behind by the in-box OpenSSH points Subsystem sftp at a path that no longer exists.
# scp speaks the sftp protocol, so without this every file transfer fails with a connection reset.
$cfg = 'C:\ProgramData\ssh\sshd_config'
$sftp = "$dest\sftp-server.exe"
if ((Test-Path $cfg) -and (Test-Path $sftp)) {
    $lines = Get-Content $cfg | Where-Object { $_ -notmatch '^\s*Subsystem\s+sftp' }
    $lines += "Subsystem sftp `"$sftp`""
    Set-Content -Path $cfg -Value $lines -Encoding ascii
}

Set-Service sshd -StartupType Automatic
Restart-Service sshd -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

"sshd=$((Get-Service sshd -ErrorAction SilentlyContinue).Status)"
"listening=$([bool](Get-NetTCPConnection -State Listen -LocalPort 22 -ErrorAction SilentlyContinue))"
"version=$(& "$dest\sshd.exe" -V 2>&1)"
'@
        Write-Host ($after -split "`n" | ForEach-Object { "    $_" }) -Separator "`n"

        if ($after -notmatch 'listening=True') {
            Write-Audit -Result Failed -Action "sshd listening on tcp/22" `
                -Detail "still down after replacing the in-box OpenSSH"
            throw ("Guest is not ready for ssh: $($failures -join '; '). The standalone OpenSSH install " +
                "did not recover it either. See the output above.")
        }
        Write-Audit -Result Verified -Action "sshd listening on tcp/22" `
            -Detail "recovered with the standalone Win32-OpenSSH build"
    }

    Wait-ForSsh
    Write-Host ""
    Write-Host "  ssh $AdminUser@$(Get-VmPublicIp)"
}

function Invoke-Provision {

    Write-Step "installing the toolchain"

    if (-not (Test-Vm)) { throw "VM $VmName does not exist. Run -Action Create first." }
    Resolve-Keys
    Wait-ForSsh

    $state = Invoke-InGuest -Capture -Description "checking what is already installed" -Script @'
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = if (Test-Path $vswhere) {
    & $vswhere -products * -version "[16.0,17.0)" -latest -property catalog_productDisplayVersion
} else { "" }
"choco=$([bool](Get-Command choco -ErrorAction SilentlyContinue))"
"vs2019=$(if ($vs) { $vs } else { 'missing' })"
"perl=$(Test-Path 'C:\Strawberry\perl\bin\perl.exe')"
"nasm=$(Test-Path "$env:ProgramFiles\NASM\nasm.exe")"
'@

    if ($state -notmatch "choco=True" -or $Force) {
        Invoke-InGuest -Description "Chocolatey" -Script @'
Set-ExecutionPolicy Bypass -Scope Process -Force
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
Invoke-Expression ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
'@
    } else {
        Write-Skip "Chocolatey"
    }

    # Visual Studio is the long pole at 30-45 minutes. Over SSH its output streams, so you can see progress.
    if ($state -match "vs2019=missing" -or $Force) {
        Invoke-InGuest -Description "Visual Studio 2019 Community (30-45 minutes)" -Script @'
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine")
choco install -y --no-progress visualstudio2019community `
    --params "'--add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended'"
if ($LASTEXITCODE -notin @(0, 3010)) { throw "choco exit code $LASTEXITCODE" }
'@
    } else {
        Write-Skip "Visual Studio 2019"
    }

    if ($state -notmatch "perl=True" -or $state -notmatch "nasm=True" -or
        $Force) {
        # Only what OpenSSL needs to configure and assemble. No git: the build host receives one script,
        # not a repository.
        Invoke-InGuest -Description "Perl, NASM" -Script @'
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine")
foreach ($p in @("strawberryperl", "nasm")) {
    choco install -y --no-progress $p
    if ($LASTEXITCODE -notin @(0, 3010)) { throw "choco install $p exit code $LASTEXITCODE" }
}
'@
    } else {
        Write-Skip "Perl, NASM"
    }

    $report = Invoke-InGuest -Capture -Description "verifying the toolchain" -Script @'
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -products * -version "[16.0,17.0)" -latest -property catalog_productDisplayVersion
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";$env:ProgramFiles\NASM"
$os = Get-CimInstance Win32_OperatingSystem
"OS   : $($os.Caption) $($os.Version)"
"VS   : $vs"
"perl : $(((perl -v) -join ' ') -replace '\s+',' ')"
"nasm : $((nasm -v) -join ' ')"
'@
    Write-Host $report

    if ($report -notmatch "Windows 10") {
        Write-Warning "Guest does not report Windows 10. Check the image SKU against 140sp4985 section 5.3."
    }
    if ($report -notmatch "VS\s*:\s*16\.") {
        throw "Visual Studio 2019 (16.x) is not present. Build-FipsProvider.ps1 pins to it and will refuse to run."
    }
}

function Invoke-Build {

    Write-Step "building the FIPS provider in the guest (20-30 minutes)"

    if (-not (Test-Vm)) { throw "VM $VmName does not exist. Run -Action Create first." }
    Resolve-Keys
    Wait-ForSsh

    if (-not $Force) {
        $existing = Invoke-InGuest -Capture -Description "checking for a previous build" -Script @'
$e = Get-ChildItem C:\fips-evidence -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1
if ($e -and (Test-Path (Join-Path $e.FullName 'artifacts\fips.dll'))) { "built=$($e.Name)" }
else { "built=no" }
'@
        if ($existing -match "built=openssl") {
            Write-Skip "build already present in the guest (pass -Force to rebuild)"
            return
        }
    }

    # Send the one script the build host needs. Cloning the product repository onto a build host would put
    # the entire codebase somewhere that only needs a single file, and would force the branch to be pushed
    # before anything could be built.
    $builder = Join-Path $PSScriptRoot "Build-FipsProvider.ps1"
    if (-not (Test-Path $builder)) { throw "Build-FipsProvider.ps1 not found next to this script." }

    $target = "$AdminUser@$(Get-VmPublicIp)"
    & $script:sshExe @(Get-SshArgs) $target "if not exist C:\fips mkdir C:\fips" | Out-Null
    & $script:scpExe @(Get-SshArgs) $builder "${target}:C:/fips/Build-FipsProvider.ps1"
    if ($LASTEXITCODE -ne 0) { throw "scp failed while sending Build-FipsProvider.ps1 to the guest." }
    Write-Audit -Result Created -Action "sent Build-FipsProvider.ps1" `
        -Detail "$((Get-Item $builder).Length) bytes from $builder"

    # Build-FipsProvider.ps1 clears its own work tree and prefix, so re-running this is safe.
    $script = @'
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ";$env:ProgramFiles\NASM"

C:\fips\Build-FipsProvider.ps1 `
    -WorkRoot C:\fips-build `
    -Prefix C:\openssl-install `
    -EvidenceRoot C:\fips-evidence
'@

    Invoke-InGuest -Description "build" -Script $script
}

function Invoke-Fetch {

    Write-Step "fetching artifacts and evidence"

    if (-not (Test-Vm)) { throw "VM $VmName does not exist." }
    Resolve-Keys
    Wait-ForSsh

    Invoke-InGuest -Description "packaging the evidence" -Script @'
$ErrorActionPreference = 'Stop'
$evidence = Get-ChildItem C:\fips-evidence -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $evidence) { throw 'No evidence directory in the guest. Run -Action Build first.' }
$zip = 'C:\fips-evidence.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $evidence.FullName -DestinationPath $zip
"packaged $((Get-Item $zip).Length) bytes from $($evidence.Name)"
'@

    New-Item -ItemType Directory -Force -Path $LocalOutput | Out-Null
    $localZip = Join-Path $LocalOutput "fips-evidence.zip"
    $target = "$AdminUser@$(Get-VmPublicIp)"

    Write-Host "  downloading"
    & $script:scpExe @(Get-SshArgs) "${target}:C:/fips-evidence.zip" $localZip
    if ($LASTEXITCODE -ne 0) { throw "scp failed while downloading the evidence archive." }

    Expand-Archive -Path $localZip -DestinationPath $LocalOutput -Force
    Write-Host "  extracted to $LocalOutput"

    foreach ($name in @("fips.dll", "openssl.exe")) {
        $f = Get-ChildItem $LocalOutput -Recurse -Filter $name -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($f) {
            $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            Write-Host ("  {0,-12} {1,10:N0} bytes  {2}" -f $f.Name, $f.Length, $h)
        } else {
            Write-Warning "$name not found in the fetched evidence."
            Write-Audit -Result Failed -Action "artifact $name" -Detail "not present in the fetched evidence"
        }
    }

    $evidenceDir = Get-ChildItem $LocalOutput -Directory -Filter "openssl-*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($evidenceDir) {
        Write-EvidenceRecord -EvidenceDir $evidenceDir.FullName
        Write-Audit -Result Created -Action "build record" -Detail "$($evidenceDir.FullName)\build-record.md"
    } else {
        Write-Warning "No openssl-* evidence directory found under $LocalOutput."
    }
}

function Write-EvidenceRecord {
    <#
        Produces the human-readable half of the evidence package: what was built, from what source, on what
        machine, with which compiler, and how each of those maps to the security policy. The machine-readable
        half is manifest.json, written by Build-FipsProvider.ps1 inside the guest.

        Written at fetch time because that is the first moment every fact is known on this side.
    #>
    param([Parameter(Mandatory)][string]$EvidenceDir)

    $manifestPath = Get-ChildItem $EvidenceDir -Recurse -Filter "manifest.json" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $manifestPath) {
        Write-Warning "No manifest.json in the fetched evidence; skipping the build record."
        return
    }
    $m = Get-Content $manifestPath.FullName -Raw | ConvertFrom-Json

    # "latest" resolves to a dated version at deployment time. Record what was actually deployed, because
    # "latest" means something different tomorrow.
    $vm = az vm show --resource-group $ResourceGroup --name $VmName --output json | ConvertFrom-Json
    $img = $vm.storageProfile.imageReference
    $account = az account show --output json | ConvertFrom-Json

    $guest = Invoke-InGuest -Capture -Description "recording the guest environment" -Script @'
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -products * -version "[16.0,17.0)" -latest -format json | ConvertFrom-Json |
    Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$msvc = Get-ChildItem (Join-Path $vs.installationPath "VC\Tools\MSVC") -Directory |
    Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty Name
"os|$($os.Caption)"
"osversion|$($os.Version)"
"osbuild|$($os.BuildNumber)"
"vs|$($vs.displayName) $($vs.installationVersion)"
"msvc|$msvc"
'@
    $g = @{}
    foreach ($line in ($guest -split "`n")) {
        if ($line -match '^\s*([a-z]+)\|(.+?)\s*$') { $g[$Matches[1]] = $Matches[2] }
    }

    $artifacts = foreach ($name in @("fips.dll", "openssl.exe")) {
        $f = Get-ChildItem $EvidenceDir -Recurse -Filter $name -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($f) {
            [pscustomobject]@{
                Name   = $name
                Bytes  = $f.Length
                Sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }

    $rows = ($artifacts | ForEach-Object {
        "| ``$($_.Name)`` | $('{0:N0}' -f $_.Bytes) | ``$($_.Sha256)`` |"
    }) -join "`n"

    $record = @"
# FIPS provider build record

Generated $((Get-Date).ToUniversalTime().ToString("u")) by ``fips/scripts/New-AzureFipsBuildVm.ps1``.

This record and the files beside it are the evidence package for one build of the OpenSSL FIPS Provider. It is
written automatically at the end of the build so that nothing is reconstructed from memory afterwards.

## What was built

| | |
| --- | --- |
| Module | OpenSSL FIPS Provider $($m.moduleVersion) |
| CMVP certificate | #$($m.cmvpCertificate) ($($m.cmvpStandard)), sunset $($m.cmvpSunset) |
| Built at (UTC) | $($m.builtAtUtc) |

## Source

| | |
| --- | --- |
| Distribution | ``$($m.sourceUrl)`` |
| SHA-256 expected | ``$($m.sourceSha256Expected)`` |
| SHA-256 verified | ``$($m.sourceSha256Actual)`` |

The build aborts before extracting if these differ, so a recorded build is a build from verified source.

## Build method

| | |
| --- | --- |
| Security policy method | $($m.securityPolicyMethod) |
| Configure arguments | ``$($m.configureArguments)`` |
| Deviations from the method | $(($m.methodDeviations) -join ', ') |
| Test suite run | $($m.testsRun) |

CMVP Management Manual section 7.9.2 footnote 5 permits a user to recompile a validated module only where the
security policy provides specific recompilation guidance, and requires that "the methods in the Security Policy
must be followed without modification". Section 11.1 of 140sp4985 gives that method for Windows. Any entry other
than "none" in the deviations row above is a claim that needs justifying.

## Operating environment of the build host

| | |
| --- | --- |
| Operating system | $($g['os']) |
| Version / build | $($g['osversion']) / $($g['osbuild']) |
| Compiler | $($g['vs']) |
| MSVC toolset | $($g['msvc']) |
| Perl | $($m.toolchain.perl) |
| NASM | $($m.toolchain.nasm) |

Section 5.3 of 140sp4985 records **"Windows 10: Visual Studio 2019"** as the compiler used to build the module
for the tested Windows operational environment. The rows above are what this build actually used.

## Where it was built

| | |
| --- | --- |
| Platform | Microsoft Azure |
| Region | $($vm.location) |
| VM size | $($vm.hardwareProfile.vmSize) |
| Image publisher | $($img.publisher) |
| Image offer | $($img.offer) |
| Image SKU | $($img.sku) |
| Image version deployed | **$($img.exactVersion)** |
| Subscription | $($account.name) |

``exactVersion`` is the dated image actually deployed. The deployment requested ``latest``, which resolves
differently over time; this is the resolved value.

## Artifacts

| File | Bytes | SHA-256 |
| --- | --- | --- |
$rows

These are the **unsigned** build outputs, and they are published unsigned on purpose. ZDEW's installer build
Authenticode-signs every binary it packages, and these go through that same pass rather than a separate one, so
a change to how signing works can never leave them behind. The hashes above are what ``fips/provider.json``
pins, and what the installer verifies on download before signing.

``openssl fipsinstall`` then runs at install time, after signing, so the module's integrity HMAC covers exactly
the bytes that ship.

``fipsmodule.cnf`` is deliberately absent. Its content is deterministic -- the MAC is over the bytes of
``fips.dll``, which are identical everywhere -- but section 11.1 of the security policy requires the
installation steps be performed on each platform where the module is used, so the self-tests run in that
operational environment.

## Files in this package

| File | What it is |
| --- | --- |
| ``build-record.md`` | This document |
| ``manifest.json`` | The same facts, machine-readable, written inside the build host |
| ``configdata.txt`` | Full OpenSSL build configuration dump |
| ``configure.log`` | Output of ``perl Configure enable-fips`` |
| ``nmake.log`` | Compilation output |
| ``nmake-test.log`` | Test suite output, including the module self-tests |
| ``nmake-install.log`` | Installation output |
| ``artifacts/`` | ``fips.dll`` and ``openssl.exe`` as built |
| ``openssl-$($m.moduleVersion).tar.gz`` | The verified source distribution |
| ``run-log-*.json`` | Every action this orchestration took, including the ones that were skipped |
| ``transcript-*.log`` | Full console transcript, including compiler and test output |

## Run log

``run-log-*.json`` records each action with a UTC timestamp and one of a closed set of outcomes:

| Outcome | Meaning |
| --- | --- |
| ``Created`` | Something now exists that did not before |
| ``Updated`` | Something existed and was changed |
| ``Skipped`` | Checked, already in the desired state, nothing done |
| ``Verified`` | Checked, no change intended, result recorded |
| ``Started`` / ``Completed`` | Stage boundaries, with elapsed time on guest steps |
| ``Failed`` | Attempted and did not succeed |

Skips are recorded deliberately. In a log that only records changes, "we checked and it was already correct"
and "we never checked" are indistinguishable. A failed run still writes its log.

## Reading this as an auditor

1. The source hash above matches OpenSSL's published SHA-256 for that release.
2. ``configure.log`` and ``configdata.txt`` show the build was configured by the security policy's method.
3. ``nmake-test.log`` shows the module's self-tests and algorithm known-answer tests passing on this host.
4. The artifact hashes tie the binaries in this package to that build.
5. ``fips/provider.json`` in the desktop-edge-win repository ties the signed, published binaries to a release.
6. On any installed machine, ``fips/doc/test-plan.md`` verifies the module in place and that the tunneler loaded it.

See ``fips/doc/compliance-position.md`` for the claim these artifacts support, and its limits.
"@

    $path = Join-Path $EvidenceDir "build-record.md"
    Set-Content -Path $path -Value $record -Encoding UTF8
    Write-Host "  build record: $path"
}

function Invoke-Rdp {
    <#
        Writes a .rdp file with the address and username filled in, and local drives redirected so the
        evidence directory can be dragged back without another transfer mechanism. The fallback for when
        driving the guest remotely is not working.
    #>
    Write-Step "writing an RDP shortcut"

    if (-not (Test-Vm)) { throw "VM $VmName does not exist. Run -Action Create first." }

    $ip = Get-VmPublicIp
    $myIp = (Invoke-RestMethod https://api.ipify.org).Trim()
    Set-NsgRule -Name "allow-rdp-from-me" -Port 3389 -Priority 1000 -SourceCidr "$myIp/32"

    New-Item -ItemType Directory -Force -Path $LocalOutput | Out-Null
    $path = Join-Path $LocalOutput "$VmName.rdp"

    # drivestoredirect:s:* maps this machine's drives into the session, which is how the build output comes
    # back without needing scp or a storage account.
    @(
        "full address:s:$ip"
        "username:s:$AdminUser"
        "prompt for credentials:i:1"
        "authentication level:i:2"
        "screen mode id:i:2"
        "session bpp:i:32"
        "redirectclipboard:i:1"
        "drivestoredirect:s:*"
        "autoreconnection enabled:i:1"
    ) | Set-Content -Path $path -Encoding ASCII

    Write-Audit -Result Created -Action "RDP shortcut" -Detail $path

    Write-Host ""
    Write-Host "  $path"
    Write-Host "  user: $AdminUser"
    Write-Host ""
    Write-Host "Inside the VM, from an elevated PowerShell:"
    Write-Host "  Set-ExecutionPolicy Bypass -Scope Process -Force"
    Write-Host "  `$p = [Net.ServicePointManager]::SecurityProtocol"
    Write-Host "  [Net.ServicePointManager]::SecurityProtocol = `$p -bor 3072"
    Write-Host "  iex ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))"
    Write-Host "  # copy fips\scripts\Build-FipsProvider.ps1 across the redirected drive, then:"
    Write-Host "  C:\fips\Build-FipsProvider.ps1 -EvidenceRoot C:\fips-evidence -EnsureToolchain"
    Write-Host ""
    Write-Host "Visual Studio 2019 is not on this image and the build pins to it:"
    Write-Host "  choco install -y visualstudio2019community ``"
    Write-Host "      --params `"'--add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended'`""
    Write-Host ""
    Write-Host "Copy C:\fips-evidence back through the redirected drive when it finishes."
}

function Invoke-Destroy {
    Write-Step "deleting resource group $ResourceGroup"
    if (-not (Test-ResourceGroup)) {
        Write-Skip "resource group $ResourceGroup does not exist"
        return
    }
    Write-Warning "This removes the VM, its disks, and everything else in the group."
    az group delete --name $ResourceGroup --yes --no-wait
    Write-Host "Deletion started. Artifacts and evidence must already be off the machine."
}

#------------------------------------------------------------------------------------------------------------

# Console transcript alongside the structured log. The structured log says what was decided; the transcript
# carries everything the build itself printed, which is where compiler and test output lives.
New-Item -ItemType Directory -Force -Path $LocalOutput | Out-Null
$script:TranscriptPath = Join-Path $LocalOutput "transcript-$($script:RunId).log"
try {
    Start-Transcript -Path $script:TranscriptPath -Append | Out-Null
} catch {
    Write-Warning "Could not start a transcript: $($_.Exception.Message)"
    $script:TranscriptPath = $null
}

$failed = $null
try {
    Assert-Tools
    Write-Audit -Result Verified -Action "invocation" `
        -Detail "-Action $Action as $env:USERNAME on $env:COMPUTERNAME, run $($script:RunId)"

    switch ($Action) {
        "Create"    { Invoke-Create }
        "Prep"      { Invoke-Prep }
        "Provision" { Invoke-Provision }
        "Build"     { Invoke-Build }
        "Fetch"     { Invoke-Fetch }
        "Rdp"       { Invoke-Rdp }
        "Destroy"   { Invoke-Destroy }
        "All" {
            Invoke-Create
            Invoke-Provision
            Invoke-Build
            Invoke-Fetch

            Write-Step "done"
            Write-Host "artifacts and evidence : $LocalOutput"
            Write-Host ""
            Write-Host "Next: sign fips.dll and openssl.exe, then fips\scripts\Publish-FipsProvider.ps1."
            Write-Host "Stop paying for the VM when you are finished:"
            Write-Host "  .\fips\scripts\New-AzureFipsBuildVm.ps1 -Action Destroy"
        }
    }
    Write-Audit -Result Completed -Action "run $($script:RunId)" -Detail "-Action $Action finished"
} catch {
    $failed = $_
    # A failed run is still evidence, and an audit trail that only survives success is not an audit trail.
    Write-Audit -Result Failed -Action "run $($script:RunId)" -Detail $_.Exception.Message
} finally {
    Save-AuditLog -Directory $LocalOutput | Out-Null
    if ($script:TranscriptPath) { try { Stop-Transcript | Out-Null } catch { } }
}

if ($failed) { throw $failed }
