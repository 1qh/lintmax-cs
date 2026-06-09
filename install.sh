#!/usr/bin/env bash
set -euo pipefail

# Installs lintmax-cs as a .NET global tool from NuGet (or a local --source).
version="${1:-latest}"
source_arg=""
if [ "${LINTMAX_SOURCE:-}" != "" ]; then
	source_arg="--add-source ${LINTMAX_SOURCE}"
fi

if dotnet tool list -g | grep -q "lintmax-cs"; then
	# shellcheck disable=SC2086
	dotnet tool update -g lintmax-cs ${source_arg} >/dev/null 2>&1
else
	# shellcheck disable=SC2086
	dotnet tool install -g lintmax-cs ${source_arg} >/dev/null 2>&1
fi

echo ok
