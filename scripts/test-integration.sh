#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
profile="${1:-${INTEGRATION_PROFILE:-all}}"

case "$profile" in
  live|redis|redis-kafka|all)
    ;;
  *)
    echo "Usage: $0 [live|redis|redis-kafka|all]" >&2
    exit 2
    ;;
esac

INTEGRATION_PROFILE="$profile" "$repo_root/scripts/test-diagnostics.sh"
