# ADR-017: CI opens a homelab-infra pull request for each published image

Status: accepted (2026-11-11). Part D2 of the per-half rollout (ADR-012). Amends [ADR-009](009-local-and-ci-deploy.md):
a published image now also reaches the homelab cluster, once a human merges the pull request this opens. Amended the
same day after code and security review: environment-scoped secret, provenance check, `ci-bump` label, 12-hex branch
names, ancestry ordering, and a corrected account of what the token can do.

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
- **The job's `if` is not a security control; the environment is.** The job declares `environment: homelab-infra`, a
  GitHub environment restricted to deployments from `main`, and the token is an environment secret there. A workflow
  edited on any other branch cannot read it, whatever its `if` says. While a repository-level `HOMELAB_INFRA_TOKEN`
  still exists, any branch's workflow can read that one, so it must be deleted once the environment secret is set
  (runbook section 7).
- Each leg re-resolves `ghcr.io/maseko-lucky-9/dispute-portal-<half>:<sha>` to its digest with
  `docker buildx imagetools inspect --format '{{.Manifest.Digest}}'` (anonymous: the packages are public), retries
  three times, and fails unless the result is `sha256:` and 64 lowercase hex characters.
- **The digest is bound to its provenance** before anything uses it: `gh attestation verify
  oci://ghcr.io/maseko-lucky-9/dispute-portal-<half>@<digest> --repo maseko-lucky-9/transaction-dispute-portal
  --signer-workflow maseko-lucky-9/transaction-dispute-portal/.github/workflows/ci.yml --source-ref refs/heads/main
  --source-digest <sha> --deny-self-hosted-runners` (with the run's own token and `attestations: read`). A tag can be
  moved; an attestation signed by `ci.yml` on `main` for exactly this commit cannot be forged without that workflow.
  Verification failure fails the job.
- It clones homelab-infra with `gh repo clone`, and `scripts/open-homelab-bump-pr.sh` does the rest: it branches
  `bump/dispute-portal-<half>-<12 hex of the sha>` off `origin/master` and runs
  `scripts/bump-homelab-image.sh <half> <digest> apps/dispute-portal/kustomization.yaml`. That script changes
  exactly one `digest:` line, the one under `- name: ghcr.io/maseko-lucky-9/dispute-portal-<half>` inside the
  `images:` block, and refuses anything ambiguous (no block, two blocks, the entry missing or present twice, an entry
  without exactly one `digest:` line, a malformed digest, a half other than `api` or `web`).
- If the file already pins that digest, the leg prints "already at this digest", closes this run's own open PR if an
  earlier attempt left one (it would be a no-op), and succeeds. Otherwise it commits with the subject
  `chore(dispute-portal): bump <half> image to <sha7>`, pushes, and opens a pull request against `master`, labelled
  `ci-bump` (the label is created idempotently with `gh label create --force`), whose body (written to a file, passed
  with `--body-file`) gives the portal commit, the verified digest, what merging does, the contract rule and the rollback.
- **Ordering is by portal ancestry, not by run order.** The job checks the portal out with full history. Before touching
  anything it lists this half's bump PRs (head branch `bump/dispute-portal-<half>-<12 hex>` AND label `ci-bump`, same
  repository): if any open or merged one is for a commit that descends from this run's commit, this run is stale (an
  old run re-run): it says so and exits 0 without pushing, opening or closing anything. Only open PRs whose commit is a
  strict ancestor of this run's commit are closed as superseded ("Superseded by <new PR>", branch deleted). PRs for
  unrelated commits, for commits unknown to the portal history, without the label, or from forks are left alone. A
  check on "is this the head of main" was rejected: a web bump for commit A must still open when an unrelated backend
  commit B landed after it.
- **One open bump PR per half** follows from that, and the per-half `concurrency` group (`bump-homelab-<half>`, never
  cancelled) keeps two legs for the same half from racing.
- **Merging stays manual.** The job only opens pull requests. Merging one is the deploy: ArgoCD syncs, the
  `dispute-migrate` Sync hook Job (wave 4) applies pending migrations, then the api and web waves roll.
- **Contract rule** (stated in every PR body): if the portal commit changed `docs/openapi.json`, merge the api bump
  first and keep the API backward compatible for one release, because the UI deploys independently.
- Tests, both run in the `changes` job: `scripts/test-bump-homelab-image.sh` (table test of the bump script against
  fixtures whose `images:` block is byte-for-byte the one on homelab-infra master) and
  `scripts/test-open-homelab-bump-pr.sh` (the PR step against a throwaway portal history, a local bare repository and a
  stub `gh`: stale runs touch nothing, only ancestors are superseded, the label and the no-op and reuse paths).

## Why a separate job

`publish` holds `packages: write`, `id-token: write` and `attestations: write`. The cross-repository token must not sit
in a job that can also push images or mint OIDC tokens: a compromised step there would hold both. `bump-homelab` has
`permissions: contents: read` and `attestations: read` only, and the token reaches only three steps: the presence
check, the clone, and the PR step (as the `GH_TOKEN` environment variable; `gh auth setup-git` makes git ask gh for it,
so it is never in a URL, an argument, the remote configuration or a log line, and no step uses `set -x`). The
provenance step uses the run's own read-only token, not this one.

## Why re-resolve the digest instead of passing it from `publish`

