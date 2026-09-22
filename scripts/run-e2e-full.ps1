# Full lane: the entire E2E suite (~1450 tests, several hours — shard for parallel runs).
# Use scripts/run-e2e-smoke.ps1 as the fast PR gate. See docs/e2e-test-lanes.md.
param(
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj'

# Keep disk usage bounded on long runs: failure traces stay enabled only when
# the caller asks for them explicitly.
if (-not $env:TM_E2E_TRACE_ON_FAILURE) { $env:TM_E2E_TRACE_ON_FAILURE = 'false' }

$testArgs = @('test', $project, '--logger', 'trx;LogFileName=e2e-full.trx', '--logger', 'console;verbosity=minimal')
if ($Filter) { $testArgs += @('--filter', $Filter) }

& dotnet @testArgs
exit $LASTEXITCODE
