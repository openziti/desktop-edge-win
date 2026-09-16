# Building and publishing the FIPS provider

This file is intended to get you running quickly. It outlines the prerequisites necessary,
build a VM on Azure using the proper OS, install pre-requisites on that VM, build the FIPS provider,
and collect the durable evidence necessary to pass an audit.

[The scripts in ../scripts](../scripts/) are all used to do this work. Read them for more details.

## Before you start

```powershell
winget install --id Microsoft.AzureCLI -e
```

Close and reopen PowerShell so `az` is on `PATH`, then:

```powershell
az login
az account set --subscription "<your subscription>"
```

You need a subscription with Visual Studio entitlement -- Windows 10 client images in Azure require it.

## 1. Build the module

```powershell
.\fips\scripts\New-AzureFipsBuildVm.ps1 -Action All
```

Creates an Azure Windows 10 Pro VM, installs Visual Studio 2019, Strawberry Perl and NASM, builds OpenSSL
**3.1.2** from source with `enable-fips`, runs the test suite, and copies the artifacts and evidence back to
`D:\fips-buildhost\azure`.

Prompts once for an SSH public key; press Enter to accept your default. Generates and prints the VM's
administrator password -- record it.

About 40 minutes. Safe to re-run: it skips anything already done, so an interrupted run resumes.

You end up with:

| Path | What |
| --- | --- |
| `...\openssl-3.1.2-<stamp>\artifacts\` | `fips.dll`, `openssl.exe`, `libcrypto-3-x64.dll`, `libssl-3-x64.dll` |
| `...\openssl-3.1.2-<stamp>\` | `manifest.json`, `build-record.md`, build and test logs, source tarball |
| `...\run-log-<stamp>.json` | Every action the run took, including the skipped ones |
| `...\transcript-<stamp>.log` | Full console output |

The evidence directory is what gets retained. Details in
[compliance-position.md](compliance-position.md#evidence-we-retain).

## 2. Verify it works with ziti-edge-tunnel

```powershell
.\fips\scripts\Test-FipsWithTunneler.ps1 `
    -ArtifactDir D:\fips-buildhost\azure\openssl-3.1.2-<stamp>\artifacts
```

Runs on your own machine, installs nothing, touches no service. Downloads a ziti-edge-tunnel release, runs
`openssl fipsinstall` against the module, and confirms the tunneler loads it and reports `[FIPS]`. Includes a
negative control, so a pass means something.

Eight checks, under a minute. Stop here if any fail.

## 3. Publish it

```powershell
.\fips\scripts\Publish-FipsProvider.ps1 -EvidenceDir D:\fips-buildhost\azure\openssl-3.1.2-<stamp> -DryRun
.\fips\scripts\Publish-FipsProvider.ps1 -EvidenceDir D:\fips-buildhost\azure\openssl-3.1.2-<stamp>
```

Creates a **draft** GitHub release in `openziti/desktop-edge-win` tagged `fips-provider-3.1.2-<date>`,
attaches the four artifacts plus `evidence.zip`, and writes `fips/provider.json` with the download URLs and
SHA-256 hashes.

`-DryRun` prints the release notes, the exact `gh` command, and the pin file without creating anything.

Then:

1. Review the draft on GitHub and publish it. The URLs in `provider.json` do not resolve until you do.
2. Commit `fips/provider.json`. That is what `Installer/build.ps1` reads to fetch the module at build time.

The artifacts are published unsigned on purpose -- ZDEW's installer build signs them along with everything
else it packages. See [build-fips-provider.md](build-fips-provider.md#publishing).

## 4. Stop the VM, then destroy it

Deallocate as soon as the build is done. Compute stops billing; the disk and public IP continue at roughly
40 USD a month, against about 140 running.

```powershell
az vm deallocate --resource-group fips-build-rg --name fips-build
```

Destroy it once a FIPS-enabled installation passes [test-plan.md](test-plan.md). At that point the artifacts
are proven and any future rebuild is a deliberate project with time to spare for step 1.

```powershell
.\fips\scripts\New-AzureFipsBuildVm.ps1 -Action Destroy
```

**Do not keep it as a record of the build.** A stopped VM does not preserve the machine that compiled the
module: restart it later and it patches itself, the network rules point at an address you no longer have, and
the image SKU may be gone. The evidence directory from step 1 is the record, it is 16 MB, and it does not rot.

The previous FIPS build machine was stopped rather than destroyed and was unusable a year later. Step 1 exists
so the machine is disposable.

## When to do this again

Rarely. The module is pinned to OpenSSL 3.1.2 because that is the only version with an active CMVP
certificate, so there is no routine update to chase. Rebuild when:

- A newer OpenSSL FIPS provider gets validated, and you decide to move to it.
- You need the module for a platform you have not built for.
- Certificate #4985 sunsets on **2030-03-10**.

A CVE in OpenSSL is **not** on that list. See
[compliance-position.md](compliance-position.md#cves-in-the-validated-module).

`vcruntime140.dll` ships beside the module but is not built here -- `Get-FipsProvider.ps1` takes it from the
ZDEW build machine's Visual Studio install, so refreshing it never requires this VM.
