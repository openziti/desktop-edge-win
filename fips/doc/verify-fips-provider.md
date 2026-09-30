# Verifying a freshly built FIPS provider

Do all of this before the module goes anywhere near the installer. Each stage answers a different question, and
the stages get progressively closer to what a customer will actually run.

## Stage 1 -- the module loads under its own openssl.exe

This is the OpenSSL project's own smoke test. It proves the DLL is intact and its self-tests pass. It does not
prove anything about ZDEW.

```powershell
Set-Location C:\openssl-install

.\bin\openssl.exe fipsinstall -pedantic `
    -module C:/openssl-install/lib/ossl-modules/fips.dll `
    -out C:/openssl-install/fipsmodule.test.cnf
```

`-pedantic` is the form §11.1 of the security policy documents for a non-default config location. Use it
everywhere, so the bench test exercises the same configuration the installer produces.

Expect `INSTALL PASSED`. Then point a config at it:

```ini
openssl_conf = openssl_init

.include "C:/openssl-install/fipsmodule.test.cnf"

[openssl_init]
providers = provider_sect

[provider_sect]
fips = fips_sect
default = default_sect

[fips_sect]
module = C:/openssl-install/lib/ossl-modules/fips.dll

[default_sect]
activate = 1
```

```powershell
Copy-Item .\ssl\openssl.cnf .\ssl\openssl.cnf.orig
# write the block above into .\ssl\openssl.cnf
.\bin\openssl.exe list -providers
```

```
Providers:
  default
    name: OpenSSL Default Provider
    version: 3.1.2
    status: active
  fips
    name: OpenSSL FIPS Provider
    version: 3.1.2
    status: active
```

