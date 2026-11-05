#!/usr/bin/env bash
# Decides which parts of the pipeline a change touches (ADR-012). Reads changed paths on stdin, one per line, and
# prints five `name=true|false` lines:
#   backend   run the backend job (build, tests, coverage gate, vulnerable packages, constraint scan)
#   frontend  run the frontend job (lint, typecheck, tests, build, OpenAPI types)
#   stack     build and scan both images and run the full-stack E2E
#   api       publish the api image (its content changed)
#   web       publish the web image (its content changed)
# A first line of `ALL` (used when the diff could not be computed) turns everything on: a broken filter must
# over-build, never under-build. A path that matches nothing (docs, specs, README) turns nothing on.
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
    ALL)
      backend=true; frontend=true; stack=true; api=true; web=true ;;
    backend/src/*)
      backend=true; stack=true; api=true ;;
    backend/*|DisputePortal.slnx|.config/*|scripts/*)
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
      backend=true; frontend=true ;;
    Dockerfile|.dockerignore)
      backend=true; stack=true; api=true; web=true ;;
    docker-compose.yml)
      backend=true; stack=true ;;
    deploy/postgres/*)
      # roles.sql is linked into the integration tests.
      backend=true; stack=true ;;
    deploy/*|e2e/*|.trivyignore)
      stack=true ;;
    .github/workflows/ci.yml)
      backend=true; frontend=true; stack=true ;;
  esac
done

printf 'backend=%s\nfrontend=%s\nstack=%s\napi=%s\nweb=%s\n' "$backend" "$frontend" "$stack" "$api" "$web"
