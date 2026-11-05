#!/usr/bin/env bash
# Tests for scripts/changed-paths.sh and scripts/changed-files.sh. Prints failing cases and exits 1; silent on success.
# Run by the `changes` CI job.
#   1. a table: each row is "path|names that must be true" (everything else must be false)
#   2. every path the Dockerfile copies must publish an image (so a new COPY cannot go unclassified)
#   3. changed-files.sh on a scratch repository: non-ASCII, tab and renamed paths must reach the classifier
set -uo pipefail
cd "$(dirname "$0")/.."
root=$PWD

fail=0
classify() { scripts/changed-paths.sh | sed -n 's/=true$//p' | paste -sd, -; }
check() {
  local path="$1" want="$2" got
  got=$(printf '%s\n' "$path" | classify)
  if [ "$got" != "$want" ]; then
    echo "changed-paths: '$path' -> '$got', expected '$want'" >&2
    fail=1
  fi
  # publish downloads the images artifact, which only exists when the stack ran
  case ",$got," in
    *,api,*|*,web,*) case ",$got," in *,stack,*) ;; *) echo "changed-paths: '$path' publishes without the stack ('$got')" >&2; fail=1 ;; esac ;;
  esac
}
everything="backend,frontend,stack,api,web"
verify="backend,frontend,stack"

# --- 1. the table
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
check "scripts/check-image-pins.sh" "backend"
# frontend only: the web image changes, the api image does not
check "frontend/src/components/transactions-page.tsx" "frontend,stack,web"
check "frontend/package-lock.json" "frontend,stack,web"
check "frontend/nginx/default.conf" "frontend,stack,web"
# shared fixtures and contract
check "frontend/src/lib/validation-cases.json" "backend,frontend,stack,web"
check "docs/openapi.json" "backend,frontend"
check ".editorconfig" "backend,frontend,stack,api"
# whole-stack inputs
check "Dockerfile" "backend,stack,api,web"
check ".dockerignore" "backend,stack,api,web"
check "docker-compose.yml" "backend,stack"
check "deploy/postgres/roles.sql" "backend,stack"
check "deploy/keycloak/dispute-portal-realm.json" "stack"
check "e2e/tests/auth.spec.ts" "stack"
check ".trivyignore" "stack"
# the pipeline and its classifier: verify everything, publish nothing
check ".github/workflows/ci.yml" "$verify"
check ".github/workflows/release.yml" "$verify"
check "scripts/changed-paths.sh" "$verify"
check "scripts/changed-files.sh" "$verify"
check "scripts/test-changed-paths.sh" "$verify"
# real files under each rule, so narrowing a pattern is caught
check "backend/src/DisputePortal.Api/DisputePortal.Api.csproj" "backend,stack,api"
check "backend/src/DisputePortal.Api/packages.lock.json" "backend,stack,api"
check "backend/src/My File.cs" "backend,stack,api"
check "frontend/package.json" "frontend,stack,web"
check "frontend/vite.config.ts" "frontend,stack,web"
check "deploy/bootstrap/bootstrap.sh" "stack"
check "deploy/vault/policies/dispute-api.hcl" "stack"
check "deploy/postgres/init.sh" "backend,stack"
check "e2e/playwright.config.ts" "stack"
check "scripts/check-constraints.sh" "backend"
# near misses: similar names that must not trigger anything
check "dockerfile" "$verify"
check "docs/ALL" ""
check "frontend.md" ""
# inert: nothing to build
check "README.md" ""
check "docs/runbook.md" ""
check "docs/decisions/012-backend-frontend-layout-and-path-scoped-cicd.md" ""
check "specs/001-transaction-disputes/tasks.md" ""
check ".githooks/pre-commit" ""
check ".github/dependabot.yml" ""
check ".gitleaks.toml" ""
check "perf/k6/smoke.js" ""
check "api.http" ""
# fail closed: an unclassified path runs the verification jobs; a quoted path or ALL builds and publishes everything
check "some/new/unlisted/file.txt" "$verify"
check "Directory.Packages.props" "$verify"
check "nuget.config" "$verify"
check ".github/CODEOWNERS" "$verify"
check "\"backend/src/Api/caf\\303\\251.cs\"" "$everything"
check "ALL" "$everything"

