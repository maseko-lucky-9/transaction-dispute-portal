#!/usr/bin/env bash
# Prints the paths a git revision range changed, one per line, for scripts/changed-paths.sh.
# Usage: scripts/changed-files.sh <git diff revision arguments>, e.g. origin/main...HEAD or <before> <sha>.
# Two pitfalls of a plain `git diff --name-only` would make the classifier under-build, so it is avoided:
#   - without -z, git C-quotes a path with a non-ASCII byte, tab, newline, quote or backslash ("backend/src/caf\303\251.cs"),
#     which matches no rule;
#   - with rename detection only the new path is listed, so moving a file OUT of backend/src (or frontend, or the
#     Dockerfile) would look like a change to an unrelated path.
# So: NUL-delimited output (never quoted) and no rename detection (both the old and the new path are listed). A name
# that contains a newline splits into fragments; the first keeps the directory prefix, and the classifier only ever
# turns things on, so the worst case is over-building. Exits non-zero if git fails (the caller then builds everything).
set -uo pipefail
git diff --no-renames -z --name-only "$@" | tr '\0' '\n'
exit "${PIPESTATUS[0]}"
