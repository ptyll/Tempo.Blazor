# Performance lane: the wall-clock budget specs (TestCategory=Performance) - Modeling M26 (LCP, large model,
# tree filter, scroll jank, notation switch), the Spreadsheet BenchmarkPage readiness metrics and the Email
# editor's 50+ block keystroke bound.
#
# These budgets only hold on an IDLE machine: under the full lane's load the same build measures several
# times the idle value (docs/e2e-test-lanes.md), so they are excluded from scripts/run-e2e-full.ps1 and run
# here, serially, in ONE process with nothing else running. Do not run this next to a build or another lane.
#
# Output: TestResults/e2e-perf-<timestamp>/perf.trx + summary.json.
param(
    # Extra VSTest filter ANDed with TestCategory=Performance (e.g. 'FullyQualifiedName~ModelingPerformanceM26').
    [string]$Filter = '',
    [switch]$NoBuild,
    [string]$ReportDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'e2e-lane-common.ps1')

$project = Join-Path $repoRoot 'tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj'
if (-not $ReportDir) { $ReportDir = Join-Path $repoRoot ("TestResults/e2e-perf-{0:yyyyMMdd-HHmmss}" -f (Get-Date)) }
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

if (-not $env:TM_E2E_TRACE_ON_FAILURE) { $env:TM_E2E_TRACE_ON_FAILURE = 'false' }
if (-not $env:LANG) { $env:LANG = 'en_US.UTF-8' }
if (-not $env:LC_ALL) { $env:LC_ALL = 'en_US.UTF-8' }

if (-not $NoBuild) {
    & dotnet build $project --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }
}

$filter = 'TestCategory=Performance'
if ($Filter) { $filter = "($filter)&($Filter)" }

[void](Clear-E2EOrphanedHosts -Reason 'before the perf lane')
$watch = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet test $project --no-build --filter $filter `
    --results-directory $ReportDir --logger 'trx;LogFileName=perf.trx' --logger 'console;verbosity=normal'
$exit = $LASTEXITCODE
$watch.Stop()
[void](Clear-E2EOrphanedHosts -Reason 'after the perf lane')

$summary = Read-E2ETrx -Path (Join-Path $ReportDir 'perf.trx')
$report = [pscustomobject]@{
    Filter   = $filter
    ExitCode = $exit
    Seconds  = [int]$watch.Elapsed.TotalSeconds
    Totals   = if ($summary) { [pscustomobject]@{ Total = $summary.Total; Passed = $summary.Passed; Failed = $summary.Failed; Skipped = $summary.Skipped } } else { $null }
    Failures = if ($summary) { $summary.Failures } else { @() }
}
$report | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $ReportDir 'summary.json') -Encoding utf8
Write-Host "[e2e-perf] report: $ReportDir"
if (-not $summary -or $summary.Total -eq 0) { Write-Host '[e2e-perf] no performance specs ran - check the filter / the Performance category.'; exit 1 }
exit $exit