`publish` is a matrix. A job's outputs are shared by all its legs, so with two legs the last leg to finish overwrites
the other's `digest` and the api digest could be pinned for the web image. Reading the digest back from the registry by
the `<sha>` tag is per leg by construction and proves the tag is pullable; the provenance check then proves the digest
is the one `ci.yml` built for that commit, so a re-pointed tag cannot slip in.

## Who acts in homelab-infra

A personal access token acts as its owner. The push, the pull request, the label, the comments and the closes are made
by the token's owner account; only the commit's author and committer name and e-mail are set to `github-actions[bot]`,
and the commit is unsigned, so that identity is a label, not proof. A GitHub App installation token is the upgrade: real
bot authorship, tokens that live one hour and are minted per run, and permissions held by the App rather than a person.

## The token (`HOMELAB_INFRA_TOKEN`)

- An environment secret of the portal's `homelab-infra` environment (deployments from `main` only). A fine-grained
  personal access token with access to `maseko-lucky-9/homelab-infra` only, permissions **Contents: read and write**
  and **Pull requests: read and write** (plus the mandatory **Metadata: read**). No Workflows permission, no other
  repository, no organisation or account permissions.
- If it is missing (unset, or a run that cannot read secrets) the first step fails the job with a clear error; the image
  is still published, only the PR is missing. It never succeeds silently.
- **What a leaked token can do:** push branches to homelab-infra and open, comment on, close and merge pull requests
  there. It has no direct access to the cluster, ArgoCD or Vault except via the unprotected `master` (below), none to
  any other repository, and it cannot read the portal's secrets. The intended boundary is that a human merges, so the
  worst case is noise and a malicious PR a reviewer must notice; review bump PRs as code (the diff must be one
  `digest:` line).
- **That boundary does not hold yet.** A fine-grained token's Contents write cannot be limited to some branches, and
  `master` in homelab-infra is not protected (`gh api repos/maseko-lucky-9/homelab-infra/branches/master` reported
  `"protected": false` on 2026-11-11; branch protection and rulesets on a private repository need GitHub Pro, the
  rulesets API answers 403). Until `master` requires a pull request, a leaked token can push straight to `master`, and
  ArgoCD auto-syncs every Application that tracks it: that is cluster write access by another route. Concretely:
  - The root app-of-apps (`argocd/monitoring-root.yaml`) syncs `argocd/applications` at `HEAD` with `prune: true` and
    `selfHeal: true`. A push can edit any included Application, or any app's manifests, to deploy cluster-scoped RBAC
    or privileged pods. Every child Application uses project `default` and the repository has no AppProject manifest
    ([UNVERIFIED] whether the live `default` project is restricted), so the reach is the ArgoCD application
    controller's own privileges: cluster-admin in a default install.
  - It can rewrite Vault policies and auth roles through `apps/vault-config-operator/manifests`.
  - It can cascade-delete PersistentVolumeClaims through prune and finalizers; homelab-infra's own comments on the
    umami Application record that this has happened before.
  - It can read the whole private homelab-infra repository, and with Contents write on an unprotected `master` it can
    force-push and rewrite history, so `git log` is not evidence after a leak; use the repository's activity view and
    the account's security log.
  - It cannot change homelab-infra's workflow files (no Workflows permission) [UNVERIFIED: not tested].
- **Durable options** (the owner decides; none is implemented here): branch protection on homelab-infra `master`
  (GitHub Pro); making homelab-infra public once it holds no secrets (protection is free there); moving the two digest
  pins into a small dedicated repository that only the `dispute-portal` Application reads, so the token never touches
  homelab-infra; a GitHub App token (short-lived, but still Contents write, so it needs protection too); a dedicated
  AppProject for dispute-portal (partial: it limits what the dispute-portal Application can deploy, not edits to other
  Applications). Until then: short expiry, rotation, the environment restriction, and revoking on any doubt.
- **Rotation:** set an expiry (90 days at most). To rotate: create the new token with the same scope, update the secret
  (`gh secret set HOMELAB_INFRA_TOKEN --env homelab-infra -R maseko-lucky-9/transaction-dispute-portal`, value on
  stdin), run `ci` by `workflow_dispatch` from `main` with `dry_run` false to prove it, then revoke the old token. On a
  suspected leak, revoke first, then review homelab-infra's activity view, open PRs and branches named `bump/`.

## Consequences

- A `workflow_dispatch` from `main` with `dry_run` false re-publishes the chosen halves for the current `main` commit
  and opens (or refreshes) their bump PRs. With `dry_run` true nothing is pushed and no PR is opened.
- A re-run of the job, or a second non-dry dispatch of the same commit, reuses the branch name: the branch belongs to
  this job and names one commit and one half, so it is force-pushed, and the open PR for it is reused with its body
  refreshed (a re-publish rebuilds the image and can change the digest).
- One failed `publish` leg makes `publish` as a whole `failure`, which blocks BOTH bump legs, including the half that
  did publish: that half's bump waits for a later push that publishes it again, a re-run of the failed jobs, or a
  dispatch of that half.
- Re-running an old run's `bump-homelab` job is harmless: it sees the newer open or merged bump PR and stops. It does
  not see a newer bump that was closed without merging, so after such a close an old re-run would open its PR again.
- The contract rule is stated in every PR, not detected: the job does not know whether the published range touched
  `docs/openapi.json`.
- The cross-repository steps (clone, push, PR, label, close) need the secret and cannot run locally; their script is
  tested against a local bare repository with a stub `gh`, and the first real proof is a dispatch run after merge.
