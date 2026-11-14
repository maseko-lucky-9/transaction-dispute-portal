#!/usr/bin/env bash
# Tests for scripts/open-homelab-bump-pr.sh (ADR-017): runs it against a throwaway portal history and a local bare
# repository standing in for homelab-infra, with a stub `gh` that serves canned pull requests and logs every call.
# Proves the ordering (stale runs touch nothing; only ancestors are superseded; unrelated, unlabelled, fork and other-half
# PRs are left alone), the one-line commit, the label, the reuse path and the no-op path. Not the real GitHub API.
# Prints failing cases and exits 1; prints one summary line on success. Run by the `changes` CI job.
# OPEN_PR=<path> runs the cases against another copy of the script (the mutation checks use this).
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1
root=$PWD
OPEN_PR=${OPEN_PR:-$root/scripts/open-homelab-bump-pr.sh}

w=$(mktemp -d)
trap 'rm -rf "$w"' EXIT
fail=0
cases=0
bad() { echo "test-open-homelab-bump-pr: $*" >&2; fail=1; }
quiet_git() { git -c user.email=t@example.com -c user.name=t -c commit.gpgsign=false -c init.defaultBranch=main "$@"; }

API0="sha256:7acfd3b221c2af0dae7f4eda2120397c863b3855e92d7afa57f712ec30434445"
WEB0="sha256:d11d8ee70c368e754ace8869b989f2579e1c6ab6bde0f701fc39603e0c52d8ed"
D1="sha256:$(printf '%064d' 0 | tr 0 1)"
D2="sha256:$(printf '%064d' 0 | tr 0 2)"

# --- the portal history: c1 - c2 - c3 on main, c2b forks from c1, u is an orphan
portal="$w/portal"
quiet_git init -q "$portal"
commit() { quiet_git -C "$portal" commit -q --allow-empty -m "$1"; git -C "$portal" rev-parse HEAD; }
c1=$(commit c1); c2=$(commit c2); c3=$(commit c3)
quiet_git -C "$portal" checkout -q -b side "$c1"; c2b=$(commit c2b)
quiet_git -C "$portal" checkout -q --orphan lonely; u=$(commit u)
quiet_git -C "$portal" checkout -q main
s12() { printf '%s' "${1:0:12}"; }
br() { printf 'bump/dispute-portal-%s-%s' "$1" "$(s12 "$2")"; }

# --- the stub gh: canned PRs from $STUB/prs.json, own-branch PRs from $STUB/heads ("<branch> <number> <url>")
mkdir -p "$w/bin"
cat > "$w/bin/gh" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$STUB/log"
case "$1 $2" in
  "pr list")
    head=""; lbl=""; prev=""
    for a in "$@"; do
      [ "$prev" = --head ] && head=$a
      [ "$prev" = --label ] && lbl=$a
      prev=$a
    done
    if [ -n "$head" ]; then
      awk -v h="$head" '$1 == h { print $2, $3 }' "$STUB/heads"
    elif [ -n "$lbl" ]; then
      jq --arg l "$lbl" '[.[] | select(any(.labels[]; .name == $l)) | del(.labels)]' "$STUB/prs.json"
    else
      jq '[.[] | del(.labels)]' "$STUB/prs.json"
    fi ;;
  "pr create") echo "https://github.com/maseko-lucky-9/homelab-infra/pull/900" ;;
esac
exit 0
STUB
chmod +x "$w/bin/gh"

# pr <number> <half> <sha or 12-hex> <state> <labelled:yes|no> [cross:yes|no]
pr() {
  local labels='[{"name":"ci-bump"}]' cross=false
  [ "$5" = yes ] || labels='[]'
  [ "${6:-no}" = yes ] && cross=true
  printf '{"number":%s,"headRefName":"%s","state":"%s","isCrossRepository":%s,"labels":%s}\n' \
    "$1" "$(br "$2" "$3")" "$4" "$cross" "$labels"
}

