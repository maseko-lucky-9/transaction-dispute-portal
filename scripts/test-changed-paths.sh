#!/usr/bin/env bash
# Table test for scripts/changed-paths.sh: each row is "path|names that must be true" (everything else must be
# false). Prints failing rows and exits 1; silent on success. Run by the `changes` CI job.
set -uo pipefail
cd "$(dirname "$0")/.."

fail=0
check() {
  local path="$1" want="$2" got
  got=$(printf '%s\n' "$path" | scripts/changed-paths.sh | sed -n 's/=true$//p' | paste -sd, -)
  if [ "$got" != "$want" ]; then
    echo "changed-paths: '$path' -> '$got', expected '$want'" >&2
    fail=1
  fi
}

# backend only: the api image changes, the web image does not
check "backend/src/DisputePortal.Api/Program.cs" "backend,stack,api"
check "backend/src/DisputePortal.Domain/Disputes/Dispute.cs" "backend,stack,api"
check "global.json" "backend,stack,api"
check "Directory.Build.props" "backend,stack,api"
# backend tests and tooling: the job runs, no image changes
check "backend/tests/DisputePortal.UnitTests/MoneyTests.cs" "backend"
check "DisputePortal.slnx" "backend"
check ".config/dotnet-tools.json" "backend"
check "scripts/coverage.sh" "backend"
# frontend only: the web image changes, the api image does not
check "frontend/src/components/transactions-page.tsx" "frontend,stack,web"
check "frontend/package-lock.json" "frontend,stack,web"
check "frontend/nginx/default.conf" "frontend,stack,web"
# shared fixtures and contract
check "frontend/src/lib/validation-cases.json" "backend,frontend,stack,web"
check "docs/openapi.json" "backend,frontend"
check ".editorconfig" "backend,frontend"
# whole-stack inputs
check "Dockerfile" "backend,stack,api,web"
check ".dockerignore" "backend,stack,api,web"
check "docker-compose.yml" "backend,stack"
check "deploy/postgres/roles.sql" "backend,stack"
check "deploy/keycloak/dispute-portal-realm.json" "stack"
check "e2e/tests/auth.spec.ts" "stack"
check ".trivyignore" "stack"
check ".github/workflows/ci.yml" "backend,frontend,stack"
# nothing to build
check "README.md" ""
check "docs/runbook.md" ""
check "specs/001-transaction-disputes/tasks.md" ""
check ".githooks/pre-commit" ""
check ".github/dependabot.yml" ""
check "perf/k6/smoke.js" ""
check "some/new/unlisted/file.txt" ""
# the diff could not be computed: build and publish everything
check "ALL" "backend,frontend,stack,api,web"

# several paths combine
got=$(printf '%s\n' "backend/src/DisputePortal.Api/Program.cs" "frontend/src/main.tsx" "README.md" | scripts/changed-paths.sh | sed -n 's/=true$//p' | paste -sd, -)
if [ "$got" != "backend,frontend,stack,api,web" ]; then
  echo "changed-paths: combined paths -> '$got'" >&2
  fail=1
fi
# an empty diff turns nothing on
got=$(printf '' | scripts/changed-paths.sh | sed -n 's/=true$//p' | paste -sd, -)
if [ -n "$got" ]; then
  echo "changed-paths: empty diff -> '$got', expected nothing" >&2
  fail=1
fi

exit "$fail"
