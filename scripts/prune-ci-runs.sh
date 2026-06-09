#!/usr/bin/env bash
set -euo pipefail

# Deletes every completed workflow run except the current one.
repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
keep="${KEEP_RUN_ID:-}"
gh run list --status completed --limit 400 --json databaseId --jq '.[].databaseId' | while read -r id; do
	if [[ ${id} != "${keep}" ]]; then
		gh api -X DELETE "repos/${repo}/actions/runs/${id}" > /dev/null 2>&1
	fi
done

echo ok