# setup <pr lines...>: a fresh homelab remote (master + a branch per listed PR) and a clone; resets the stub
setup() {
  local sc="$w/sc"
  rm -rf "$sc"; mkdir -p "$sc/runner" "$sc/stub"
  export STUB="$sc/stub"
  : > "$STUB/log"; : > "$STUB/heads"
  printf '%s\n' "$@" | jq -s '.' > "$STUB/prs.json"
  quiet_git init -q --bare "$sc/remote.git"
  quiet_git clone -q "$sc/remote.git" "$sc/seed" 2>/dev/null
  mkdir -p "$sc/seed/apps/dispute-portal"
  printf '%s\n' "resources:" "  - api.yaml" "" "images:" \
    "  - name: ghcr.io/maseko-lucky-9/dispute-portal-api" "    digest: $API0" \
    "  - name: ghcr.io/maseko-lucky-9/dispute-portal-web" "    digest: $WEB0" > "$sc/seed/apps/dispute-portal/kustomization.yaml"
  ( cd "$sc/seed" && quiet_git add -A && quiet_git commit -q -m k && quiet_git branch -M master && git push -q origin master ) >/dev/null 2>&1
  local line head
  for line in "$@"; do
    head=$(printf '%s' "$line" | jq -r .headRefName)
    git -C "$sc/seed" push -q origin "master:refs/heads/$head" >/dev/null 2>&1
  done
  git -C "$sc/remote.git" symbolic-ref HEAD refs/heads/master
  quiet_git clone -q --depth=1 --branch master "file://$sc/remote.git" "$sc/runner/homelab-infra" 2>/dev/null
}
# seed_branch <branch> <api digest>: the remote branch gets a commit that differs from master (an earlier attempt)
seed_branch() {
  ( cd "$w/sc/seed" && quiet_git checkout -q -B "$1" master &&
    sed "s|digest: $API0|digest: $2|" apps/dispute-portal/kustomization.yaml > k.tmp && mv k.tmp apps/dispute-portal/kustomization.yaml &&
    quiet_git commit -q -am "earlier attempt" && git push -q origin "HEAD:refs/heads/$1" ) >/dev/null 2>&1
}
own_pr() { printf '%s %s %s\n' "$1" "$2" "https://github.com/maseko-lucky-9/homelab-infra/pull/$2" >> "$STUB/heads"; }

# run <half> <digest> <sha>: sets $out and $rc
run() {
  cases=$((cases + 1))
  out=$(cd "$w" && env PATH="$w/bin:$PATH" STUB="$STUB" HALF="$1" DIGEST="$2" GITHUB_SHA="$3" GH_TOKEN=stub \
    HOMELAB_REPO=maseko-lucky-9/homelab-infra HOMELAB_FILE=apps/dispute-portal/kustomization.yaml \
    HOMELAB_DIR="$w/sc/runner/homelab-infra" PORTAL_DIR="${PORTAL_OVERRIDE:-$portal}" BUMP_SCRIPT="$root/scripts/bump-homelab-image.sh" \
    GITHUB_SERVER_URL=https://github.com GITHUB_REPOSITORY=maseko-lucky-9/transaction-dispute-portal GITHUB_RUN_ID=42 \
    RUNNER_TEMP="$w/sc/runner" bash "$OPEN_PR" 2>&1)
  rc=$?
}
called() { grep -Eq "$1" "$STUB/log"; }
remote_has() { git -C "$w/sc/remote.git" rev-parse --verify --quiet "refs/heads/$1" >/dev/null; }
mutating='^(pr (create|close|edit)|label create)'

# --- S1: a new digest on c3. #11 (c1, ancestor) is superseded; everything else stays.
setup "$(pr 11 api "$c1" OPEN yes)" "$(pr 12 api "$c2b" OPEN yes)" "$(pr 13 api "$u" OPEN yes)" \
  "$(pr 14 api "$c2" OPEN no)" "$(pr 15 web "$c1" OPEN yes)" "$(pr 16 api ffffffffffff OPEN yes)" \
  "$(pr 17 api "$c2" OPEN yes yes)"
run api "$D1" "$c3"
[ "$rc" -eq 0 ] || bad "S1: exit $rc: $out"
called "^pr create .*--head $(br api "$c3") .*--label ci-bump" || bad "S1: no labelled pr create for $(br api "$c3"): $(cat "$STUB/log")"
called '^label create ci-bump .*--force' || bad "S1: the label is not created idempotently"
called '^pr close 11 ' || bad "S1: the ancestor's PR #11 was not closed"
for n in 12 13 14 15 16 17; do called "^pr close $n " && bad "S1: PR #$n was closed (only ancestors with the label, same repo and half)"; done
remote_has "$(br api "$c1")" && bad "S1: the superseded branch was not deleted"
for b in "$(br api "$c2b")" "$(br api "$u")" "$(br api "$c2")" "$(br web "$c1")"; do remote_has "$b" || bad "S1: branch $b was deleted"; done
d=$(git -C "$w/sc/remote.git" diff master "$(br api "$c3")" | grep -E '^[-+] ' | tr '\n' '|')
[ "$d" = "-    digest: $API0|+    digest: $D1|" ] || bad "S1: the bump commit is not the one api digest line: $d"
a=$(git -C "$w/sc/remote.git" log -1 --format='%an <%ae>|%s' "$(br api "$c3")")
[ "$a" = "github-actions[bot] <41898282+github-actions[bot]@users.noreply.github.com>|chore(dispute-portal): bump api image to ${c3:0:7}" ] || bad "S1: commit identity or subject: $a"

# --- S2: a re-run of the c1 run while c3's PR is open: stale, touches nothing
setup "$(pr 21 api "$c3" OPEN yes)"
run api "$D1" "$c1"
[ "$rc" -eq 0 ] || bad "S2: exit $rc: $out"
case "$out" in *"stale run"*) ;; *) bad "S2: no 'stale run' message: $out" ;; esac
called "$mutating" && bad "S2: a stale run changed PRs: $(cat "$STUB/log")"
remote_has "$(br api "$c1")" && bad "S2: a stale run pushed a branch"

