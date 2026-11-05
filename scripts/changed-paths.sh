#!/usr/bin/env bash
# Decides which parts of the pipeline a change touches (ADR-012). Reads changed paths on stdin, one per line, and
# prints five `name=true|false` lines:
#   backend   run the backend job (build, tests, coverage gate, vulnerable packages, constraint scan)
#   frontend  run the frontend job (lint, typecheck, tests, build, OpenAPI types)
#   stack     build and scan both images and run the full-stack E2E
#   api       publish the api image (its content changed)
#   web       publish the web image (its content changed)
# Fail closed: a first line of `ALL` (the diff could not be computed) or a path that looks quoted turns everything on,
# and a path that matches no rule at all runs the verification jobs (backend, frontend, stack), so a new kind of file
# is built until someone classifies it. Only the explicit inert list below (docs, specs, README, hooks, ...) turns
# nothing on. Publishing is narrower on purpose: an image is published only for the paths that change its content,
# and scripts/test-changed-paths.sh checks that every path the Dockerfile copies is one of them.
# Plain variables and `case`, so it also runs on macOS bash 3.2.
set -uo pipefail

backend=false
frontend=false
stack=false
api=false
web=false

while IFS= read -r path; do
  [ -z "$path" ] && continue
  case "$path" in
    ALL|\"*)
      backend=true; frontend=true; stack=true; api=true; web=true ;;
    backend/src/*)
      backend=true; stack=true; api=true ;;
    backend/*|DisputePortal.slnx|.config/*)
      backend=true ;;
    global.json|Directory.Build.props)
      backend=true; stack=true; api=true ;;
    frontend/src/lib/validation-cases.json)
      # Read by the backend unit tests (csproj link) as well as by the SPA.
      backend=true; frontend=true; stack=true; web=true ;;
    frontend/*)
      frontend=true; stack=true; web=true ;;
    docs/openapi.json)
      # The contract: the backend snapshot test and the frontend type generation both check it.
      backend=true; frontend=true ;;
    .editorconfig)
      # Copied into the api build stage by the Dockerfile (it can change analyzer results) and read by both jobs.
      backend=true; frontend=true; stack=true; api=true ;;
    Dockerfile|.dockerignore)
      backend=true; stack=true; api=true; web=true ;;
    docker-compose.yml)
      backend=true; stack=true ;;
    deploy/postgres/*)
      # roles.sql is linked into the integration tests.
      backend=true; stack=true ;;
    deploy/*|e2e/*|.trivyignore)
      stack=true ;;
    .github/workflows/*|scripts/changed-paths.sh|scripts/changed-files.sh|scripts/test-changed-paths.sh)
      # The pipeline and its classifier: verify everything, publish nothing (no image content changed).
      backend=true; frontend=true; stack=true ;;
    scripts/*)
      backend=true ;;
    docs/*|specs/*|perf/*|.specify/*|.claude/*|.githooks/*|.github/dependabot.yml|.gitleaks.toml|*.md|api.http|.gitignore|.gitattributes)
      : ;;
    *)
      backend=true; frontend=true; stack=true ;;
  esac
done

printf 'backend=%s\nfrontend=%s\nstack=%s\napi=%s\nweb=%s\n' "$backend" "$frontend" "$stack" "$api" "$web"
