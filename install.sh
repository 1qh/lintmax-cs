#!/usr/bin/env bash
set -euo pipefail

# Installs or updates lintmax-cs as a .NET global tool.
version="${1:-}"
args=(lintmax-cs)
if [ -n "$version" ]; then
	args+=(--version "$version")
fi
if [ -n "${LINTMAX_SOURCE:-}" ]; then
	args+=(--add-source "$LINTMAX_SOURCE")
fi

if dotnet tool list -g | grep -q lintmax-cs; then
	dotnet tool update -g "${args[@]}" >/dev/null 2>&1
else
	dotnet tool install -g "${args[@]}" >/dev/null 2>&1
fi

echo ok
