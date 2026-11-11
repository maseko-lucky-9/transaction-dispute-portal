# ADR-017: CI opens a homelab-infra pull request for each published image

Status: accepted (2026-11-11). Part D2 of the per-half rollout (ADR-012). Amends [ADR-009](009-local-and-ci-deploy.md):
a published image now also reaches the homelab cluster, once a human merges the pull request this opens.

## Context

`publish` (ADR-012) pushes `ghcr.io/maseko-lucky-9/dispute-portal-{api,web}:<sha>` on `main`, one matrix leg per half
whose image changed. The homelab cluster runs the portal from the private repository `maseko-lucky-9/homelab-infra`
(default branch `master`), where ArgoCD's `dispute-portal` Application auto-syncs `apps/dispute-portal`. That
directory's `kustomization.yaml` ends with an `images:` block, the only place the two digests live; the Deployments and
the migrate Job name the image without a tag or digest. Until now a deploy meant editing that block by hand.

## Decision

- A new job `bump-homelab` in `ci.yml` runs after `publish`, as a matrix over the same `halves` output. It runs only
  when `publish` succeeded and actually pushed: the same rule as `publish`'s `DO_PUSH` (a push to `main`, or a
  `workflow_dispatch` from `main` with `dry_run` false), inlined in the job's `if` because a job-level `if` cannot read
  `env`. A dry run, a pull request or a skipped `publish` skips the job entirely: no clone, no token use.
- Each leg re-resolves `ghcr.io/maseko-lucky-9/dispute-portal-<half>:<sha>` to its digest with
  `docker buildx imagetools inspect --format '{{.Manifest.Digest}}'` (anonymous: the packages are public), retries
  three times, and fails unless the result is `sha256:` and 64 lowercase hex characters.
- It clones homelab-infra with `gh repo clone`, branches `bump/dispute-portal-<half>-<sha7>` off `origin/master`, and
  runs `scripts/bump-homelab-image.sh <half> <digest> apps/dispute-portal/kustomization.yaml`. The script changes
  exactly one `digest:` line, the one under `- name: ghcr.io/maseko-lucky-9/dispute-portal-<half>` inside the
  `images:` block, and refuses anything ambiguous (no block, two blocks, the entry missing or present twice, an entry
  without exactly one `digest:` line, a malformed digest, a half other than `api` or `web`). `scripts/test-bump-homelab-image.sh`
  is its table test (run in the `changes` job, next to the classifier's).
- If the file already pins that digest, the leg prints "already at this digest" and succeeds. Otherwise it commits as
  `github-actions[bot]` with the subject `chore(dispute-portal): bump <half> image to <sha7>`, pushes, and opens a pull
  request against `master` whose body (written to a file, passed with `--body-file`) gives the portal commit, the
  digest, what merging does, the contract rule and the rollback.
- **One open bump PR per half.** After opening its PR the leg closes every other open, same-repository PR whose head is
  `bump/dispute-portal-<half>-<7 hex>`, with the comment "Superseded by <new PR>", and deletes its branch. When master
  already has the digest, the older PRs are closed as superseded by master. The per-half `concurrency` group
  (`bump-homelab-<half>`, never cancelled) keeps two legs for the same half from racing.
- **Merging stays manual.** The job only opens pull requests. Merging one is the deploy: ArgoCD syncs, the
  `dispute-migrate` Sync hook Job (wave 4) applies pending migrations, then the api and web waves roll.
- **Contract rule** (stated in every PR body): if the portal commit changed `docs/openapi.json`, merge the api bump
  first and keep the API backward compatible for one release, because the UI deploys independently.

## Why a separate job

`publish` holds `packages: write`, `id-token: write` and `attestations: write`. The cross-repository token must not sit
in a job that can also push images or mint OIDC tokens: a compromised step there would hold both. `bump-homelab` has
`permissions: contents: read` only, and the token reaches only three steps: the presence check, the clone, and the PR step (as the
`GH_TOKEN` environment variable; `gh auth setup-git` makes git ask gh for it, so it is never in a URL, an argument, the
remote configuration or a log line, and no step uses `set -x`).

## Why re-resolve the digest instead of passing it from `publish`

`publish` is a matrix. A job's outputs are shared by all its legs, so with two legs the last leg to finish overwrites
the other's `digest` and the api digest could be pinned for the web image. Reading the digest back from the registry by
the immutable `<sha>` tag is per leg by construction and also proves the tag is pullable.

## The token (`HOMELAB_INFRA_TOKEN`)

- A repository secret of the portal. A fine-grained personal access token with access to `maseko-lucky-9/homelab-infra`
  only, permissions **Contents: read and write** and **Pull requests: read and write** (plus the mandatory
  **Metadata: read**). No other repository, no organisation or account permissions.
- If it is missing (unset, or a run that cannot read secrets) the first step fails the job with a clear error; the image
  is still published, only the PR is missing. It never succeeds silently.
- **What a leaked token can do:** push branches to homelab-infra and open, comment on, close and merge pull requests
  there. It has no direct access to the cluster, ArgoCD, Vault or any other repository, and it cannot read the portal's
  secrets. The intended boundary is that a human merges, so the worst case is noise and a malicious PR a reviewer must
  notice; review bump PRs as code (the diff must be one `digest:` line).
- **That boundary does not hold yet.** A fine-grained token's Contents write cannot be limited to some branches, and
  `master` in homelab-infra is not protected (`gh api repos/maseko-lucky-9/homelab-infra/branches/master` reported
  `"protected": false` on 2026-11-11; branch protection and rulesets on a private repository need GitHub Pro, the
  rulesets API answers 403). Until `master` requires a pull request, a leaked token can push straight to `master`, and
  ArgoCD auto-syncs every Application that tracks it: that is cluster write access by another route. Closing it needs
  branch protection on homelab-infra (GitHub Pro, or making the repository public once it holds no secrets), not a
  change here. Until then: short expiry, rotation, and revoking on any doubt.
- **Rotation:** set an expiry (90 days at most). To rotate: create the new token with the same scope, update the secret
  (`gh secret set HOMELAB_INFRA_TOKEN -R maseko-lucky-9/transaction-dispute-portal`, value on stdin), run `ci` by
  `workflow_dispatch` from `main` with `dry_run` false to prove it, then revoke the old token. On a suspected leak,
  revoke first, then review homelab-infra's open PRs and branches named `bump/`.

## Consequences

- A `workflow_dispatch` from `main` with `dry_run` false re-publishes the chosen halves for the current `main` commit
  and opens (or refreshes) their bump PRs. With `dry_run` true nothing is pushed and no PR is opened.
- A re-run of the job, or a second non-dry dispatch of the same commit, reuses the branch name: the branch belongs to
  this job and names one commit and one half, so it is force-pushed and the open PR for it is reused.
- One failed `publish` leg makes `publish` as a whole `failure`, which blocks both bump legs, including the half that
  did publish. Re-run the failed jobs, or dispatch that half.
- Re-running an *old* run's `bump-homelab` job supersedes a newer open PR for that half. Merge or re-dispatch the newer
  commit to recover; do not re-run old bump jobs.
- The contract rule is stated in every PR, not detected: the job does not know whether the published range touched
  `docs/openapi.json`.
- The cross-repository steps (clone, push, PR, close) need the secret and cannot run locally; their shell was exercised
  against a local bare repository with a stubbed `gh`, and the first real proof is a dispatch run after merge.
