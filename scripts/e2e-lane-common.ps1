# Shared helpers of the E2E lane scripts (run-e2e-full.ps1, run-e2e-perf.ps1).
# Dot-source this file; it defines functions only and has no side effects.
# See docs/e2e-test-lanes.md.

# The fixed ports of the self-hosted demo apps (PlaywrightTestBase.EnsureDemoHostsAsync):
# API, WASM (https + http), Server, InteractiveAuto.
$script:E2EDemoPorts = @(5100, 7106, 5010, 7107, 7108)

function Stop-E2EProcessTree {
    param([int]$ProcessId)
    # taskkill /T takes the whole `dotnet run` -> host child chain; Stop-Process would orphan the child.
    & taskkill.exe /PID $ProcessId /T /F *> $null
}

function Clear-E2EOrphanedHosts {
    <#
      Reaps demo hosts a crashed or killed test process left behind. An orphan keeps its port bound, so the
      next shard would adopt a half-dead host (or fail with "address already in use"); a clean slate between
      shards is what makes one shard's crash unable to cascade into the next.
      Skipped when TM_E2E_SELF_HOST=false: then the operator owns the hosts and they must not be touched.

      WINDOWS ONLY (Get-NetTCPConnection, Win32_Process, taskkill); on another OS it warns and does nothing.
      It matches by the demo ports and by the command line of `dotnet`/`Tempo.Blazor.Demo*` processes, so it ALSO kills
      a demo you started by hand for development (`dotnet run` / `dotnet watch` of Tempo.Blazor.Demo*, or anything
      else listening on 5100/7106/5010/7107/7108) - do not run the lane scripts next to a dev session you want to keep.
      -NoReap makes it a dry run: it only lists (and counts) what would be killed and kills nothing.
      Returns the number of processes reaped (or, with -NoReap, found).
    #>
    param(
        [string]$Reason = 'between shards',
        [switch]$NoReap
    )

    if ($env:TM_E2E_SELF_HOST -eq 'false') { return 0 }
    if ($env:OS -ne 'Windows_NT') {
        Write-Warning 'Clear-E2EOrphanedHosts is Windows-only; skipping.'
        return 0
    }

    $victims = [System.Collections.Generic.HashSet[int]]::new()

    # 1) whatever listens on a demo port (the host itself, found by PORT)
    $listeners = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $script:E2EDemoPorts -contains $_.LocalPort }
    foreach ($l in $listeners) { [void]$victims.Add([int]$l.OwningProcess) }

    # 2) `dotnet run` wrappers and host dlls of the demo projects that no longer listen (found by PID/command line)
    $demoProject = 'Tempo\.Blazor\.Demo(\.Api|\.Server|\.InteractiveAuto)?(\.dll|\.csproj|\\|\s|$)'
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name LIKE 'Tempo.Blazor.Demo%'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $demoProject -and $_.ProcessId -ne $PID } |
        ForEach-Object { [void]$victims.Add([int]$_.ProcessId) }

    if ($NoReap) {
        if ($victims.Count -gt 0) {
            Write-Host "[e2e] -NoReap: $($victims.Count) demo host process(es) would be reaped ($Reason): $(@($victims) -join ', ')"
        }
        return $victims.Count
    }

    foreach ($id in $victims) {
        if ($id -gt 4 -and $id -ne $PID) { Stop-E2EProcessTree -ProcessId $id }
    }

    if ($victims.Count -gt 0) {
        # give the OS a moment to release the sockets before the next host binds them
        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-Date) -lt $deadline) {
            $still = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
                Where-Object { $script:E2EDemoPorts -contains $_.LocalPort }
            if (-not $still) { break }
            Start-Sleep -Milliseconds 500
        }
        Write-Host "[e2e] reaped $($victims.Count) orphaned demo host process(es) ($Reason)."
    }

    return $victims.Count
}

