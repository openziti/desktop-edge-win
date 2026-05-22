# Building and Testing

## Prerequisites

- Visual Studio 2022 (Community or higher) with the ".NET desktop development" workload, or just MSBuild and the .NET Framework 4.8 targeting pack. VS is not strictly required but makes WPF development and debugging significantly easier due to the XAML designer and integrated debugger.
- NuGet CLI
- PowerShell 7+
- [OpenZiti CLI](https://github.com/openziti/ziti) (`ziti`) for local testing

For installer builds only:
- Advanced Installer (version specified in [`adv-inst-version`](./adv-inst-version))
- AWS KMS credentials and the OpenZiti `.p12` signing certificate (for signed builds)

## Build

From the project root in a Developer PowerShell for VS 2022 (or any shell with `msbuild` on the PATH):

```powershell
nuget restore ZitiDesktopEdge.sln
msbuild ZitiDesktopEdge.sln /p:Configuration=Release
```

The built UI executable will be at `DesktopEdge\bin\Release\`. When building from Visual Studio in Debug configuration, output goes to `DesktopEdge\bin\Debug\` instead.

## Test Locally

To test ZDEW you need a running OpenZiti network with a controller and router. The quickest way to get one is `ziti edge quickstart`. This command generates a PKI, starts a controller, and starts an edge router all in a single process. The `--home` flag controls where the quickstart writes its configuration and PKI files — use any directory you like.

The following is an example setup. Paths and names can be changed to suit your environment.

**Terminal 1** -- start a quickstart network:
```powershell
ziti edge quickstart --home C:\temp\ziti-test
```

**Terminal 2** -- create test identities from the project root:
```powershell
.\scripts\setup-ids-for-test.ps1 `
    -ClearIdentitiesOk `
    -Url "https://localhost:1280" `
    -RouterName "router-quickstart" `
    -ZitiHome "C:\temp\ziti-quickstart"
```

- `Url` is the address of the controller started by the quickstart
- `RouterName` is required when using `-Url`. This is the name of the router the quickstart created (defaults to `router-quickstart`)
- `ZitiHome` is the directory where the script writes the generated JWT files

The script provisions identities, auth policies, external JWT signers, and services on the controller.

Enroll the generated JWTs into ZDEW using the "add identity" button in the top right of the UI.

For the full list of test cases to run before a release, see [`manual-testing.md`](./manual-testing.md).

## Run Unit Tests

Two test projects, both targeting .NET 8.0 SDK:

- `ZitiUpdateService.Tests/` -- covers the `ziti-monitor` Windows service: PE signature
  validation, maintenance-window cadence math (Daily/Weekly/Monthly + ByWeekday ordinal
  resolution), InstallationCritical age-threshold, snap-to-window arithmetic, plus
  full-calendar verification of every documented compliance preset across all 12 months
  of 2025. ~350 test cases, ~300ms runtime.
- `ZitiDesktopEdge.Tests/` -- placeholder for future WPF tray-UI tests. No tests yet.

Canonical entry point (what CI runs):

```powershell
# All tests (surfaces the 2 documented SignedFilesTest cert/path failures locally)
.\scripts\run-tests.ps1

# What CI runs -- filters out the SignedFilesTest cases (tracked separately)
.\scripts\run-tests.ps1 -CiMode

# Only cadence tests
.\scripts\run-tests.ps1 -Filter "FullyQualifiedName~MaintenanceWindow"

# Only one compliance preset
.\scripts\run-tests.ps1 -Filter "FullyQualifiedName~Preset_CJIS"
```

The script auto-discovers any `*.Tests.csproj` under the repo root, so adding a new
test project doesn't require workflow or script edits.

You can also call `dotnet test` directly:

```powershell
dotnet test ZitiUpdateService.Tests/ZitiUpdateService.Tests.csproj
```

In Visual Studio: both test projects are in `ZitiDesktopEdge.sln` (gated out of Release
config so `Installer\build.ps1` is unaffected). Test Explorer (`Ctrl+E,T`) discovers
them automatically.

## Build the Installer

```powershell
.\Installer\build.ps1
.\Installer\build.ps1 -Win32Crypto:$true
```

Output goes to `Installer/Output/`. The script downloads `ziti-edge-tunnel`, builds everything, and runs Advanced Installer. Without signing credentials the build succeeds but the installer won't pass automatic upgrade validation.

See [`releasing.md`](./releasing.md) for the full release process.

## Project Layout

```
DesktopEdge/               WPF UI application
ZitiDesktopEdge.Client/    IPC client library (UI <-> ziti-edge-tunnel service)
ZitiUpdateService/         Windows service for polling and applying updates
ZitiUpgradeSentinel/       Post-upgrade cleanup utility
AWSSigner.NET/             AWS KMS code signing integration
ZitiUpdateService.Tests/   Unit tests for the monitor service (.NET 8.0)
ZitiDesktopEdge.Tests/     Placeholder for future WPF UI tests (.NET 8.0)
Installer/                 Advanced Installer project and build script
scripts/                   Automation scripts (see below)
release-streams/           Update channel JSON files (stable, latest, beta)
```

## Scripts

Scripts for releasing, promoting, and utilities live in [`scripts/`](./scripts/) and assume the working directory is the repository root.
