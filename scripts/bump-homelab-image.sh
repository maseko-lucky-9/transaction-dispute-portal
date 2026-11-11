#!/usr/bin/env bash
# Pins one portal image to a new digest in homelab-infra's apps/dispute-portal/kustomization.yaml (ADR-017).
#   scripts/bump-homelab-image.sh <api|web> <sha256:64 lowercase hex> <path/to/kustomization.yaml>
# The file's `images:` block is the only place the digests live. This edits exactly one line: the `digest:` line of
# the entry whose list item is exactly `- name: ghcr.io/maseko-lucky-9/dispute-portal-<half>`, inside that block.
# Every other byte (comments, other entries, a missing final newline) and the file's mode stay as they were.
# Prints `unchanged` and exits 0 when the entry already has that digest; prints `bumped ...` after an edit.
# Exits non-zero, touching nothing, on a bad argument, a missing file, a missing or duplicated `images:` block or
# entry, or an entry without exactly one `digest:` line. No network, no kustomize: awk, head and tail only, so it runs
# on macOS bash 3.2 and on ubuntu-latest.
set -euo pipefail

die() { echo "bump-homelab-image: $*" >&2; exit 1; }

[ "$#" -eq 3 ] || die "usage: $0 <api|web> <sha256:digest> <kustomization.yaml>"
half=$1
digest=$2
file=$3

case "$half" in
  api|web) ;;
  *) die "half must be 'api' or 'web', got '$half'" ;;
esac
digest_re='^sha256:[0-9a-f]{64}$'
[[ $digest =~ $digest_re ]] || die "digest must be sha256: followed by 64 lowercase hex characters, got '$digest'"
[ -f "$file" ] || die "no such file: $file"
[ -r "$file" ] && [ -w "$file" ] || die "cannot read and write: $file"

image="ghcr.io/maseko-lucky-9/dispute-portal-$half"

# One pass: prints "<images blocks> <entries named $image> <digest lines in that entry> <line number> <current value>".
# The block starts at a top-level `images:` line and ends at the next top-level line that is not a comment. An entry
# starts at a list item; only an item that is exactly `- name: <image>` (fields split on whitespace) is ours, so the
# image named in a comment, another half or a longer name such as `<image>-canary` never matches.
read -r blocks entries digests line current < <(awk -v img="$image" '
  BEGIN { blocks = 0; entries = 0; digests = 0; line = 0; current = "-"; in_block = 0; ours = 0 }
  /^images:[[:space:]]*$/ { blocks++; in_block = 1; ours = 0; next }
  in_block && /^[^[:space:]#]/ { in_block = 0; ours = 0 }
  !in_block { next }
  /^[[:space:]]*-([[:space:]]|$)/ {
    ours = ($1 == "-" && $2 == "name:" && $3 == img && NF == 3)
    if (ours) entries++
    next
  }
  ours && $1 == "digest:" { digests++; line = NR; current = (NF == 2 ? $2 : "-") }
  END { print blocks, entries, digests, line, current }
' "$file")

[ "$blocks" -eq 1 ] || die "expected one top-level 'images:' block in $file, found $blocks"
[ "$entries" -eq 1 ] || die "expected '- name: $image' once in the images block of $file, found it $entries times"
[ "$digests" -eq 1 ] || die "expected one 'digest:' line under '- name: $image' in $file, found $digests"
[ "$current" != "-" ] || die "the 'digest:' line under '- name: $image' (line $line) is not 'digest: <value>'"

if [ "$current" = "$digest" ]; then
  echo "unchanged"
  exit 0
fi

old_line=$(sed -n "${line}p" "$file")
indent=${old_line%%digest:*}
total=$(awk 'END { print NR }' "$file")
newline_at_end=true
[ -z "$(tail -c 1 "$file")" ] || newline_at_end=false

tmp=$(mktemp "${TMPDIR:-/tmp}/bump-homelab-image.XXXXXX")
trap 'rm -f "$tmp"' EXIT
{
  head -n "$((line - 1))" "$file"
  if [ "$line" -eq "$total" ] && [ "$newline_at_end" = false ]; then
    printf '%sdigest: %s' "$indent" "$digest"
  else
    printf '%sdigest: %s\n' "$indent" "$digest"
  fi
  tail -n "+$((line + 1))" "$file"
} > "$tmp"
# Write through the existing file rather than moving the temp file over it, so its mode and owner stay.
cat "$tmp" > "$file"
echo "bumped $image: $current -> $digest (line $line)"