This config deliberately leaves `default` active, because the question here is only "does the module load". It is
**not** the configuration ZDEW ships. See
[zdew-integration.md](zdew-integration.md#the-configuration-we-ship).

## Stage 2 -- the module loads with no C runtime on the box

The reason to care is in
[build-fips-provider.md](build-fips-provider.md#c-runtime-linkage): a `/MD` build imports `vcruntime140.dll`,
which Windows does not ship, and the resulting failure names a provider, not a missing DLL.

```powershell
# On the build machine, inspect the import table.
dumpbin /dependents C:\openssl-install\lib\ossl-modules\fips.dll
```

A static-CRT build lists only `KERNEL32.dll`, `ADVAPI32.dll` and `USER32.dll`. Anything mentioning
`vcruntime140` or `api-ms-win-crt-*` means the module needs a redistributable on every target machine.

Then confirm it on a clean VM with no Visual Studio and no VC++ redistributable installed. Copy `fips.dll` and
`openssl.exe` across and repeat stage 1 there.

## Stage 3 -- the module loads inside ziti-edge-tunnel

**Confirmed working, 2026-09-16.** Run it with:

```powershell
.\fips\scripts\Test-FipsWithTunneler.ps1 -ArtifactDir <evidence>\artifacts
```

The rest of this section explains what that script does and why the result was not a foregone conclusion.

ZDEW's `fips.dll` is built with MSVC. The OpenSSL core that loads it is **3.6.3, built with mingw-w64** and
statically linked into `ziti-edge-tunnel.exe`. Nothing had exercised that combination.

It works because the provider interface is a plain C dispatch table, and a FIPS provider takes its allocator
and its file I/O from core upcalls precisely so it can be built independently of the core. A cross-toolchain
load failing at the integrity check is the kind of problem that surfaces weeks later as an unexplained TLS
error, so it is verified before any installer work rather than assumed.

The version gap is also deliberate: a 3.1.2 provider under a 3.6.3 core is supported ("you can build OpenSSL
3.4 and use the OpenSSL 3.0.9 FIPS provider with it"), though four minor versions is wide enough to be worth
confirming rather than trusting.

### What `version -v` can and cannot tell you

`ziti-edge-tunnel version -v` calls `default_tls_context()` and prints immediately. The `openssl.cnf`
resolution and `tlsuv_set_config_path()` sit in the **run** path, after command dispatch, so a config file
beside the exe has no effect on that subcommand. Expecting `[FIPS]` from a bare `version -v` produces a false
negative on a perfectly configured machine.

`tls_lib_version()` reports the marker from `EVP_default_properties_is_fips_enabled(global_ctx)`. With
`global_ctx` unset that queries the **default** library context, which OpenSSL auto-configures from
`OPENSSL_CONF`. Setting that variable therefore exercises the same core loading the same module, without
running a tunnel:

```powershell
$env:OPENSSL_CONF = "C:\temp\fips-stage\openssl.cnf"
& C:\temp\fips-stage\ziti-edge-tunnel.exe version -v
```

That proves the module loads. It does not prove the production resolution path, where the service finds
`openssl.cnf` next to its own exe during `run` -- that needs a real installation and is covered by
[test-plan.md](test-plan.md).

```powershell
$stage = "C:\temp\fips-stage"
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# ziti-edge-tunnel.exe and wintun.dll from the release zip, plus our two build artifacts
Copy-Item C:\temp\zet\ziti-edge-tunnel.exe, C:\temp\zet\wintun.dll -Destination $stage
Copy-Item C:\openssl-install\lib\ossl-modules\fips.dll -Destination $stage
Copy-Item C:\openssl-install\bin\openssl.exe -Destination $stage

& "$stage\openssl.exe" fipsinstall -pedantic -out "$stage\fipsmodule.cnf" -module "$stage\fips.dll"

$appdir = $stage -replace '\\', '/'
@"
openssl_conf = openssl_init

.include "$appdir/fipsmodule.cnf"

[openssl_init]
providers = provider_sect
alg_section = algorithm_sect

[provider_sect]
base = base_sect
fips = fips_sect

[base_sect]
activate = 1

[fips_sect]
module = $appdir/fips.dll

[algorithm_sect]
default_properties = fips=yes
"@ | Set-Content -Path "$stage\openssl.cnf" -Encoding ASCII

& "$stage\ziti-edge-tunnel.exe" version -v
```

Pass condition -- the `tlsuv` line carries `[FIPS]`:

```
tlsuv:  v0.44.0[OpenSSL 3.6.3 9 Jun 2026 [FIPS]]
```

Failure modes and what they mean:

| Symptom | Meaning |
| --- | --- |
| No `[FIPS]` suffix, no error | `openssl.cnf` was not found, or `alg_section` / `default_properties` is missing |
| `failed to load config from [...]` | Config syntax error, bad `.include` path, or the module would not activate |
| Provider activation failure | Integrity HMAC mismatch (was the DLL modified after `fipsinstall`?), missing C runtime, or a genuine cross-toolchain incompatibility |

### Negative control

A test that cannot fail proves nothing. Repeat with `module` pointing at a path that does not exist, and confirm
the tunneler reports a config load failure rather than quietly carrying on. If it carries on, the `[FIPS]` suffix
alone is not sufficient evidence and the log line has to be checked too.

## Stage 4 -- a Ziti session actually works in FIPS-only mode

With `default` deactivated and `fips=yes` required, only algorithms the 3.1.2 FIPS provider implements are
available. Notably **X25519 and Ed25519 are not in it.** OpenSSL filters the TLS 1.3 group list by what is
available, so a handshake should fall back to P-256/P-384, but that is a claim to verify against a real
controller and router rather than to assume.

Run the full happy path against a local test network:

```powershell
ziti edge quickstart --home C:\temp\ziti-test
.\scripts\setup-ids-for-test.ps1 -ClearIdentitiesOk -Url https://localhost:1280 `
    -RouterName router-quickstart -ZitiHome C:\temp\ziti-test
```

Check: enrollment, controller login, service listing, a dial through a service, MFA if the identity uses it, and
external JWT signer auth if in scope. Enrollment is the most interesting case, because it generates a key pair --
if the FIPS provider cannot produce the key type the controller expects, that is where it surfaces.

Record what worked and what did not. Any algorithm the FIPS provider will not supply is a functional limitation
of FIPS mode and belongs in the release notes, not in a bug report.
