#!/usr/bin/env bash
set -euo pipefail

# Smoke-tests the four commands' dispatch + exit-code contract.
tool="${1:-dotnet bin/Release/net10.0/lintmax-cs.dll}"
fail=0

ver="$(${tool} version)"
if ! printf '%s' "${ver}" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
	echo "FAIL version-format: got '${ver}'" >&2
	fail=1
fi

rc=0
${tool} bogus > /dev/null 2>&1 || rc=$?
if [[ ${rc} != "2" ]]; then
	echo "FAIL unknown-exit: got ${rc}" >&2
	fail=1
fi

rules_count="$(${tool} rules 2> /dev/null | wc -l | tr -d ' ')"
if [[ ${rules_count} -lt 100 ]]; then
	echo "FAIL rules: expected >=100 got ${rules_count}" >&2
	fail=1
fi

if [[ ${fail} -eq 0 ]]; then
	echo ok
else
	exit 1
fi
