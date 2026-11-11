#!/usr/bin/env bash
# Tests for scripts/bump-homelab-image.sh (ADR-017) against fixtures shaped like homelab-infra's
# apps/dispute-portal/kustomization.yaml (header comments, a resources: list, the commented images: block).
# Prints failing cases and exits 1; prints one summary line on success. Run by the `changes` CI job.
# BUMP=<path> runs the cases against another copy of the script (the mutation checks use this).
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1
root=$PWD
BUMP=${BUMP:-$root/scripts/bump-homelab-image.sh}
BASH_BIN=${BASH_BIN:-bash}

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

fail=0
cases=0
bad() { echo "test-bump-homelab-image: $*" >&2; fail=1; }

hex() { printf "%064d" 0 | tr 0 "$1"; }
API0="sha256:7acfd3b221c2af0dae7f4eda2120397c863b3855e92d7afa57f712ec30434445"
WEB0="sha256:d11d8ee70c368e754ace8869b989f2579e1c6ab6bde0f701fc39603e0c52d8ed"
D1="sha256:$(hex 1)"
D2="sha256:$(hex 2)"

# fixture <file> <api digest> <web digest> [variant]
# variants: real (default), no-web, dup-api, no-images, two-images, canary, outside, no-digest, two-digests,
#           no-final-newline, extra-token, after, adjacent-images
# The images block of `real` is byte-for-byte the one on homelab-infra master at 8bdcaff (2026-11-11): two-space list
# items, four-space `digest:` lines, the four comment lines above it.
fixture() {
  local out=$1 api=$2 web=$3 variant=${4:-real}
  {
    printf '%s\n' \
      "# Transaction dispute portal $(printf '\342\200\224') namespace \`dispute-portal\`. Synced by argocd/applications/dispute-portal.yaml." \
      "# Validate (client side only, no cluster):  KUBECONFIG=/dev/null kubectl kustomize apps/dispute-portal" \
      "#" \
      "# Sync-waves:  0 namespace+quota+default SA" \
      "#              4 keycloak, migrate Job   (the Job gates everything after it)" \
      "apiVersion: kustomize.config.k8s.io/v1beta1" \
      "kind: Kustomization" \
      "" \
      "resources:" \
      "  - 00-namespace.yaml" \
      "  - rbac.yaml" \
      "  - api.yaml" \
      "  - web.yaml" \
      "  - ingress.yaml"
    if [ "$variant" = outside ]; then
      # the same name and a digest line under another top-level key: not the images block
      printf '%s\n' "" "replacements:" "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api" "    digest: $API0"
    fi
    if [ "$variant" != no-images ]; then
      printf '%s\n' \
        "" \
        "# THE ONLY PLACE the image digests live. The Deployments and the migrate Job name the image without a tag or digest" \
        "# (\`ghcr.io/maseko-lucky-9/dispute-portal-api\`) and the transformer below adds the pin at render time. The portal repo's" \
        "# CI opens a pull request that changes one \`digest:\` line here after each publish (scripts/bump-homelab-image.sh there)." \
        "# Never hand-edit a digest anywhere else: a literal in a manifest would be silently overridden by this block." \
        "images:"
      [ "$variant" = canary ] && printf '%s\n' "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api-canary" "    digest: $D2"
      if [ "$variant" = extra-token ]; then
        printf '%s\n' "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api extra"
      else
        printf '%s\n' "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api"
      fi
      [ "$variant" != no-digest ] && printf '%s\n' "    digest: $api"
      [ "$variant" = two-digests ] && printf '%s\n' "    digest: $api"
      if [ "$variant" = dup-api ]; then
        printf '%s\n' "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api" "    digest: $api"
      fi
      if [ "$variant" != no-web ]; then
        printf '%s\n' "  - name: ghcr.io/maseko-lucky-9/dispute-portal-web"
        if [ "$variant" = no-final-newline ]; then printf '%s' "    digest: $web"; else printf '%s\n' "    digest: $web"; fi
      fi
    fi
    if [ "$variant" = after ]; then
      # a later top-level key with the same name and a digest line: outside the block, so neither counted nor edited
      printf '%s\n' "replacements:" "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api" "    digest: $API0"
    fi
    if [ "$variant" = adjacent-images ]; then
      # a second images: key straight after the first, starting with a bare digest line (before any list item)
      printf '%s\n' "images:" "    digest: $D2" "  - name: ghcr.io/maseko-lucky-9/dispute-portal-web" "    digest: $D2"
    fi
    if [ "$variant" = two-images ]; then
      printf '%s\n' "images:" "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api" "    digest: $api"
    fi
  } > "$out"
}

