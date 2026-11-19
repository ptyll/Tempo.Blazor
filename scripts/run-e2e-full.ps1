# Full lane: the entire E2E suite (~1450 tests) as ONE unattended command.
#
# The suite is split BY TEST CLASS into shards that run one after another (the demo hosts bind fixed ports,
# so shards cannot overlap). Between shards orphaned demo hosts (API 5100, WASM 7106/5010, Server 7107,
# InteractiveAuto 7108) are reaped, so a host crash or a killed test process in one shard cannot cascade into
# the next one. Performance-budget specs (TestCategory=Performance) are excluded - they measure wall-clock
# budgets that only hold on an idle machine; run them with scripts/run-e2e-perf.ps1.
#
# Output (under -ReportDir, default TestResults/e2e-full-<timestamp>):
#   shard-<n>.trx       one TRX per shard
#   summary.json        totals, per-shard results, aborted shards, every failure (+ the serial-rerun verdict), host restarts
#   summary.md          the same as a table (incl. an Aborted column)
#
# The run is RED (exit 1) for: a failed test that is not proven a contention flake, a shard that produced no TRX,
# and an ABORTED shard (crashed / killed test process: non-zero exit with no failed test, or TRX outcome
# Aborted/Error). A shard whose filter matched nothing (0 tests) is an empty shard, not an abort.
#
# Every shard runs under the script's OWN watchdog: the normal-verbosity console log appends a line per
# finished test, so $InactivityTimeoutMinutes of total silence (or the $ShardTimeoutMinutes wall-clock
# budget) means the testhost is wedged - the script captures the testhost's managed stacks, kills the
# tree and marks the shard KilledBy ('inactivity'/'budget') in the summary. 2026-11-18 shard 1 produced
# no TRX at all because an orphaned Playwright protocol await (the shared browser's transport died
# mid-test) blocked MSTest's synchronous invocation forever; reproduced 2026-11-19 and the frozen
# testhost showed the executor thread parked in Task.InternalWait(-1) through InvokeAsSynchronousTask.
# (--blame-hang was removed: on SDK 10.0.303 vstest never spawns the hang collector, so the flag was a
# no-op; the crash collector --blame-crash stays.)
#
# Use scripts/run-e2e-smoke.ps1 as the fast PR gate. See docs/e2e-test-lanes.md.
param(
    # Extra VSTest filter ANDed with the class selection (e.g. 'TestCategory=WASM'). Empty = the whole suite.
    [string]$Filter = '',
    # Number of class shards (run serially). More shards = a host crash costs less, each shard pays host start-up.
    [int]$Shards = 8,
    # Run only these shard indexes (1-based), e.g. -OnlyShard 3,5 to repeat a red shard.
    [int[]]$OnlyShard = @(),
    # Re-run failed tests once, serially in a fresh process, to separate contention flakes from real defects.
    [switch]$RerunFailed,
    # Include the Performance category (default: excluded, see run-e2e-perf.ps1).
    [switch]$IncludePerformance,
    # Skip the up-front build (the E2E project must already be built).
    [switch]$NoBuild,
    # Outer wall-clock budget per shard. On expiry the shard tree is killed, the testhost stacks captured,
    # and the shard is marked KilledBy='budget' in the summary.
    [int]$ShardTimeoutMinutes = 240,
    # Kill a shard whose run log grew by NO new test completions for this long. The console logger runs at
    # normal verbosity, so every finished test (passed/failed/skipped) appends a line: total silence for
    # 30 minutes means no test completed for 30 minutes - a live shard always finishes tests faster than
    # that. On expiry the testhost's managed stacks are captured before the tree is killed and the shard
    # is marked KilledBy='inactivity' in the summary.
    [int]$InactivityTimeoutMinutes = 30,
    [string]$ReportDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'e2e-lane-common.ps1')

$projectDir = Join-Path $repoRoot 'tests/Tempo.Blazor.E2E'
$project = Join-Path $projectDir 'Tempo.Blazor.E2E.csproj'
if (-not $ReportDir) { $ReportDir = Join-Path $repoRoot ("TestResults/e2e-full-{0:yyyyMMdd-HHmmss}" -f (Get-Date)) }
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null

# Keep disk usage bounded on long runs: failure traces stay enabled only when the caller asks for them.
if (-not $env:TM_E2E_TRACE_ON_FAILURE) { $env:TM_E2E_TRACE_ON_FAILURE = 'false' }
# The localized assertions of the lane assume an English process culture (docs/e2e-test-lanes.md).
if (-not $env:LANG) { $env:LANG = 'en_US.UTF-8' }
if (-not $env:LC_ALL) { $env:LC_ALL = 'en_US.UTF-8' }

