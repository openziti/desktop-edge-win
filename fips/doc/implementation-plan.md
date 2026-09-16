# Implementation plan

Ordered so that each phase de-risks the next. Do not start phase 3 before phase 2 passes -- the installer work is
the expensive part and it is wasted if the module will not load inside the tunneler.

## Phase 1 -- reproducible module build

- [ ] Re-read the current security policy for CMVP certificate #4985 and reconcile it with
      [build-fips-provider.md](build-fips-provider.md). The procedure previously used was written against the
      2023-12-29 draft; the certificate was validated 11 March 2025 and updated 21 November 2025.
- [ ] C runtime: **decided** -- keep the documented `/MD` build and solve loading in the installer (VC++
      redistributable prerequisite, or ship `vcruntime140.dll`). `/MT` modifies the security policy's build
      method and forfeits the MM §7.9.2 footnote 5 cover. Choose which of the two installer options to take.
- [ ] Run `fips\scripts\Build-FipsProvider.ps1` end to end on a clean **Windows 10 x64** VM with **Visual Studio 2019**,
      matching §5.3 of the security policy. Keep the VM image reference. CI output is a test artifact.
- [ ] Archive the evidence package produced by the script somewhere that outlives the VM.
- [ ] Do **not** sign as part of the module build. ZDEW's installer build already signs every binary it
      packages, and `fips.dll` and `openssl.exe` go through that same pass. Keeping one signing process means
      a change to how signing works never leaves these two files behind.

      The consequence for publishing: the release and `fips/provider.json` carry the **unsigned** hashes,
      which is what ties the shipped bytes back to this build. The installer verifies those hashes on
      download, then signs. `fipsinstall` runs at install time on the signed file, so the integrity HMAC
      still covers what actually ships.

The build machine from the previous attempt was an Azure VM that has been stopped for over a year. Treat it as
gone. The script exists so the procedure no longer lives on one host.

## Phase 2 -- bench verification

- [x] **Stages 1 to 3 pass** (`fips/scripts/Test-FipsWithTunneler.ps1`, 2026-09-16). An MSVC-built 3.1.2 `fips.dll`
      loads into the mingw-built OpenSSL 3.6.3 core inside `ziti-edge-tunnel.exe`, and the tunneler reports
      `[FIPS]`. The negative control confirms the marker is meaningful. This was the single largest unknown in
      the plan; it is cleared, and none of the upstream fallbacks are needed.
- [x] Module self-tests pass on a host matching neither the certificate's tested environment nor the build
      host: `Module_Integrity`, every algorithm KAT, all three DRBGs, every KDF, signatures and key agreement.
- [ ] Stage 4: functional pass against a quickstart network in FIPS-only mode, with any algorithm limitations
      written down. X25519 and Ed25519 are absent from the 3.1.2 provider, so enrollment and TLS 1.3 group
      negotiation are the cases to watch.

## Phase 3 -- build pipeline

**Decided:** the artifacts are published as assets on a **public GitHub release** in this repository and pinned
by SHA-256 in `fips/provider.json`. They are not committed. The earlier attempt committed both binaries
(8.7 MB) into `Installer/openssl/`; a blob in git history is not an evidence chain, and a public release plus a
hash lets a customer verify our claim without asking us for anything.

- [x] **First provider release published** (2026-09-16), tag `fips-provider-3.1.2-20260916`, marked not-latest
      so it cannot displace a ZDEW product release. All four artifacts plus `evidence.zip` attached.
- [x] `fips/provider.json` generated and verified: every pinned hash matches a fresh download from the
      published URLs.
- [ ] Commit `fips/provider.json`.
- [ ] `Installer/build.ps1`: call `Installer/Get-FipsProvider.ps1` with `-Manifest "${checkoutRoot}\fips\provider.json"`
      and `-Destination "${buildPath}\service"`
      alongside the existing `ziti-edge-tunnel` fetch, so the FIPS files land next to `ziti-edge-tunnel.exe`
      exactly as they will on the target machine. It fails the build on a hash mismatch.
- [ ] Decide whether a FIPS-less build should skip the fetch. Recommendation: always fetch. The files are inert
      unless `openssl.cnf` exists, and having the MSI able to install the feature is the point.
- [ ] Record the FIPS provider version and `fips.dll` hash in `scripts/build-summary.txt` and in the build log
      next to the existing `ziti-edge-tunnel version -v` capture.

## Phase 4 -- installer

Every item here touches `Installer/ZitiDesktopEdge.aip`. **Do not edit the AIP without asking first** -- see the
root `CLAUDE.md`. What follows is the specification to agree before any AIP work happens.

The earlier attempt already built most of this and it was reverted. Recovering it is useful; shipping it as it
stood is not, because of the upgrade gap below.

- [ ] Optional feature `EnableFIPS` (`Level=4`), enabled by a `MsiConditionComponent` row on the
      `ZITI_ENABLE_FIPS` property.
