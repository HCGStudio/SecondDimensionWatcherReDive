#!/usr/bin/env bash
set -euo pipefail

fuse_binary="${1:-}"
if [[ -z "$fuse_binary" || ! -x "$fuse_binary" ]]; then
    echo "usage: $0 /path/to/sdwfuse" >&2
    exit 2
fi

skip_mount() {
    local reason="$1"
    if [[ "${SDW_REQUIRE_FUSE_MOUNT:-0}" == "1" ]]; then
        echo "FUSE mount smoke is required but unavailable: $reason" >&2
        exit 1
    fi
    echo "::notice title=FUSE mount smoke skipped::$reason"
    exit 0
}

[[ "$(uname -s)" == "Linux" ]] || skip_mount "Linux is required"
[[ -c /dev/fuse ]] || skip_mount "/dev/fuse is not exposed by this runner"
[[ -r /dev/fuse && -w /dev/fuse ]] || skip_mount "/dev/fuse is not accessible"
command -v fusermount3 >/dev/null 2>&1 || skip_mount "fusermount3 is not installed"
command -v mountpoint >/dev/null 2>&1 || skip_mount "mountpoint is not installed"

smoke_root="$(mktemp -d)"
mount_dir="$smoke_root/mount"
server_log="$smoke_root/server.log"
fuse_log="$smoke_root/fuse.log"
mkdir -p "$mount_dir"
server_pid=""
fuse_pid=""

cleanup() {
    set +e
    if mountpoint -q "$mount_dir"; then
        fusermount3 -u "$mount_dir"
    fi
    if [[ -n "$fuse_pid" ]]; then
        kill "$fuse_pid" 2>/dev/null
        wait "$fuse_pid" 2>/dev/null
    fi
    if [[ -n "$server_pid" ]]; then
        kill "$server_pid" 2>/dev/null
        wait "$server_pid" 2>/dev/null
    fi
    rm -rf "$smoke_root"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

node deployments/ci/fuse-mount-smoke.mjs >"$server_log" 2>&1 &
server_pid="$!"
for _ in {1..50}; do
    if curl --silent --fail \
        --user smoke-user:smoke-token \
        "http://127.0.0.1:15097/api/vfs/stat?path=/" >/dev/null; then
        break
    fi
    sleep 0.1
done
curl --silent --fail \
    --user smoke-user:smoke-token \
    "http://127.0.0.1:15097/api/vfs/stat?path=/" >/dev/null

"$fuse_binary" mount "$mount_dir" \
    --server http://127.0.0.1:15097 \
    --username smoke-user \
    --password smoke-token \
    --cache-ttl 0 \
    --foreground >"$fuse_log" 2>&1 &
fuse_pid="$!"

mounted=0
for _ in {1..100}; do
    if mountpoint -q "$mount_dir"; then
        mounted=1
        break
    fi
    if ! kill -0 "$fuse_pid" 2>/dev/null; then
        cat "$fuse_log" >&2
        exit 1
    fi
    sleep 0.1
done
if [[ "$mounted" != "1" ]]; then
    cat "$fuse_log" >&2
    echo "sdwfuse did not mount within 10 seconds" >&2
    exit 1
fi

test "$(cat "$mount_dir/library/probe.txt")" = "sdwfuse mount smoke"
if touch "$mount_dir/should-fail" 2>/dev/null; then
    echo "read-only mount unexpectedly accepted a write" >&2
    exit 1
fi

fusermount3 -u "$mount_dir"
wait "$fuse_pid"
fuse_pid=""
echo "FUSE mount smoke passed"
