param (
    [Alias("v")]
    [switch]$Detailed,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$project = Join-Path $PSScriptRoot "..\ZitiDesktopEdge.Client.IntegrationTests\ZitiDesktopEdge.Client.IntegrationTests.csproj"
$verbosity = if ($Detailed) { "detailed" } else { "normal" }

if ($Detailed) { $env:INTEGRATION_TEST_LOG = "Debug" }

dotnet test $project --logger "console;verbosity=$verbosity" @ExtraArgs
