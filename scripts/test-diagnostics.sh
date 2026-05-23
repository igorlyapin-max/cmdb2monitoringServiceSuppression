#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
integration_profile="${INTEGRATION_PROFILE:-offline}"

echo "== check monitoring UI syntax =="
node --check "$repo_root/src/monitoring-ui-api/public/app.js"

echo "== validate monitoring UI config =="
node "$repo_root/src/monitoring-ui-api/scripts/validate-config.mjs"

echo "== run monitoring UI regression checks =="
node "$repo_root/tests/ui-regressions.mjs"

echo "== run autotest coverage contracts =="
node "$repo_root/tests/autotest-plan-contracts.mjs"

echo "== build shared diagnostic contracts =="
"$repo_root/scripts/dotnet" build "$repo_root/tests/sharedcontracts/sharedcontracts.csproj" -v minimal /p:NuGetAudit=false -m:1

echo "== run shared diagnostic contracts =="
"$repo_root/scripts/dotnet" run --no-build --project "$repo_root/tests/sharedcontracts/sharedcontracts.csproj"

run_live=0
run_redis=0
run_redis_kafka=0
case "$integration_profile" in
  offline|"")
    ;;
  live)
    run_live=1
    ;;
  redis)
    run_redis=1
    ;;
  redis-kafka)
    run_redis_kafka=1
    ;;
  all)
    run_live=1
    run_redis=1
    run_redis_kafka=1
    ;;
  *)
    echo "Unsupported INTEGRATION_PROFILE='$integration_profile'. Use offline, live, redis, redis-kafka, or all." >&2
    exit 2
    ;;
esac

if [[ "${LIVE:-0}" == "1" ]]; then
  run_live=1
fi

if [[ "${LIVE_REDIS:-0}" == "1" ]]; then
  run_redis=1
fi

if [[ "${LIVE_REDIS_KAFKA:-0}" == "1" ]]; then
  run_redis_kafka=1
fi

if [[ "$run_live" == "1" ]]; then
  echo "== build live integration checks =="
  "$repo_root/scripts/dotnet" build "$repo_root/tests/integrationchecks/integrationchecks.csproj" -v minimal /p:NuGetAudit=false -m:1

  echo "== run live integration checks =="
  "$repo_root/scripts/dotnet" run --no-build --project "$repo_root/tests/integrationchecks/integrationchecks.csproj"
fi

if [[ "$run_redis" == "1" ]]; then
  echo "== run Redis runtime e2e checks =="
  node "$repo_root/tests/redis-runtime-e2e.mjs"
fi

if [[ "$run_redis_kafka" == "1" ]]; then
  echo "== run Redis Kafka semantic dedup e2e checks =="
  node "$repo_root/tests/redis-kafka-dedup-e2e.mjs"
fi
