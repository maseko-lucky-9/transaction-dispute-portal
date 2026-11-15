#!/usr/bin/env bash
# ADR-018: the Capitec Keycloak login theme lives twice: here (deploy/keycloak/themes/dispute-portal/login/) and, as flat
# files, in homelab-infra (apps/dispute-portal/keycloak-theme/, built into a ConfigMap). No published image carries it, so
# an edit here reaches the cluster only if someone copies it over. This compares the two by git blob hash (`git
# hash-object` here, the contents API's `sha` there: the same hash, so nothing is downloaded) and fails on any difference:
# a changed file, a file on one side only, or two portal files that would collide once flattened.
# Exit 0 in sync, 1 drift, 2 the comparison could not be made (no access, empty listing). Silent success is not an option:
# an empty side fails, so a renamed directory cannot turn this into a check of nothing.
# Reads homelab-infra with GH_TOKEN (CI: the `homelab-infra` environment secret) or a local `gh auth login`.
# THEME_DIR, HOMELAB_REPO, HOMELAB_DIR and HOMELAB_REF override the defaults; the test puts a stub `gh` on PATH.
# Plain bash and `sort`, so it also runs on macOS bash 3.2. ponytail: compares bytes only; a NEW theme file also needs a
# `files:` line in homelab-infra's kustomization.yaml and a mount, which this cannot see.
set -uo pipefail
export LC_ALL=C
cd "$(dirname "$0")/.." || exit 2

THEME_DIR=${THEME_DIR:-deploy/keycloak/themes/dispute-portal/login}
HOMELAB_REPO=${HOMELAB_REPO:-maseko-lucky-9/homelab-infra}
HOMELAB_DIR=${HOMELAB_DIR:-apps/dispute-portal/keycloak-theme}
HOMELAB_REF=${HOMELAB_REF:-master}

# "<blob hash> <flat name>" per line, sorted by name. Dotfiles (.DS_Store) are not theme files.
portal=$(find "$THEME_DIR" -type f ! -name '.*' 2>/dev/null | while IFS= read -r f; do
  printf '%s %s\n' "$(git hash-object -- "$f")" "${f##*/}"
done | sort -k2)
if [ -z "$portal" ]; then
  echo "keycloak-theme-sync: no theme files under $THEME_DIR" >&2; exit 2
fi
dups=$(printf '%s\n' "$portal" | awk '{ print $2 }' | uniq -d)
if [ -n "$dups" ]; then
  echo "keycloak-theme-sync: these names collide once the theme is flattened for the ConfigMap: $dups" >&2; exit 1
fi

if ! homelab=$(gh api "repos/$HOMELAB_REPO/contents/$HOMELAB_DIR?ref=$HOMELAB_REF" \
    --jq '.[] | select(.type == "file") | "\(.sha) \(.name)"' | sort -k2); then
  echo "keycloak-theme-sync: could not list $HOMELAB_REPO:$HOMELAB_DIR (no read access, a missing token, or the path moved)" >&2; exit 2
fi
if [ -z "$homelab" ]; then
  echo "keycloak-theme-sync: $HOMELAB_REPO:$HOMELAB_DIR lists no files" >&2; exit 2
fi

if [ "$portal" = "$homelab" ]; then
  echo "keycloak theme in sync: $(printf '%s\n' "$portal" | wc -l | tr -d ' ') files identical to $HOMELAB_REPO@$HOMELAB_REF:$HOMELAB_DIR"
  exit 0
fi
{
  echo "keycloak theme DRIFT: the cluster serves the homelab-infra copy (-), this repository holds the other (+):"
  diff -u --label "homelab-infra:$HOMELAB_DIR" --label "portal:$THEME_DIR" \
    <(printf '%s\n' "$homelab") <(printf '%s\n' "$portal") | tail -n +3 | grep -E '^[-+]'
  echo "Fix: copy the portal files into $HOMELAB_DIR (flat names) in a homelab-infra pull request, or revert the portal edit. ADR-018."
} >&2
exit 1
