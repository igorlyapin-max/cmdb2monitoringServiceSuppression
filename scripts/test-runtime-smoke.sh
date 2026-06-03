#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port_offset="${RUNTIME_SMOKE_PORT_OFFSET:-0}"
log_dir="${TMPDIR:-/tmp}/cmdb2m-runtime-smoke"
mkdir -p "$log_dir"

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  "$repo_root/scripts/dotnet" build "$repo_root/cmdb2monitoringServiceSuppression.slnx" -v minimal /p:NuGetAudit=false -m:1
fi

services=(
  "cmdbwebhooks2kafka|dotnet|src/cmdbwebhooks2kafka/cmdbwebhooks2kafka.csproj|6080"
  "cmdbaggregation2cmdbuild|dotnet|src/cmdbaggregation2cmdbuild/cmdbaggregation2cmdbuild.csproj|6081"
  "cmdbconfigbuilder|dotnet|src/cmdbconfigbuilder/cmdbconfigbuilder.csproj|6082"
  "zabbixconfig2api|dotnet|src/zabbixconfig2api/zabbixconfig2api.csproj|6083"
  "cmdbmodelmaterializer|dotnet|src/cmdbmodelmaterializer/cmdbmodelmaterializer.csproj|6084"
  "monitoring-ui-api|node|src/monitoring-ui-api/server.mjs|6091"
)

modes=(
  "normal|false|Basic"
  "debug-basic|true|Basic"
  "debug-verbose|true|Verbose"
)

wait_for_http() {
  local pid="$1"
  local url="$2"
  local log_file="$3"
  for _ in {1..80}; do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
      echo "process $pid exited before $url became ready" >&2
      tail -n 80 "$log_file" >&2 || true
      return 1
    fi
    sleep 0.25
  done

  echo "timeout waiting for $url" >&2
  tail -n 80 "$log_file" >&2 || true
  return 1
}

for service in "${services[@]}"; do
  IFS='|' read -r name kind entry default_port <<< "$service"
  port=$((default_port + port_offset))
  for mode in "${modes[@]}"; do
    IFS='|' read -r mode_name debug_enabled debug_level <<< "$mode"
    log_file="$log_dir/${name}-${mode_name}.log"
    echo "== smoke $name $mode_name on port $port =="

    if [[ "$kind" == "dotnet" ]]; then
      env \
        ASPNETCORE_URLS="http://127.0.0.1:${port}" \
        Kafka__Enabled=false \
        Debug__Enabled="$debug_enabled" \
        Debug__Level="$debug_level" \
        Readiness__CheckExternalDependencies=false \
        "$repo_root/scripts/dotnet" run --no-build --no-launch-profile --project "$repo_root/$entry" >"$log_file" 2>&1 &
    else
      env \
        NODE_ENV=Production \
        MONITORING_UI_HOST=127.0.0.1 \
        MONITORING_UI_PORT="$port" \
        MONITORING_UI_DEBUG_ENABLED="$debug_enabled" \
        MONITORING_UI_DEBUG_LEVEL="$debug_level" \
        MONITORING_UI_READINESS_CHECK_EXTERNAL_DEPENDENCIES=false \
        node "$repo_root/$entry" >"$log_file" 2>&1 &
    fi
    pid=$!

    if ! wait_for_http "$pid" "http://127.0.0.1:${port}/health" "$log_file"; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
      exit 1
    fi
    curl -fsS "http://127.0.0.1:${port}/ready" >/dev/null
    curl -fsS "http://127.0.0.1:${port}/metrics" >/dev/null

    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  done
done

echo "Runtime smoke checks passed."
