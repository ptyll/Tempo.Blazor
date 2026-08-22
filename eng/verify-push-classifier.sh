#!/usr/bin/env bash
# Probe the push classifier against a local NuGet endpoint.
#
# THIS CLASSIFIER IS BOUND TO SDK WORDING, not to an exit code. Measured over SDK 10.0.111:
#   PUBLISHED = "Your package was pushed."  AND/OR  "Created http"
#   SKIPPED   = "already exists at feed"    AND/OR  "Conflict http"
#   anything else = UNRESOLVED = red
# A future SDK that rewords those sentences will classify every package UNRESOLVED and fail this
# probe (and the real push) loudly. That direction is deliberate. The treatment is to re-measure
# here, not to guess.
#
# WHEN TO RUN: on an SDK bump, and before every release. Both publish workflows call this after
# pack and before the real push. PACKAGE_OUTPUT defaults to ./packages (the staging directory
# pack just filled). A release run therefore sees published=26 / skipped=26; a fixture run can
# point PACKAGE_OUTPUT at a smaller set.
#
# RECIPE, measured rather than imagined:
#   python http.server serving a v3 service index + PackagePublish/2.0.0
#   NuGet.Config with allowInsecureConnections=true, passed as --configfile (PUSH_CONFIGFILE)
#   PUSH_SOURCE / PUSH_API_KEY / PACKAGE_OUTPUT from the environment
#   201 Created  → published=$total, exit 0
#   409 Conflict → skipped=$total,   exit 1  (the job is supposed to be red: the number is spent)
set -euo pipefail

output="${PACKAGE_OUTPUT:-./packages}"
if [[ ! -d "$output" ]]; then
  echo "PACKAGE_OUTPUT '$output' is not a directory; nothing to classify." >&2
  exit 1
fi

