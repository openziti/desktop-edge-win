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
- [x] Commit `fips/provider.json`.
- [x] `Installer/build.ps1`: call `Installer/Get-FipsProvider.ps1` with `-Manifest "${checkoutRoot}\fips\provider.json"`
      and `-Destination "${buildPath}\service"`
      alongside the existing `ziti-edge-tunnel` fetch, so the FIPS files land next to `ziti-edge-tunnel.exe`
      exactly as they will on the target machine. It fails the build on a hash mismatch.
- [x] **Decided:** always fetch. The files are inert unless `openssl.cnf` exists, and they are gated by the
      `EnableFIPS` feature, so a machine that does not ask for FIPS never receives them.
- [ ] Record the FIPS provider version and `fips.dll` hash in `scripts/build-summary.txt` and in the build log
      next to the existing `ziti-edge-tunnel version -v` capture.

## Phase 4 -- installer

Every item here touches `Installer/ZitiDesktopEdge.aip`. **Do not edit the AIP without asking first** -- see the
root `CLAUDE.md`. What follows is the specification to agree before any AIP work happens.

The earlier attempt already built most of this and it was reverted. Recovering it is useful; shipping it as it
stood is not, because of the upgrade gap below.

- [x] Optional feature `EnableFIPS`, Installation Behavior "Not installed" with **Installed if**
      `ZITI_ENABLE_FIPS="1"`. The quotes matter: Advanced Installer accepts the unquoted `ZITI_ENABLE_FIPS=1`
      and it then never matches, because MSI evaluates a string property against an integer literal as false.
- [x] `fips.dll`, `openssl.exe`, `libcrypto-3-x64.dll` and `libssl-3-x64.dll` as components in `APPDIR` with
      `DigSign="true"`; `vcruntime140.dll` alongside them without it, since Microsoft already signed it.
- [x] A checkbox on `FolderDlg`, defaulted **off**, with text telling the reader to enable it only if they
      understand the implications. `FolderDlg` rather than `OptionalFeatsDlg` because it is the only page
      between Welcome and VerifyReady on a fresh install.
- [x] Deferred, elevated `SetFipsConfig` custom action after `InstallFiles`: runs `openssl.exe fipsinstall
      -pedantic`, writes `openssl.cnf` with `APPDIR` substituted and forward slashes, confirms the provider
      activates, and fails the install on any error. Conditioned `&EnableFIPS=3`, so it also runs on upgrades.
- [x] `RemoveFipsConfig` removes `openssl.cnf` and `fipsmodule.cnf`, conditioned `&EnableFIPS=2` so it covers
      both uninstall and a maintenance run that unticks the feature. Verified: neither file is left behind.

### The upgrade gap

The earlier attempt conditioned the generation action on `NOT Installed AND ZITI_ENABLE_FIPS`. ZDEW's
auto-updater runs the new MSI **silently**, and in a silent major upgrade nothing sets `ZITI_ENABLE_FIPS`, so
a machine that was in FIPS mode would quietly stop being in FIPS mode at its next automatic update.

What actually happens is the opposite, and it was observed rather than reasoned about. Every build gets a new
`ProductCode`, so every install over an existing one is a major upgrade, which means `MigrateFeatureStates`
runs. From an install log:

```
PROPERTY CHANGE: Modifying ZITI_ENABLE_FIPS property. Its current value is '0'. Its new value: '1'.
MigrateFeatureStates: based on existing product, setting feature 'EnableFIPS' to 'Absent' state.
Feature: EnableFIPS; Installed: Absent;   Request: Absent;   Action: Absent
```

The previous installation's feature selection wins over the current command line. That is the behaviour we
want for the silent case -- a FIPS machine stays a FIPS machine, and `SetFipsConfig` re-runs because it is
conditioned on `&EnableFIPS=3` rather than on `NOT Installed`, so a new `fips.dll` gets a matching
`fipsmodule.cnf`.

It also means **FIPS cannot be turned on by upgrading**. Passing `ZITI_ENABLE_FIPS=1` to an upgrade of a
non-FIPS installation does nothing. Enabling it on an existing install needs `ADDLOCAL=EnableFIPS` or a clean
install. Document that; it will otherwise be reported as a bug.

Still outstanding:

- [ ] Persist the choice at install time: `HKLM\SOFTWARE\NetFoundry\Ziti Desktop Edge`, `FipsEnabled`
      (REG_DWORD), so the setting is legible outside MSI's feature state -- for support, for inventory, and
      for the tray UI.
### Managed policy

Regulated fleets configure by policy, not by clicking a checkbox on 4,000 machines, so "require FIPS" has to
be expressible through Group Policy and MDM. ZDEW already has that machinery --
`ZitiUpdateService/windows/gpo/NetFoundry.ZitiMonitorService.admx`, documented in
`ZitiUpdateService/POLICY-ADMIN-GUIDE.md` -- but FIPS does not fit its existing shape, and that needs
deciding before any of it is built.

Every policy today (`UpdateTimer`, `InstallationCritical`, the maintenance window settings) is read **at
runtime** by `ziti-monitor`, which can act on a changed value immediately. FIPS is not a runtime setting. It
is MSI feature state fixed at install time, and a policy cannot switch on a feature whose files are not on
disk. So there are two halves, and only one of them enforces anything:

- [ ] **Installer side.** `AppSearch` the policy value into `ZITI_ENABLE_FIPS` so a policied machine installs
      the feature without anyone passing a command line. On its own this only works for fresh installs:
      `MigrateFeatureStates` makes an existing installation's feature selection win on every upgrade, so
      turning FIPS *on* for a machine that lacks it also needs `ADDLOCAL=EnableFIPS`. Decide whether the
      installer forces that when policy requires FIPS, or whether policy-driven enablement is a documented
      reinstall.
- [ ] **Runtime side.** `ziti-monitor` reads the policy and refuses to start the tunneler when policy requires
      FIPS and the machine is not in FIPS mode, logging why. This is the half that actually enforces, and it
      is also the answer to "what happens when `fipsinstall` fails on a machine whose administrator declared
      it must not run non-approved cryptography".
- [ ] Decide what the policy is named and where it lives in the ADMX tree, alongside the existing update
      settings rather than in a category of its own.
- [ ] `POLICY-ADMIN-GUIDE.md` carries compliance presets (CJIS, DISA STIG, PCI, NIST, NERC CIP, HITRUST).
      Whoever reads those is the exact audience for FIPS, so the install flag belongs next to the update
      cadence settings in the same presets.

### Automatic update URL

`AutomaticUpdateURL_Text` lets a fleet override the update stream URL entirely. Anything that depends on
changing what a stream file advertises -- notably the `-win32crypto` migration in
[../../doc/win32crypto-deprecation.md](../../doc/win32crypto-deprecation.md) -- does not reach a fleet pointing
that policy at its own mirror. Those customers have to be told directly.

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
