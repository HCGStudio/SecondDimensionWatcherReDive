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

tag_name_prefix="pre-${base_version}."
git_ref_prefix="refs/tags/${tag_name_prefix}"
if ! remote_tags=$(git ls-remote --refs --tags "$remote" "${git_ref_prefix}*"); then
  echo "could not list prerelease tags from $remote" >&2
  exit 1
fi

max_sequence=0
consider_tag_name() {
  local tag_name="$1"
  local sequence
  local sequence_value

  [[ "$tag_name" == "$tag_name_prefix"* ]] || return 0
  sequence="${tag_name#"$tag_name_prefix"}"
  [[ "$sequence" =~ ^(0|[1-9][0-9]*)$ ]] || return 0

  if [ "${#sequence}" -gt 5 ] || \
     { [ "${#sequence}" -eq 5 ] && [[ "$sequence" > "$max_component" ]]; }; then
    echo "prerelease sequence is outside the supported range: $tag_name" >&2
    exit 1
  fi

  sequence_value=$((10#$sequence))
  if [ "$sequence_value" -gt "$max_sequence" ]; then
    max_sequence="$sequence_value"
  fi
}

while IFS=$'\t' read -r _object_id ref_name; do
  [ -n "${ref_name:-}" ] || continue
  [[ "$ref_name" == refs/tags/* ]] || continue
  consider_tag_name "${ref_name#refs/tags/}"
done <<< "$remote_tags"

if [ -n "${PRERELEASE_TAG_NAMES_FILE:-}" ]; then
  if [ ! -r "$PRERELEASE_TAG_NAMES_FILE" ]; then
    echo "prerelease tag names file is not readable: $PRERELEASE_TAG_NAMES_FILE" >&2
    exit 1
  fi
  while IFS= read -r tag_name || [ -n "$tag_name" ]; do
    consider_tag_name "$tag_name"
  done < "$PRERELEASE_TAG_NAMES_FILE"
fi

if [ "$max_sequence" -ge "$max_component" ]; then
  echo "prerelease sequence exhausted for $base_version" >&2
  exit 1
fi

printf '%s.%d\n' "$base_version" "$((max_sequence + 1))"