$base = if ($IncludePerformance) { '' } else { 'TestCategory!=Performance' }
$extra = @($base, $(if ($Filter) { "($Filter)" } else { '' }) | Where-Object { $_ }) -join '&'

if (-not $NoBuild) {
    Write-Host '[e2e] building the E2E project once (shards run --no-build)...'
    & dotnet build $project --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }
}

# One shard's `dotnet test` under the script's own watchdog ($InactivityTimeoutMinutes no-progress rule
# plus the $ShardTimeoutMinutes outer wall-clock budget). Output is streamed through a per-shard file so
# the lane log keeps its content while the runner stays killable (a bare `& dotnet test` cannot be
# interrupted from the script).
# Returns { ExitCode; KilledBy } where KilledBy is 'inactivity', 'budget' or '' (W3) — the summary and
# the rerun verdict report WHY a shard went down without results.
function Invoke-E2EShardTest {
    param(
        [Parameter(Mandatory)][string]$TestFilter,
        [Parameter(Mandatory)][string]$ResultsDirectory,
        [Parameter(Mandatory)][string]$TrxName,
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter(Mandatory)][int]$BudgetMinutes,
        [Parameter(Mandatory)][int]$SilenceMinutes,
        [Parameter(Mandatory)][string]$Label
    )

    $stdoutPath = "$LogPath.stdout.txt"
    $stderrPath = "$LogPath.stderr.txt"
    $argumentLine = (
        "test `"$project`" --no-build --filter `"$TestFilter`"" +
        " --results-directory `"$ResultsDirectory`"" +
        " --logger `"trx;LogFileName=$TrxName`" --logger `"console;verbosity=normal`"" +
        " --blame-crash"
    )
    $proc = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $argumentLine `
        -NoNewWindow -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    # W2: materialize the Process handle NOW. A freshly started Process has no OS handle until one is
    # touched; without this, the watchdog loop's $proc.WaitForExit / Kill below can throw
    # ("No process is associated") on the exact moments it is needed most.
    $null = $proc.Handle

    # The watchdog loop: normal-verbosity console output appends a line per finished test, so the
    # stdout file's size is a completion heartbeat. Total silence for $SilenceMinutes (or the outer
    # wall-clock budget) means the testhost is wedged - capture its managed stacks as evidence, then
    # kill the tree. The shard goes down red WITH diagnostics; the lane goes on.
    $killedBy = ''
    $deadline = (Get-Date).AddMinutes($BudgetMinutes)
    $lastGrowth = Get-Date
    $lastLength = 0
    # The testhost's exact PID, resolved from the process tree (dotnet test → vstest.console →
    # testhost). dotnet-stack -n fails when several testhosts are alive, so the evidence capture
    # targets the one that belongs to THIS shard.
    $script:__testhostPid = 0
    function Find-E2EShardTesthostPid([int]$rootPid) {
        $consoles = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.ParentProcessId -eq $rootPid }
        foreach ($console in $consoles) {
            $host_ = Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $_.ParentProcessId -eq $console.ProcessId } |
                Select-Object -First 1
            if ($host_) { return [int]$host_.ProcessId }
        }
        return 0
    }
    function Save-E2EStacksEvidence([System.Diagnostics.Process]$ChildProc, [int]$targetPid, [string]$stacksPath, [string]$label) {
        # W1: dotnet-stack runs detached (Start-Process) with a hard 120 s ceiling — the tool hangs
        # on a wedged testhost exactly as often as the testhost hangs, and the inline `&` call made
        # the watchdog itself the next stuck process. The tree kill runs ALWAYS, capture or not.
        $stackCaptured = $false
        try {
            if (-not (Get-Command dotnet-stack -ErrorAction SilentlyContinue)) {
                Write-Host "[e2e] dotnet-stack was not found on PATH — skipping the stacks capture for $label (install it with: dotnet tool install -g dotnet-stack)."
            }
            elseif ($targetPid -gt 0 -and (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) {
                $stackProc = Start-Process -FilePath (Get-Command dotnet-stack).Source `
                    -ArgumentList "report -p $targetPid" -NoNewWindow -PassThru `
                    -RedirectStandardOutput $stacksPath -RedirectStandardError "$stacksPath.err.txt"
                $null = $stackProc.Handle
                if (-not $stackProc.WaitForExit(120000)) {
                    try { & taskkill.exe /PID $stackProc.Id /T /F *> $null } catch { }
                    Write-Host "[e2e] dotnet-stack did not finish within 120 s for $label — it was killed; partial output kept at $stacksPath"
                }
                else { $stackCaptured = $true }
            }
            else {
                $stackProc = Start-Process -FilePath (Get-Command dotnet-stack).Source `
                    -ArgumentList "report -n testhost" -NoNewWindow -PassThru `
                    -RedirectStandardOutput $stacksPath -RedirectStandardError "$stacksPath.err.txt"
                $null = $stackProc.Handle
                if (-not $stackProc.WaitForExit(120000)) {
                    try { & taskkill.exe /PID $stackProc.Id /T /F *> $null } catch { }
                    Write-Host "[e2e] dotnet-stack did not finish within 120 s for $label — it was killed; partial output kept at $stacksPath"
                }
                else { $stackCaptured = $true }
            }
        }
        catch {
            Write-Host "[e2e] the stacks capture for $label failed: $($_.Exception.Message)"
        }
        if ($stackCaptured) {
            Write-Host "[e2e] $label was killed; testhost stacks: $stacksPath"
        }
        # Always: the kill is the recovery, the stacks capture is only the evidence.
        & taskkill.exe /PID $ChildProc.Id /T /F *> $null
        $null = $ChildProc.WaitForExit(60000)
    }
    while (-not $proc.WaitForExit(20000)) {
        $length = 0
        try { $length = (Get-Item $stdoutPath -ErrorAction Stop).Length } catch { }
        if ($length -gt $lastLength) {
            $lastLength = $length
            $lastGrowth = Get-Date
        }
        elseif (((Get-Date) - $lastGrowth).TotalMinutes -ge $SilenceMinutes) {
            Write-Host "[e2e] $Label completed no test for $SilenceMinutes minutes — capturing testhost stacks and killing it."
            if ($script:__testhostPid -eq 0) { $script:__testhostPid = Find-E2EShardTesthostPid $proc.Id }
            $killedBy = 'inactivity'
            Save-E2EStacksEvidence $proc $script:__testhostPid "$LogPath.testhost-stacks.txt" $Label
            break
        }

        if ((Get-Date) -gt $deadline) {
            Write-Host "[e2e] $Label exceeded its $BudgetMinutes min wall-clock budget — capturing testhost stacks and killing it."
            if ($script:__testhostPid -eq 0) { $script:__testhostPid = Find-E2EShardTesthostPid $proc.Id }
            $killedBy = 'budget'
            Save-E2EStacksEvidence $proc $script:__testhostPid "$LogPath.testhost-stacks.txt" $Label
            break
        }
    }
    Get-Content $stdoutPath -ErrorAction SilentlyContinue | Write-Host
    Get-Content $stderrPath -ErrorAction SilentlyContinue | Write-Host
    return [pscustomobject]@{ ExitCode = $proc.ExitCode; KilledBy = $killedBy }
}

$classes = @(Get-E2ETestClasses -ProjectDirectory $projectDir)
if ($classes.Count -eq 0) { throw 'no test classes found' }
$plan = @(Split-E2EShards -Classes $classes -Count $Shards)
Write-Host ("[e2e] {0} classes (~{1} tests) in {2} shard(s); report dir: {3}" -f $classes.Count, ($classes | Measure-Object Tests -Sum).Sum, $plan.Count, $ReportDir)

$restartsBefore = Get-E2EHostRestartCount -RepoRoot $repoRoot
$relaunchesBefore = Get-E2EBrowserRelaunchCount -RepoRoot $repoRoot
$started = Get-Date
$shardResults = [System.Collections.Generic.List[object]]::new()

[void](Clear-E2EOrphanedHosts -Reason 'before the run')

foreach ($shard in $plan) {
    if ($OnlyShard.Count -gt 0 -and $OnlyShard -notcontains $shard.Index) { continue }

    $trxName = "shard-$($shard.Index).trx"
    $filter = New-E2EClassFilter -ClassNames $shard.Classes -Extra $extra
    Write-Host ("[e2e] shard {0}/{1}: {2} classes, ~{3} tests" -f $shard.Index, $plan.Count, $shard.Classes.Count, $shard.Total)

    $shardWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $shardRun = Invoke-E2EShardTest -TestFilter $filter -ResultsDirectory $ReportDir -TrxName $trxName `
        -LogPath (Join-Path $ReportDir "shard-$($shard.Index)") `
        -BudgetMinutes $ShardTimeoutMinutes `
        -SilenceMinutes $InactivityTimeoutMinutes -Label "shard $($shard.Index)"
    $exit = $shardRun.ExitCode
    $killedBy = $shardRun.KilledBy
    if ($killedBy) { Write-Host "[e2e] shard $($shard.Index) was killed by the watchdog ($killedBy)." }
    $shardWatch.Stop()

    # A shard that crashed (host/test process gone) leaves orphans; reap them before the next shard starts.
    $reaped = Clear-E2EOrphanedHosts -Reason "after shard $($shard.Index)"

    $summary = Read-E2ETrx -Path (Join-Path $ReportDir $trxName)

    # Empty shard: the filter selected no test (TRX with 0 results, or no TRX and the MTP "zero tests ran" exit code 8).
    # Not a crash, so neither aborted nor "without results".
    $empty = ($summary -and $summary.Total -eq 0) -or (-not $summary -and $exit -eq 8)
    # Aborted: non-zero exit although no test failed (the process died / was killed / MTP errored), or the TRX itself
    # says Aborted/Error. A normal red shard (Failed > 0) is NOT aborted - its failures are reported individually.
    $aborted = (-not $empty) -and $summary -and (
        (($exit -ne 0) -and ($summary.Failed -eq 0)) -or ($summary.Outcome -in @('Aborted', 'Error')))
    if ($empty) { Write-Host "[e2e] shard $($shard.Index) is empty (the filter matched no tests)." }
    if ($aborted) { Write-Host "[e2e] shard $($shard.Index) ABORTED (exit $exit, outcome '$($summary.Outcome)') - the run will fail." }

    $shardResults.Add([pscustomobject]@{
            Shard       = $shard.Index
            Classes     = $shard.Classes.Count
            ExitCode    = $exit
            KilledBy    = $killedBy
            Seconds     = [int]$shardWatch.Elapsed.TotalSeconds
            ReapedHosts = $reaped
            Empty       = [bool]$empty
            Aborted     = [bool]$aborted
            Outcome     = if ($summary) { $summary.Outcome } else { '' }
            NotExecuted = if ($summary) { $summary.NotExecuted } else { 0 }
            NoTrx       = ($null -eq $summary) -and (-not $empty)   # the process died before it wrote results: the whole shard is unclassified
            Total       = if ($summary) { $summary.Total } else { 0 }
            Passed      = if ($summary) { $summary.Passed } else { 0 }
            Failed      = if ($summary) { $summary.Failed } else { 0 }
            Skipped     = if ($summary) { $summary.Skipped } else { 0 }
            Failures    = if ($summary) { $summary.Failures } else { @() }
        })
}

