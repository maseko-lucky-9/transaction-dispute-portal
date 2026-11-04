#!/usr/bin/env bash
# Constrain, don't ban: EF Core + Npgsql is the data layer. Print offenders and exit 1; silent when clean.
set -uo pipefail
cd "$(dirname "$0")/.."
deny='Sqlite|SqlClient|EntityFrameworkCore\.InMemory|MediatR|NHibernate|Dapper|MySql|Oracle'
hits=$(find . \( -name .git -o -name node_modules -o -name bin -o -name obj -o -name .specify -o -name .claude \) -prune -o \
  \( -name '*.csproj' -o -name '*.props' -o -name '*.targets' \) -type f -print |
  while read -r file; do grep -EiH "$deny" "$file"; done)
if [ -n "$hits" ]; then
  echo "banned package reference (see docs/decisions):"
  echo "$hits"
  exit 1
fi
