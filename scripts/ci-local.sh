#!/bin/sh
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
cd "${root}"
DOTNET_ROOT=$(dotnet --list-runtimes | head -1 | sed 's#.*\[\(.*\)/shared/.*#\1#')
export DOTNET_ROOT
work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT
dotnet pack -c Release -o "${work}/pack" > "${work}/pack.log" 2>&1 || {
  grep -E 'error|warning' "${work}/pack.log" | sort -u >&2
  echo 'pack failed' >&2
  exit 1
}
dotnet tool install --tool-path "${work}/tools" lintmax-cs --add-source "${work}/pack" > /dev/null
"${work}/tools/lintmax-cs" check
bash scripts/smoke.sh "${work}/tools/lintmax-cs" > "${work}/smoke.log" 2>&1 || {
  tail -20 "${work}/smoke.log" >&2
  echo 'smoke failed' >&2
  exit 1
}
dotnet run --project tests/VersionControl.Tests.csproj > "${work}/test.log" 2>&1 || {
  tail -20 "${work}/test.log" >&2
  echo 'tests failed' >&2
  exit 1
}
echo ok
