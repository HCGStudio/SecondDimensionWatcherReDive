#!/usr/bin/env bash

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
version_script="$script_dir/next-prerelease-version.sh"
fixture_root=$(mktemp -d)
trap 'rm -rf -- "$fixture_root"' EXIT

remote="$fixture_root/remote.git"
seed="$fixture_root/seed"
git init --bare --quiet "$remote"
git init --quiet "$seed"
git -C "$seed" config user.email ci@example.invalid
git -C "$seed" config user.name CI
git -C "$seed" commit --allow-empty --quiet -m fixture
git -C "$seed" remote add origin "$remote"

assert_version() {
  local expected="$1"
  local base_version="$2"
  local actual
  actual=$(cd "$seed" && "$version_script" "$base_version")
  if [ "$actual" != "$expected" ]; then
    echo "expected $expected, got $actual" >&2
    exit 1
  fi
}

assert_version 2.2.0.1 2.2.0

for tag in \
  pre-2.2.0.7 \
  pre-2.2.0.120 \
  pre-2.2.0.001 \
  pre-2.2.0.invalid \
  pre-2.3.0.999; do
  git -C "$seed" tag "$tag"
done
git -C "$seed" push --quiet origin --tags
assert_version 2.2.0.121 2.2.0

git -C "$seed" tag pre-3.0.0.65534
git -C "$seed" push --quiet origin pre-3.0.0.65534
if (cd "$seed" && "$version_script" 3.0.0 >/dev/null 2>&1); then
  echo "expected an exhausted prerelease sequence to fail" >&2
  exit 1
fi

if (cd "$seed" && "$version_script" 2.02.0 >/dev/null 2>&1); then
  echo "expected a non-canonical base version to fail" >&2
  exit 1
fi

echo "prerelease version tests passed"
