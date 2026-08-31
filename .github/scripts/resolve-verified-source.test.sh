#!/usr/bin/env bash

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_script="$script_dir/resolve-verified-source.sh"
verified_sha=0123456789abcdef0123456789abcdef01234567
dispatch_sha=89abcdef0123456789abcdef0123456789abcdef

assert_output() {
  local expected="$1"
  shift
  local actual
  actual=$(env "$@" "$source_script")
  if [ "$actual" != "$expected" ]; then
    echo "expected $expected, got $actual" >&2
    exit 1
  fi
}

assert_rejected() {
  local scenario="$1"
  shift
  if env "$@" "$source_script" >/dev/null 2>&1; then
    echo "expected publication source to be rejected: $scenario" >&2
    exit 1
  fi
}

valid_run=(
  EVENT_NAME=workflow_run
  CONCLUSION=success
  RUN_EVENT=push
  DEFAULT_BRANCH=main
  HEAD_BRANCH=main
  REPOSITORY=HCGStudio/SecondDimensionWatcherReDive
  HEAD_REPOSITORY=HCGStudio/SecondDimensionWatcherReDive
  VERIFIED_SHA="$verified_sha"
)

assert_output "$verified_sha" "${valid_run[@]}"
assert_output "$dispatch_sha" \
  EVENT_NAME=workflow_dispatch \
  DEFAULT_BRANCH=main \
  DISPATCH_REF=refs/heads/main \
  DISPATCH_SHA="$dispatch_sha"

assert_rejected "pull request verification" \
  "${valid_run[@]}" RUN_EVENT=pull_request
assert_rejected "failed verification" \
  "${valid_run[@]}" CONCLUSION=failure
assert_rejected "non-default branch" \
  "${valid_run[@]}" HEAD_BRANCH=feature
assert_rejected "fork verification" \
  "${valid_run[@]}" HEAD_REPOSITORY=someone/fork
assert_rejected "manual feature-branch run" \
  EVENT_NAME=workflow_dispatch \
  DEFAULT_BRANCH=main \
  DISPATCH_REF=refs/heads/feature \
  DISPATCH_SHA="$dispatch_sha"
assert_rejected "unknown event" EVENT_NAME=pull_request

echo "verified source boundary tests passed"
