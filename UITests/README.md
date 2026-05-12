# ZDEW UI Tests (Appium / WinAppDriver)

End-to-end UI automation for the ZDEW WPF tray app. Tests launch a Debug build of `ZitiDesktopEdge.exe`
against a mock IPC server (so `ziti-monitor` / `ziti-edge-tunnel` don't have to be running), drive the UI
via Appium + WinAppDriver, and capture screenshots + assertions for visual review.

The tests live under `UITests/UITests.Appium/`. The wire-protocol shapes the mock replays are
captured from real `ziti-edge-tunnel` and `ziti-monitor` traffic at TRACE log level; see
`MockIpc/MockIpcServer.cs` for the canonical reply/event shapes.

## What you need (one-time install)

These all install at user scope unless noted. Order matters in a couple of places.

### 1. .NET 9 SDK (for the test runner)

Download from https://dotnet.microsoft.com/download. Verify:

```powershell
dotnet --list-sdks
# should show 9.0.x
```

The product itself targets .NET Framework 4.8; the test runner is .NET 9 and only consumes the WPF
binary, so the .NET 9 SDK is sufficient on the test side.

### 2. MSBuild + VS Build Tools (for building the WPF app)

Install Visual Studio 2022/2026 Community OR "Build Tools for Visual Studio" with the
`Microsoft.Component.MSBuild` workload. The test script auto-locates `msbuild.exe` via `vswhere`.

### 3. NuGet CLI

```powershell
winget install Microsoft.NuGet
# or download nuget.exe from https://www.nuget.org/downloads and put it on PATH
```

### 4. Node.js + npm

Required to install Appium. https://nodejs.org/ (any LTS).

### 5. Appium 2.x and the Windows driver

```powershell
npm install -g appium
appium driver install --source=npm appium-windows-driver
```

### 6. WinAppDriver

`appium-windows-driver` calls out to Microsoft's `WinAppDriver.exe`. The driver tries to install it on
first session, but the bundled MSI install is async and often quietly fails. Install it explicitly:

```powershell
$msi = "$env:TEMP\WindowsApplicationDriver_1.2.1.msi"
Invoke-WebRequest "https://github.com/microsoft/WinAppDriver/releases/download/v1.2.1/WindowsApplicationDriver_1.2.1.msi" -OutFile $msi
Start-Process msiexec.exe -ArgumentList "/i",$msi,"/quiet","/norestart" -Verb RunAs -Wait

# verify
Test-Path "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"   # should be True
```

Accept the UAC prompt. WinAppDriver only needs to be installed once per machine.

### 7. (Optional) Focus Assist / Do Not Disturb

Toast notifications from other apps (Teams, Slack, Outlook) can steal focus mid-test and produce
flaky failures. Element-level screenshots aren't affected (they pull the window's own bitmap), but
input synthesis can be. Toggle Do Not Disturb on for the duration of a test run:

- Windows 11: Win+N -> click the bell icon, choose Do Not Disturb.

## How to run

From the `try-appium` worktree root:

```powershell
UITests\quick-run.ps1
```

That script:

1. Removes any `.verified.png` baselines under `UITests\UITests.Appium\Tests\` so AutoVerify regenerates them
   (pass `-ResetBaselines @('Visual_*','LogLevel_*')` to only nuke specific patterns).
2. Invokes `run-ui-tests.ps1 -AutoVerify -SkipBuild`, which auto-starts `appium` if it isn't already
   listening on port 4723.
3. Filters wire-protocol JSON spam out of stdout and writes a clean log to
   `UITests\TestResults\run-output.txt`.
4. Opens `UITests\TestResults\gallery.html` in the default browser.

Pass `-Build` to force a full `nuget restore` + `msbuild` rebuild before running. By default it skips the
build to keep iteration fast.

### First run vs subsequent runs

- **First run**: needs `-Build` so the WPF app and test project actually exist:
  ```powershell
  UITests\quick-run.ps1 -Build
  ```
- **Subsequent runs**: skip the build, just iterate on tests:
  ```powershell
  UITests\quick-run.ps1
  ```

### Running a subset of tests

#### By category

Every test is tagged with a `Category` trait. `quick-run.ps1 -Category` accepts one
or many:

| Category                  | Tests                                                              |
| ------------------------- | ------------------------------------------------------------------ |
| `MainScreen`              | Landing screen rendering, identity list, toggles, sort headers     |
| `IdentityDetail`          | Opening identity-detail screen, ext-auth Authorize click           |
| `IdentityDetailServices`  | Service list, detail icon, filter, Forget button                   |
| `Mfa`                     | Enable, disable, MFA-needed, MFA-enabled-at-start, QR dialog       |
| `Sort`                    | Sort header clicks, alphabetical / case-insensitive / status group |
| `TunnelSettings`          | Tunnel Config screen open, Edit Values, Save                       |
| `LogLevel`                | Set Logging Level walkthrough                                      |
| `AutomaticUpdate`         | (placeholder for future auto-update tests)                         |

```powershell
# just one category
UITests\quick-run.ps1 -Category Mfa