# ---- optional serial re-run of the failures (contention flake vs. real defect) ------------------------
$verdicts = @{}
$allFailures = @($shardResults | ForEach-Object { $_.Failures } | Where-Object { $_ })
if ($RerunFailed -and $allFailures.Count -gt 0) {
    Write-Host "[e2e] re-running $($allFailures.Count) failed test(s) serially..."
    # One rerun per EXACT test (class + method from the TRX, FullyQualifiedName=<class>.<method>); data-row failures of the
    # same method share it. A failure whose identity could not be resolved is left unclassified (never a fuzzy match).
    $byFqn = @{}
    foreach ($f in $allFailures) {
        if (-not $f.Fqn) { $verdicts[$f.Test] = 'unclassified (test identity not found in the TRX)'; continue }
        if (-not $byFqn.ContainsKey($f.Fqn)) {
            $rerunTrx = "rerun-$($f.Fqn -replace '[^\w]', '_').trx"
            [void](Clear-E2EOrphanedHosts -Reason 'before a serial re-run')
            $rerunRun = Invoke-E2EShardTest -TestFilter "FullyQualifiedName=$($f.Fqn)" -ResultsDirectory $ReportDir -TrxName $rerunTrx `
                -LogPath (Join-Path $ReportDir ("rerun-" + ($f.Fqn -replace '[^\w]', '_'))) `
                -BudgetMinutes $ShardTimeoutMinutes `
                -SilenceMinutes $InactivityTimeoutMinutes -Label "the re-run of $($f.Fqn)"
            $rerun = Read-E2ETrx -Path (Join-Path $ReportDir $rerunTrx)
            # W3: a rerun the watchdog killed tells nothing about the test's own verdict — the shard
            # environment wedged AGAIN, so it is reported as deterministic-with-watchdog-kill, never
            # as a flake that "passed serially".
            $byFqn[$f.Fqn] = if ($rerunRun.KilledBy) {
                'deterministic (killed by watchdog)'
            }
            elseif (-not $rerun -or $rerun.Total -lt 1) {
                'unclassified (rerun matched 0 tests)'
            }
            elseif ($rerun.Failed -ge 1) {
                'deterministic (red serially)'
            }
            elseif ($rerun.Outcome -in @('Aborted', 'Error')) {
                'deterministic (rerun aborted)'
            }
            else {
                'contention-flake (passed serially)'
            }
        }
        $verdicts[$f.Test] = $byFqn[$f.Fqn]
    }
    [void](Clear-E2EOrphanedHosts -Reason 'after the re-runs')
}

# ---- report ---------------------------------------------------------------------------------------------
$totals = [pscustomobject]@{
    Total        = ($shardResults | Measure-Object Total -Sum).Sum
    Passed       = ($shardResults | Measure-Object Passed -Sum).Sum
    Failed       = ($shardResults | Measure-Object Failed -Sum).Sum
    Skipped      = ($shardResults | Measure-Object Skipped -Sum).Sum
    ShardsWithoutResults = @($shardResults | Where-Object NoTrx).Count
    ShardsAborted = @($shardResults | Where-Object Aborted).Count
    ShardsEmpty  = @($shardResults | Where-Object Empty).Count
    HostRestarts = (Get-E2EHostRestartCount -RepoRoot $repoRoot) - $restartsBefore
    BrowserRelaunches = (Get-E2EBrowserRelaunchCount -RepoRoot $repoRoot) - $relaunchesBefore
    Minutes      = [Math]::Round(((Get-Date) - $started).TotalMinutes, 1)
}

$report = [pscustomobject]@{
    StartedAt   = $started.ToString('o')
    Filter      = $extra
    ShardCount  = $plan.Count
    Totals      = $totals
    Shards      = $shardResults
    AbortedShards = @($shardResults | Where-Object Aborted | ForEach-Object { $_.Shard })
    Failures    = @($allFailures | ForEach-Object {
            [pscustomobject]@{ Test = $_.Test; Message = $_.Message; Verdict = $verdicts[$_.Test] }
        })
}
$report | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $ReportDir 'summary.json') -Encoding utf8

$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine('# E2E full lane')
[void]$md.AppendLine('')
[void]$md.AppendLine(("Total {0} - passed {1} - failed {2} - skipped {3} - aborted shards {4} - shards without results {5} - host restarts {6} - browser relaunches {7} - {8} min" -f $totals.Total, $totals.Passed, $totals.Failed, $totals.Skipped, $totals.ShardsAborted, $totals.ShardsWithoutResults, $totals.HostRestarts, $totals.BrowserRelaunches, $totals.Minutes))
[void]$md.AppendLine('')
[void]$md.AppendLine('| Shard | Classes | Tests | Passed | Failed | Aborted | Exit | Killed by | Seconds | Reaped hosts |')
[void]$md.AppendLine('|---|---|---|---|---|---|---|---|---|---|')
foreach ($s in $shardResults) {
    $abortedCell = if ($s.Aborted) { 'ABORTED' } elseif ($s.Empty) { 'empty' } elseif ($s.NoTrx) { 'no TRX' } else { '' }
    [void]$md.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |" -f $s.Shard, $s.Classes, $s.Total, $s.Passed, $s.Failed, $abortedCell, $s.ExitCode, $s.KilledBy, $s.Seconds, $s.ReapedHosts))
}
if ($report.AbortedShards.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine(('Aborted shards (crashed or killed test process): {0}' -f ($report.AbortedShards -join ', ')))
}
if ($report.Failures.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('## Failures')
    foreach ($f in $report.Failures) {
        [void]$md.AppendLine(("- ``{0}`` - {1}" -f $f.Test, $(if ($f.Verdict) { $f.Verdict } else { 'unclassified (use -RerunFailed)' })))
    }
}
$md.ToString() | Set-Content -Path (Join-Path $ReportDir 'summary.md') -Encoding utf8

Write-Host ''
Write-Host $md.ToString()
Write-Host "[e2e] report: $ReportDir"

# Red = a failed test that is not proven a contention flake, a shard that produced no results at all, or an aborted shard.
$blocking = @($report.Failures | Where-Object { $_.Verdict -notlike 'contention-flake*' }).Count
if ($blocking -gt 0 -or $totals.ShardsWithoutResults -gt 0 -or $totals.ShardsAborted -gt 0) { exit 1 }
exit 0