mode() { ls -l "$1" | cut -c1-10; }

# bump <args...>: runs the script; sets $out and $rc
bump() { out=$("$BASH_BIN" "$BUMP" "$@" 2>&1); rc=$?; }

# a bump that must succeed and make <file> byte-identical to <expected>, changing exactly one line
expect_bump() {
  local name=$1 file=$2 expected=$3; shift 3
  local before="$scratch/before"
  cases=$((cases + 1))
  cp "$file" "$before"
  bump "$@" "$file"
  [ "$rc" -eq 0 ] || { bad "$name: exit $rc ($out)"; return; }
  case "$out" in bumped*) ;; *) bad "$name: expected 'bumped ...', got '$out'" ;; esac
  cmp -s "$file" "$expected" || bad "$name: result differs from the expected file: $(diff "$expected" "$file" | head -5 | tr '\n' ' ')"
  local d
  d=$(diff "$before" "$file")
  [ "$(printf '%s\n' "$d" | grep -c '^[<>]')" -eq 2 ] && printf '%s\n' "$d" | head -n 1 | grep -Eq '^([0-9]+)c\1$' ||
    bad "$name: the diff is not exactly one changed line: $(printf '%s' "$d" | tr '\n' ' ')"
}

# a run that must exit non-zero, name the reason (<message> is a fixed substring of the output) and leave <file> untouched
expect_reject() {
  local name=$1 file=$2 message=$3; shift 3
  local before="$scratch/before"
  cases=$((cases + 1))
  cp "$file" "$before"
  bump "$@" "$file"
  [ "$rc" -ne 0 ] || bad "$name: accepted (exit 0, '$out'), expected a rejection"
  case "$out" in *"$message"*) ;; *) bad "$name: expected the message to contain '$message', got '$out'" ;; esac
  cmp -s "$file" "$before" || bad "$name: the file changed although the run was rejected"
}

# a run that must print `unchanged`, exit 0 and leave <file> untouched
expect_unchanged() {
  local name=$1 file=$2; shift 2
  local before="$scratch/before"
  cases=$((cases + 1))
  cp "$file" "$before"
  bump "$@" "$file"
  [ "$rc" -eq 0 ] && [ "$out" = unchanged ] || bad "$name: expected exit 0 and 'unchanged', got exit $rc: '$out'"
  cmp -s "$file" "$before" || bad "$name: the file changed on a no-op"
}

f="$scratch/kustomization.yaml"
want="$scratch/expected.yaml"

# --- bumps: one entry, the other, both in either order; every other byte is the same (cmp with a fresh fixture)
fixture "$f" "$API0" "$WEB0"; fixture "$want" "$D1" "$WEB0"
expect_bump "api only" "$f" "$want" api "$D1"
fixture "$f" "$API0" "$WEB0"; fixture "$want" "$API0" "$D1"
expect_bump "web only" "$f" "$want" web "$D1"
fixture "$f" "$API0" "$WEB0"; fixture "$want" "$D1" "$WEB0"
expect_bump "api then web (1/2)" "$f" "$want" api "$D1"
fixture "$want" "$D1" "$D2"
expect_bump "api then web (2/2)" "$f" "$want" web "$D2"
fixture "$f" "$API0" "$WEB0"; fixture "$want" "$API0" "$D2"
expect_bump "web then api (1/2)" "$f" "$want" web "$D2"
fixture "$want" "$D1" "$D2"
expect_bump "web then api (2/2)" "$f" "$want" api "$D1"

# --- no-op and idempotency
fixture "$f" "$API0" "$WEB0"
expect_unchanged "same api digest" "$f" api "$API0"
expect_unchanged "same web digest" "$f" web "$WEB0"
fixture "$want" "$D1" "$WEB0"
expect_bump "idempotent (first run)" "$f" "$want" api "$D1"
expect_unchanged "idempotent (second run)" "$f" api "$D1"

# --- file mode is preserved (a temp file moved into place would be 0600)
fixture "$f" "$API0" "$WEB0"; chmod 640 "$f"; before_mode=$(mode "$f")
fixture "$want" "$D1" "$WEB0"
expect_bump "mode (bump)" "$f" "$want" api "$D1"
cases=$((cases + 1))
[ "$(mode "$f")" = "$before_mode" ] || bad "mode: was $before_mode, now $(mode "$f")"
chmod 644 "$f"

