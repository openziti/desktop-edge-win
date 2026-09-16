# Proving FIPS mode on an installed machine

This procedure verifies that a machine running Ziti Desktop Edge for Windows is performing its Ziti
cryptography with the OpenSSL FIPS Provider. It is written so it can be handed to an operator or an auditor and
run without access to the build environment.

`APPDIR` below is the ZDEW install directory, by default
`C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge`.

## 1 -- the files are present

```powershell
$appdir = "C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge"
Get-ChildItem "$appdir\*" -Include fips.dll, openssl.exe, libcrypto-3-x64.dll, libssl-3-x64.dll,
    vcruntime140.dll, openssl.cnf, fipsmodule.cnf | Select-Object Name, Length, LastWriteTime
```

All seven must exist. Five are installed by the MSI: `fips.dll` is the validated module, `openssl.exe` is the
tool that configures it, and the other three are what `openssl.exe` needs in order to run -- it imports both
OpenSSL DLLs and the C runtime.

The remaining two are generated on this machine at install time and are never shipped. `fipsmodule.cnf` should
be newer than or equal to `fips.dll`: it is generated after the module is placed and is keyed to its bytes.

Their absence on a machine is not a fault. FIPS is off by default; it is installed only when
`ZITI_ENABLE_FIPS=1` is set, by the installer checkbox or on the command line.

## 2 -- the module is the one we shipped, and it is signed

```powershell
Get-FileHash "$appdir\fips.dll" -Algorithm SHA256
Get-AuthenticodeSignature "$appdir\fips.dll" | Format-List Status, SignerCertificate, TimeStamperCertificate
```

The hash must match the `fipsDllSha256` value in the build's evidence manifest. `Status` must be `Valid` and the
signer must be the NetFoundry code-signing certificate.

## 3 -- the module passes its own integrity and self-tests

```powershell
& "$appdir\openssl.exe" fipsinstall -in "$appdir\fipsmodule.cnf" -module "$appdir\fips.dll" -verify
```

Expect `VERIFY PASSED`. This recomputes the module MAC and compares it against the value recorded at install
time. A failure means `fips.dll` changed after installation, or `fipsmodule.cnf` belongs to a different build.

This is a read-only check. It does not rewrite `fipsmodule.cnf`.

## 4 -- the shipped configuration activates only approved providers

```powershell
Get-Content "$appdir\openssl.cnf"

$env:OPENSSL_CONF = "$appdir\openssl.cnf"
& "$appdir\openssl.exe" list -providers
$env:OPENSSL_CONF = $null
```

The configuration is supplied through the environment because `list` has no `-config` option in 3.1.2. Set it
for this shell only. Nothing machine-wide should be set: the tunneler finds the same file by looking next to
its own executable, and a machine-wide `OPENSSL_CONF` would apply to every OpenSSL process on the box.

Expected provider list: `base` and `fips`, both `active`, with the FIPS provider reporting version `3.1.2`. The
`default` provider must **not** appear. `openssl.cnf` must contain `default_properties = fips=yes`.

If `default` is active, the configuration is not a FIPS configuration -- non-approved algorithms remain
reachable -- regardless of whether the FIPS provider also loaded.

## 5 -- the tunneler loaded that configuration

**Do not use `ziti-edge-tunnel.exe version -v` for this.** That subcommand prints from a default TLS context
and returns before the tunneler resolves `openssl.cnf`, so it reports no `[FIPS]` marker even on a correctly
configured machine. The marker only appears on the `run` path, which is what the service uses.

The running service's log is the evidence. At each start:

```
	- openssl config   : configured using C:\...\Ziti Desktop Edge\openssl.cnf found by default location
```

```powershell
Select-String -Path "$appdir\logs\service\ziti-tunneler.log*" -Pattern "openssl config" | Select-Object -Last 5
```

Absence of that line means no configuration was loaded and the tunneler is using OpenSSL's built-in
implementations, not the validated module.

To confirm the module itself loads and passes its self-tests on this machine, independently of the service,
point `OPENSSL_CONF` at the installed config and ask the tunneler binary directly:

```powershell
$env:OPENSSL_CONF = "$appdir\openssl.cnf"
& "$appdir\ziti-edge-tunnel.exe" version -v
Remove-Item Env:\OPENSSL_CONF
```

```
tlsuv:  v0.44.0[OpenSSL 3.6.3 9 Jun 2026 [FIPS]]
```

That routes the same core to the same module through the default library context rather than the one the
service builds. Two separate facts, both worth having: the log line proves the service loaded the config, and
this proves the module in that config actually works.

## 6 -- Ziti still works

FIPS mode restricts the available algorithm set, so a functional pass is part of the proof rather than a
formality. The FIPS provider in 3.1.2 does not implement X25519 or Ed25519.

Run, and record the result of each:

| Check | Why it is here |
| --- | --- |
| Enroll a new identity | Generates a key pair. First place an unavailable algorithm shows up |
| Service starts and reaches the controller | TLS 1.3 handshake, certificate chain validation |
| Identity list populates with services | Controller API session over TLS |
| Dial a service end to end | Data path, including E2EE mode negotiation |
| MFA / TOTP enrolment and use | HMAC and hashing through the provider |
| External JWT signer authentication | Token signature verification |
| Restart the `ziti` service | Configuration reload and self-tests on a cold start |
| Reboot | Confirms nothing depended on install-session state |

`Restart-Service ziti` is sufficient for most of these; use a full reboot for the last.

## 7 -- the setting survives an upgrade

The failure this catches is described in
[implementation-plan.md](implementation-plan.md#the-upgrade-gap----fix-this-or-the-feature-is-a-lie): an
automatic silent upgrade that drops FIPS mode without saying so.

1. Note the current state -- steps 4 and 5 above.
2. Trigger an automatic update (`Restart-Service ziti-monitor` forces an immediate check).
3. After the upgrade completes, repeat steps 1 through 5.

`openssl.cnf` and `fipsmodule.cnf` must still be present, `fipsmodule.cnf` must verify against the **new**
`fips.dll`, and the `[FIPS]` marker must still be there. A missing `fipsmodule.cnf`, or one that fails to verify,
means the post-upgrade regeneration did not run.

## 8 -- uninstall leaves nothing behind

```powershell
Get-ChildItem $appdir -ErrorAction SilentlyContinue
```

`openssl.cnf` and `fipsmodule.cnf` must be gone. A stale `openssl.cnf` referencing a `fips.dll` that no longer
exists will stop a later reinstall's tunneler from establishing TLS at all.

## What to hand over

A completed run of this procedure, plus the module build's evidence manifest, is the evidence package. Together
they establish: this specific validated module, built from this verified source, is installed on this machine,
passes its self-tests here, is configured to exclude non-approved algorithms, and is the code path the tunneler
actually uses.