# multiple categories at once (comma-separated)
UITests\quick-run.ps1 -Category Mfa,Sort,TunnelSettings

# everything (no filter)
UITests\quick-run.ps1
```

Under the hood this passes `--filter "Category=Mfa|Category=Sort|..."` to
`dotnet test`. The same OR syntax works directly:

```powershell
dotnet test UITests\UITests.Appium\UITests.Appium.csproj `
    --filter "Category=Mfa|Category=Sort"
```

#### By individual test name

```powershell
dotnet test UITests\UITests.Appium\UITests.Appium.csproj `
    --filter "FullyQualifiedName~MFA_EnableShowsQRDialog"
```

## What gets produced

After a run, `UITests\TestResults\` contains:

| File / folder                | What it is                                                                  |
| ---------------------------- | --------------------------------------------------------------------------- |
| `gallery.html`               | Side-by-side visual review. Every test as a card, multi-step screenshots inline. |
| `report.md`                  | Markdown summary: pass/fail table + failure details.                        |
| `results.trx`                | Visual Studio test-results format. Open in VS Test Explorer for full output. |
| `run-output.txt`             | Filtered stdout (wire-protocol JSON noise dropped).                         |
| `screenshots\<TestName>\*.png` | Per-step captures saved by tests that use `SaveStep`.                     |

Verify-style baselines (committed to git for diff reviews) live next to the test source files:

```
UITests\UITests.Appium\Tests\SmokeTests.<Name>.verified.png
UITests\UITests.Appium\Tests\SmokeTests.<Name>.received.png   <- only on mismatch
```

## Code layout

```
UITests/
  README.md                              <-- this file
  quick-run.ps1                          <-- nuke baselines + run + open gallery
  run-ui-tests.ps1                       <-- the heavy lifter; manages appium + dotnet test
  UITests.sln
  UITests.Appium/
    UITests.Appium.csproj                <-- net9.0-windows xUnit + Appium 5 + Verify
    AssemblyInfo.cs                      <-- disables xUnit parallelism (single UI session safety)
    GlobalUsings.cs
    Drivers/
      AppiumSession.cs                   <-- launches mock + UI, attaches Appium
    MockIpc/
      MockIpcServer.cs                   <-- 4 named-pipe servers; mimics ziti-edge-tunnel + ziti-monitor
      Fixtures/*.json                    <-- canned status payloads
    Tests/
      TestHelpers.cs                     <-- WaitFor / ById / Capture / SaveStep / OpenMainMenu
      TestLifecycleLog.cs                <-- xUnit attribute that logs START/DONE per test
      FixtureBuilder.cs                  <-- programmatic JObject builders (50-identity status, ...)
      LandingSession.cs                  <-- shared-session IClassFixture
      LandingReadOnlyTests.cs            <-- read-only assertions (8 tests share 1 UI launch)
      SmokeTests.cs                      <-- state-changing + alt-fixture + visual tests
  TestResults/                            <-- regenerated every run; safe to delete
```

## Adding a new test

1. Pick a class based on what your test needs:
   - Read-only assertion against the default landing screen -> `LandingReadOnlyTests` (free, shares session).
   - Anything that mutates UI state, opens a screen, or uses an alt fixture -> `SmokeTests` (own session).

2. Use the helpers in `TestHelpers`:
   - `WaitForId(s, "X")` -- polls UIA tree for AutomationId
   - `WaitFor(s, By.XPath("//Text[@Name='Y']"))` -- polls by any XPath
   - `OpenMainMenu(s)` -- robust hamburger click with retry
   - `Capture(s)` -- byte[] PNG of the window
   - `SaveStep(s, testName, "01-before")` -- writes to `TestResults\screenshots\<testName>\01-before.png`
     so the gallery renders it inline.
   - `VerifyPng(png)` -- runs Verify-style baseline comparison, also drops the latest run into
     `TestResults\screenshots\` for the gallery's single-shot column.

3. Always tag with `[Fact(Timeout = 120000)]` so a hang fails the test instead of blocking the suite.

4. Asserting on mock IPC traffic:
   - Tunneler commands (DataClient channel): `s.Mock.ReceivedCommandNames`, `s.Mock.ReceivedRequests`
   - Service commands (MonitorClient channel, `Op`/`Action` shape): `s.Mock.ReceivedMonitorOps`,
     `s.Mock.ReceivedMonitorRequests`

5. For new mock IPC handlers (a Command the UI sends that the mock doesn't yet recognize):
   - Add a case in `BuildReply` (data IPC) or extend `BuildMonitorReply` (monitor IPC).
   - If the UI expects a follow-up async event, enqueue it on `_eventPush.Writer.TryWrite(...)`.

## Common failures and what they mean

| Symptom                                                                                  | Fix                                                                                      |
| ----------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `'appium' not found on PATH`                                                              | Install Appium per step 5 above.                                                         |
| `WinAppDriver.exe has not been found`                                                    | Install WinAppDriver per step 6.                                                         |
| `Appium could not attach to ZDEW window within ...`                                       | UI process crashed; check `DesktopEdge\bin\Debug\` for a crash dump or look at the run log. |
| `Timed out waiting for By.XPath: //*[@Name='Advanced Settings']`                          | A WPF custom control's UIA peer didn't expose what we expect. Try by `AutomationId` instead. |
| Test hangs, last log line is `START : SmokeTests.X`                                       | That test is stuck. The `[Fact(Timeout = ...)]` should abort eventually; Ctrl+C otherwise. |
| Multiple tests are screenshotting an unfamiliar UAC prompt                                | Production toast registration triggered. ZDEW_DISABLE_TOASTS / ZDEW_ENABLE_TOASTS gating. |
| `Cannot process argument transformation on parameter 'AppiumPort'`                        | Powershell parameter splatting bug. Use named-parameter hashtable (`@{ Foo = $true }`). |

## ZDEW source changes the test harness depends on

The tests rely on a handful of small env-var-gated hooks in the product code. These ship in the
binary but are inert unless the env var is set:

- `ZDEW_UI_TEST=1` -- forces `MainWindow.Show()` + `ShowInTaskbar=true` so Appium can attach without
  driving the system tray icon. Also disables window transparency + chrome for clean screenshots.
- `ZDEW_IPC_PIPE_PREFIX=<prefix>` -- prefixes both pipe names (`ziti-edge-tunnel.sock` and
  `OpenZiti\ziti-monitor\ipc`) so the test mock can host pipes without colliding with a running
  production service.
- `ZDEW_ENABLE_TOASTS=1` -- toast registration is opt-in (production needs this set somehow). Tests
  leave it unset so test runs don't write COM activator entries to HKCU.

Search for these strings in `DesktopEdge/MainWindow.xaml.cs` and `ZitiDesktopEdge.Client/` to see
exactly where they branch.

## Tweaking speed

A full pass currently takes ~40-60 seconds. Where the time goes:

- WAD session attach: ~1.5s per test.
- WPF process startup: ~1s per test.
- Driver.Quit() teardown: ~0.5s per test.

The 8 read-only landing tests share a single session via `LandingSession` (`IClassFixture`),
which already saves ~24s. Further speedups would require sharing sessions across alt-fixture tests
(harder, since each test needs different IPC payloads -- mock would need a hot-swap status API).