- [ ] `fips.dll` and `openssl.exe` as components in `APPDIR` with `DigSign="true"`.
- [ ] An `OptionalFeatsDlg` page carrying the checkbox, defaulted **off**, with text that tells the reader they
      almost certainly do not want it.
- [ ] Deferred, elevated custom action after `InstallFiles`: write `openssl.cnf` with `[APPDIR]` substituted and
      forward slashes, run `openssl.exe fipsinstall`, and fail the install on a non-zero exit.
- [ ] Custom action on uninstall that removes `openssl.cnf` and `fipsmodule.cnf`. (The earlier attempt had this;
      keep it. Note that ZDEW uninstall already has a known gap around leftover state -- the pending-update
      scheduled task -- so do not add another.)

### The upgrade gap -- fix this or the feature is a lie

The earlier attempt conditioned the generation action on `NOT Installed AND ZITI_ENABLE_FIPS`. ZDEW's
auto-updater runs the new MSI **silently**. In a silent major upgrade nothing sets `ZITI_ENABLE_FIPS`, so the
feature's condition evaluates false, the feature is not installed, the custom action does not run, and a machine
that was in FIPS mode quietly stops being in FIPS mode at the next automatic update. No error, no log entry, no
UI change.

Required behaviour:

- [ ] Persist the choice at install time: `HKLM\SOFTWARE\NetFoundry\Ziti Desktop Edge`, `FipsEnabled` (REG_DWORD).
- [ ] `AppSearch` that value into `ZITI_ENABLE_FIPS` early in the install sequence, so a silent upgrade inherits
      the machine's existing setting.
- [ ] Run the generation action on **every** install and major upgrade, not only first install. `fipsmodule.cnf`
      is keyed to the bytes of `fips.dll`; a new `fips.dll` needs a new `fipsmodule.cnf`.
- [ ] A managed-policy override, so MDM and Group Policy can *require* FIPS regardless of what the interactive
      installer was told. This belongs with ZDEW's other managed policies -- see
      `ZitiUpdateService/POLICY-ADMIN-GUIDE.md` and `ZitiUpdateService/windows/gpo/`. Regulated fleets configure
      by policy, not by clicking a checkbox on 4,000 machines.
- [ ] Decide what happens when policy says "required" and `fipsinstall` fails. Recommendation: refuse to start
      the tunneler and say why, rather than silently running non-approved cryptography on a machine whose
      administrator has declared it must not.

## Phase 5 -- surfacing real state

- [ ] Replace the presence check in `DesktopEdge/Views/Screens/MainMenu.xaml.cs`. The earlier attempt showed the
      FIPS panel when `File.Exists(fips.dll)` was true, which is proof that a file was copied and nothing more.
- [ ] Read the actual state from the tunneler: the `[FIPS]` marker in the `tlsuv` version string, or the
      `- openssl config : configured using ... found by ...` startup log line. Both are described in
      [zdew-integration.md](zdew-integration.md#proving-it-at-runtime).
- [ ] Show the provider version and a link to CMVP certificate #4985, not a Wikipedia article on FIPS 140-2. The
      module is validated under **140-3**, and 140-2 validations move to the CMVP historical list in
      September 2026.
- [ ] Log the FIPS state in the monitor service log and include it in support bundles, so the
      `debug-ziti-desktop-edge-win` workflow can tell whether a reporting machine was in FIPS mode.

## Phase 6 -- documentation and release

- [ ] Release-notes entry that states the certificate number, the provider version, the supported operational
      environment, and the known algorithm limitations.
- [ ] A customer-facing page: how to enable it, how to verify it themselves, what is and is not covered. Point it
      at [compliance-position.md](compliance-position.md) rather than restating the claim in new words.
- [ ] Add the FIPS cases to `manual-testing.md`.
- [ ] Subscribe someone to OpenSSL's FIPS/CVE announcements and write down what happens when one lands. See
      [compliance-position.md](compliance-position.md#cves-in-the-validated-module).

## Open questions for the tunneler and SDK maintainers

1. Is the `-win32crypto` Windows build being retired? It is still published for v1.19.0, and ZDEW's paired
   release streams and promotion tooling exist only to serve it.
2. Is an MSVC-built FIPS provider loading into the mingw-built static OpenSSL core a configuration upstream is
   willing to support, or should ZDEW expect a change in how OpenSSL is linked?
3. How is the E2EE mode selected and can it be pinned to `tls` (or `aes-gcm`) from the client side? The FIPS
   claim covers the transport regardless; it covers the E2EE payload only in those modes.
4. Does anything in ziti-edge-tunnel or ziti-sdk-c call OpenSSL outside the library context created by
   `tlsuv`? Anything on the default context is unaffected by `openssl.cnf` and sits outside the boundary.