# --- S3: the same when the newer PR is already merged
setup "$(pr 31 api "$c3" MERGED yes)"
run api "$D1" "$c1"
[ "$rc" -eq 0 ] && ! called "$mutating" && ! remote_has "$(br api "$c1")" || bad "S3: a run older than a MERGED bump PR was not treated as stale (rc $rc): $(cat "$STUB/log")"

# --- S4: an unrelated (sibling) open PR does not make the run stale, and is not closed
setup "$(pr 41 api "$c2b" OPEN yes)"
run api "$D1" "$c3"
[ "$rc" -eq 0 ] && called '^pr create ' && ! called '^pr close ' || bad "S4: a sibling PR changed the outcome (rc $rc): $(cat "$STUB/log")"

# --- S5: master already pins the digest: this run's own PR and the older one are closed, nothing pushed
setup "$(pr 51 api "$c3" OPEN yes)" "$(pr 52 api "$c1" OPEN yes)"
own_pr "$(br api "$c3")" 51
run api "$API0" "$c3"
[ "$rc" -eq 0 ] || bad "S5: exit $rc: $out"
case "$out" in *"already at this digest"*) ;; *) bad "S5: no 'already at this digest': $out" ;; esac
called '^pr close 51 ' || bad "S5: this run's own no-op PR #51 was not closed"
called '^pr close 52 ' || bad "S5: the older PR #52 was not closed"
called '^pr (create|edit) ' && bad "S5: a no-op run created or edited a PR"

# --- S6: a re-run of the same commit with a rebuilt digest: the branch is replaced, the PR reused and refreshed.
# The remote branch already holds an earlier attempt (digest D1) that is not an ancestor of the new commit, so only a
# forced push can replace it.
setup "$(pr 61 api "$c3" OPEN yes)"
seed_branch "$(br api "$c3")" "$D1"
git -C "$w/sc/remote.git" show "$(br api "$c3"):apps/dispute-portal/kustomization.yaml" | grep -q "digest: $D1" || bad "S6: the seeded branch does not differ from master"
own_pr "$(br api "$c3")" 61
run api "$D2" "$c3"
[ "$rc" -eq 0 ] || bad "S6: exit $rc: $out"
called '^pr edit https://github.com/maseko-lucky-9/homelab-infra/pull/61 .*--add-label ci-bump .*--body-file ' || bad "S6: the open PR was not refreshed: $(cat "$STUB/log")"
called '^pr create ' && bad "S6: a second PR was created for the same branch"
called '^pr close ' && bad "S6: a PR was closed"
k=$(git -C "$w/sc/remote.git" show "$(br api "$c3"):apps/dispute-portal/kustomization.yaml")
printf '%s\n' "$k" | grep -q "digest: $D2" && ! printf '%s\n' "$k" | grep -q "digest: $D1" || bad "S6: the branch was not replaced by the new digest"
grep -q "$D2" "$w/sc/runner/bump-pr-body.md" || bad "S6: the body does not carry the new digest"

# --- S7: a digest the bump script refuses: fails, changes nothing
setup "$(pr 71 api "$c1" OPEN yes)"
run api "sha256:XYZ" "$c3"
[ "$rc" -ne 0 ] || bad "S7: a bad digest exited 0"
called "$mutating" && bad "S7: a failed run changed PRs"
remote_has "$(br api "$c3")" && bad "S7: a failed run pushed"

# --- S8: a web run only ever looks at web PRs
setup "$(pr 81 api "$c1" OPEN yes)" "$(pr 82 web "$c1" OPEN yes)" "$(pr 83 api "$c3" OPEN yes)"
run web "$D1" "$c2"
[ "$rc" -eq 0 ] || bad "S8: exit $rc (an api PR for a newer commit must not make a web run stale): $out"
called '^pr close 82 ' || bad "S8: the older web PR was not closed"
called '^pr close (81|83) ' && bad "S8: an api PR was closed by a web run"

# --- S9: the portal history must contain GITHUB_SHA (a shallow checkout would break the ordering)
setup
run api "$D1" "0123456789abcdef0123456789abcdef01234567"
[ "$rc" -ne 0 ] && ! called "$mutating" || bad "S9: an unknown GITHUB_SHA was not refused (rc $rc)"

# --- S10: a shallow portal checkout fails closed before any decision (the ancestry checks would silently say "no")
setup "$(pr 101 api "$c1" OPEN yes)"
quiet_git clone -q --depth 1 "file://$portal" "$w/shallow" 2>/dev/null
PORTAL_OVERRIDE="$w/shallow" run api "$D1" "$c3"
[ "$rc" -ne 0 ] || bad "S10: a shallow portal clone was accepted"
case "$out" in *"shallow clone"*) ;; *) bad "S10: no 'shallow clone' message: $out" ;; esac
called "$mutating" && bad "S10: a shallow run changed PRs"
remote_has "$(br api "$c3")" && bad "S10: a shallow run pushed"

if [ "$fail" -eq 0 ]; then
  echo "test-open-homelab-bump-pr: $cases cases passed"
fi
exit "$fail"
