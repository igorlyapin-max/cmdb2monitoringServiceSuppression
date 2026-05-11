#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "== check monitoring UI syntax =="
node --check "$repo_root/src/monitoring-ui-api/public/app.js"

echo "== validate monitoring UI config =="
node "$repo_root/src/monitoring-ui-api/scripts/validate-config.mjs"

echo "== build shared diagnostic contracts =="
"$repo_root/scripts/dotnet" build "$repo_root/tests/sharedcontracts/sharedcontracts.csproj" -v minimal /p:NuGetAudit=false

echo "== run shared diagnostic contracts =="
"$repo_root/scripts/dotnet" run --no-build --project "$repo_root/tests/sharedcontracts/sharedcontracts.csproj"

if [[ "${LIVE:-0}" == "1" ]]; then
  echo "== build live integration checks =="
  "$repo_root/scripts/dotnet" build "$repo_root/tests/integrationchecks/integrationchecks.csproj" -v minimal /p:NuGetAudit=false

  echo "== run live integration checks =="
  "$repo_root/scripts/dotnet" run --no-build --project "$repo_root/tests/integrationchecks/integrationchecks.csproj"
fi
