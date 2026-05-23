#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "== run offline config and contract diagnostics =="
INTEGRATION_PROFILE=offline "$repo_root/scripts/test-diagnostics.sh"
