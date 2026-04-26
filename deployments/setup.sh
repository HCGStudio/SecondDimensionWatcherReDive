#!/bin/bash
set -euo pipefail

# 二次元观测器 Re:Dive - 部署脚本
# 用法: bash setup.sh
# 支持系统包安装（推荐）、容器部署、通用 tar.gz 三种方式

REPO="HCGStudio/SecondDimensionWatcherReDive"
COMPOSE_URL="https://raw.githubusercontent.com/$REPO/main/deployments/podman-compose.yml"

# ============================================================
# Helper functions
# ============================================================

detect_arch() {
    case "$(uname -m)" in
        x86_64)  echo "x64" ;;
        aarch64) echo "arm64" ;;
        *)       echo "unsupported" ;;
    esac
}

# Map arch for deb/rpm/pacman filenames (amd64/arm64)
pkg_arch() {
    case "$1" in
        x64)  echo "amd64" ;;
        arm64) echo "arm64" ;;
    esac
}

detect_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        case "$ID" in
            debian|ubuntu|linuxmint|pop) echo "deb" ;;
            fedora|rhel|centos|rocky|alma|openeuler) echo "rpm" ;;
            arch|manjaro|endeavouros) echo "pacman" ;;
            *) echo "unknown" ;;
        esac
    else
        echo "unknown"
    fi
}

# Get release asset URLs from GitHub API
# Args: $1 = "stable" or "pre"
# Outputs: one asset URL per line
get_release_assets() {
    local channel="$1"
    local api_url

    if [ "$channel" = "stable" ]; then
        api_url="https://api.github.com/repos/$REPO/releases/latest"
    else
        # Get the most recent pre-release
        api_url="https://api.github.com/repos/$REPO/releases?per_page=20"
    fi

    local json
    json=$(curl -fsSL "$api_url" 2>/dev/null) || {
        echo "error" ; return
    }

    if [ "$channel" = "stable" ]; then
        echo "$json" | python3 -c "
import json, sys
try:
    data = json.load(sys.stdin)
    for a in data.get('assets', []):
        print(a['browser_download_url'])
except:
    pass
" 2>/dev/null
    else
        echo "$json" | python3 -c "
import json, sys
try:
    releases = json.load(sys.stdin)
    for r in releases:
        if r.get('prerelease'):
            for a in r.get('assets', []):
                print(a['browser_download_url'])
            break
except:
    pass
" 2>/dev/null
    fi
}

# Find a matching asset URL by pattern
# Args: $1 = list of URLs (newline-separated), $2 = grep pattern
find_asset() {
    echo "$1" | grep -i "$2" | head -1
}

# ============================================================
# Valkey configuration (shared across deployment methods)
# ============================================================

configure_valkey() {
    echo
    echo "=== Valkey 分布式缓存配置 ==="
    echo "Valkey 是 Redis 兼容的高性能缓存服务器。"
    echo "未配置时将使用内存缓存（单实例足够，无需额外服务）。"
    echo
    read -rp "是否配置 Valkey 分布式缓存？[y/N] " CONFIGURE_VALKEY

    VALKEY_CONN="" VALKEY_INSTANCE=""

    if [[ "${CONFIGURE_VALKEY:-}" =~ ^[Yy]$ ]]; then
        echo
        read -rp "Valkey 连接字符串 [localhost:6379]: " VALKEY_CONN
        VALKEY_CONN="${VALKEY_CONN:-localhost:6379}"
        read -rp "缓存键前缀 [sdw-redive:]: " VALKEY_INSTANCE
        VALKEY_INSTANCE="${VALKEY_INSTANCE:-sdw-redive:}"

        echo
        echo "Valkey 配置:"
        echo "  连接字符串: $VALKEY_CONN"
        echo "  键前缀:     $VALKEY_INSTANCE"
    else
        echo "跳过 Valkey 配置，将使用内存缓存。"
    fi
}

# ============================================================
# AI configuration (shared across deployment methods)
# ============================================================

