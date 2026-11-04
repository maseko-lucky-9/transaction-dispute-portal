# Time log (commit span, not hours worked)

Derived only from commit timestamps. It says when commits were made, not how long anyone worked: it includes waiting
for CI, review, and time with no commits, and AI agents did most of the typing. Do not read it as effort or billable
time.

Source: `git log` on the squash commits on `main` (merge time) and on each PR's branch ref in `origin` (first and last
commit, commits since the merge-base with `main`). All times are 2026-11-04, UTC+02:00 (SAST), as recorded by git.

| PR | Branch | Commits on branch | First commit | Last commit | Merged to `main` |
|---|---|---|---|---|---|
| (initial) | `main` | 1 | 09:57:28 | 09:57:28 | 09:57:28 (`c8267ee`) |
| #1 walking skeleton | `feat/walking-skeleton` | 14 | 10:42:11 | 11:43:29 | 11:49:02 (`f6678a3`) |
| #8 spec and design | `docs/spec-and-design` | 2 | 10:17:01 | 11:50:00 | 12:09:43 (`0a5a96b`) |
| #11 transactions | `feat/transactions` | 7 | 12:37:26 | 13:08:00 | 13:20:04 (`de11c8e`) |
| #13 disputes | `feat/disputes` | 18 | 14:31:41 | 15:50:11 | 15:58:46 (`6685906`) |
| #14 agent | `feat/agent` | 3 | 16:36:06 | 16:50:35 | 16:56:39 (`e354afd`) |
| #15 tick tasks | `docs/tasks-progress` | 1 | 17:05:48 | 17:05:48 | 17:10:37 (`3295dd0`) |
| #16 ops hardening | `feat/ops-hardening` | 3 | 17:26:33 | 17:29:53 | 17:41:13 (`15c3b67`) |

Notes
- #16: GitHub's PR commit list (`gh pr view 16 --json commits`) reports 4 commits with the last at 17:35:54, while the
  branch ref in `origin` gave 3 commits ending 17:29:53. The fourth commit is the merge commit made by
  `gh pr update-branch` (`936f20e`) to bring `main` (docs PR #15) into the branch, which branch protection requires
  before merging; it adds no code.
- The merge-to-merge order is not the order of first commits: #8 (docs) started before #1 finished, as the plan intended.
- Everything on this table happened inside one calendar day. The plan scheduled the work across 2026-11-04 to 11-10;
  PR7 (this change, T060) is not in the table because it is not merged.
- No hours are stated because none were recorded.
