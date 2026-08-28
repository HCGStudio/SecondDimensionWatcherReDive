#!/bin/bash
set -euo pipefail

# migrate-config.sh — Migrate legacy Inference:* config to AI:* structure
#
# Run after upgrading to v2.2+ on system-package or tarball installs.
# Container deployments use environment variables and don't need this.
#
# Usage:
#   sudo bash migrate-config.sh [config-path]
#
# Default config path: /etc/sdw-redive/appsettings.yml

CONFIG="${1:-/etc/sdw-redive/appsettings.yml}"

if [ ! -f "$CONFIG" ]; then
    echo "Error: config file not found: $CONFIG" >&2
    exit 1
fi

# Already migrated?
if grep -qE '^AI:' "$CONFIG"; then
    echo "Already migrated (AI: section exists)."
    exit 0
fi

# ── Extract values from the Inference section ────────────────────────
# Uses awk to scope reads to the Inference: block so identically named
# keys elsewhere are ignored.
extract() {
    awk -v key="$1" '
        /^Inference:/ { in_section=1; next }
        /^[^[:space:]#]/ { in_section=0 }
        in_section && $0 ~ "^[[:space:]]+" key ":" {
            val=$0
            sub(/^[^:]*:[[:space:]]*/, "", val)
            gsub(/^["'"'"']|["'"'"'][[:space:]]*$/, "", val)
            print val
            exit
        }
    ' "$CONFIG"
}

PROVIDER=$(extract Provider)
API_KEY=$(extract ApiKey)
BASE_URL=$(extract BaseUrl)
MODEL=$(extract Model)
MAX_TOKENS=$(extract MaxTokens)

if [ -z "$API_KEY" ] && [ -z "$PROVIDER" ]; then
    echo "No legacy Inference config found — nothing to migrate."
    exit 0
fi

PROVIDER="${PROVIDER:-OpenAI}"
MAX_TOKENS="${MAX_TOKENS:-1024}"

echo "Detected legacy config:"
echo "  Provider:  $PROVIDER"
echo "  BaseUrl:   ${BASE_URL:-(default)}"
echo "  Model:     ${MODEL:-(default)}"
echo "  MaxTokens: $MAX_TOKENS"
if [ -n "$API_KEY" ]; then
    echo "  ApiKey:    (set)"
else
    echo "  ApiKey:    (empty)"
fi

# ── Build per-provider values ────────────────────────────────────────
if echo "$PROVIDER" | grep -qi anthropic; then
    A_KEY="$API_KEY"
    A_URL="${BASE_URL:-https://api.anthropic.com}"
    A_MODEL="${MODEL:-claude-sonnet-4-20250514}"
    A_MT="$MAX_TOKENS"
    O_KEY=""
    O_URL="https://api.openai.com/v1"
    O_MODEL="gpt-4o-mini"
    O_MT="1024"
    O_MODE="ChatCompletions"
else
    O_KEY="$API_KEY"
    O_URL="${BASE_URL:-https://api.openai.com/v1}"
    O_MODEL="${MODEL:-gpt-4o-mini}"
    O_MT="$MAX_TOKENS"
    O_MODE="ChatCompletions"
    A_KEY=""
    A_URL="https://api.anthropic.com"
    A_MODEL="claude-sonnet-4-20250514"
    A_MT="1024"
fi

# ── Backup ───────────────────────────────────────────────────────────
cp "$CONFIG" "${CONFIG}.bak"
echo
echo "Backup saved to ${CONFIG}.bak"

# ── Write new AI section to a temp file ──────────────────────────────
TMPAI=$(mktemp)
cat > "$TMPAI" << EOF
# AI 推断配置（可选，留空 ApiKey 则禁用）
# Provider: OpenAI 或 Anthropic（也支持任何 OpenAI 兼容端点）
AI:
  Provider: "$PROVIDER"
  OpenAI:
    BaseUrl: $O_URL
    ApiMode: $O_MODE
    ApiKey: "$O_KEY"
    Model: $O_MODEL
    MaxTokens: $O_MT
  Anthropic:
    BaseUrl: $A_URL
    ApiKey: "$A_KEY"
    Model: $A_MODEL
    MaxTokens: $A_MT
    ApiVersion: "2023-06-01"
EOF

# ── Transform config ─────────────────────────────────────────────────
# 1. Insert AI section just before the Inference: line.
# 2. Remove migrated keys from the Inference section (keep RateLimitDelayMs).
TMPOUT=$(mktemp)
awk -v tmpai="$TMPAI" '
    /^Inference:/ && !ai_done {
        while ((getline line < tmpai) > 0) print line
        close(tmpai)
        ai_done = 1
        in_inf = 1
        print          # print the Inference: header itself
        next
    }
    in_inf && /^[^[:space:]#]/ { in_inf = 0 }
    in_inf && /^[[:space:]]+(Provider|ApiKey|BaseUrl|Model|MaxTokens):/ { next }
    { print }
' "$CONFIG" > "$TMPOUT"
cat "$TMPOUT" > "$CONFIG"
rm -f "$TMPOUT"
rm -f "$TMPAI"

echo
echo "Migration complete.  Please verify: $CONFIG"