configure_ai() {
    echo
    echo "=== AI 元数据推断配置（必填） ==="
    echo "AI 推断用于自动识别动画的 TMDB ID、季度、集数等元数据。"
    echo "需要 AI API 密钥和 TMDB API 密钥。"

    AI_PROVIDER="" AI_BASE_URL="" AI_API_KEY="" AI_MODEL="" TMDB_KEY=""

    echo
    echo "选择 AI 提供商："
    echo "  1) OpenAI（默认）"
    echo "  2) Anthropic"
    echo "  3) OpenAI 兼容端点（Ollama / vLLM / LiteLLM 等）"
    read -rp "请选择 [1/2/3]: " AI_CHOICE

    local default_model
    case "${AI_CHOICE:-1}" in
        2)
            AI_PROVIDER="Anthropic"
            AI_BASE_URL="https://api.anthropic.com"
            default_model="claude-sonnet-4-20250514"
            ;;
        3)
            AI_PROVIDER="OpenAI"
            while [ -z "${AI_BASE_URL:-}" ]; do
                read -rp "请输入 API 端点 URL: " AI_BASE_URL
                [ -z "$AI_BASE_URL" ] && echo "API 端点 URL 不能为空。"
            done
            default_model="gpt-4o-mini"
            ;;
        *)
            AI_PROVIDER="OpenAI"
            AI_BASE_URL="https://api.openai.com/v1"
            default_model="gpt-4o-mini"
            ;;
    esac

    while [ -z "${AI_API_KEY:-}" ]; do
        read -rp "请输入 API Key: " AI_API_KEY
        if [ -z "$AI_API_KEY" ]; then
            echo "API Key 不能为空。如确实无法获取，请按 Ctrl+C 退出后稍后手动配置。"
        fi
    done
    read -rp "请输入模型名称 [${default_model}]: " AI_MODEL
    AI_MODEL="${AI_MODEL:-$default_model}"

    echo
    echo "TMDB API Key（必填，用于番剧元数据、季度信息和海报图片）"
    echo "申请步骤："
    echo "  1) 注册账号: https://www.themoviedb.org/signup"
    echo "  2) 打开:     https://www.themoviedb.org/settings/api"
    echo "  3) 点击 \"创建\" → 选择 \"Developer\"（免费） → 填写表单"
    echo "  4) 复制 \"API 密钥 (v3 auth)\" 字段的值"
    while [ -z "${TMDB_KEY:-}" ]; do
        read -rp "请输入 TMDB API Key: " TMDB_KEY
        if [ -z "$TMDB_KEY" ]; then
            echo "TMDB API Key 不能为空。如确实无法获取，请按 Ctrl+C 退出后稍后手动配置。"
        fi
    done

    echo
    echo "AI 配置:"
    echo "  Provider: $AI_PROVIDER"
    echo "  Base URL: $AI_BASE_URL"
    echo "  Model:    $AI_MODEL"
    echo "  TMDB:     (已设置)"
}

# ============================================================
# Method 1: System package install
# ============================================================

