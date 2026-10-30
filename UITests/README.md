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
flaky failures. Screenshots aren't affected (`Capture` draws the window itself with Win32 PrintWindow), but
input synthesis can be. Toggle Do Not Disturb on for the duration of a test run:

- Windows 11: press Win+N, click the bell icon, and choose Do Not Disturb.

## How to run

`run-ui-tests.ps1` builds the app in Debug, starts `appium` on port 4723 if it isn't already listening, runs
the suite, and writes `TestResults\report.md`, `gallery.html` and `run-output.txt`.

```powershell
# first run: builds the app and the tests
UITests\run-ui-tests.ps1 -OpenGallery
# later runs: skip the app build
UITests\run-ui-tests.ps1 -SkipBuild -OpenGallery
```

Other switches: `-Tier`, `-Category` or `-Filter` to run a subset, `-AutoVerify` and
`-ResetBaselines` for screenshot baselines.

#### By tier

`-Tier` picks the same slice of the suite a CI job runs, and a run with no `-Tier`, `-Category` or `-Filter` is
`Mock`. Two ZDEW windows can't share one desktop, so each integration tier needs a machine of its own.

| Tier                    | Tests                                                    |
| ----------------------- | -------------------------------------------------------- |
| `Mock`                  | Every test outside `Integration`, monitor tier included  |
| `IntegrationExtAuth`    | `ExternalAuth*` classes                                  |
| `IntegrationMfa`        | `Mfa*` and `RemoveMfa*` classes                          |
| `IntegrationLegacyAuth` | `LegacyAuth*` classes                                    |
| `Integration`           | Every other `Integration` test                           |

### Visual baselines (AutoVerify)

Baselines are not committed. They live in zips on the `uitest-baselines` release, and `UITests\baselines-zip` names
the one a commit tests against. Every CI run pulls that zip first, and so does a local run:

```powershell
UITests\pull-baselines.ps1 -Repo openziti/desktop-edge-win
```

A run leaves `.verified.png` baselines alone. A screenshot that differs past the ImageMagick tolerance fails and
writes a `.received.png` beside its baseline for review, and a test with no baseline yet fails the same way.

After an intentional UI change or a new test, run the UI Tests workflow by hand on the branch with
`regenerate_baselines` checked. Every tier runs with `-AutoVerify` on the current zip, and once all pass the Publish
baselines job uploads `baselines-<version>-<commit>.zip`, holding the changed and new screens beside the untouched
ones, so other branches keep their zip. The run's summary names the zip: write that name to `UITests\baselines-zip`
and commit it with the change.

To accept baselines locally for a try-out (CI never sees them):

```powershell
# Regenerate everything
UITests\run-ui-tests.ps1 -SkipBuild -AutoVerify -ResetBaselines '*'

# Regenerate just a few
UITests\run-ui-tests.ps1 -SkipBuild -AutoVerify -ResetBaselines @('Visual_*','MainMenu_*')
```

### Running a subset of tests

#### By category

Every test is tagged with a `Category` trait. `-Category` accepts one or many:

| Category                  | Tests                                                              |
| ------------------------- | ------------------------------------------------------------------ |
| `MainScreen`              | Landing screen rendering, identity list, toggles, sort headers     |
| `IdentityDetail`          | Opening identity-detail screen, ext-auth Authorize click           |
| `Sort`                    | Sort header clicks, alphabetical / case-insensitive / status group |
| `TunnelSettings`          | Tunnel Config screen open, Edit Values, Save                       |
| `Integration`             | UI tests against a real ZET and controller                         |
| `Screenshots`             | Every test that compares a screenshot against a baseline           |
| `Placement`               | Docked window stays inside the work area as its size changes       |

```powershell
# just one category
UITests\run-ui-tests.ps1 -SkipBuild -Category Sort

# multiple categories at once (comma-separated)
UITests\run-ui-tests.ps1 -SkipBuild -Category MainScreen,Sort,TunnelSettings

# integration, from an elevated shell, with the binaries named the way ziti-tunnel-sdk-c's run-ci.ps1 takes them
$env:ZET_BIN = "C:\path\to\ziti-edge-tunnel.exe"
$env:ZITI_BIN = "C:\path\to\ziti.exe"
$env:IDP_BIN = "C:\path\to\dex.exe"
UITests\run-ui-tests.ps1 -SkipBuild -Tier IntegrationExtAuth
```

Under the hood this passes `-filterVSTest "Category=MainScreen|Category=Sort|..."` to the test project, which xUnit v3
builds as its own runner, so `dotnet test` does not run it. With Appium and WinAppDriver already listening, the same
filter works directly:

```powershell
dotnet run --project UITests\UITests.Appium -- -reporter verbose -showLiveOutput `
    -filterVSTest "Category=MainScreen|Category=Sort"
