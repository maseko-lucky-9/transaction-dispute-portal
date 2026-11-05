#!/usr/bin/env bash
# Backend line-coverage gate. Runs the unit tests and then the integration tests (Docker needed) against the instrumented
# DisputePortal.Api and DisputePortal.Domain, merges the two runs, and fails if total line coverage is below the minimum.
# Needs `dotnet build -c Release` first (CI builds once). Output: coverage/coverage.cobertura.xml.
#
# The minimum is the measured total line coverage rounded down to the nearest 5, capped at 80. Measured 91.0% (2572 of
# 2826 lines, unit and integration merged): 90 rounded down, so 80. A floor that catches a real slide without turning
# every small PR into a coverage chore. COVERAGE_MIN overrides it for a local experiment only.
set -euo pipefail
cd "$(dirname "$0")/.."

MIN_LINE_COVERAGE="${COVERAGE_MIN:-80}"
OUT="$PWD/coverage"
mkdir -p "$OUT"

# Only the two shipped assemblies (not tests or packages); the generated migrations would only dilute the number.
# %2C is the comma MSBuild would otherwise split the list on.
common=(
  -c Release --no-build --nologo
  /p:CollectCoverage=true
  "/p:Include=[DisputePortal.Api]*%2C[DisputePortal.Domain]*"
  "/p:ExcludeByFile=**/Migrations/**/*.cs"
)

dotnet test backend/tests/DisputePortal.UnitTests "${common[@]}" \
  /p:CoverletOutputFormat=json "/p:CoverletOutput=$OUT/unit/"

dotnet test backend/tests/DisputePortal.IntegrationTests "${common[@]}" \
  "/p:MergeWith=$OUT/unit/coverage.json" \
  /p:CoverletOutputFormat=cobertura "/p:CoverletOutput=$OUT/" \
  "/p:Threshold=$MIN_LINE_COVERAGE" /p:ThresholdType=line /p:ThresholdStat=total

echo "line coverage gate passed (minimum ${MIN_LINE_COVERAGE}%)"