function Get-E2ETestClasses {
    <#
      Enumerates the test classes of the E2E project with their [TestMethod] counts by scanning the sources
      (no build, no discovery run). A partial class spread over several files is merged by name, and a class
      that INHERITS its tests from a base class counts the base's tests too (MSTest reports an inherited test
      under the derived class, so the derived class must be in a shard or its tests would silently never run).
      Returns objects { Name; Tests }.
    #>
    param([Parameter(Mandatory)][string]$ProjectDirectory)

    $own = @{}        # class -> [TestMethod] count declared in its own sources
    $baseOf = @{}     # class -> first base-type name
    $files = Get-ChildItem -Path $ProjectDirectory -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $methodCount = ([regex]::Matches($text, '\[(Test|DataTest)Method\b')).Count

        # file-scoped or block namespace is irrelevant: the filter matches ".<Class>." inside the FQN.
        $declared = [regex]::Matches($text,
            '(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|partial\s+|abstract\s+)*class\s+(?<n>\w+)(?:<[^>]*>)?\s*(?::\s*(?<b>[\w\.]+))?')
        $names = @($declared | ForEach-Object { $_.Groups['n'].Value } | Select-Object -Unique)
        foreach ($m in $declared) {
            if ($m.Groups['b'].Success) {
                $baseOf[$m.Groups['n'].Value] = ($m.Groups['b'].Value -split '\.')[-1]
            }
        }
        if ($methodCount -eq 0 -or $names.Count -eq 0) { continue }

        # Several classes in one file: spread the methods evenly (an estimate for balancing only).
        $perClass = [Math]::Max(1, [int][Math]::Ceiling($methodCount / $names.Count))
        foreach ($n in $names) {
            if ($own.ContainsKey($n)) { $own[$n] += $perClass } else { $own[$n] = $perClass }
        }
    }

    $result = @{}
    foreach ($name in @($own.Keys) + @($baseOf.Keys | Where-Object { -not $own.ContainsKey($_) })) {
        $total = 0
        $seen = @{}
        for ($c = $name; $c -and -not $seen.ContainsKey($c); $c = $baseOf[$c]) {
            $seen[$c] = $true
            if ($own.ContainsKey($c)) { $total += $own[$c] }
        }
        if ($total -gt 0) { $result[$name] = $total }
    }

    $result.GetEnumerator() | ForEach-Object { [pscustomobject]@{ Name = $_.Key; Tests = $_.Value } }
}
function Split-E2EShards {
    <#
      Longest-processing-time-first split of classes into N shards balanced by test count. Deterministic:
      the same sources always give the same shards, so a failed shard can be re-run by its index.
    #>
    param(
        [Parameter(Mandatory)][object[]]$Classes,
        [Parameter(Mandatory)][int]$Count
    )

    $Count = [Math]::Max(1, [Math]::Min($Count, $Classes.Count))
    $shards = 1..$Count | ForEach-Object { [pscustomobject]@{ Index = $_; Total = 0; Classes = [System.Collections.Generic.List[string]]::new() } }
    foreach ($c in ($Classes | Sort-Object -Property @{Expression = 'Tests'; Descending = $true}, Name)) {
        $target = $shards | Sort-Object Total, Index | Select-Object -First 1
        $target.Classes.Add($c.Name)
        $target.Total += $c.Tests
    }
    return $shards
}

function New-E2EClassFilter {
    param([string[]]$ClassNames, [string]$Extra)

    # FQN = Namespace.Class.Method, so ".Class." selects exactly one class (no prefix collisions).
    $classPart = '(' + (($ClassNames | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|') + ')'
    if ($Extra) { return "($Extra)&$classPart" }
    return $classPart
}

function Read-E2ETrx {
    <#
      Summarises a TRX file into counts, the run outcome and the list of failed tests.
      Outcome     = ResultSummary/@outcome ('Completed', 'Failed', 'Aborted', 'Error', ...): a crashed/killed run is
                    'Aborted'/'Error' even when no test result is red.
      NotExecuted = ResultSummary/Counters/@notExecuted (tests that never ran).
      Each failure carries the EXACT identity (ClassName + MethodName -> Fqn) joined from
      UnitTestResult/@testId to UnitTest/@id -> TestMethod/@className + @name, so a rerun can address it with
      FullyQualifiedName=<class>.<method> instead of a fuzzy name match.
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path $Path)) { return $null }
    [xml]$trx = Get-Content -Raw -Path $Path
    $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $methodOf = @{}
    foreach ($u in @($trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns))) {
        $tm = $u.SelectSingleNode('t:TestMethod', $ns)
        if ($tm) { $methodOf[[string]$u.id] = $tm }
    }

    $results = @($trx.SelectNodes('//t:UnitTestResult', $ns))
    $failed = @($results | Where-Object { $_.outcome -eq 'Failed' })

    $summaryNode = $trx.SelectSingleNode('//t:ResultSummary', $ns)
    $outcome = if ($summaryNode -and $summaryNode.outcome) { [string]$summaryNode.outcome } else { '' }
    $notExecutedAttr = $trx.SelectSingleNode('//t:ResultSummary/t:Counters/@notExecuted', $ns)
    $notExecuted = if ($notExecutedAttr) { [int]$notExecutedAttr.Value } else { 0 }

    [pscustomobject]@{
        Outcome     = $outcome
        Total       = $results.Count
        Passed      = @($results | Where-Object { $_.outcome -eq 'Passed' }).Count
        Failed      = $failed.Count
        Skipped     = @($results | Where-Object { $_.outcome -in @('NotExecuted', 'Inconclusive') }).Count
        NotExecuted = $notExecuted
        Failures    = @($failed | ForEach-Object {
                $tm = $methodOf[[string]$_.testId]
                $className = if ($tm) { [string]$tm.className } else { '' }
                $methodName = if ($tm) { [string]$tm.name } else { '' }
                [pscustomobject]@{
                    Test       = $_.testName
                    ClassName  = $className
                    MethodName = $methodName
                    Fqn        = if ($className -and $methodName) { "$className.$methodName" } else { '' }
                    Message    = ($_.Output.ErrorInfo.Message | Out-String).Trim()
                }
            })
    }
}

function Get-E2EHostRestartCount {
    param([string]$RepoRoot)
    $log = Join-Path $RepoRoot 'TestResults/host-restarts.jsonl'
    if (-not (Test-Path $log)) { return 0 }
    return @(Get-Content $log | Where-Object { $_.Trim() }).Count
}
