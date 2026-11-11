#!/usr/bin/env bash
# The `bump-homelab` CI job's last step (ADR-017): opens, refreshes or skips the homelab-infra pull request that pins
# one portal image to a new digest, and closes the bump PRs it supersedes. Never merges.
#
# Environment (all required unless a default is shown):
#   HALF, DIGEST, GITHUB_SHA       the half (api|web), its verified sha256 digest, the portal commit that published it
#   HOMELAB_REPO, HOMELAB_FILE     owner/repo and the kustomization path inside it
#   HOMELAB_DIR                    a clone of HOMELAB_REPO whose `origin` push works (default $RUNNER_TEMP/homelab-infra)
#   PORTAL_DIR                     a FULL-history clone of the portal, for the ancestry checks (default: this repo)
#   BUMP_SCRIPT                    default: bump-homelab-image.sh next to this file
#   GITHUB_SERVER_URL, GITHUB_REPOSITORY, GITHUB_RUN_ID, RUNNER_TEMP, GITHUB_STEP_SUMMARY (optional)
#   GH_TOKEN                       read by `gh` and, through `gh auth setup-git`, by `git push`; never printed here
#
# Bump PRs are recognised by BOTH their head branch `bump/dispute-portal-<half>-<12 hex>` and the label `ci-bump`.
# Ordering is by portal ancestry, never by run order:
#   - another open or merged bump PR for this half whose commit DESCENDS from GITHUB_SHA means this run is stale (an old
#     run re-run): print why and exit 0 without touching anything;
#   - an open one whose commit is an ANCESTOR of GITHUB_SHA is superseded: closed with a comment, its branch deleted;
#   - one whose commit is unrelated, or unknown to the portal history, is left alone.
set -euo pipefail

die() { echo "open-homelab-bump-pr: $*" >&2; exit 1; }
for var in HALF DIGEST GITHUB_SHA HOMELAB_REPO HOMELAB_FILE RUNNER_TEMP GITHUB_SERVER_URL GITHUB_REPOSITORY GITHUB_RUN_ID; do
  [ -n "${!var:-}" ] || die "$var is not set"
done
here=$(cd "$(dirname "$0")" && pwd)
HOMELAB_DIR=${HOMELAB_DIR:-$RUNNER_TEMP/homelab-infra}
PORTAL_DIR=${PORTAL_DIR:-$(cd "$here/.." && pwd)}
BUMP_SCRIPT=${BUMP_SCRIPT:-$here/bump-homelab-image.sh}
summary=${GITHUB_STEP_SUMMARY:-/dev/null}

label="ci-bump"
sha12=${GITHUB_SHA:0:12}
short=${GITHUB_SHA:0:7}
prefix="bump/dispute-portal-${HALF}-"
branch="${prefix}${sha12}"
title="chore(dispute-portal): bump ${HALF} image to ${short}"
portal_commit="${GITHUB_SERVER_URL}/${GITHUB_REPOSITORY}/commit/${GITHUB_SHA}"
run_url="${GITHUB_SERVER_URL}/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}"

git -C "$PORTAL_DIR" cat-file -e "${GITHUB_SHA}^{commit}" 2>/dev/null || die "$GITHUB_SHA is not in $PORTAL_DIR (it needs full history)"

# --- ordering: decide before touching anything
known=$(gh pr list --repo "$HOMELAB_REPO" --state all --label "$label" --limit 200 \
  --json number,headRefName,state,isCrossRepository |
  jq -r --arg p "$prefix" \
    '.[] | select(.isCrossRepository == false and (.headRefName | test("^" + $p + "[0-9a-f]{12}$"))) | "\(.number) \(.state) \(.headRefName)"')
superseded=""
while read -r number state head; do
  [ -n "${number:-}" ] || continue
  other=${head#"$prefix"}
  [ "$other" != "$sha12" ] || continue
  other_full=$(git -C "$PORTAL_DIR" rev-parse --verify --quiet "${other}^{commit}" 2>/dev/null || true)
  if [ -z "$other_full" ]; then
    echo "ignoring #${number} (${head}): ${other} is not a commit in the portal history"
    continue
  fi
  if git -C "$PORTAL_DIR" merge-base --is-ancestor "$GITHUB_SHA" "$other_full"; then
    case "$state" in
      OPEN|MERGED)
        echo "stale run: bump PR #${number} (${head}, ${state}) is for ${other_full}, a newer commit than ${GITHUB_SHA}; nothing to do" | tee -a "$summary"
        exit 0 ;;
    esac
  elif git -C "$PORTAL_DIR" merge-base --is-ancestor "$other_full" "$GITHUB_SHA"; then
    if [ "$state" = OPEN ]; then superseded="${superseded}${number} ${head}"$'\n'; fi
  else
    echo "ignoring #${number} (${head}): ${other} is neither an ancestor nor a descendant of ${GITHUB_SHA}"
  fi
