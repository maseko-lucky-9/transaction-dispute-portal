# ADR-018: a drift check for the hand-copied Keycloak login theme

Status: accepted (2026-11-15). Follow-up to [ADR-012](012-backend-frontend-layout-and-path-scoped-cicd.md) and
[ADR-017](017-homelab-image-bump-prs.md).

## Context

The Capitec login theme lives twice. `deploy/keycloak/themes/dispute-portal/login/` here (`theme.properties`,
`resources/css/dispute-portal.css`, `resources/img/logo.jpg`) is what `docker-compose.yml` mounts into the local
Keycloak. `apps/dispute-portal/keycloak-theme/` in `maseko-lucky-9/homelab-infra` holds the same three files flat; its
`kustomization.yaml` builds them into a ConfigMap with `configMapGenerator`, so the name carries a content hash and a
changed file rolls Keycloak by itself.

No published image carries the theme, so `scripts/changed-paths.sh` classifies `deploy/*` as `stack` only (right: the
stack E2E should see a theme edit, and an image publish would be wrong). Everything ADR-017 added hangs off a publish:
`bump-homelab` needs `publish` to have succeeded and matrixes over `halves`, so a theme-only change never reaches it.
Nothing fails and no homelab pull request opens; the cluster keeps the old theme. The two copies were byte-identical
when this was written (git blob hashes `a1f09f1`, `21b1e7b`, `5d0cd59`), so the gap is a process one.

## Decision

A **drift detector**, not an automated pull request.

- `scripts/check-keycloak-theme-sync.sh` hashes each theme file here (`git hash-object`) and compares it with the `sha`
  the contents API reports for `apps/dispute-portal/keycloak-theme/` on homelab-infra `master` (the same git blob hash,
  so nothing is downloaded). It fails on a changed file, a file on one side only, two portal files that would collide
  once flattened, and when either side lists nothing. Exit 0 in sync, 1 drift, 2 the comparison could not be made. The
  mapping is the flat file name, not a hard-coded list, so a new theme file is drift until it is copied.
- `.github/workflows/keycloak-theme-sync.yml` runs it on a push to `main` that touches the theme (or the check), daily,
  and on demand. One job, `environment: homelab-infra`, `contents: read`, the token in one step only. Without the token
  it fails with an error rather than comparing nothing.
- `scripts/test-check-keycloak-theme-sync.sh` (a stub `gh`, run by the `changes` job beside the other script tests)
  breaks the listing one way at a time. It was mutation-checked: disabling each rule the script enforces turns it red.

**Why a workflow of its own.** A failed job in `ci.yml` makes the whole run `failure`, and `changes` takes the diff base
for a push from the last run on `main` that succeeded (ADR-012): a drift that stays red would stop that base advancing
and re-classify (and re-publish) an ever larger range. A separate workflow cannot touch that, or the required `ci-ok`.

**Why after the merge and not on the pull request.** The cross-repository token is an environment secret that only
deployments from `main` can read, on purpose (ADR-017). A token any branch's workflow could read would hand every pull
request a key to the private repository, so the pull-request run cannot compare. The check therefore fires as the edit
lands on `main` and every day until the copies agree. Doing the homelab pull request first also passes: the check is
symmetric.

## Why not open the homelab pull request automatically

It would copy the theme and open a PR in the same way the digest bump does, and it is the better experience. It costs
more than this problem is worth now: a trigger that exists without a publish (a new job or workflow, since
`bump-homelab` cannot be reached), a script that maps the nested files to the flat ones and the supersede and reuse
logic `open-homelab-bump-pr.sh` already needed, its own stub tests, and a second place that holds the token with
**Contents write** on a `master` that cannot be protected (ADR-017). The detector holds nothing it can write with, is a
fraction of the code, and the theme changes rarely. Revisit if theme edits become frequent; the detector stays useful
then as the check that the automated pull request did its job. Removing the duplication (the theme in a published image,
or fetched at render time) is the real fix and a larger change to ADR-012's classification; not done here.

## Consequences

- The signal comes after the merge, as a red run of `keycloak-theme-sync` on `main` (GitHub notifies the pusher, or for
  a scheduled run the account that last edited the cron line), not as a failed check on the pull request. It is not a
  required check and blocks nothing; that is deliberate.
- It compares bytes only. A new theme file also needs a `files:` line in homelab-infra's `kustomization.yaml` and a mount
  in its Keycloak Deployment, which this cannot see.
- It reads with the write-capable `HOMELAB_INFRA_TOKEN` (the ADR-017 token). A read-only fine-grained token for this
  workflow alone would be tidier; it is another secret to create and rotate, so it is not done.
- `environment: homelab-infra` records one deployment to that environment per run, so the daily schedule adds a line a
  day to its deployment list.
- Scheduled workflows are paused after 60 days without repository activity; a dispatch from `main` re-enables one.
- Like the ADR-017 cross-repository steps it cannot run on a pull request. The first real run is the push to `main` that
  merges it (or a `workflow_dispatch`); the script itself was run against the live homelab-infra, in sync, and with a
  one-byte theme edit, which exits 1 and names the file.