install_system_package() {
    local arch distro parch assets url

    arch=$(detect_arch)
    if [ "$arch" = "unsupported" ]; then
        echo "Error: 不支持的架构 $(uname -m)，请使用 tar.gz 安装方式。"
        exit 1
    fi
    parch=$(pkg_arch "$arch")

    distro=$(detect_distro)
    if [ "$distro" = "unknown" ]; then
        echo "无法识别发行版，将使用 tar.gz 安装方式。"
        install_tarball
        return
    fi

    echo
    echo "检测到: 发行版=$distro  架构=$parch"
    echo "正在获取发布信息..."

    assets=$(get_release_assets "$RELEASE_CHANNEL")
    if [ -z "$assets" ] || [ "$assets" = "error" ]; then
        if [ "$RELEASE_CHANNEL" = "stable" ]; then
            echo "暂无正式版发布，请选择预发布版。"
        else
            echo "Error: 无法获取发布信息。"
        fi
        exit 1
    fi

    # Find matching package
    case "$distro" in
        deb)
            url=$(find_asset "$assets" "\.deb" | grep -i "$parch" | head -1)
            ;;
        rpm)
            url=$(find_asset "$assets" "\.rpm" | grep -i "$parch" | head -1)
            ;;
        pacman)
            url=$(find_asset "$assets" "\.pkg\.tar\.zst" | grep -i "$parch" | head -1)
            ;;
    esac

    if [ -z "${url:-}" ]; then
        echo "未找到匹配的包 (distro=$distro, arch=$parch)，回退到 tar.gz。"
        install_tarball
        return
    fi

    local filename
    filename=$(basename "$url")
    echo "下载: $filename"
    curl -fsSL -o "/tmp/$filename" "$url"

    echo "安装包..."
    case "$distro" in
        deb)    sudo dpkg -i "/tmp/$filename" || sudo apt-get install -f -y ;;
        rpm)    sudo rpm -U --force "/tmp/$filename" ;;
        pacman) sudo pacman -U --noconfirm "/tmp/$filename" ;;
    esac
    rm -f "/tmp/$filename"

    echo
    echo "包安装完成。配置文件: /etc/sdw-redive/appsettings.yml"

    # Guide through essential config
    configure_system_config "/etc/sdw-redive/appsettings.yml"

    echo
    read -rp "是否启动并启用服务？[Y/n] " START_SERVICE
    if [[ ! "${START_SERVICE:-Y}" =~ ^[Nn]$ ]]; then
        sudo systemctl enable --now sdw-redive
        echo "服务已启动: http://localhost:5097"
    fi
}

# ============================================================
# Method 3: Tar.gz install
# ============================================================

install_tarball() {
    local arch assets url

    arch=$(detect_arch)
    if [ "$arch" = "unsupported" ]; then
        echo "Error: 不支持的架构 $(uname -m)。"
        exit 1
    fi

    echo
    echo "正在获取发布信息..."

    assets=$(get_release_assets "$RELEASE_CHANNEL")
    if [ -z "$assets" ] || [ "$assets" = "error" ]; then
        if [ "$RELEASE_CHANNEL" = "stable" ]; then
            echo "暂无正式版发布，请选择预发布版。"
        else
            echo "Error: 无法获取发布信息。"
        fi
        exit 1
    fi

    url=$(find_asset "$assets" "linux-${arch}\.tar\.gz")
    if [ -z "${url:-}" ]; then
        echo "Error: 未找到 linux-${arch} tar.gz 包。"
        exit 1
    fi

    local filename
    filename=$(basename "$url")
    echo "下载: $filename"
    curl -fsSL -o "/tmp/$filename" "$url"

    echo "安装到 /usr/lib/sdw-redive/ ..."
    sudo mkdir -p /usr/lib/sdw-redive
    sudo tar -xzf "/tmp/$filename" -C /usr/lib/sdw-redive
    rm -f "/tmp/$filename"

    # Config file
    sudo mkdir -p /etc/sdw-redive
    if [ ! -f /etc/sdw-redive/appsettings.yml ]; then
        sudo cp /usr/lib/sdw-redive/appsettings.yml /etc/sdw-redive/appsettings.yml
    fi

    # Systemd service
    if [ -f /usr/lib/sdw-redive/sdw-redive.service ]; then
        sudo cp /usr/lib/sdw-redive/sdw-redive.service /usr/lib/systemd/system/sdw-redive.service
        sudo systemctl daemon-reload
    fi

    # Create system user
    if ! getent group sdw-redive >/dev/null 2>&1; then
        sudo groupadd --system sdw-redive
    fi
    if ! getent passwd sdw-redive >/dev/null 2>&1; then
        sudo useradd --system --no-create-home --shell /usr/sbin/nologin \
            --gid sdw-redive --home-dir /var/lib/sdw-redive sdw-redive
    fi
    sudo mkdir -p /var/lib/sdw-redive/downloads
    sudo chown -R sdw-redive:sdw-redive /var/lib/sdw-redive

    # Generate JwtSecret if placeholder present
    if grep -q '<Please fill this with a 32 length random string>' /etc/sdw-redive/appsettings.yml 2>/dev/null; then
        local jwt
        jwt=$(openssl rand -base64 36)
        sudo sed -i "s|<Please fill this with a 32 length random string>|${jwt}|" /etc/sdw-redive/appsettings.yml
        echo "已生成 JwtSecret。"
    fi

    echo
    echo "tar.gz 安装完成。配置文件: /etc/sdw-redive/appsettings.yml"

    configure_system_config "/etc/sdw-redive/appsettings.yml"

    echo
    read -rp "是否启动并启用服务？[Y/n] " START_SERVICE
    if [[ ! "${START_SERVICE:-Y}" =~ ^[Nn]$ ]]; then
        sudo systemctl enable --now sdw-redive
        echo "服务已启动: http://localhost:5097"
    fi
}

