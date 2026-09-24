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

- Windows 11: Win+N -> click the bell icon, choose Do Not Disturb.

## How to run

`run-ui-tests.ps1` builds the app in Debug, starts `appium` on port 4723 if it isn't already listening, runs
the suite, and writes `TestResults\report.md`, `gallery.html` and `run-output.txt`.

```powershell
# first run: builds the app and the tests
UITests\run-ui-tests.ps1 -OpenGallery
# later runs: skip the app build
UITests\run-ui-tests.ps1 -SkipBuild -OpenGallery
```

Other switches: `-Category` or `-Filter` to run a subset, `-Trace` for per-step timing lines, `-AutoVerify` and
`-ResetBaselines` for screenshot baselines.

### Visual baselines (Verify / AutoVerify)

A run leaves `.verified.png` baselines alone. A screenshot that differs past the ImageMagick tolerance fails and
writes a `.received.png` beside its baseline for review. After an intentional UI change, accept new baselines:

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
| `IdentityDetailServices`  | Service list, detail icon, filter, Forget button                   |
| `Mfa`                     | Enable, disable, MFA-needed, MFA-enabled-at-start, QR dialog       |
| `Sort`                    | Sort header clicks, alphabetical / case-insensitive / status group |
| `TunnelSettings`          | Tunnel Config screen open, Edit Values, Save                       |
| `LogLevel`                | Set Logging Level walkthrough                                      |
| `AddIdentityFlow`         | End-to-end JWT enroll + MFA + regenerate + forget (one long test)  |
| `Screenshots`             | Every test that compares a screenshot against a baseline           |
| `Placement`               | Docked window stays inside the work area as its size changes       |

```powershell
# just one category
UITests\run-ui-tests.ps1 -SkipBuild -Category Mfa

# multiple categories at once (comma-separated)
UITests\run-ui-tests.ps1 -SkipBuild -Category Mfa,Sort,TunnelSettings
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
    --filter "FullyQualifiedName~EnablingMfaShowsSetupDialog"
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

Verify-style baselines (committed to git for diff reviews) live next to the test source files:

```
UITests\UITests.Appium\Tests\SmokeTests.<Name>.verified.png
UITests\UITests.Appium\Tests\SmokeTests.<Name>.received.png   <- only on mismatch
```

## Code layout

```
UITests/
  README.md                              <-- this file
  run-ui-tests.ps1                       <-- builds, manages appium, runs dotnet test, writes the reports
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

3. Always tag with `[Fact(Timeout = 20000)]`. Anything that genuinely needs longer (multi-hop menu
   navigation, alt-fixture loads, the Sort walkthrough) should bump per-test to 30000 (or 40000
   for `SortHeadersReorderIdentities`) and include a comment explaining why. Limits are about twice a local
   run because the GitHub Windows runner is that much slower. Untagged tests can hang the whole
   suite.

4. Asserting on mock IPC traffic:
   - Tunneler commands (DataClient channel): `s.Mock.ReceivedCommandNames`, `s.Mock.ReceivedRequests`
   - Service commands (MonitorClient channel, `Op`/`Action` shape): `s.Mock.ReceivedMonitorOps`,
     `s.Mock.ReceivedMonitorRequests`

5. Mock helpers for simulating tunneler-side events (so tests don't have to drive flows that pop
   real OS dialogs/browsers):
   - `s.Mock.PushExtAuthSuccess(identifier)` -- emits an `Op=identity Action=added` event with
     `NeedsExtAuth=false`, the same shape `ziti-edge-tunnel` sends post-OIDC-login. Use this
     instead of clicking `AuthenticateWithProvider`, which calls `Process.Start(url)` and would
     launch a real browser.
   - MFA code gating: `SubmitMFA` / `VerifyMFA` / `RemoveMFA` check the submitted `Code` field.
     `123456` (`MockIpcServer.AcceptedMfaCode`) always succeeds and `666666`
     (`MockIpcServer.RejectedMfaCode`) always fails, like ZET 1.19 does for a bad code: `Success=false`,
     `Code=500`, "the token provided was invalid". Any other code is checked as a real TOTP against the
     secret from `EnableMFA`. Enrollment flows that don't carry a code still succeed.

6. For new mock IPC handlers (a Command the UI sends that the mock doesn't yet recognize):
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
| Multiple tests are screenshotting an unfamiliar UAC prompt                                | Toast registration ran, so `ZDEW_UI_TEST` was not set on the launched process. |
| `Cannot process argument transformation on parameter 'AppiumPort'`                        | Powershell parameter splatting bug. Use named-parameter hashtable (`@{ Foo = $true }`). |

## ZDEW source changes the test harness depends on

The tests rely on a handful of small env-var-gated hooks in the product code. These ship in the
binary but are inert unless the env var is set:

- `ZDEW_UI_TEST=1` -- sets `ShowInTaskbar=true` and activates the window so Appium can attach without
  driving the system tray icon. Also disables window transparency + chrome for clean screenshots, and skips toast
  activation registration so test runs don't write COM activator entries to HKCU.
- `ZDEW_IPC_PIPE_PREFIX=<prefix>` -- prefixes both pipe names (`ziti-edge-tunnel.sock` and
  `OpenZiti\ziti-monitor\ipc`) so the test mock can host pipes without colliding with a running
  production service.

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
