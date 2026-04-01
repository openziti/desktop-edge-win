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

## Run Signature Validation Tests

The test project only covers PE signature validation used by the update service. It does not test the UI or service client. Requires .NET 6.0 SDK.

```powershell
dotnet test ZitiDesktopEdgeTests/ZitiDesktopEdgeTests.csproj
```

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
ZitiDesktopEdgeTests/      Unit tests (.NET 6.0)
Installer/                 Advanced Installer project and build script
scripts/                   Automation scripts (see below)
release-streams/           Update channel JSON files (stable, latest, beta)
```

## Scripts

All scripts live in [`scripts/`](./scripts/) and assume the working directory is the repository root.

| Script | Purpose |
|--------|---------|
| `setup-ids-for-test.ps1` | Provision test identities and services on a controller |
| `build-test-release.ps1` | Full local build with release stream generation for upgrade testing |
| `prepare-beta.ps1` | Create a beta release branch and open a PR |
| `publish-release.sh` | Create a GitHub release from CI build artifacts |
| `promote.ps1` | Promote a release stream (e.g. beta to latest) |
| `update-versions.ps1` | Stamp version across all project files |
| `verify-streams.ps1` | Check that release stream download URLs are reachable |
| `fetch-zac.ps1` | Download the Ziti Administration Console |
