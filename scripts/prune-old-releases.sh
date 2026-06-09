#!/usr/bin/env bash
set -euo pipefail

# Keeps only the highest-version release + tag; deletes all older ones.
keep="$(gh release list --json tagName --jq '.[].tagName' | sort -V | tail -n1)"
gh release list --json tagName --jq '.[].tagName' | while read -r tag; do
	if [[ ${tag} != "${keep}" ]]; then
		gh release delete "${tag}" --cleanup-tag --yes > /dev/null 2>&1
	fi
done

echo ok
