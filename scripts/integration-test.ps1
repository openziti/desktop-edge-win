param (
    [Alias("v")]
    [switch]$Detailed,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$project = Join-Path $PSScriptRoot "..\ZitiDesktopEdge.Client.IntegrationTests\ZitiDesktopEdge.Client.IntegrationTests.csproj"
$verbosity = if ($Detailed) { "detailed" } else { "normal" }

$env:INTEGRATION_TEST_LOG = if ($Detailed) { "Debug" } else { "" }

dotnet test $project -tl:off --logger "console;verbosity=$verbosity" @ExtraArgs
