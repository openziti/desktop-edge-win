# Retiring the -win32crypto distribution

Target date: **2027-03-16** (six months from 2026-09-16).

## Why it exists, and why it can go

The `-win32crypto` build ships a `ziti-edge-tunnel.exe` whose TLS runs on Windows CNG instead of OpenSSL. It
exists for customers who need FIPS-validated cryptography and were getting it from Microsoft's validated CNG
modules, because ZDEW's normal build had no validated option.

That reason is gone. As of 2.11.8.0 the OpenSSL build can load the OpenSSL FIPS Provider 3.1.2 (CMVP #4985),
so a single distribution serves both audiences. Keeping two means every release, promotion and hotfix happens
twice.

One constraint that does not change: **only the OpenSSL build can load the FIPS provider.** A `-win32crypto`
build must never be shipped with the FIPS feature enabled. Until the `-win32crypto` build is gone, that is a
release-checklist item.

Upstream has not retired its side. ziti-edge-tunnel v1.19.0 still publishes
`ziti-edge-tunnel-Windows_x86_64-win32crypto.zip`. Confirm the tunneler maintainers' plans before the final
step, but ZDEW can stop distributing it regardless.

## The part that needs care

Installed clients poll a URL compiled into the binary. `ZitiDesktopEdge.Client/Utility/GithubAPI.cs`:

```csharp
#if WIN32CRYPTO
    public const string ProdUrl = "https://get.openziti.io/zdew/stable-win32crypto.json";
#else
    public const string ProdUrl = "https://get.openziti.io/zdew/stable.json";
#endif
```

An existing `-win32crypto` installation will keep asking for `stable-win32crypto.json` forever. Deleting that
file, or letting it go stale, silently strands those machines on an unpatched build -- no error, no prompt,
nothing in the UI. Both stable streams are on 2.11.3.2, published 2026-08-31, because `scripts/promote.ps1`
moves them together. That has to stay true for the whole window.

The migration therefore runs through that file rather than around it: keep serving it, and at the cutover
point have it advertise the **regular** build. Both variants come from the same `ZitiDesktopEdge.aip` with the
same `UpgradeCode`, so the normal installer upgrades a `-win32crypto` installation in place. Verify that on a
VM before relying on it.

## Plan

### Now -- announce

- [ ] Release-notes entry for 2.11.8.0: the `-win32crypto` distribution is deprecated, will receive no new
      features, and stops being published after 2027-03-16. Say what replaces it and that FIPS is the reason
      it existed.
- [ ] Note it in `releasing.md` next to the dual-build instructions, so nobody adds work to a dying variant.
- [ ] Note it in `BUILDING.md`.
- [ ] Tell known `-win32crypto` customers directly. The install base is small enough to name, and a release
      note is not a migration plan.

### Through the window -- keep publishing, add no features

- [ ] Keep promoting both builds together, as `scripts/promote.ps1` already does.
- [ ] Never enable the FIPS feature in a `-win32crypto` build.
- [ ] When a customer asks about FIPS, move them to the OpenSSL build with FIPS enabled rather than to
      `-win32crypto`.

### At the cutover -- migrate, then remove

- [ ] Point `stable-win32crypto.json`, and the beta and latest versions of it, at the **regular** artifact, so installed
      clients upgrade themselves onto the OpenSSL build. Test this on a VM first: install `-win32crypto`,
      repoint the stream, confirm the update installs and the tray app comes back healthy.
- [ ] Leave those JSON files in place afterwards. They cost nothing and they are the only thing keeping older
      installations reachable.
- [ ] Only then remove the build machinery.

## What has to change when the build is removed

Actual `-win32crypto` distribution machinery:

| File | What is there |
| --- | --- |
| `Installer/build.ps1` | `-Win32Crypto` parameter, `$versionQualifier`, which ZET zip is downloaded |
| `scripts/build-test-release.ps1` | `-Win32Crypto` parameter and qualifier plumbing |
| `scripts/promote.ps1` | promotes both builds together |
| `scripts/prepare-beta.ps1` | downloads both ZET variants to read dependency versions |
| `scripts/publish-release.sh` | publishes the win32crypto build to JFrog |
| `scripts/verify-streams.ps1` | checks the `-win32crypto` stream files |
| `.github/workflows/installer.build.yml` | second build step, `.\Installer\build.ps1 -Win32Crypto:$true` |
| `Directory.Build.targets`, `ZitiUpdateService.csproj` | the `WIN32CRYPTO` compilation constant |
| `ZitiDesktopEdge.Client/Utility/GithubAPI.cs` | the `#if WIN32CRYPTO` update URL |
| `DesktopEdge/Views/Screens/MainMenu.xaml.cs` | `#if WIN32CRYPTO` crypto label in the UI |
| `release-streams/{beta,latest,stable}-win32crypto.json` | keep -- see above |
| `releasing.md`, `BUILDING.md` | dual-build instructions |
| `fips/doc/zdew-integration.md` | "The win32crypto variant" section |

**Not** related, despite the name -- leave alone:

- `ZitiUpdateService/checkers/PeFile/Win32Crypto.cs` and `SignedFileValidator.cs` -- P/Invoke wrappers for
  Authenticode signature checking. Named after the Win32 crypto API, nothing to do with the tunneler variant.
- `scripts/read-crash-dump.ps1` and `doc/troubleshooting/**` -- these identify which variant produced a crash
  dump, and stay useful while any `-win32crypto` install survives.
- `release-notes-archive/`, and historical entries in `release-notes.md` -- history, not configuration.
