# 容器部署指南

本文档介绍如何使用容器（Podman / Docker）部署二次元观测器 Re:Dive。

> **强烈推荐使用 Podman**。Podman 无守护进程、默认 rootless 运行、兼容 OCI 标准，更适合自托管服务。以下示例均以 Podman 为主，Docker 用户将对应命令中的 `podman` 替换为 `docker` 即可。

## 架构概览

容器化部署包含四个服务：

| 服务 | 镜像 | 说明 |
|------|------|------|
| **sdw-redive** | `ghcr.io/hcgstudio/sdw-redive` | 主应用（前端 + 后端一体） |
| **qbittorrent** | `lscr.io/linuxserver/qbittorrent` | 下载客户端 |
| **db** | `postgres:16-alpine` | PostgreSQL 数据库 |
| **valkey** | `valkey/valkey:9-alpine` | 分布式缓存（Redis 兼容） |

存储卷：
- `downloads` — sdw-redive 和 qbittorrent **共享**，用于下载文件的读写
- `pgdata` — PostgreSQL 数据持久化
- `valkeydata` — Valkey 缓存数据持久化
- `appdata` — 登录密码文件与运行时敏感配置的 Data Protection 密钥环

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

通用部署脚本支持系统包安装、容器部署和 tar.gz 三种方式。运行后选择「容器部署」即可：

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/setup.sh)
```

脚本会引导完成：
- 选择部署方式（选 2 为容器部署）
- 自动检测 `podman-compose` / `podman compose` / `docker compose`
- 生成随机数据库密码和 JwtSecret
- 交互式配置 AI 推断（可选）
- 启动所有服务

脚本会把随机数据库密码和 JwtSecret 写入 `podman-compose.yml`，不会在终端回显，并将该文件权限设为 `0600`。请像保护密钥一样保护与备份它。

### 2b. 手动配置（可选）

如果不使用快速启动脚本，可以手动操作：

```bash
mkdir -p ~/sdw-redive && cd ~/sdw-redive
curl -O https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/podman-compose.yml
chmod 0600 podman-compose.yml
```

编辑 `podman-compose.yml`，**必须修改以下内容**：

```yaml
# db 服务中
POSTGRES_PASSWORD: sdw_password    # ← 修改为强密码

# sdw-redive 服务中
ConnectionStrings__sdw: "Host=db;Username=sdw;Password=sdw_password;Database=sdw"  # ← 密码与上面一致
JwtSecret: "CHANGE_ME_TO_A_32_CHAR_RANDOM_STRING"  # ← 修改为随机字符串（至少 32 位）
```

Compose 文件包含数据库、JWT 和可能的上游凭据，因此应在写入任何秘密之前按上例将权限限制为 `0600`。

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
| `DataProtection__KeyRingPath` | 网页保存密钥/密码所用的持久化加密密钥环 | `/app/data/data-protection-keys` |
| `FileStore__Local` | 下载文件存储路径 | `/downloads` |
| `MediaLibrary__ScanInterval` | 持续监控目录的轮询间隔 | `00:05:00` |
| `MediaLibrary__SettlingPeriod` | 新文件写入完成后的稳定等待时间 | `00:00:30` |
| `MediaLibrary__MissingGracePeriod` | 条目缺失后保留观看/审核记录的宽限期 | `1.00:00:00` |
| `MediaLibrary__AllowedRoots__0`, `__1`, ... | 允许导入的服务端根目录白名单 | `/media` |
| `Torrent__Remote__Url` | qBittorrent API 地址 | `http://qbittorrent:8080` |
| `Valkey__ConnectionString` | Valkey 连接字符串 | 空（使用内存缓存） |
| `TmdbApiKey` | TMDB API 密钥 | 空（海报功能不可用） |
| `DisableCors` | 允许跨域 | `true` |
| `MikananiFeeds__0`, `__1`, ... | RSS 订阅源 URL | 空 |
| `AI__Provider` | AI 推断提供商 (`OpenAI` / `Anthropic`) | `OpenAI` |
| `AI__Engine` | `BuiltIn` 或 `CodexAppServer` | `BuiltIn` |
| `AI__OpenAI__ApiKey` | OpenAI API 密钥 | 空（禁用推断） |
| `AI__OpenAI__Model` | OpenAI 模型名称 | `gpt-4o-mini` |
| `AI__OpenAI__BaseUrl` | OpenAI API 端点 | `https://api.openai.com/v1` |
| `AI__OpenAI__ApiMode` | `Responses`；Ollama/vLLM 等旧兼容端点使用 `ChatCompletions` | 随附配置为 `Responses`；旧配置缺省为 `ChatCompletions` |
| `AI__CodexAppServer__Endpoint` | 本地 Agent 的 WebSocket 地址 | 空 |
| `AI__CodexAppServer__BearerToken` | app-server / 反向代理要求的 Bearer token | 空 |
| `AI__CodexAppServer__PermissionProfile` | `:read-only` 或管理员定义的 permission profile id | `:read-only` |

### 网页运行时设置

