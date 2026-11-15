#!/usr/bin/env bash
# Tests for scripts/check-keycloak-theme-sync.sh (ADR-018) with a stub `gh` that serves a canned "<sha> <name>" listing,
# so no network and no token. The listing is built from the repository's real theme files, then broken one way at a time:
# every drift shape must fail, and the cases where the comparison cannot be made must not pass. Prints failing cases and
# exits 1; silent on success. Run by the `changes` CI job.
# CHECK=<path> runs the cases against another copy of the script (the mutation checks use this).
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1
root=$PWD
CHECK=${CHECK:-$root/scripts/check-keycloak-theme-sync.sh}
w=$(mktemp -d)
trap 'rm -rf "$w"' EXIT
fail=0

# The stub: prints $STUB_LISTING as the contents API would after --jq, or fails like an unauthenticated gh.
mkdir -p "$w/bin"
cat > "$w/bin/gh" <<'STUB'
#!/usr/bin/env bash
[ -n "${STUB_FAIL:-}" ] && { echo "gh: HTTP 404" >&2; exit 1; }
cat "$STUB_LISTING"
STUB
chmod +x "$w/bin/gh"
run() { PATH="$w/bin:$PATH" STUB_LISTING="$w/listing" "$CHECK" >"$w/out" 2>&1; echo $?; }
expect() { # name, want rc, got rc
  [ "$2" = "$3" ] || { echo "check-keycloak-theme-sync: $1: exit $3, expected $2: $(head -c 400 "$w/out")" >&2; fail=1; }
}

theme=deploy/keycloak/themes/dispute-portal/login
find "$theme" -type f ! -name '.*' | while IFS= read -r f; do printf '%s %s\n' "$(git hash-object -- "$f")" "${f##*/}"; done > "$w/real"
[ "$(wc -l < "$w/real")" -ge 3 ] || { echo "check-keycloak-theme-sync: found fewer than 3 theme files under $theme" >&2; exit 1; }
first_name=$(awk 'NR == 1 { print $2 }' "$w/real")

# in sync, whatever order the API lists them in
sort -r "$w/real" > "$w/listing"; expect "in sync" 0 "$(run)"
grep -q 'in sync' "$w/out" || { echo "check-keycloak-theme-sync: in sync: no confirmation line: $(cat "$w/out")" >&2; fail=1; }
# one byte different on the homelab side: the blob hash changes, the name does not
awk 'NR == 1 { $1 = substr($1, 1, 39) (substr($1, 40, 1) == "0" ? "1" : "0") substr($1, 41) } { print }' "$w/real" > "$w/listing"
expect "changed file" 1 "$(run)"
grep -q "$first_name" "$w/out" || { echo "check-keycloak-theme-sync: changed file: the output does not name $first_name: $(cat "$w/out")" >&2; fail=1; }
# a file the homelab copy lacks, and one it has that the portal does not
tail -n +2 "$w/real" > "$w/listing"; expect "missing on homelab" 1 "$(run)"
{ cat "$w/real"; echo "0000000000000000000000000000000000000000 extra.css"; } > "$w/listing"; expect "extra on homelab" 1 "$(run)"
# the comparison cannot be made: gh fails, or lists nothing
cp "$w/real" "$w/listing"; expect "gh failure" 2 "$(STUB_FAIL=1 run)"
: > "$w/listing"; expect "empty listing" 2 "$(run)"
# no theme files at all
cp "$w/real" "$w/listing"
mkdir -p "$w/empty"; expect "empty theme dir" 2 "$(THEME_DIR=$w/empty run)"
# a listing that MATCHES the portal, so only the collision rule can fail this one: two files that flatten to one name
mkdir -p "$w/dup/a" "$w/dup/b"; printf 1 > "$w/dup/a/x.css"; printf 2 > "$w/dup/b/x.css"
for f in "$w/dup/a/x.css" "$w/dup/b/x.css"; do printf '%s x.css\n' "$(git hash-object -- "$f")"; done > "$w/listing"
expect "colliding names" 1 "$(THEME_DIR=$w/dup run)"
# a stray .DS_Store is not a theme file
mkdir -p "$w/ds"; printf 1 > "$w/ds/x.css"; printf 2 > "$w/ds/.DS_Store"
printf '%s x.css\n' "$(git hash-object -- "$w/ds/x.css")" > "$w/listing"
expect "dotfile ignored" 0 "$(THEME_DIR=$w/ds run)"

exit "$fail"
