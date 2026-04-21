# ZitiDesktopEdge.Client.IntegrationTests

Integration tests that drive `ZitiDesktopEdge.Client.DataClient` against a real
`ziti-edge-tunnel` service talking to a real local `ziti` quickstart. These
tests are the foundation for automating the IPC-layer portions of
[`manual-testing.md`](../manual-testing.md).

## Prerequisites

1. **`ziti.exe`** on `PATH` — used to spin up a fresh quickstart network.
2. **`pwsh.exe`** (PowerShell 7+) on `PATH` — the setup script requires it.
3. **`ziti-edge-tunnel`** installed and running as the Windows service — the
   tests connect to its named pipes (`ziti-edge-tunnel.sock`,
   `ziti-edge-tunnel-event.sock`). An admin shell is required the first time
   the service is installed.

When any of these prerequisites are missing, the tests report as
**Inconclusive** (skipped) rather than failing, so the project still builds
cleanly on a machine that isn't fully set up.

## What the fixture does

`QuickstartFixture` runs once per test assembly and:

1. Creates a fresh temp `ZitiHome` under `%TEMP%\zdew-integration-<guid>`.
2. Invokes `..\scripts\setup-ids-for-test.ps1 -ClearIdentitiesOk
   -NonInteractive -ZitiHome <temp> -Normal`, which starts the quickstart and
   provisions the `normal-user-*` identities.
3. On teardown, kills `ziti.exe` and removes the temp directory.

Tests access the provisioned identity JWTs via
`QuickstartFixture.IdentityDir`.

## Running

```powershell
dotnet test ZitiDesktopEdge.Client.IntegrationTests\ZitiDesktopEdge.Client.IntegrationTests.csproj
```

## Current tests

- `ConnectAndStatusTests.Connect_GetStatus_ReturnsTunnelInfo` — smoke test:
  `DataClient` connects to the named pipes and a `Status` command round-trips.
