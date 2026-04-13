# 容器部署指南

本文档介绍如何使用容器（Podman / Docker）部署二次元观测器 Re:Dive。

> **强烈推荐使用 Podman**。Podman 无守护进程、默认 rootless 运行、兼容 OCI 标准，更适合自托管服务。以下示例均以 Podman 为主，Docker 用户将对应命令中的 `podman` 替换为 `docker` 即可。

## 架构概览

容器化部署包含三个服务：

| 服务 | 镜像 | 说明 |
|------|------|------|
| **sdw-redive** | `ghcr.io/hcgstudio/sdw-redive` | 主应用（前端 + 后端一体） |
| **qbittorrent** | `lscr.io/linuxserver/qbittorrent` | 下载客户端 |
| **db** | `postgres:16-alpine` | PostgreSQL 数据库 |

存储卷：
- `downloads` — sdw-redive 和 qbittorrent **共享**，用于下载文件的读写
- `pgdata` — PostgreSQL 数据持久化

## 快速开始

### 1. 安装 Podman 和 podman-compose

```bash
# Debian / Ubuntu
sudo apt install podman podman-compose

# Fedora
sudo dnf install podman podman-compose

# Arch Linux
sudo pacman -S podman podman-compose

# macOS
brew install podman podman-compose
podman machine init && podman machine start
```

### 2. 一键启动（推荐）

快速启动脚本会自动下载 compose 模版、生成随机数据库密码和 JwtSecret、启动所有服务：

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/setup.sh)
```

也可以指定部署目录：

```bash
curl -fsSL -o setup.sh https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/setup.sh
bash setup.sh ~/my-sdw
```

脚本会自动检测 `podman-compose`、`podman compose` 或 `docker compose`，优先使用 Podman。运行完成后会打印生成的密码，请妥善保存。

### 2b. 手动配置（可选）

如果不使用快速启动脚本，可以手动操作：

```bash
mkdir -p ~/sdw-redive && cd ~/sdw-redive
curl -O https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/podman-compose.yml
```

编辑 `podman-compose.yml`，**必须修改以下内容**：

```yaml
# db 服务中
POSTGRES_PASSWORD: sdw_password    # ← 修改为强密码

# sdw-redive 服务中
ConnectionStrings__sdw: "Host=db;Username=sdw;Password=sdw_password;Database=sdw"  # ← 密码与上面一致
JwtSecret: "CHANGE_ME_TO_A_32_CHAR_RANDOM_STRING"  # ← 修改为随机字符串（至少 32 位）
```

生成随机 JwtSecret：

```bash
openssl rand -base64 36
```

然后启动：

```bash
podman-compose up -d
```

### 3. 访问服务
- 主应用：`http://localhost:5097`
- qBittorrent WebUI：`http://localhost:8080`

### 4. 获取 qBittorrent 初始密码

```bash
podman logs qbittorrent 2>&1 | grep "temporary password"
```

默认用户名为 `admin`，使用日志中显示的临时密码登录后修改。

然后在 `podman-compose.yml` 中确认 `Torrent__Remote__Url` 指向 `http://qbittorrent:8080`（默认已配置）。

## 配置详解

所有配置通过环境变量传递，格式为 `Section__Key`（双下划线分隔层级）：

| 环境变量 | 说明 | 默认值 |
|----------|------|--------|
| `ConnectionStrings__sdw` | PostgreSQL 连接字符串 | 必填 |
| `JwtSecret` | JWT 签名密钥（>=32 字符） | 必填 |
| `FileStore__Local` | 下载文件存储路径 | `/downloads` |
| `Torrent__Remote__Url` | qBittorrent API 地址 | `http://qbittorrent:8080` |
| `TmdbApiKey` | TMDB API 密钥 | 空（海报功能不可用） |
| `DisableCors` | 允许跨域 | `true` |
| `MikananiFeeds__0`, `__1`, ... | RSS 订阅源 URL | 空 |
| `Inference__Provider` | AI 推断提供商 (`OpenAI` / `Anthropic`) | `OpenAI` |
| `Inference__ApiKey` | AI API 密钥 | 空（禁用推断） |
| `Inference__Model` | AI 模型名称 | `gpt-4o-mini` |
| `Inference__BaseUrl` | AI API 端点 | `https://api.openai.com/v1` |

### 启用 AI 元数据推断

在 `sdw-redive` 服务的 `environment` 中添加：

```yaml
Inference__ApiKey: "sk-your-api-key"
Inference__Provider: "OpenAI"
Inference__Model: "gpt-4o-mini"
TmdbApiKey: "your-tmdb-api-key"
```

### 启用 TMDB 海报

```yaml
TmdbApiKey: "your-tmdb-api-key"
```

前往 [TMDB](https://www.themoviedb.org/settings/api) 免费申请 API 密钥。

## 服务管理

```bash
# 启动
podman-compose up -d

# 查看状态
podman-compose ps

# 查看日志
podman-compose logs -f sdw-redive

# 重启
podman-compose restart sdw-redive

# 停止所有服务
podman-compose down

# 停止并删除数据卷（⚠️ 会丢失所有数据）
podman-compose down -v
```

## 更新

```bash
# 拉取最新镜像
podman-compose pull sdw-redive

# 重新创建容器
podman-compose up -d sdw-redive
```

数据库迁移在应用启动时自动执行。数据卷不受影响。

## 自定义下载路径

默认下载目录存储在 `downloads` 卷中。如需挂载到宿主机特定目录：

```yaml
volumes:
  downloads:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /path/to/your/downloads
```

确保 qbittorrent 和 sdw-redive 的 volume 映射保持一致。

## 反向代理

生产环境建议在前面放置反向代理处理 TLS。

### Caddy 示例

```
sdw.example.com {
    reverse_proxy 127.0.0.1:5097
}
```

### Nginx 示例

```nginx
server {
    listen 443 ssl http2;
    server_name sdw.example.com;

    ssl_certificate     /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://127.0.0.1:5097;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

## 本地构建镜像

如需从源码构建：

```bash
podman build -f Containerfile -t sdw-redive .
```

然后将 `podman-compose.yml` 中的 `image` 替换为本地镜像名：

```yaml
sdw-redive:
  image: localhost/sdw-redive:latest
```

## Docker 用户

将上述所有 `podman` 命令替换为 `docker`，`podman-compose` 替换为 `docker compose` 即可。Compose 文件格式完全兼容。