```

#### By individual test name

```powershell
UITests\run-ui-tests.ps1 -SkipBuild -Filter "FullyQualifiedName~SortTests"
```

## What gets produced

After a run, `UITests\TestResults\` contains:

| File / folder                | What it is                                                                  |
| ---------------------------- | --------------------------------------------------------------------------- |
| `gallery.html`               | Side-by-side visual review. Every test as a card, multi-step screenshots inline. |
| `report.md`                  | Markdown summary: pass/fail table + failure details.                        |
| `results.trx`                | Visual Studio test-results format. Open in VS Test Explorer for full output. |
| `run-output.txt`             | The console log without the wire-protocol JSON (the console keeps it).     |
| `screenshots\<TestName>\*.png` | Per-step captures saved by tests that use `SaveStep`.                     |

Baselines live next to the test source files:

```
UITests\UITests.Appium\Tests\SmokeTests.<Name>.verified.png
UITests\UITests.Appium\Tests\SmokeTests.<Name>.received.png   <- only on mismatch
```

## Code layout

```
UITests/
  README.md                              <-- this file
  run-ui-tests.ps1                       <-- builds, manages appium, runs the test project, writes the reports
  UITests.sln
  UITests.Appium/
    UITests.Appium.csproj                <-- net9.0-windows10.0.17763.0 xUnit v3 + Appium 5 + Magick.NET
    AssemblyInfo.cs                      <-- disables xUnit parallelism (single UI session safety)
    GlobalUsings.cs
    Drivers/
      AppiumSession.cs                   <-- launches mock + UI, attaches Appium
      Quickstart.cs                      <-- ziti edge quickstart with its own CLI login, for integration tests
      ZetProcess.cs                      <-- ziti-edge-tunnel run -P, for integration tests
      LoggedProcess.cs                   <-- child process logged to a file, killed with its tree
    MockIpc/
      MockIpcServer.cs                   <-- 4 named-pipe servers; mimics ziti-edge-tunnel + ziti-monitor
      RelayIpcServer.cs                  <-- forwards the data pipes to a real ziti-edge-tunnel -P, records the traffic
      IpcPipes.cs                        <-- pipe accept loop shared by the mock and the relay
      Fixtures/*.json                    <-- canned status payloads
    Tests/
      TestHelpers.cs                     <-- WaitFor / WaitForId / Capture / VerifyStep / OpenMainMenu
      Step.cs                            <-- step lines, printed live under the running test
      FixtureBuilder.cs                  <-- programmatic JObject builders (50-identity status, ...)
      SmokeTests.cs                      <-- state-changing + alt-fixture + visual tests
      IntegrationFixture.cs              <-- starts the quickstart and ZET once per collection
      <ZetFile>Tests.cs                  <-- UI tests against a real ZET and controller; elevated, -Category Integration only
  TestResults/                            <-- regenerated every run; safe to delete
```

## Adding a new test

1. Anything that mutates UI state, opens a screen, or uses an alt fixture goes in `SmokeTests` (own session).

2. Use the helpers in `TestHelpers`:
   - `WaitForId(s, "X")`: polls the UIA tree for an AutomationId
   - `WaitFor(s, By.XPath("//Text[@Name='Y']"))`: polls by any XPath
   - `OpenMainMenu(s)`: hamburger click with retry
   - `Capture(s)`: byte[] PNG of the window
   - `SaveStep(s, testName, "01-before")`: writes to `TestResults\screenshots\<testName>\01-before.png`
     so the gallery renders it inline.
   - `VerifyPng(png, testName)`: compares the capture with its pulled baseline, also drops the latest run into
     `TestResults\screenshots\` for the gallery's single-shot column.

3. Always tag with `[Fact(Timeout = ...)]`, since an untagged test can hang the whole suite. Set it to about twice a
   local run, because the GitHub Windows runner is that much slower.

4. Service commands (MonitorClient channel, `Op`/`Action` shape) are in `s.Mock.ReceivedMonitorRequests`.

5. The mock's data pipe answers only `Status`. Anything that sends a command to ZET belongs in the integration tier.

## Common failures and what they mean

| Symptom                                                                                  | Fix                                                                                      |
| ----------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `'appium' not found on PATH`                                                              | Install Appium per step 5 above.                                                         |
| `WinAppDriver.exe has not been found`                                                    | Install WinAppDriver per step 6.                                                         |
| `Appium could not attach to ZDEW window within ...`                                       | UI process crashed; check `DesktopEdge\bin\Debug\` for a crash dump or look at the run log. |
| `Timed out waiting for By.XPath: //*[@Name='Advanced Settings']`                          | A WPF custom control's UIA peer didn't expose what we expect. Try by `AutomationId` instead. |
| The run prints nothing for minutes                                                        | A test prints its block only when it ends. A stuck one prints when its `[Fact(Timeout = ...)]` fails it; Ctrl+C otherwise. |
| Multiple tests are screenshotting an unfamiliar UAC prompt                                | Toast registration ran, so `ZDEW_UI_TEST` was not set on the launched process. |
| `Cannot process argument transformation on parameter 'AppiumPort'`                        | Powershell parameter splatting bug. Use named-parameter hashtable (`@{ Foo = $true }`). |

## ZDEW source changes the test harness depends on

Two environment variables switch on test hooks in the product code. Both are read only in a Debug build, which is why
the suite runs one, and a Release build compiles them out:

- `ZDEW_UI_TEST=1` (`App.UiTestMode` in `DesktopEdge/App.xaml.cs`): shows the window in the taskbar and activates it,
  so Appium attaches without the tray icon. It also paints a white background for captures, freezes the connected
  time at 00:00:00, shows blurbs without a fade, and skips the toast COM activator registration under HKCU. Search
  `DesktopEdge/MainWindow.xaml.cs` for `App.UiTestMode` to see each branch.
- `ZDEW_IPC_PIPE_PREFIX=<prefix>` (`IPCServer.PipePrefix` in `ZitiDesktopEdge.Client/Server/IPCServer.cs`):
  prefixes the four pipe names, so the mock or relay hosts its pipes beside a running production service.
