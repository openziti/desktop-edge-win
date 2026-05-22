<#
.SYNOPSIS
    Runs the ZDEW unit test suite. Used by both CI workflows and local invocation.

.DESCRIPTION
    Runs `dotnet test` against each test project in the repo. Exits non-zero on
    any failure. Designed to be cheap, fast, and identical between local runs
    and GitHub Actions -- the workflow just calls this script.

.PARAMETER Configuration
    Build configuration to test. Defaults to Debug. CI uses Debug too; there is
    no Release-only test logic worth exercising.

.PARAMETER Filter
    Optional dotnet test --filter expression. Examples:
      .\scripts\run-tests.ps1 -Filter 'FullyQualifiedName~MaintenanceWindow'
      .\scripts\run-tests.ps1 -Filter 'FullyQualifiedName~Preset_CJIS'

.PARAMETER NoLogo
    Suppress dotnet's startup banner for cleaner CI logs.

.EXAMPLE
    # Run all tests locally
    .\scripts\run-tests.ps1

.EXAMPLE
    # Run only cadence math
    .\scripts\run-tests.ps1 -Filter 'FullyQualifiedName~MaintenanceWindow'
#>
param(
    [string] $Configuration = 'Debug',
    [string] $Filter        = '',
    [switch] $NoLogo,
    # CI mode adds the standard CI-skip filter (SignedFilesTest tests fail in
    # environments without the developer's local installer output / refreshed
    # signed artifacts -- tracked separately, see commit-plan.md and
    # project_signed_files_test_broken memory). Combined with -Filter if both
    # are supplied.
    [switch] $CiMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Restore NuGet packages for the .NET Framework projects (ZitiDesktopEdge.Client,
# ZitiUpdateService, etc.) before invoking `dotnet test`. Those projects use
# packages.config with HintPaths into ..\packages\, and `dotnet test`'s implicit
# restore only covers PackageReference-style deps -- without this step the test
# build errors with "could not find Newtonsoft" / "could not find NLog". Idempotent
# (no-op once packages are present). Skipped with a warning if nuget.exe is not on
# PATH so a dev who already has packages restored can run the script without
# installing nuget.
$nuget = Get-Command nuget -ErrorAction SilentlyContinue
if ($null -eq $nuget) {
    Write-Warning "nuget.exe not found on PATH; skipping package restore. If the test build fails with missing Newtonsoft.Json or NLog references, install nuget and re-run."
} else {
    Write-Host "Restoring NuGet packages..."
    & nuget restore (Join-Path $repoRoot 'ZitiDesktopEdge.sln') | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Error "nuget restore failed (exit $LASTEXITCODE)"
        exit 1
    }
}

# Discover all test projects. Convention: any csproj with name ending in `.Tests.csproj`
# under the repo root. Add new test projects without touching this script.
$testProjects = Get-ChildItem -Path $repoRoot -Filter '*.Tests.csproj' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

if (-not $testProjects) {
    Write-Host "No *.Tests.csproj projects found under $repoRoot"
    exit 0
}

Write-Host ('=' * 70)
Write-Host "ZDEW test runner -- $($testProjects.Count) project(s)"
Write-Host ('=' * 70)
foreach ($p in $testProjects) {
    $rel = $p.FullName.Substring($repoRoot.Length + 1)
    Write-Host "  - $rel"
}
Write-Host ''

$failed = @()
foreach ($p in $testProjects) {
    $rel = $p.FullName.Substring($repoRoot.Length + 1)
    Write-Host ('-' * 70)
    Write-Host "Running: $rel"
    Write-Host ('-' * 70)

    $dotnetArgs = @('test', $p.FullName, '--configuration', $Configuration)

    # Combine user-supplied filter with CI's standard skip list.
    $ciSkip = 'FullyQualifiedName!~SignedFilesTest'
    $effectiveFilter = if ($CiMode -and $Filter) {
        "($Filter)&($ciSkip)"
    } elseif ($CiMode) {
        $ciSkip
    } elseif ($Filter) {
        $Filter
    } else {
        ''
    }
    if ($effectiveFilter) { $dotnetArgs += @('--filter', $effectiveFilter) }
    if ($NoLogo)          { $dotnetArgs += '--nologo' }

    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        $failed += $rel
    }
}

Write-Host ''
Write-Host ('=' * 70)
if ($failed.Count -eq 0) {
    Write-Host "All test projects passed."
    exit 0
} else {
    Write-Host "FAILED: $($failed.Count) test project(s):"
    foreach ($f in $failed) { Write-Host "  - $f" }
    exit 1
}
