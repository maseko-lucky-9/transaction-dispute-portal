#!/usr/bin/env bash
# Every external image must be pinned by digest (tag@sha256:...): a re-tagged image must not slip into the build,
# the published images or the CI runner (the Trivy step mounts the Docker socket). Images built here are exempt.
set -uo pipefail
cd "$(dirname "$0")/.."
bad=""
# Dockerfile: external FROMs only (a FROM that names an earlier stage is internal).
stages=" "
while read -r line; do
  img=$(printf '%s' "$line" | sed -E 's/^FROM +(--platform=[^ ]+ +)?([^ ]+).*/\2/')
  stage=$(printf '%s' "$line" | sed -nE 's/.* [Aa][Ss] +([^ ]+).*/\1/p')
  case "$stages" in *" $img "*) ;; *) case "$img" in *@sha256:*) ;; *) bad="$bad\nDockerfile: $img";; esac;; esac
  [ -n "$stage" ] && stages="$stages$stage "
done < <(grep -E '^FROM ' Dockerfile)
# Compose: `image:` lines that are not images built from this repo.
while read -r img; do
  case "$img" in dispute-portal-*) ;; *@sha256:*) ;; *) bad="$bad\ndocker-compose.yml: $img";; esac
done < <(sed -nE 's/^ *image: *([^ #]+).*/\1/p' docker-compose.yml)
# CI: registry references in workflow files (own GHCR tags are push targets, not inputs).
while read -r img; do
  case "$img" in ghcr.io/maseko-lucky-9/*) ;; *@sha256:*) ;; *) bad="$bad\nci: $img";; esac
done < <(grep -hoE '(ghcr\.io|docker\.io|mcr\.microsoft\.com|quay\.io)/[A-Za-z0-9._/-]+:[A-Za-z0-9._-]+(@sha256:[0-9a-f]+)?' .github/workflows/*.yml)
if [ -n "$bad" ]; then
  printf 'image not pinned by digest:%b\n' "$bad"
  exit 1
fi