done <<< "$known"

close_pr() { # <number> <head branch> <superseded by>
  gh pr close "$1" --repo "$HOMELAB_REPO" --comment "Superseded by ${3}." < /dev/null
  git -C "$HOMELAB_DIR" push -q origin --delete "$2" < /dev/null || echo "could not delete branch ${2} (already gone?)"
  echo "closed #${1} (${2}), superseded by ${3}" | tee -a "$summary"
}

# --- the bump
cd "$HOMELAB_DIR"
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git checkout -q -B "$branch" origin/master
change=$("$BUMP_SCRIPT" "$HALF" "$DIGEST" "$HOMELAB_FILE")
echo "$change"

own=$(gh pr list --repo "$HOMELAB_REPO" --state open --head "$branch" --json url,number --jq '.[0] | select(. != null) | "\(.number) \(.url)"')
if git diff --quiet; then
  echo "already at this digest: homelab-infra master pins dispute-portal-${HALF}@${DIGEST}"
  superseded_by="homelab-infra master, which already pins this half to ${DIGEST} (portal ${short})"
  # This run's own PR (from an earlier attempt) would now be a no-op: close it too.
  [ -z "$own" ] || close_pr "${own%% *}" "$branch" "$superseded_by"
else
  git add -- "$HOMELAB_FILE"
  git commit -q -m "$title" -m "Portal commit: ${portal_commit}" -m "Digest: ${DIGEST}"
  body="$RUNNER_TEMP/bump-pr-body.md"
  {
    printf '%s\n' "Opened by the portal's CI ([run](${run_url}), job \`bump-homelab\`, ADR-017). Merging is manual; do not push to this branch."
    printf '\n%s\n' "- Portal commit: ${portal_commit}"
    printf '%s\n' "- Image: \`ghcr.io/maseko-lucky-9/dispute-portal-${HALF}@${DIGEST}\` (build provenance verified: signed by the portal's ci.yml on refs/heads/main for this commit)"
    printf '%s\n' "- Change: \`${change}\`"
    printf '\n%s\n' "**What merging does.** ArgoCD (Application \`dispute-portal\`, automated sync) rolls the new image out. Every sync re-runs the \`dispute-migrate\` Sync hook Job first (wave 4), which applies any pending EF Core migrations; the api and web waves only roll after it succeeds."
    printf '\n%s\n' "**Contract rule.** If this portal commit changed \`docs/openapi.json\`, merge the api bump before the web bump, and keep the API backward compatible for one release: the UI can deploy independently of the API."
    printf '\n%s\n' "**Roll back.** Revert this pull request on master (GitHub's Revert button) and merge the revert; ArgoCD syncs back to the previous digest. Migrations are forward-only, so a revert does not undo a schema change. If the migrate hook fails, see the portal's docs/runbook.md (section 8)."
  } > "$body"
  gh label create "$label" --repo "$HOMELAB_REPO" --color 5319E7 --force \
    --description "Digest bump opened by the dispute portal's CI (ADR-017)" > /dev/null
  # A re-run of this job (or a dry_run=false dispatch of the same commit) reuses the branch name. The branch is this
  # job's own, named for one commit and one half, so it is overwritten rather than appended to.
  git push -q --force origin "HEAD:refs/heads/${branch}"
  if [ -z "$own" ]; then
    url=$(gh pr create --repo "$HOMELAB_REPO" --base master --head "$branch" --title "$title" --label "$label" --body-file "$body")
  else
    url=${own#* }
    # The force-push may carry a new digest (a re-publish rebuilds the image): refresh the body to match.
    gh pr edit "$url" --repo "$HOMELAB_REPO" --title "$title" --add-label "$label" --body-file "$body" > /dev/null
    echo "reusing and refreshing the open pull request for ${branch}"
  fi
  echo "bump pull request: ${url}" | tee -a "$summary"
  superseded_by="$url"
fi

# --- at most one open bump PR per half
while read -r number head; do
  [ -n "${number:-}" ] || continue
  close_pr "$number" "$head" "$superseded_by"
done <<< "$superseded"