# ============================================================
# Shared: configure system appsettings.yml
# ============================================================

configure_system_config() {
    local config="$1"

    echo
    echo "=== 服务配置 ==="
    read -rp "是否现在配置服务连接信息？[Y/n] " DO_CONFIG
    if [[ "${DO_CONFIG:-Y}" =~ ^[Nn]$ ]]; then
        echo "跳过配置。请稍后编辑 $config"
        return
    fi

    # --- Database ---
    echo
    echo "--- 数据库配置 ---"
    read -rp "PostgreSQL 主机地址 [localhost]: " DB_HOST
    DB_HOST="${DB_HOST:-localhost}"
    read -rp "PostgreSQL 端口 [5432]: " DB_PORT
    DB_PORT="${DB_PORT:-5432}"
    read -rp "数据库名称 [sdw]: " DB_NAME
    DB_NAME="${DB_NAME:-sdw}"
    read -rp "数据库用户名 [sdw]: " DB_USER
    DB_USER="${DB_USER:-sdw}"
    read -rsp "数据库密码: " DB_PASS
    echo

    if [ -n "${DB_PASS:-}" ]; then
        local pg_conn="Host=${DB_HOST};Port=${DB_PORT};Username=${DB_USER};Password=${DB_PASS};Database=${DB_NAME}"
        sudo sed -i "s|Host=localhost;Username=sdw;Password=YOUR_PASSWORD;Database=sdw|${pg_conn}|" "$config"
        echo "数据库连接已配置。"
    else
        echo "未输入密码，跳过数据库配置。请稍后编辑 $config"
    fi

    # --- Download location ---
    echo
    echo "--- 下载存储路径 ---"
    read -rp "下载文件存储路径 [/var/lib/sdw-redive/downloads]: " DL_PATH
    if [ -n "${DL_PATH:-}" ]; then
        sudo mkdir -p "$DL_PATH"
        sudo chown sdw-redive:sdw-redive "$DL_PATH"
        sudo sed -i "s|Local: /var/lib/sdw-redive/downloads|Local: ${DL_PATH}|" "$config"
        # If using non-default path, add to systemd ReadWritePaths
        if [ -d /usr/lib/systemd/system ] && [ "$DL_PATH" != "/var/lib/sdw-redive/downloads" ]; then
            sudo mkdir -p /etc/systemd/system/sdw-redive.service.d
            printf '[Service]\nReadWritePaths=%s\n' "$DL_PATH" | sudo tee /etc/systemd/system/sdw-redive.service.d/downloads.conf >/dev/null
            sudo systemctl daemon-reload
            echo "已添加 systemd ReadWritePaths 覆盖。"
        fi
        echo "下载路径已设置为: $DL_PATH"
    else
        echo "使用默认路径: /var/lib/sdw-redive/downloads"
    fi

    # --- qBittorrent ---
    echo
    echo "--- qBittorrent 配置 ---"
    read -rp "qBittorrent Web API 地址 [http://localhost:8080]: " QB_URL
    if [ -n "${QB_URL:-}" ]; then
        sudo sed -i "s|Url: \"http://localhost:8080\"|Url: \"${QB_URL}\"|" "$config"
    fi
    echo "qBittorrent 鉴权（如未启用 WebUI 鉴权或已加入白名单，请回车跳过）"
    read -rp "  用户名: " QB_USER
    read -rsp "  密码:   " QB_PASS
    echo
    # YAML defaults for both keys are empty strings, so plain substitution
    # is safe even when the input is empty (no-op for blanks).
    sudo sed -i \
        -e "/^  Remote:/,/^[A-Z]/s|UserName: \"\"|UserName: \"${QB_USER:-}\"|" \
        -e "/^  Remote:/,/^[A-Z]/s|Password: \"\"|Password: \"${QB_PASS:-}\"|" \
        "$config"

    # --- AI config ---
    configure_ai

    # Write AI provider
    sudo sed -i "s|Provider: \"OpenAI\"|Provider: \"${AI_PROVIDER}\"|" "$config"

    # Write provider-specific config.  BaseUrl and Model defaults are unique
    # per subsection so plain substitution targets the right one.  ApiKey
    # defaults are identical ("") in both subsections, so we scope the
    # replacement with a sed address range anchored to the section header.
    if [ "$AI_PROVIDER" = "Anthropic" ]; then
        sudo sed -i \
            -e "s|BaseUrl: https://api.anthropic.com|BaseUrl: ${AI_BASE_URL}|" \
            -e "s|Model: claude-sonnet-4-20250514|Model: ${AI_MODEL}|" \
            -e '/^  Anthropic:/,/^[A-Z]/s|ApiKey: ""|ApiKey: "'"${AI_API_KEY}"'"|' \
            "$config"
    else
        sudo sed -i \
            -e "s|BaseUrl: https://api.openai.com/v1|BaseUrl: ${AI_BASE_URL}|" \
            -e "s|Model: gpt-4o-mini|Model: ${AI_MODEL}|" \
            -e '/^  OpenAI:/,/^  Anthropic:/s|ApiKey: ""|ApiKey: "'"${AI_API_KEY}"'"|' \
            "$config"
    fi

    sudo sed -i "s|TmdbApiKey: \"\"|TmdbApiKey: \"${TMDB_KEY}\"|" "$config"

    # --- Valkey config ---
    configure_valkey

    if [ -n "${VALKEY_CONN:-}" ]; then
        # Uncomment and fill the Valkey section
        sudo sed -i \
            -e "s|^# Valkey:|Valkey:|" \
            -e "s|^#   ConnectionString: \"localhost:6379\"|  ConnectionString: \"${VALKEY_CONN}\"|" \
            -e "s|^#   InstanceName: \"sdw-redive:\"|  InstanceName: \"${VALKEY_INSTANCE}\"|" \
            "$config"
    fi

    echo
    echo "配置已写入 $config"
}

