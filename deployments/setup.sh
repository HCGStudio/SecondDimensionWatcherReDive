#!/bin/bash
set -euo pipefail

# 二次元观测器 Re:Dive - 容器部署快速启动脚本
# 用法: bash setup.sh [目录]
# 默认在当前目录下创建 sdw-redive/ 并启动服务

DEPLOY_DIR="${1:-sdw-redive}"
COMPOSE_URL="https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/podman-compose.yml"

# Detect container runtime
if command -v podman-compose &>/dev/null; then
    COMPOSE="podman-compose"
elif command -v podman &>/dev/null && podman compose version &>/dev/null 2>&1; then
    COMPOSE="podman compose"
elif command -v docker &>/dev/null && docker compose version &>/dev/null 2>&1; then
    COMPOSE="docker compose"
else
    echo "Error: podman-compose, podman compose, or docker compose is required."
    exit 1
fi

echo "=== 二次元观测器 Re:Dive 容器部署 ==="
echo
echo "Using: $COMPOSE"
echo "Deploy directory: $DEPLOY_DIR"
echo

# Create directory and download compose file
mkdir -p "$DEPLOY_DIR"
curl -fsSL "$COMPOSE_URL" -o "$DEPLOY_DIR/podman-compose.yml"

# Generate secrets
DB_PASSWORD=$(openssl rand -base64 18)
JWT_SECRET=$(openssl rand -base64 36)

# Fill secrets into compose file
sed -i.bak \
    -e "s|POSTGRES_PASSWORD: sdw_password|POSTGRES_PASSWORD: ${DB_PASSWORD}|" \
    -e "s|Password=sdw_password|Password=${DB_PASSWORD}|g" \
    -e "s|CHANGE_ME_TO_A_32_CHAR_RANDOM_STRING|${JWT_SECRET}|" \
    "$DEPLOY_DIR/podman-compose.yml"
rm -f "$DEPLOY_DIR/podman-compose.yml.bak"

echo "Generated secrets:"
echo "  PostgreSQL password: $DB_PASSWORD"
echo "  JWT secret: $JWT_SECRET"
echo

# --- AI Inference configuration ---
echo "=== AI 元数据推断配置 ==="
echo "AI 推断可自动识别动画的 TMDB ID、季度、集数等元数据。"
echo "需要 AI API 密钥和 TMDB API 密钥。可稍后在 compose 文件中手动配置。"
echo
read -rp "是否现在配置 AI 推断？[y/N] " CONFIGURE_AI

if [[ "$CONFIGURE_AI" =~ ^[Yy]$ ]]; then
    echo
    echo "选择 AI 提供商："
    echo "  1) OpenAI (默认)"
    echo "  2) Anthropic"
    echo "  3) OpenAI 兼容端点（Ollama / vLLM / LiteLLM 等）"
    read -rp "请选择 [1/2/3]: " AI_CHOICE

    case "${AI_CHOICE:-1}" in
        2)
            AI_PROVIDER="Anthropic"
            AI_BASE_URL="https://api.anthropic.com"
            AI_MODEL_DEFAULT="claude-sonnet-4-20250514"
            ;;
        3)
            AI_PROVIDER="OpenAI"
            read -rp "请输入 API 端点 URL: " AI_BASE_URL
            AI_MODEL_DEFAULT="gpt-4o-mini"
            ;;
        *)
            AI_PROVIDER="OpenAI"
            AI_BASE_URL="https://api.openai.com/v1"
            AI_MODEL_DEFAULT="gpt-4o-mini"
            ;;
    esac

    read -rp "请输入 API Key: " AI_API_KEY
    read -rp "请输入模型名称 [${AI_MODEL_DEFAULT}]: " AI_MODEL
    AI_MODEL="${AI_MODEL:-$AI_MODEL_DEFAULT}"

    echo
    read -rp "请输入 TMDB API Key（用于海报和元数据，https://www.themoviedb.org/settings/api）: " TMDB_KEY

    # Append AI env vars to sdw-redive service
    # Insert before the volumes line of sdw-redive service
    sed -i.bak \
        -e "/Torrent__Remote__Url/a\\
      Inference__Provider: \"${AI_PROVIDER}\"\\
      Inference__BaseUrl: \"${AI_BASE_URL}\"\\
      Inference__ApiKey: \"${AI_API_KEY}\"\\
      Inference__Model: \"${AI_MODEL}\"\\
      TmdbApiKey: \"${TMDB_KEY}\"" \
        "$DEPLOY_DIR/podman-compose.yml"
    rm -f "$DEPLOY_DIR/podman-compose.yml.bak"

    echo
    echo "AI 推断已配置:"
    echo "  Provider: $AI_PROVIDER"
    echo "  Base URL: $AI_BASE_URL"
    echo "  Model:    $AI_MODEL"
    [ -n "${TMDB_KEY:-}" ] && echo "  TMDB:     (已设置)"
    echo
else
    echo "跳过 AI 配置。可稍后编辑 $DEPLOY_DIR/podman-compose.yml 添加。"
    echo
fi

# Start services
cd "$DEPLOY_DIR"
$COMPOSE up -d

echo
echo "=== 部署完成 ==="
echo "  App:              http://localhost:5097"
echo "  qBittorrent:      http://localhost:8080"
echo
echo "获取 qBittorrent 临时密码："
echo "  $COMPOSE logs qbittorrent 2>&1 | grep 'temporary password'"
echo
echo "配置文件: $(pwd)/podman-compose.yml"
