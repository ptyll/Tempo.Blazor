#!/usr/bin/env bash
# Fáze 7d regression gate — the Spreadsheet E2E lanes, one batch at a time (never the whole project).
# Each lane writes its own console log and appends one markdown section to the run's README.
set -u
cd /root/NetProjects/Tempo.Blazor/Tempo.Blazor || exit 1
OUT=eng/release-evidence/spreadsheet-lanes-20261030
mkdir -p "$OUT/logs"
SUMMARY="$OUT/README.md"

: > "$SUMMARY"
{
  echo "# Spreadsheet E2E regression lanes — phase 7d (2026-10-30)"
  echo
  echo "Hosts: API https://localhost:5100 + WASM https://localhost:7106 started by the implementer,"
  echo "\`TM_E2E_SELF_HOST=false\`. Each lane is one \`dotnet test --filter\` batch over the"
  echo "\`Tempo.Blazor.E2E\` project (\`--no-build\`), run sequentially on this box."
  echo
} >> "$SUMMARY"

run_lane() {
    local name="$1"
    local filter="$2"
    local log="$OUT/logs/$name.log"
    local start end dur rc line failed
    start=$(date +%s)
    echo "=== LANE $name start $(date -Is) :: $filter"
    TM_E2E_SELF_HOST=false dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj \
        -f net10.0 --no-build --filter "$filter" --nologo > "$log" 2>&1
    rc=$?
    end=$(date +%s)
    dur=$((end - start))
    line=$(grep -E "^(Passed!|Failed!)" "$log" | tail -1)
    failed=$(grep -E "^  Failed " "$log" | sed 's/^  Failed //;s/ \[.*//' | head -40)
    {
        echo "## $name"
        echo
        echo "- filter: \`$filter\`"
        echo "- duration: ${dur}s (exit $rc)"
        echo "- result: ${line:-<no summary line — see logs/$name.log>}"
        if [ -n "$failed" ]; then
            echo "- failed tests:"
            echo "$failed" | sed 's/^/  - /'
        fi
        echo
    } >> "$SUMMARY"
    echo "LANE-DONE $name rc=$rc dur=${dur}s :: ${line:-no-summary}"
}

run_lane block           "FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetBlockE2ETests."
run_lane phase3          "Name~Phase3_"
run_lane phase4          "Name~Phase4_"
run_lane phase5          "Name~Phase5_"
run_lane phase6-responsive "Name~Phase6_"
run_lane phase7-touch    "Name~Phase7_"
run_lane phase7c         "Name~Phase7c_"
run_lane phase2          "Name~Phase2_"
run_lane phase6-class    "FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetPhase6E2ETests."
run_lane baselines       "ClassName~SpreadsheetBaselineScreenshots|ClassName~SpreadsheetPhase2BaselineScreenshots|ClassName~SpreadsheetPhase3BaselineScreenshots|ClassName~SpreadsheetPhase4BaselineScreenshots|ClassName~SpreadsheetPhase5BaselineScreenshots|ClassName~SpreadsheetPhase6BaselineScreenshots"
run_lane base            "FullyQualifiedName~Tempo.Blazor.E2E.SpreadsheetE2ETests.&Name!~Phase"

echo "ALL-LANES-DONE $(date -Is)"