# ============================================================
# Method 2: Container deploy
# ============================================================

deploy_container() {
    local deploy_dir="${1:-sdw-redive}"

    # Detect container runtime
    local compose=""
    if command -v podman-compose &>/dev/null; then
        compose="podman-compose"
    elif command -v podman &>/dev/null && podman compose version &>/dev/null 2>&1; then
        compose="podman compose"
    elif command -v docker &>/dev/null && docker compose version &>/dev/null 2>&1; then
        compose="docker compose"
    else
        echo "Error: 需要 podman-compose、podman compose 或 docker compose。"
        exit 1
    fi

    echo "Using: $compose"
    echo "Deploy directory: $deploy_dir"
    echo

    mkdir -p "$deploy_dir"
    curl -fsSL "$COMPOSE_URL" -o "$deploy_dir/podman-compose.yml"

    # Generate secrets
    local db_pass jwt
    db_pass=$(openssl rand -base64 18)
    jwt=$(openssl rand -base64 36)

    sed -i.bak \
        -e "s|POSTGRES_PASSWORD: sdw_password|POSTGRES_PASSWORD: ${db_pass}|" \
        -e "s|Password=sdw_password|Password=${db_pass}|g" \
        -e "s|CHANGE_ME_TO_A_32_CHAR_RANDOM_STRING|${jwt}|" \
        "$deploy_dir/podman-compose.yml"
    rm -f "$deploy_dir/podman-compose.yml.bak"

    echo "Generated secrets:"
    echo "  PostgreSQL password: $db_pass"
    echo "  JWT secret: $jwt"

    # qBittorrent credentials (optional). For the bundled qBittorrent
    # service, leave blank and either configure BypassAuthSubnetWhitelist
    # in qBittorrent or set credentials here matching the WebUI password
    # you set on first launch.
    echo
    echo "--- qBittorrent 鉴权（如未启用 WebUI 鉴权或已加入白名单，请回车跳过）---"
    read -rp "  用户名: " QB_USER
    read -rsp "  密码:   " QB_PASS
    echo
    sed -i.bak \
        -e "s|Torrent__Remote__UserName: \"\"|Torrent__Remote__UserName: \"${QB_USER:-}\"|" \
        -e "s|Torrent__Remote__Password: \"\"|Torrent__Remote__Password: \"${QB_PASS:-}\"|" \
        "$deploy_dir/podman-compose.yml"
    rm -f "$deploy_dir/podman-compose.yml.bak"

    # AI config
    configure_ai

    # Build provider-specific env vars for the new AI:* config layout
    local ai_envs
    if [ "$AI_PROVIDER" = "Anthropic" ]; then
        ai_envs="      AI__Provider: \"${AI_PROVIDER}\"\\
      AI__Anthropic__ApiKey: \"${AI_API_KEY}\"\\
      AI__Anthropic__BaseUrl: \"${AI_BASE_URL}\"\\
      AI__Anthropic__Model: \"${AI_MODEL}\""
    else
        ai_envs="      AI__Provider: \"${AI_PROVIDER}\"\\
      AI__OpenAI__ApiKey: \"${AI_API_KEY}\"\\
      AI__OpenAI__BaseUrl: \"${AI_BASE_URL}\"\\
      AI__OpenAI__Model: \"${AI_MODEL}\""
    fi

    ai_envs="${ai_envs}\\
      TmdbApiKey: \"${TMDB_KEY}\""

    sed -i.bak \
        -e "/Torrent__Remote__Url/a\\
${ai_envs}" \
        "$deploy_dir/podman-compose.yml"
    rm -f "$deploy_dir/podman-compose.yml.bak"

    echo
    cd "$deploy_dir"
    $compose up -d

    echo
    echo "=== 容器部署完成 ==="
    echo "  App:              http://localhost:5097"
    echo "  qBittorrent:      http://localhost:8080"
    echo
    echo "获取 qBittorrent 临时密码："
    echo "  $compose logs qbittorrent 2>&1 | grep 'temporary password'"
    echo
    echo "配置文件: $(pwd)/podman-compose.yml"
}

# ============================================================
# Main
# ============================================================

echo "=== 二次元观测器 Re:Dive 部署脚本 ==="
echo
echo "选择部署方式："
echo "  1) 系统包安装 — deb/rpm/pacman（推荐）"
echo "  2) 容器部署 — Podman / Docker"
echo "  3) 通用 tar.gz 安装"
read -rp "请选择 [1/2/3]: " DEPLOY_METHOD
echo

# Release channel (for non-container methods)
RELEASE_CHANNEL="stable"
if [ "${DEPLOY_METHOD:-1}" != "2" ]; then
    echo "选择版本："
    echo "  1) 最新正式版（推荐）"
    echo "  2) 最新预发布版"
    read -rp "请选择 [1/2]: " CHANNEL_CHOICE
    echo

    case "${CHANNEL_CHOICE:-1}" in
        2) RELEASE_CHANNEL="pre" ;;
        *) RELEASE_CHANNEL="stable" ;;
    esac
fi

case "${DEPLOY_METHOD:-1}" in
    1) install_system_package ;;
    2) deploy_container ;;
    3) install_tarball ;;
    *)
        echo "无效选项。"
        exit 1
        ;;
esac
