#!/usr/bin/env bash
set -euo pipefail

# Smoke-tests the four commands' dispatch + exit-code contract.
tool="${1:-dotnet bin/Release/net10.0/lintmax-cs.dll}"
fail=0

assert() {
	local label="$1" expected="$2" actual="$3"
	if [ "$expected" != "$actual" ]; then
		echo "FAIL ${label}: expected '${expected}' got '${actual}'" >&2
		fail=1
	fi
}

ver="$($tool version)"
if ! printf '%s' "$ver" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
	echo "FAIL version-format: got '${ver}'" >&2
	fail=1
fi

set +e
$tool bogus >/dev/null 2>&1
assert "unknown-exit" "2" "$?"
$tool version >/dev/null 2>&1
assert "version-exit" "0" "$?"
set -e

rules_count="$($tool rules 2>/dev/null | wc -l | tr -d ' ')"
if [ "$rules_count" -lt 100 ]; then
	echo "FAIL rules: expected >=100 got ${rules_count}" >&2
	fail=1
fi

if [ "$fail" -eq 0 ]; then
	echo ok
else
	exit 1
fi
