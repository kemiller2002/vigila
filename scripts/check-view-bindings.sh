#!/usr/bin/env bash
# Every data-* binding in the HTML must name a key the engine actually
# projects.
#
# Limen fills bindings from the engine's ViewState by name. A key that the
# engine stops projecting, or a binding misspelled in the HTML, fails silently:
# the element simply never updates. This check turns that into a build failure.
#
# Requirements: VIG-GOV-010 (HTML stays structural), VIG-GOV-013.
set -uo pipefail

cd "$(dirname "$0")/.."

html="web/index.html"
projection="src/Vigila.Application/Dispatch.fs"
fail=0

echo "Checking view bindings against the engine's projection..."

# data-text, data-if, data-each and data-bind-* all name a projection key.
# data-event names a command, not a key, so it is checked separately.
keys=$(grep -oE 'data-(text|if|each|bind-[a-z]+)="[A-Za-z]+"' "$html" \
       | sed 's/.*="\(.*\)"/\1/' | sort -u)

# Keys projected per item inside data-each are fields of the item, not
# top-level view keys; both are declared in the same file, so one grep covers
# them.
for key in $keys; do
  if ! grep -qE "(^|[^A-Za-z])$key( |=)" "$projection"; then
    printf '  FAIL  data-* binding "%s" is not projected by %s\n' "$key" "$projection"
    fail=1
  fi
done

# Every data-event must map to a command the engine understands.
for name in $(grep -oE 'data-event="[A-Za-z]+"' "$html" | sed 's/.*="\(.*\)"/\1/' | sort -u); do
  if ! grep -q "\"$name\"" "$projection"; then
    printf '  FAIL  data-event "%s" has no case in eventToCommand\n' "$name"
    fail=1
  fi
done

if [ "$fail" -eq 0 ]; then
  echo "  OK    every binding and event in $html is known to the engine."
fi
exit "$fail"
