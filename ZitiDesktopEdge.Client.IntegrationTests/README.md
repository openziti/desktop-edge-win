# ZitiDesktopEdge.Client.IntegrationTests

Drives `ZitiDesktopEdge.Client.DataClient` against the `ziti-edge-tunnel`
Windows service and a local `ziti edge quickstart` controller.

## Prerequisites

- `ziti.exe` on `PATH` (for `ziti edge quickstart` and identity provisioning).
- `ziti-edge-tunnel` installed and running as the Windows service. First-time
  install needs an admin shell.

All tests fail outright if prerequisites are missing: `ConnectAndStatusTests`
needs the ZET pipe, `IdentityLifecycleTests` needs ZET plus the quickstart
CLI, and `IdentityOnOff_AfterServiceRestart_PreservesDisabledState`
additionally needs the ziti-monitor pipe.

## Fixture behavior (`QuickstartFixture`)

xUnit collection fixture. Runs once for classes marked `[Collection("Quickstart")]`.

- **InitializeAsync**: configures logging, IPC-cleans `TestIdentityNames`,
  starts `ziti edge quickstart` in a temp `--home`, waits on TCP 1280, logs in
  and `ziti edge create identity`s the `TestIdentityNames`. JWTs land in
  `QuickstartFixture.IdentityDir`.
- **DisposeAsync**: IPC cleanup, kills the quickstart tree, deletes temp homes.

`ConnectAndStatusTests` stays out of the collection so it doesn't boot the
controller.

## Running

Run the full suite via the wrapper script:

```powershell
.\scripts\integration-test.ps1
```

Same run with `dotnet test` detailed verbosity for more log output:

```powershell
.\scripts\integration-test.ps1 -v
```

Run only one test class (anything after `--` is forwarded to `dotnet test`):

```powershell
.\scripts\integration-test.ps1 -- --filter Lifecycle
```

Or invoke `dotnet test` directly, bypassing the wrapper:

```powershell
dotnet test ZitiDesktopEdge.Client.IntegrationTests\ZitiDesktopEdge.Client.IntegrationTests.csproj
```

## Tests

- `ConnectAndStatusTests`: smoke test. `DataClient` connects and `GetStatus`
  round-trips.
- `IdentityLifecycleTests`: enroll a JWT identity, toggle it off and on via
  `IdentityOnOff`, verify a disabled identity stays disabled after the ziti
  service is cycled, and verify a freshly enrolled identity remains active
  after the ziti service is cycled. State is observed through `GetStatus`.
