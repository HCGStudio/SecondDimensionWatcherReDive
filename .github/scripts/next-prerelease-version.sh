#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <major.minor.patch>" >&2
  exit 2
fi

base_version="$1"
remote="${PRERELEASE_TAG_REMOTE:-origin}"
max_component=65534

if [[ ! "$base_version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "invalid base version: $base_version" >&2
  exit 2
fi

IFS=. read -r major minor patch <<< "$base_version"
for component in "$major" "$minor" "$patch"; do
  if [ "${#component}" -gt 5 ] || \
     { [ "${#component}" -eq 5 ] && [[ "$component" > "$max_component" ]]; }; then
    echo "base version component exceeds $max_component: $base_version" >&2
    exit 2
  fi
done

tag_prefix="refs/tags/pre-${base_version}."
if ! remote_tags=$(git ls-remote --refs --tags "$remote" "${tag_prefix}*"); then
  echo "could not list prerelease tags from $remote" >&2
  exit 1
fi

max_sequence=0
while IFS=$'\t' read -r _object_id ref_name; do
  [ -n "${ref_name:-}" ] || continue
  [[ "$ref_name" == "$tag_prefix"* ]] || continue
  sequence="${ref_name#"$tag_prefix"}"
  [[ "$sequence" =~ ^(0|[1-9][0-9]*)$ ]] || continue

  if [ "${#sequence}" -gt 5 ] || \
     { [ "${#sequence}" -eq 5 ] && [[ "$sequence" > "$max_component" ]]; }; then
    echo "prerelease sequence is outside the supported range: $ref_name" >&2
    exit 1
  fi

  sequence_value=$((10#$sequence))
  if [ "$sequence_value" -gt "$max_sequence" ]; then
    max_sequence="$sequence_value"
  fi
done <<< "$remote_tags"

if [ "$max_sequence" -ge "$max_component" ]; then
  echo "prerelease sequence exhausted for $base_version" >&2
  exit 1
fi

printf '%s.%d\n' "$base_version" "$((max_sequence + 1))"
