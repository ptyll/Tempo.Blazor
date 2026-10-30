#!/usr/bin/env bash
# Full E2E lane without pwsh: the whole tests/Tempo.Blazor.E2E suite as N sequential class shards.
# Mirrors scripts/run-e2e-full.ps1 (LPT class split, TestCategory!=Performance, one TRX per shard,
# demo hosts reaped between shards because they bind fixed ports). Shards never overlap (OOM / port).
#
# Usage: run-e2e-shards.sh <report-dir> [shards] [only-shards]   e.g. run-e2e-shards.sh out 8 3,5
set -u
REPO=/root/NetProjects/Tempo.Blazor/Tempo.Blazor
DIR=${1:?report dir}
SHARDS=${2:-8}
ONLY=${3:-}
PROJ=$REPO/tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj
mkdir -p "$DIR/logs" "$DIR/filters"

export LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8 TM_E2E_TRACE_ON_FAILURE=false
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

node /tmp/e2e-shards.mjs "$REPO/tests/Tempo.Blazor.E2E" "$SHARDS" filters > "$DIR/shard-plan.tsv"

reap_hosts() {
  local reason=$1
  for port in 5100 7106 7150 7107 7108 5010; do
    local pids
    pids=$(ss -ltnp 2>/dev/null | grep -E ":${port} " | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u)
    for pid in $pids; do
      if [ "$pid" != "$$" ]; then
        echo "[e2e] reaping orphan host on port $port (pid $pid) - $reason" >> "$DIR/host-reaps.log"
        kill -9 "$pid" 2>/dev/null
      fi
    done
  done
}

echo "[e2e] $(date -u +%FT%TZ) start: $SHARDS shards, report dir $DIR"
while IFS=$'\t' read -r index classes tests filter; do
  if [ -n "$ONLY" ] && [[ ",$ONLY," != *",$index,"* ]]; then continue; fi
  echo "$filter" > "$DIR/filters/shard-$index.filter"
  reap_hosts "before shard $index"
  echo "[e2e] $(date -u +%FT%TZ) shard $index: $classes classes, ~$tests tests"
  start=$(date +%s)
  dotnet test "$PROJ" -c Release --no-build --filter "$filter" \
    --results-directory "$DIR" \
    --logger "trx;LogFileName=shard-$index.trx" \
    --logger "console;verbosity=minimal" \
    > "$DIR/logs/shard-$index.log" 2>&1
  code=$?
  secs=$(( $(date +%s) - start ))
  tail -2 "$DIR/logs/shard-$index.log" | sed "s/^/[e2e] shard-$index: /"
  echo "{\"shard\":$index,\"classes\":$classes,\"exit\":$code,\"seconds\":$secs}" >> "$DIR/shards.jsonl"
  echo "[e2e] $(date -u +%FT%TZ) shard $index done in ${secs}s (exit $code)"
done < "$DIR/shard-plan.tsv"
reap_hosts "after the run"
echo "[e2e] $(date -u +%FT%TZ) ALL SHARDS DONE"
