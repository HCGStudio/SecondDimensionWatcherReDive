#!/usr/bin/env bash

set -euo pipefail

case "${EVENT_NAME:-}" in
  workflow_run)
    [ "${CONCLUSION:-}" = success ] || {
      echo "upstream verification did not succeed" >&2
      exit 1
    }
    [ "${RUN_EVENT:-}" = push ] || {
      echo "only a push verification may publish" >&2
      exit 1
    }
    [ "${HEAD_BRANCH:-}" = "${DEFAULT_BRANCH:-}" ] || {
      echo "verified branch is not the default branch" >&2
      exit 1
    }
    [ "${HEAD_REPOSITORY:-}" = "${REPOSITORY:-}" ] || {
      echo "verified source is not this repository" >&2
      exit 1
    }
    commit_sha="${VERIFIED_SHA:-}"
    ;;
  workflow_dispatch)
    [ "${DISPATCH_REF:-}" = "refs/heads/${DEFAULT_BRANCH:-}" ] || {
      echo "manual publication must run from the default branch" >&2
      exit 1
    }
    commit_sha="${DISPATCH_SHA:-}"
    ;;
  *)
    echo "unsupported publication event: ${EVENT_NAME:-<unset>}" >&2
    exit 1
    ;;
esac

if [[ ! "$commit_sha" =~ ^[0-9a-f]{40}$ ]]; then
  echo "verified source is not a full commit SHA" >&2
  exit 1
fi

printf '%s\n' "$commit_sha"
