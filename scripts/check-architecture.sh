#!/usr/bin/env bash
# Enforces the four-tier dependency direction mechanically.
#
# The tiers are a doctrine (.sde/architecture/FOUR-TIER-ARCHITECTURE.md), and a
# doctrine nobody checks is a comment. This script makes Tier 1's isolation a
# build failure rather than a code-review habit.
#
# Dependencies point downward only:
#
#   Host / External Effects   (Vigila.Host.*)
#           |
#   Application / Orchestration (Vigila.Application)
#           |
#   Transition / Domain Execution (Vigila.Transition)
#           |
#   Semantic Model (Vigila.Semantic)
#
# Requirements: VIG-GOV-015 (domain independence), VIG-GOV-007 (SDE).
set -uo pipefail

cd "$(dirname "$0")/.."

fail=0
report() { printf '  FAIL  %s\n' "$1"; fail=1; }

echo "Checking four-tier dependency direction..."

# Tier 1 may reference FSharp.Core and nothing else. Any PackageReference is a
# violation, because Directory.Build.props already supplies FSharp.Core.
semantic="src/Vigila.Semantic/Vigila.Semantic.fsproj"
if grep -q 'PackageReference' "$semantic"; then
  report "$semantic declares a PackageReference; Tier 1 takes FSharp.Core only."
fi
if grep -q 'ProjectReference' "$semantic"; then
  report "$semantic declares a ProjectReference; Tier 1 depends on nothing."
fi

# Tier 2 may reference Tier 1 only, and no packages.
trans="src/Vigila.Transition/Vigila.Transition.fsproj"
if grep -q 'PackageReference' "$trans"; then
  report "$trans declares a PackageReference; Tier 2 takes FSharp.Core only."
fi
for ref in $(grep -o 'ProjectReference Include="[^"]*"' "$trans" | sed 's/.*"\(.*\)"/\1/'); do
  case "$ref" in
    */Vigila.Semantic/*) ;;
    *) report "$trans references $ref; Tier 2 may reference Tier 1 only." ;;
  esac
done

# Neither Tier 1 nor Tier 2 may name a forbidden dependency in source. Aegis is
# on this list: it belongs at a boundary, and these tiers have none.
forbidden='Aegis|System\.Net|System\.IO|HttpClient|Octokit|localStorage|System\.Text\.Json'
for tier in src/Vigila.Semantic src/Vigila.Transition; do
  while IFS= read -r hit; do
    [ -n "$hit" ] && report "$hit"
  done < <(grep -rnE "^[^/]*\b($forbidden)" "$tier" --include='*.fs' \
             | grep -vE '^\s*///' | sed 's/:.*//' | sort -u \
             | sed 's|$| references a forbidden dependency (one of: Aegis, System.Net, System.IO, HttpClient, Octokit, localStorage, System.Text.Json).|')
done

# Tier 3 and 4 must not be referenced by anything below them.
for lower in "$semantic" "$trans"; do
  if grep -qE 'Vigila\.(Application|Host)' "$lower"; then
    report "$lower references a higher tier; dependencies point downward only."
  fi
done

if [ "$fail" -eq 0 ]; then
  echo "  OK    dependencies point downward only; Tier 1 and Tier 2 are clean."
fi
exit "$fail"