shopt -s nullglob
packages=("$output"/*.nupkg)
shopt -u nullglob
total="${#packages[@]}"
if [[ "$total" -eq 0 ]]; then
  echo "No .nupkg in '$output'; a classifier probe over nothing is silence, not evidence." >&2
  exit 1
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
push_script="$root/eng/push-nuget-packages.sh"
if [[ ! -f "$push_script" ]]; then
  echo "push script not found at $push_script" >&2
  exit 1
fi

# A free port. Binding 0 and reading the port back from the READY line the stub prints.
workdir="$(mktemp -d "${TMPDIR:-/tmp}/tm-push-classifier.XXXXXX")"
cleanup() {
  if [[ -n "${stub_pid:-}" ]]; then
    kill "$stub_pid" 2>/dev/null || true
    wait "$stub_pid" 2>/dev/null || true
  fi
  rm -rf "$workdir"
}
trap cleanup EXIT

stub_py="$workdir/nuget_stub.py"
cat >"$stub_py" <<'PY'
"""Local NuGet v3 stub. MODE=created → 201; MODE=conflict → 409. Prints READY <port> on stdout.

The PUT path is the one F10 measured: SDK 10.0.111 sends HTTP/1.1, often chunked, and a
handler that answers before draining the body RSTs the client (Broken pipe). Drain first,
then respond. protocol_version = HTTP/1.1 keeps the socket open for the next package.
"""
from __future__ import annotations

import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODE = os.environ.get("NUGET_STUB_MODE", "created")


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):  # noqa: A003
        return

    def _host(self) -> str:
        return self.headers.get("Host", "127.0.0.1")

    def _read_body(self) -> bytes:
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            data = b""
            while True:
                line = self.rfile.readline().strip()
                size = int(line.split(b";")[0], 16)
                if size == 0:
                    self.rfile.readline()
                    break
                data += self.rfile.read(size)
                self.rfile.read(2)
            return data
        remaining = int(self.headers.get("Content-Length", "0") or "0")
        data = b""
        while remaining > 0:
            chunk = self.rfile.read(min(65536, remaining))
            if not chunk:
                break
            remaining -= len(chunk)
            data += chunk
        return data

    def do_GET(self):  # noqa: N802
        if "index.json" in self.path or self.path in ("/", "/v3", "/v3/"):
            body = json.dumps({
                "version": "3.0.0",
                "resources": [
                    {"@id": f"http://{self._host()}/v3/package", "@type": "PackagePublish/2.0.0"},
                    {"@id": f"http://{self._host()}/v3-flatcontainer/", "@type": "PackageBaseAddress/3.0.0"},
                ],
            }).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_response(404)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_PUT(self):  # noqa: N802
        self._read_body()
        if MODE == "conflict":
            msg = b'{"error":"already exists"}'
            self.send_response(409)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(msg)))
            self.end_headers()
            self.wfile.write(msg)
            return
        msg = b'{"ok":true}'
        self.send_response(201)
        self.send_header("Content-Type", "application/json")
        self.send_header("Location", f"http://{self._host()}/pkg")
        self.send_header("Content-Length", str(len(msg)))
        self.end_headers()
        self.wfile.write(msg)

    def do_POST(self):  # noqa: N802
        self.do_PUT()

    def do_DELETE(self):  # noqa: N802
        self.send_response(404)
        self.send_header("Content-Length", "0")
        self.end_headers()


httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
port = httpd.server_address[1]
sys.stdout.write(f"READY {port}\n")
sys.stdout.flush()
httpd.serve_forever()
PY

start_stub() {
  local mode="$1"
  if [[ -n "${stub_pid:-}" ]]; then
    kill "$stub_pid" 2>/dev/null || true
    wait "$stub_pid" 2>/dev/null || true
    stub_pid=""
  fi
  local log="$workdir/stub-$mode.log"
  NUGET_STUB_MODE="$mode" python3 "$stub_py" >"$log" 2>&1 &
  stub_pid=$!
  local i
  for i in $(seq 1 50); do
    if grep -q '^READY ' "$log" 2>/dev/null; then
      stub_port="$(awk '/^READY /{print $2; exit}' "$log")"
      return 0
    fi
    if ! kill -0 "$stub_pid" 2>/dev/null; then
      echo "nuget stub exited before READY ($mode):" >&2
      cat "$log" >&2
      return 1
    fi
    sleep 0.1
  done
  echo "nuget stub did not print READY ($mode):" >&2
  cat "$log" >&2
  return 1
}

write_config() {
  local port="$1"
  local cfg="$workdir/NuGet.Config"
  cat >"$cfg" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="tm-push-probe" value="http://127.0.0.1:${port}/v3/index.json" allowInsecureConnections="true" />
  </packageSources>
</configuration>
EOF
  printf '%s' "$cfg"
}

run_push() {
  local mode="$1"
  start_stub "$mode"
  local cfg
  cfg="$(write_config "$stub_port")"
  local out_file="$workdir/push-$mode.out"
  local rc=0
  PUSH_SOURCE="tm-push-probe" \
    PUSH_API_KEY="probe" \
    PUSH_CONFIGFILE="$cfg" \
    PACKAGE_OUTPUT="$output" \
    bash "$push_script" >"$out_file" 2>&1 || rc=$?
  cat "$out_file"
  # Carry the summary line for the caller.
  probe_rc="$rc"
  probe_out="$(cat "$out_file")"
}

echo "[classifier-probe] packages=$total from $output (SDK $(dotnet --version 2>/dev/null || echo unknown))"

run_push created
created_rc="$probe_rc"
created_out="$probe_out"
if ! printf '%s\n' "$created_out" | grep -q "published=$total "; then
  echo "201 arm: expected published=$total in the summary; classifier did not read a Created push." >&2
  echo "$created_out" >&2
  exit 1
fi
if [[ "$created_rc" -ne 0 ]]; then
  echo "201 arm: expected exit 0 with published=$total, got exit $created_rc." >&2
  exit 1
fi
echo "[classifier-probe] 201 Created → published=$total exit 0"

run_push conflict
conflict_rc="$probe_rc"
conflict_out="$probe_out"
if ! printf '%s\n' "$conflict_out" | grep -q "skipped=$total "; then
  echo "409 arm: expected skipped=$total in the summary; classifier did not read a Conflict skip." >&2
  echo "$conflict_out" >&2
  exit 1
fi
if [[ "$conflict_rc" -ne 1 ]]; then
  echo "409 arm: expected exit 1 with skipped=$total (spent number), got exit $conflict_rc." >&2
  exit 1
fi
echo "[classifier-probe] 409 Conflict → skipped=$total exit 1"

echo "[classifier-probe] SDK wording still matches the classifier (created=published, conflict=skipped)."
exit 0