# several paths combine
got=$(printf '%s\n' "backend/src/DisputePortal.Api/Program.cs" "frontend/src/main.tsx" "README.md" | classify)
if [ "$got" != "$everything" ]; then
  echo "changed-paths: combined paths -> '$got'" >&2
  fail=1
fi
# an empty diff turns nothing on
got=$(printf '' | classify)
if [ -n "$got" ]; then
  echo "changed-paths: empty diff -> '$got', expected nothing" >&2
  fail=1
fi

# the output contract: all five lines, true or false, and a zero exit
out=$(printf 'README.md\n' | scripts/changed-paths.sh); rc=$?
want_out=$(printf 'backend=false\nfrontend=false\nstack=false\napi=false\nweb=false')
if [ "$rc" != 0 ] || [ "$out" != "$want_out" ]; then
  echo "changed-paths: output contract broken (rc=$rc): $out" >&2
  fail=1
fi

# --- 2. every path the Dockerfile copies publishes an image
copy_sources=$(awk '/^COPY / && $0 !~ /--from=/ { for (i = 2; i < NF; i++) if ($i !~ /^--/) print $i }' Dockerfile | sort -u)
[ -n "$copy_sources" ] || { echo "changed-paths: found no COPY sources in the Dockerfile" >&2; fail=1; }
for src in $copy_sources; do
  case "$src" in
    */|.) probe="${src%/}/probe.file" ;;
    *) probe="$src" ;;
  esac
  [ "$src" = "./" ] && continue
  got=$(printf '%s\n' "$probe" | classify)
  case ",$got," in
    *,api,*|*,web,*) ;;
    *) echo "changed-paths: Dockerfile copies '$src' but a change to it publishes no image ('$got')" >&2; fail=1 ;;
  esac
done

# --- 3. changed-files.sh sees what a plain `git diff --name-only` would lose
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
(
  cd "$scratch" && git init -q . && git config user.email t@example.com && git config user.name t && git config commit.gpgsign false
  mkdir -p backend/src/Api docs
  printf 'a' > backend/src/Api/Plain.cs
  printf 'a' > Dockerfile
  git add -A && git commit -q -m base
  printf 'x' > "backend/src/Api/caf$(printf '\303\251').cs"
  printf 'x' > "backend/src/Api/tab$(printf '\t')name.cs"
  git mv backend/src/Api/Plain.cs docs/Plain.cs.txt
  git mv Dockerfile docker.Dockerfile
  git add -A && git commit -q -m change
) >/dev/null 2>&1
files=$(cd "$scratch" && "$root/scripts/changed-files.sh" HEAD~1 HEAD)
want_each="backend/src/Api/Plain.cs docs/Plain.cs.txt Dockerfile docker.Dockerfile"
for want in $want_each; do
  printf '%s\n' "$files" | grep -qxF "$want" || { echo "changed-files: '$want' missing from: $files" >&2; fail=1; }
done
printf '%s\n' "$files" | grep -q "^backend/src/Api/caf" || { echo "changed-files: the non-ASCII path is missing" >&2; fail=1; }
printf '%s\n' "$files" | grep -q '^"' && { echo "changed-files: a path came out C-quoted" >&2; fail=1; }
got=$(printf '%s\n' "$files" | classify)
[ "$got" = "backend,frontend,stack,api,web" ] || [ "$got" = "backend,stack,api,web" ] || { echo "changed-files: scratch change classified as '$got'" >&2; fail=1; }
( cd "$scratch" && "$root/scripts/changed-files.sh" no-such-revision >/dev/null 2>&1 ) && { echo "changed-files: a git failure must exit non-zero" >&2; fail=1; }

exit "$fail"
