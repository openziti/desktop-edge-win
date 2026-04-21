# ZitiDesktopEdge.Client.IntegrationTests

Drives `ZitiDesktopEdge.Client.DataClient` against the real `ziti-edge-tunnel`
Windows service and a local `ziti edge quickstart` controller. No mocks.

## Prerequisites

- `ziti.exe` on `PATH` (for `ziti edge quickstart`).
- `pwsh.exe` (PowerShell 7+) on `PATH`.
- `ziti-edge-tunnel` installed and running as the Windows service. First-time
  install needs an admin shell.

`ConnectAndStatusTests` reports **Inconclusive** if the ZET pipes are
unreachable. `IdentityLifecycleTests` fails outright if the quickstart
prerequisites are missing, since its `ClassInitialize` launches the controller.

## Fixture behavior (`QuickstartFixture`)

- **AssemblyInitialize**: cycles the ziti service via the monitor pipe to
  delete any `normal-user-*.json` files still loaded by ZET from a prior run.
  Only names in `TestIdentityNames` are touched; unrelated identities on the
  same ZET instance are safe.
- **ClassInitialize** (for classes that need a controller): launches
  `ziti edge quickstart` under a temp `--home`, waits for TCP port 1280, then
  runs `scripts/setup-ids-for-test.ps1 -Normal` to provision the
  `normal-user-*` identities. JWTs land in `QuickstartFixture.IdentityDir`.
- **AssemblyCleanup**: clears the test identities again, kills the quickstart
  process tree, deletes both temp homes.

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
- `IdentityLifecycleTests`: enroll a JWT identity, then toggle it off and
  back on via `IdentityOnOff`, verifying each state change through
  `GetStatus`.