# --- shapes the real file could drift into
fixture "$f" "$API0" "$WEB0" canary; fixture "$want" "$D1" "$WEB0" canary
expect_bump "a longer name (…-api-canary) is another entry" "$f" "$want" api "$D1"
fixture "$f" "$API0" "$WEB0" outside; fixture "$want" "$D1" "$WEB0" outside
expect_bump "the same name outside the images block is ignored" "$f" "$want" api "$D1"
fixture "$f" "$API0" "$WEB0" after; fixture "$want" "$D1" "$WEB0" after
expect_bump "the same name under a later top-level key is ignored" "$f" "$want" api "$D1"
fixture "$f" "$API0" "$WEB0" after; fixture "$want" "$API0" "$D1" after
expect_bump "a later top-level key does not hide the web entry" "$f" "$want" web "$D1"
fixture "$f" "$API0" "$WEB0" no-final-newline; fixture "$want" "$API0" "$D1" no-final-newline
expect_bump "a missing final newline stays missing" "$f" "$want" web "$D1"

# --- bad digests
fixture "$f" "$API0" "$WEB0"
upper=$(printf '%s' "$D1" | sed 's/^sha256:1/sha256:A/')
expect_reject "digest with uppercase hex" "$f" "digest must be" api "sha256:$(hex A)"
expect_reject "digest with one uppercase character" "$f" "digest must be" api "$upper"
expect_reject "digest with 63 hex characters" "$f" "digest must be" api "sha256:$(hex 1 | cut -c2-)"
expect_reject "digest with 65 hex characters" "$f" "digest must be" api "${D1}1"
expect_reject "digest without the sha256: prefix" "$f" "digest must be" api "$(hex 1)"
expect_reject "digest with another algorithm" "$f" "digest must be" api "sha512:$(hex 1)"
expect_reject "digest with a leading space" "$f" "digest must be" api " $D1"
expect_reject "digest with a trailing newline" "$f" "digest must be" api "$D1
"
expect_reject "digest with a non-hex character" "$f" "digest must be" api "sha256:$(hex 1 | sed 's/^1/g/')"
expect_reject "empty digest" "$f" "digest must be" api ""
expect_reject "a tag instead of a digest" "$f" "digest must be" api "latest"

# --- bad halves
expect_reject "unknown half" "$f" "half must be" worker "$D1"
expect_reject "half in upper case" "$f" "half must be" API "$D1"
expect_reject "empty half" "$f" "half must be" "" "$D1"
expect_reject "half with a suffix" "$f" "half must be" api-canary "$D1"
expect_reject "two halves in one argument" "$f" "half must be" "api web" "$D1"
fixture "$f" "$API0" "$WEB0" canary
expect_reject "a half that names another entry (api-canary)" "$f" "half must be" api-canary "$D1"

# --- the entry or the block is missing, duplicated or malformed
fixture "$f" "$API0" "$WEB0" no-web
expect_reject "missing web entry" "$f" "found it 0 times" web "$D1"
fixture "$f" "$API0" "$WEB0" dup-api
expect_reject "duplicate api entry" "$f" "found it 2 times" api "$D1"
fixture "$f" "$API0" "$WEB0" no-images
expect_reject "no images block" "$f" "images:' block in" api "$D1"
fixture "$f" "$API0" "$WEB0" two-images
expect_reject "two images blocks" "$f" "images:' block in" api "$D1"
fixture "$f" "$API0" "$WEB0" adjacent-images
expect_reject "a second images key straight after the first" "$f" "images:' block in" api "$D1"
expect_reject "a second images key straight after the first (web)" "$f" "images:' block in" web "$D1"
fixture "$f" "$API0" "$WEB0" extra-token
expect_reject "an extra token after the image name is not our entry" "$f" "found it 0 times" api "$D1"
fixture "$f" "$API0" "$WEB0" no-digest
expect_reject "entry without a digest line" "$f" "found 0" api "$D1"
fixture "$f" "$API0" "$WEB0" two-digests
expect_reject "entry with two digest lines" "$f" "one 'digest:' line" api "$D1"

# --- arguments and files
cases=$((cases + 1))
bump api "$D1" "$scratch/does-not-exist.yaml"
[ "$rc" -ne 0 ] && [ ! -e "$scratch/does-not-exist.yaml" ] || bad "missing file: exit $rc, '$out'"
cases=$((cases + 1))
bump api "$D1"
[ "$rc" -ne 0 ] || bad "two arguments: accepted"

if [ "$fail" -eq 0 ]; then
  echo "test-bump-homelab-image: $cases cases passed"
fi
exit "$fail"