首次登录后可在「设置」中修改 AI/TMDB、qBittorrent、媒体库扫描、异常阈值和 NFS。网页值保存在 PostgreSQL，优先于上表的环境变量；敏感值加密后存储且不会通过 API 回显。`appdata` 卷中的 Data Protection 密钥环必须保留，否则重启后的应用无法解密已保存的密钥。

如果运行多个应用副本并让它们连接同一个 PostgreSQL 数据库，必须把 `DataProtection__KeyRingPath` 指向所有副本共享的同一持久化密钥环（且都使用内置 application name `SecondDimensionWatcherReDive`）。实例各自使用本地密钥环会导致其他副本无法解密数据库中的运行时密钥和密码。

数据库、JWT、`FileStore__Local`、Valkey 和 CORS 仍只能由部署环境配置。NFS 监听配置保存后需要重启容器，其他支持项对后续请求和新任务热生效。后台定时任务的间隔变更不会中断已经开始的等待，最迟会在当前等待周期结束后采用新值。

设置页提交的 API key 和密码会经过浏览器与服务端之间的连接；除严格的本机访问外，必须为网页入口配置 HTTPS。带凭据的 AI 与 qBittorrent 上游也应使用 TLS，或只在受信任的隔离容器网络中使用明文 HTTP。

### 导入现有媒体库

先将宿主机媒体目录以只读方式挂载到应用容器：

```yaml
services:
  sdw-redive:
    volumes:
      - /path/to/anime:/media/anime:ro
```

重启容器后，在网页「设置 → 现有媒体库导入」中填写容器内路径
`/media/anime`。必须至少配置一个 `MediaLibrary__AllowedRoots__*` 白名单项，否则不会接受任何导入源；导入路径还不能与 `FileStore__Local` 管理的下载目录相同、互为父目录或以其他方式重叠。系统把每个一级子目录作为一个多集媒体项、每个一级视频文件
作为一个单集媒体项，复用现有正则和 AI 推断建立虚拟路径。导入和后续对账只会修改数据库中的媒体记录与虚拟映射；即使扫描发现源条目消失，也绝不会复制、移动、重命名或删除原文件。缺失条目会先撤下虚拟映射，并在 `MediaLibrary__MissingGracePeriod` 内保留观看与审核记录。

### 启用 AI 元数据推断

在 `sdw-redive` 服务的 `environment` 中添加：

```yaml
AI__Provider: "OpenAI"
AI__OpenAI__ApiKey: "sk-your-api-key"
AI__OpenAI__BaseUrl: "https://api.openai.com/v1"
AI__OpenAI__ApiMode: "Responses"
AI__OpenAI__Model: "gpt-4o-mini"
TmdbApiKey: "your-tmdb-api-key"
```

也可以不在 Compose 文件中写入密钥，启动后从网页「设置 → AI / 媒体」配置。

### 使用 Codex app-server

本地 Agent 使用 Codex app-server 的实验性 WebSocket 协议，需要 0.144.5 或兼容版本提供 `permissionProfile/list` 与 `permissions`。应用默认选择 `:read-only`，并核验服务端实际启用的 profile、`readOnly` sandbox 和 `networkAccess=false`；不满足时会失败关闭。当前 `:read-only` 仍能读取 app-server 操作系统账号可读的主机文件，agent 网络开关也不阻止 app-server 自身访问模型 API，因此必须使用独立低权限账号、隔离的 `HOME`/`CODEX_HOME` 和空工作目录，不要挂载媒体库、应用配置、Data Protection 密钥环或其他秘密。也不要在隔离配置中启用个人 MCP、skills 或 plugins。可配置管理员定义的更严格 profile，但应用仍要求最终结果只读且 agent 网络关闭。详见 [OpenAI Codex app-server 文档](https://learn.chatgpt.com/docs/app-server)。

app-server 必须运行在独立低权限宿主机用户下，从不含业务文件或密钥的空目录启动，并使用仅含专用登录与最小配置的隔离 `HOME`/`CODEX_HOME`。不要复用容器服务账户、管理员或开发者账户，也不要在该配置目录启用个人 MCP servers、skills 或 plugins。只读 sandbox 不是完整信任边界：提示词注入仍可能读取该 profile 与操作系统账户权限允许的内容或调用能力，转接的 dynamic tools 也可能修改应用数据；聊天、RSS、种子名和媒体元数据均应视为不受信任输入。

容器中的 `127.0.0.1` 指向容器自身，通常不能直接连接宿主机 loopback app-server。跨网络连接时，请为宿主机 app-server 配置带 TLS 与认证的反向代理，并在网页填写 `wss://...` 与 Bearer token；不能把无认证端点暴露到公网。只有 app-server 与应用确实位于同一网络命名空间时才可使用 loopback `ws://`。参见 [Codex App Server 官方文档](https://learn.chatgpt.com/docs/app-server)。

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

更新前建议先执行并验证一次快照；完整的定时、加密和灾难恢复流程见 [备份、恢复与逻辑数据迁移](backup-restore.md)。数据库备份不包含 `downloads` 或外部媒体目录，这些卷需要独立快照。

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
