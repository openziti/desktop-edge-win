# Building the OpenSSL FIPS Provider for Windows

The output of this procedure is two files:

- `fips.dll` -- the validated cryptographic module, shipped with ZDEW.
- `openssl.exe` -- the OpenSSL command-line tool, shipped with ZDEW so the installer can run `openssl fipsinstall`
  on the target machine.

Both come from the same build tree. Nothing else from the build is shipped.

## Ground rules

These are not style preferences. Breaking any of them breaks the claim.

1. **Only OpenSSL 3.1.2.** It is the sole OpenSSL-project release with an active FIPS certificate (#4985). A newer
   3.1.x patch release is *not* validated, even though it fixes CVEs. See
   [compliance-position.md](compliance-position.md#cves-in-the-validated-module) for how to handle a CVE.
2. **Verify the source before building.** Check the tarball SHA-256 against the published value and against the
   value in the certificate's security policy. This is the first link in the evidence chain and the one auditors
   pull on hardest.
3. **Use the Configure line the security policy specifies, and nothing else.** This is not a nicety. Our
   permission to rebuild a validated module at all comes from CMVP Management Manual §7.9.2 footnote 5:

   > A user may post-validation recompile a module if the unmodified source code is available and the module's
   > Security Policy provides specific guidance on acceptable recompilation methods to be followed as a specific
   > exception to this guidance. **The methods in the Security Policy must be followed without modification** to
   > comply with this guidance.

   The method in §11.1 of the #4985 security policy, verbatim, is:

   ```
   perl Configure enable-fips
   nmake
   nmake install
   ```

   Add a flag to that and the footnote-5 cover goes away. Record the full build configuration as evidence.
4. **Never ship a pre-generated `fipsmodule.cnf`.** It embeds an HMAC of `fips.dll` and is generated per
   installation by `openssl fipsinstall`. See [zdew-integration.md](zdew-integration.md).
5. **Read the current security policy before each build.** Certificate #4985 was validated 11 March 2025 and
   updated 21 November 2025. Any procedure written against an earlier draft of the policy is stale. The
   authoritative document is linked from the CMVP certificate page:
   <https://csrc.nist.gov/projects/cryptographic-module-validation-program/certificate/4985>

## Build host

A clean, disposable Windows VM. Record the image and the toolchain versions in the evidence manifest, because
"which compiler" is a question you will be asked.

The whole thing is one command; see [runbook.md](runbook.md) for the operator's version.

```powershell
.\fips\scripts\New-AzureFipsBuildVm.ps1 -Action All
```

It creates an Azure VM, installs the toolchain, compiles, and copies the artifacts and evidence back.

### Why this image

| Choice | Reason |
| --- | --- |
| `MicrosoftWindowsDesktop:Windows-10:win10-22h2-pro-g2` | §5.3 of 140sp4985 names "Windows 10" and Table 3 names "Windows 10 Pro". Enterprise, LTSC and AVD variants are all further from that wording |
| Visual Studio 2019 Community | §5.3 records "Windows 10: Visual Studio 2019" as the compiler used for the tested environment. `Build-FipsProvider.ps1` pins to `[16.0,17.0)` and refuses anything else |
| Strawberry Perl, NASM | OpenSSL needs Perl to configure and NASM to assemble the x86-64 primitives |
| Azure | The Windows licence comes with the subscription. Windows 10 client images require Visual Studio subscription entitlement, asserted by `--license-type Windows_Client` |

Azure is not required. Any Windows 10 x64 machine with VS 2019 works -- run `Build-FipsProvider.ps1` on it
directly. The script exists so the machine is disposable and the toolchain is recorded rather than remembered.

Building on Windows 10 with VS 2019 is a deliberate choice, not convenience. It is the only Windows entry in
the certificate's tested list, and matching it removes an argument you would otherwise have to win. See
[compliance-position.md](compliance-position.md#two-unresolved-points).

## Automated build

Preferred. Run from an ordinary PowerShell 7 prompt; the script locates and imports the MSVC environment itself.

```powershell
.\fips\scripts\Build-FipsProvider.ps1 -EvidenceRoot C:\fips-evidence
```

Add `-EnsureToolchain` on a fresh VM to have Perl and NASM installed via Chocolatey. Visual Studio is never
installed automatically.

The script performs the whole sequence below, refuses to continue on a source hash mismatch, and writes an
evidence manifest. Read [the script](../scripts/Build-FipsProvider.ps1) rather than trusting this description.

There is deliberately no CI build. A GitHub Windows runner is Windows Server with Visual Studio 2022, and
building a shipping module there would put the compiler outside what §5.3 records. The reasoning, including the
argument that the build host's OS is a separate and easier question than its compiler, is in
[compliance-position.md](compliance-position.md#two-unresolved-points).

## Publishing

`fips.dll` and `openssl.exe` are distributed as assets on a **public GitHub release** in this repository, and
`Installer/build.ps1` downloads them with a pinned SHA-256. They are not committed to the repository.

Publishing in the open is the point rather than a convenience. The compliance claim rests on being able to show
what was built from what. A binary anyone can download and hash, alongside its evidence package, lets a customer
verify the claim without asking us for anything -- which is a materially better position than "here are our
build steps, trust us".

```powershell
.\fips\scripts\Publish-FipsProvider.ps1 -EvidenceDir C:\fips-evidence\openssl-3.1.2-20260916-101500
```

The script creates a draft release tagged `fips-provider-<version>-<date>`, attaches the artifacts and
`evidence.zip`, generates release notes containing the source hash, the build configuration, the artifact
hashes and the certificate reference, and rewrites `fips/provider.json` with the pin.

**The published artifacts are unsigned, deliberately.** ZDEW's installer build already Authenticode-signs every
binary it packages, and these go through that same pass. One signing process means a change to how signing
works never leaves these files behind.

So the hashes in `provider.json` are the **unsigned** hashes. They tie the bytes the installer downloads back
to this build, which is what they are for. The installer verifies them, then signs. `fipsinstall` runs at
install time on the signed file, so the module's integrity HMAC still covers exactly what ships.

Never reuse a release tag. Consumers pin by tag, so a mutated tag silently changes what ships.

### Consuming the pin

`Installer/Get-FipsProvider.ps1` reads `fips/provider.json`, downloads each asset, verifies its SHA-256 and
stages it. A mismatch throws rather than warns. It lives beside the installer because that is its only
caller, but it runs standalone so a pin can be checked before it is committed.

```powershell
.\Installer\Get-FipsProvider.ps1 -Manifest .\fips\provider.json -Destination .\Installer\build\service
```

`fips/provider.json` does not exist until the first release is published; `Get-FipsProvider.ps1` says so plainly
rather than building an installer with no module in it.

## Manual build

Use this only to understand or audit what the script does.

Everything from `vcvars64.bat` onward must run in **one** shell that has the MSVC environment loaded. The usual
way to get that wrong is to launch `vcvars64.bat` from PowerShell: it configures a child `cmd.exe` that exits
immediately, leaving the parent PowerShell session with no `nmake` and no compiler. Either use the *x64 Native
Tools Command Prompt for VS 2019* and cmd syntax throughout, or import the environment into PowerShell the way
the script does.

From an **x64 Native Tools Command Prompt for VS 2019** (cmd syntax, `^` continuations):

```bat
mkdir C:\fips-build
cd C:\fips-build

curl -L -O https://github.com/openssl/openssl/releases/download/openssl-3.1.2/openssl-3.1.2.tar.gz
certutil -hashfile openssl-3.1.2.tar.gz SHA256
rem expect: a0ce69b8b97ea6a35b96875235aa453b966ba3cba8af2de23657d8b6767d6539

tar -xzf openssl-3.1.2.tar.gz
cd openssl-3.1.2

rem The security policy's method is the bare 'perl Configure enable-fips'. VC-WIN64A is what that
rem auto-selects on an x64 MSVC host, and --prefix/--openssldir only relocate the install tree, which
rem the policy contemplates by documenting 'openssl fipsinstall' for non-default locations.
perl Configure enable-fips VC-WIN64A ^
  --prefix=C:\openssl-install ^
  --openssldir=C:\openssl-install\ssl

perl configdata.pm --dump > C:\fips-evidence\configdata.txt

nmake
nmake test
nmake install
```

A successful install reports:

```
*** Installing FIPS module
install providers\fips.dll -> C:\openssl-install\lib\ossl-modules\fips.dll
*** Installing FIPS module configuration
install providers\fipsmodule.cnf -> C:\openssl-install\ssl\fipsmodule.cnf
```

Take `C:\openssl-install\lib\ossl-modules\fips.dll` and `C:\openssl-install\bin\openssl.exe`. Discard the
`fipsmodule.cnf` that `nmake install` produced; it is keyed to the build machine's copy of the DLL and has no
value on a target machine.

### Notes on the manual sequence

- Run `perl Configure` **once**. Running it twice in the same tree leaves a half-configured build; if you need
  to reconfigure, start from a fresh extraction of the tarball. Naming `VC-WIN64A` explicitly is equivalent to
  what the bare form auto-selects on an x64 MSVC host and makes the evidence unambiguous.
- `nmake test` is not optional. It runs the provider's self-tests and the algorithm known-answer tests. Capture
  the output; a passing test run is part of the evidence package.
- `--openssldir` sets the path compiled into `openssl.exe` as its default config location. ZDEW never relies on
  that path -- the installer passes explicit paths -- but set it anyway so an operator running `openssl.exe` by
  hand gets predictable behaviour.

## C runtime linkage

This is the one real packaging decision and it needs to be settled deliberately.

`VC-WIN64A` defaults to the **dynamic** C runtime (`/MD`). A `fips.dll` built that way imports
`vcruntime140.dll`, which does **not** ship with Windows. Dropped into the ZDEW install directory on a machine
with no Visual C++ redistributable, it fails to load, and OpenSSL reports a provider activation failure rather
than anything that names the missing DLL.

Two ways out:

1. **Dynamic CRT plus redistributable.** Keep the documented build and make the VC++ redistributable an
   installer prerequisite, or ship `vcruntime140.dll` next to `fips.dll`. Cost: a new prerequisite in an
   installer that currently has none, and a second file whose servicing you now own.
2. **Static CRT.** Build with `/MT`. The resulting `fips.dll` imports only `KERNEL32`, `ADVAPI32` and `USER32`,
   and loads anywhere. This is what the artifact from the earlier attempt did -- its import table shows no CRT
   dependency, and its embedded PDB path (`...\Desktop\openssl-static\openssl-3.1.2\providers\fips.pdb`) shows
   it came from a tree configured differently from the steps that were written down at the time.

Recommendation: **dynamic CRT**, and solve the loading problem in the installer.

This reverses the intuitive answer, so here is the reasoning. `/MT` changes no cryptographic code, and on a
purely technical reading it is harmless. But the permission to rebuild a validated module at all is CMVP
Management Manual §7.9.2 footnote 5, and its condition is that the security policy's methods "must be followed
without modification". Adding `/MT` to `perl Configure enable-fips` modifies the method. The trade is a second
redistributable file against the sentence that makes our whole claim work, and that is not a close call.

If a technical dead end forces `/MT` anyway, `Build-FipsProvider.ps1 -StaticCrt $true` will do it, warn loudly,
and record the deviation in the evidence manifest under `methodDeviations` so nobody discovers it during an
audit.

Note that take-1's shipped `fips.dll` was a static-CRT build, which means that artifact was produced by a
modified method. Do not reuse it.

## Evidence to retain per build

Keep all of it, in an archive that outlives the build VM. See
[compliance-position.md](compliance-position.md#evidence-we-retain).

- The source tarball, plus its computed and published SHA-256.
- `perl configdata.pm --dump` output.
- Full `nmake` and `nmake test` logs.
- SHA-256 of the produced `fips.dll` and `openssl.exe`.
- Toolchain versions: `cl.exe` banner, `perl -v`, `nasm -v`, Windows build number.
- The Authenticode signature applied to `fips.dll`, and its timestamp.
