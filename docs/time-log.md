# Time log (commit span, not hours worked)

Derived only from commit timestamps. It says when commits were made, not how long anyone worked: it includes waiting
for CI, review, and time with no commits, and AI agents did most of the typing. Do not read it as effort or billable
time.

Source: the GitHub API for each merged pull request (`gh pr list --state merged` for the merge time and squash commit,
`GET /pulls/{n}/commits` for the first and last commit's committer date). Times are UTC+02:00 (SAST), shown as
month-day hour:minute in 2026. Regenerated on 2026-11-16 at `main` `74b1d94`.

The initial commit `c8267ee` was made on 11-04 at 09:57, before any pull request.

| PR | Title | Branch | Commits | First commit | Last commit | Merged to `main` |
|---|---|---|---|---|---|---|
| #1 | feat: walking skeleton (compose stack, BFF auth via Keycloak, Vault, CI, E2E login) | `feat/walking-skeleton` | 14 | 11-04 10:42 | 11-04 11:43 | 11-04 11:49 (`f6678a3`) |
| #8 | docs: spec-kit constitution, spec, plan, tasks, ADRs and system design | `docs/spec-and-design` | 2 | 11-04 11:49 | 11-04 11:50 | 11-04 12:09 (`0a5a96b`) |
| #11 | feat: transactions slice (fail-closed owner filter, keyset paging, web page) | `feat/transactions` | 7 | 11-04 12:37 | 11-04 13:08 | 11-04 13:20 (`de11c8e`) |
| #13 | feat: disputes (idempotent submit, history, detail, withdraw) | `feat/disputes` | 18 | 11-04 14:31 | 11-04 15:50 | 11-04 15:58 (`6685906`) |
| #14 | feat(api): agent queue and dispute review | `feat/agent` | 3 | 11-04 16:36 | 11-04 16:50 | 11-04 16:56 (`e354afd`) |
| #15 | docs(docs): tick the tasks shipped in PR3 to PR5 | `docs/tasks-progress` | 1 | 11-04 17:05 | 11-04 17:05 | 11-04 17:10 (`3295dd0`) |
| #16 | feat(obs): ops hardening, coverage gate, agent E2E | `feat/ops-hardening` | 4 | 11-04 17:26 | 11-04 17:35 | 11-04 17:41 (`15c3b67`) |
| #17 | docs(docs): final criteria evidence, runbook, threat model, tests | `docs/final` | 3 | 11-04 17:49 | 11-04 17:58 | 11-04 18:35 (`b01abad`) |
| #18 | docs(docs): correct the HSTS claim in ADR-008 | `docs/adr-008-hsts` | 1 | 11-04 18:46 | 11-04 18:46 | 11-04 18:50 (`0e882b3`) |
| #19 | chore(repo): hardening pass, audit, evidence, CodeQL fix | `chore/hardening-pass` | 11 | 11-04 19:39 | 11-04 19:49 | 11-04 19:57 (`4dc67d6`) |
| #20 | feat(tests): k6 p95 smoke test and measured SLO results | `feat/k6-docs` | 4 | 11-04 20:00 | 11-04 20:11 | 11-04 20:18 (`ebcf6dd`) |
| #21 | feat(bff): end sessions on oidc back-channel logout | `feat/backchannel-logout` | 13 | 11-04 20:50 | 11-04 21:57 | 11-04 23:11 (`bdc98a2`) |
| #22 | refactor(repo): split backend and frontend into their own folders | `refactor/backend-frontend-layout` | 2 | 11-05 19:26 | 11-05 19:33 | 11-05 19:39 (`d43d325`) |
| #24 | ci(ci): run and publish only the half a change touches | `ci/path-scoped-pipeline` | 3 | 11-05 20:18 | 11-05 20:42 | 11-05 20:46 (`82325b9`) |
| #25 | feat(api): serve swagger ui under /api behind a flag | `feat/api-docs-ui` | 2 | 11-05 20:36 | 11-05 20:56 | 11-05 21:01 (`d98d129`) |
| #27 | test(tests): every endpoint answers a database outage | `test/endpoint-outage-2` | 3 | 11-05 21:08 | 11-05 21:09 | 11-05 21:13 (`56e46b5`) |
| #28 | feat(api): serialise a customer's submits with an advisory lock | `feat/dispute-cap-lock` | 4 | 11-05 21:11 | 11-05 21:31 | 11-05 21:36 (`781426e`) |
| #29 | docs(docs): bring the README and threat model up to date | `docs/readme-after-series` | 1 | 11-05 21:43 | 11-05 21:43 | 11-05 21:46 (`1d291c6`) |
| #30 | feat(web): render nginx upstreams from DP_* variables (ADR-015) | `feat/web-nginx-templated-upstreams` | 2 | 11-08 02:35 | 11-08 02:36 | 11-08 02:42 (`9b6bdcc`) |
| #31 | feat(web): template X-Forwarded-Proto via DP_FORWARDED_PROTO | `feat/web-forwarded-proto` | 2 | 11-11 10:35 | 11-11 10:37 | 11-11 10:42 (`3f7dd43`) |
| #32 | feat(api): log in to Vault with Kubernetes auth as well as AppRole | `feat/vault-kubernetes-auth` | 4 | 11-11 10:40 | 11-11 10:53 | 11-11 10:58 (`47df112`) |
| #33 | fix(vault): pin Vault's server certificate and log the failure cause | `fix/vault-pinned-server-cert` | 2 | 11-11 12:02 | 11-11 12:21 | 11-11 12:27 (`5a64276`) |
| #43 | feat(ci): open a homelab-infra digest-bump PR after each publish | `feat/homelab-image-bump-prs` | 3 | 11-11 20:05 | 11-11 20:45 | 11-11 20:49 (`ca9e89e`) |
| #44 | chore(web): add a meta description to the page | `chore/web-meta-description` | 1 | 11-14 12:53 | 11-14 12:53 | 11-14 12:58 (`d422c54`) |
| #45 | docs(docs): record the accepted unprotected-master risk in ADR-017 | `docs/adr-017-accepted-risk` | 1 | 11-14 13:45 | 11-14 13:45 | 11-14 13:48 (`6596faf`) |
| #46 | feat(api): let swagger ui send the csrf header on every operation | `feat/api-docs-security-requirement` | 1 | 11-14 15:16 | 11-14 15:16 | 11-14 15:20 (`56339b8`) |
| #48 | feat(web): add capitec brand tokens and colour-coded status pills | `feat/brand-tokens` | 4 | 11-15 14:30 | 11-15 14:43 | 11-15 15:37 (`fd5fcae`) |
| #49 | feat(web): restyle the shell, tables and filter bar as crimson pill | `feat/brand-shell` | 1 | 11-15 15:40 | 11-15 15:40 | 11-15 15:53 (`a00e4a6`) |
| #50 | feat(web): redesign the landing and home pages in the crimson pill look | `feat/brand-landing` | 3 | 11-15 15:59 | 11-15 16:13 | 11-15 16:19 (`2c9a6a6`) |
| #51 | feat(web): add agent status links and an icon-only lifecycle stepper | `feat/brand-agent` | 3 | 11-15 16:00 | 11-15 16:19 | 11-15 16:49 (`241b016`) |
| #52 | feat(auth): add a Capitec-branded Keycloak login theme | `feat/keycloak-theme` | 1 | 11-15 18:14 | 11-15 18:14 | 11-15 18:54 (`6311852`) |
| #47 | docs(docs): add five capitec brand mock-up directions | `docs/brand-mockups` | 3 | 11-15 14:03 | 11-15 20:58 | 11-15 20:59 (`6be6b85`) |
| #53 | docs(docs): explain the openapi publish gating and auth-origin 404 | `docs/explain-openapi-publish-and-auth-origin-404` | 1 | 11-15 21:33 | 11-15 21:33 | 11-15 21:35 (`7dd0d54`) |
| #54 | ci(ci): fail when the keycloak login theme drifts from homelab-infra | `claude/strange-thompson-37cea3` | 2 | 11-15 21:41 | 11-15 21:45 | 11-15 21:56 (`677bf18`) |
| #55 | fix(web): outline the reject action and colour inline links blue gem | `fix/agent-reject-variant-and-link-colour` | 1 | 11-16 11:15 | 11-16 11:15 | 11-16 11:31 (`a7f06cf`) |
| #56 | feat(compose): seed keycloak demo users idempotently and add two agents | `feat/compose-demo-agents` | 2 | 11-16 11:39 | 11-16 11:42 | 11-16 12:01 (`23b7ada`) |
| #57 | feat(web): show recent transactions on home and let agents read reasons | `feat/home-transactions-and-reasons` | 2 | 11-16 12:06 | 11-16 12:07 | 11-16 12:36 (`b747048`) |
| #58 | feat(api): show agents the disputed customer's recent transactions | `feat/agent-customer-transactions` | 2 | 11-16 12:56 | 11-16 12:57 | 11-16 13:03 (`df018ea`) |
| #59 | feat(api): assign disputes to agents with claim, take next and release | `feat/agent-assignment` | 1 | 11-16 14:39 | 11-16 14:39 | 11-16 14:55 (`91e6f60`) |
| #60 | feat(web): add the agent workbench with take next and release | `feat/agent-workbench` | 3 | 11-16 15:32 | 11-16 15:47 | 11-16 15:57 (`1f1f1a6`) |
| #61 | feat(db): require an assignee exactly while a dispute is under review | `feat/assignment-invariant` | 1 | 11-16 16:33 | 11-16 16:33 | 11-16 16:52 (`13a50f7`) |
| #62 | fix(auth): round the login card's accent bar and bust stale theme caches | `fix/keycloak-login-theme` | 1 | 11-16 17:47 | 11-16 17:47 | 11-16 17:53 (`74b1d94`) |

Dependabot: 13 pull requests merged, 11-05 to 11-14; not listed.

Notes
- 42 pull requests by the owner's account and 13 by Dependabot. Commits were made on seven calendar days: 11-04, 11-05,
  11-08, 11-11, 11-14, 11-15 and 11-16. The plan scheduled the work for 11-04 to 11-10; everything merged from 11-11
  onwards came after that date.
- A commit count includes merge commits that `gh pr update-branch` made to bring `main` into a branch, which branch
  protection requires before merging; they add no code.
- Committer dates move when a branch is rebased, so a first commit can read later than the work began. The earlier
  version of this log, read from branch author dates, put #8's first commit at 10:17, before #1 had merged.
- No hours are stated because none were recorded.
