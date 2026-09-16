# FIPS 140-3 validated cryptography in Ziti Desktop Edge for Windows

This folder documents how Ziti Desktop Edge for Windows (ZDEW) obtains, ships, activates and proves the use of a
FIPS 140-3 validated cryptographic module.

ZDEW does not implement cryptography. Its TLS and key handling come from `ziti-edge-tunnel.exe`, which embeds
OpenSSL. FIPS support therefore reduces to one job: make that embedded OpenSSL load the **OpenSSL FIPS Provider**
instead of its own built-in algorithm implementations, and be able to demonstrate that it did.

## FIPS validated cryptography

> Ziti Desktop Edge for Windows can be installed in a mode where all TLS and key-management cryptography is
> performed by the OpenSSL FIPS Provider, a module validated under FIPS 140-3 ([CMVP certificate **#4985**](https://csrc.nist.gov/projects/cryptographic-module-validation-program/certificate/4985),
> sunset 10 March 2030). The product itself is not a validated module and is not submitted to the CMVP.

Please **be careful** when referencing FIPS as the wording matters. Read and fully understand
[compliance-position.md](doc/compliance-position.md) before writing marketing copy, answering a customer
questionnaire, or filling in an audit response.

## Fast facts

- Validated module: **OpenSSL FIPS Provider 3.1.2**, CMVP certificate #4985, FIPS 140-3, Security Level 1,
  sunset 10 March 2030.
- 3.1.2 is the only OpenSSL-project FIPS provider with an active certificate. Newer OpenSSL releases may *use*
  it, but must not build their own. Do not "upgrade" the provider to match the core library version.
- Source: `openssl-3.1.2.tar.gz`, SHA-256 `a0ce69b8b97ea6a35b96875235aa453b966ba3cba8af2de23657d8b6767d6539`.
- Consuming core library is OpenSSL **3.6.3+ (version may change)**, statically linked into
  `ziti-edge-tunnel.exe` (as of ziti-edge-tunnel v1.19.0).
- `fipsmodule.cnf` **must be generated on every installation** (`fipsinstall -pedantic`) and cannot be
  shipped. Not because its content differs -- the MAC is over the bytes of `fips.dll`, which are identical
  everywhere -- but because §11.1 of the security policy requires the installation steps be performed on each
  platform where the module is used, so the self-tests run in that operational environment.
- The OpenZiti project is allowed to build the module itself by CMVP Management Manual §7.9.2 footnote 5,
  which requires the security policy's build method be followed **without modification**.
- Four artifacts are distributed as assets on a public GitHub release, pinned by SHA-256 in
  `fips/provider.json`, and are not committed to this repository. This avoids adding binaries and bloat to
  the checkout. `vcruntime140.dll` ships alongside them but comes from the ZDEW build machine, not the
  release -- see [zdew-integration.md](doc/zdew-integration.md).

## Documentation index

**Start here if you just need to build the module: [runbook.md](doc/runbook.md).** Four commands, about an hour.

| Document | What it covers |
| --- | --- |
| [runbook.md](doc/runbook.md) | The operator's page: build, verify, publish, tear down |
| [New-AzureFipsBuildVm.ps1](scripts/New-AzureFipsBuildVm.ps1) | Builds the module end to end on an Azure Windows 10 VM. What the runbook drives |
| [Test-FipsWithTunneler.ps1](scripts/Test-FipsWithTunneler.ps1) | Proves the module loads inside `ziti-edge-tunnel`. Runs locally |
| [Publish-FipsProvider.ps1](scripts/Publish-FipsProvider.ps1) | Publishes a build as a GitHub release and writes `provider.json` |
| [Get-FipsProvider.ps1](../Installer/Get-FipsProvider.ps1) | Fetches the pinned artifacts with hash verification, for `Installer/build.ps1` |
| [Build-FipsProvider.ps1](scripts/Build-FipsProvider.ps1) | The compile itself, with the evidence manifest. Runs inside the build VM |
| [build-fips-provider.md](doc/build-fips-provider.md) | Why the build is the way it is, and the publishing rules |
| [verify-fips-provider.md](doc/verify-fips-provider.md) | Bench verification of a freshly built module, before it goes near the installer |
| [zdew-integration.md](doc/zdew-integration.md) | How `ziti-edge-tunnel` finds and loads the provider, and what ZDEW has to place on disk |
| [implementation-plan.md](doc/implementation-plan.md) | The work items to get this shipped, in order, with the known gaps |
| [compliance-position.md](doc/compliance-position.md) | What we may and may not claim, the operational-environment scope, and the evidence we retain |
| [test-plan.md](doc/test-plan.md) | How to prove, on a real machine, that validated cryptography is in use |
