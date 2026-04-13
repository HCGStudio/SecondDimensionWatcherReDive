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

# Start services
cd "$DEPLOY_DIR"
$COMPOSE up -d

echo
echo "Done! Services are starting."
echo "  App:              http://localhost:5097"
echo "  qBittorrent:      http://localhost:8080"
echo
echo "Get qBittorrent temp password:"
echo "  $COMPOSE logs qbittorrent 2>&1 | grep 'temporary password'"
