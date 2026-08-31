# 服务器部署指南

本文档介绍如何在 Linux 主机上部署二次元观测器 Re:Dive。

- [Linux 部署（deb / rpm / pacman）](#linux-部署)

---

# Linux 部署

通过 deb / rpm / pacman 包部署。

## 前置条件

- **ASP.NET Core 10 Runtime** — 应用以 framework-dependent 方式打包，需预先安装运行时
- **PostgreSQL** — 数据库
- **qBittorrent** — 开启 Web API

### 安装 ASP.NET Core Runtime

从 [Microsoft 官方文档](https://learn.microsoft.com/dotnet/core/install/linux) 获取适合你发行版的安装指引，或手动添加 Microsoft 软件源后安装：

```bash
# Debian / Ubuntu
sudo apt install aspnetcore-runtime-10.0

# Fedora / RHEL
sudo dnf install aspnetcore-runtime-10.0

# Arch Linux
sudo pacman -S aspnet-runtime-10.0
```

## 安装

### 快速安装（推荐）

部署脚本会自动检测发行版和架构，下载并安装对应的包，引导配置：

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/setup.sh)
# 选择 1) 系统包安装
```

### 手动安装

从 [GitHub Releases](https://github.com/HCGStudio/SecondDimensionWatcherReDive/releases) 下载对应架构和格式的包：

```bash
# Debian / Ubuntu
sudo dpkg -i sdw-redive_*.deb

# Fedora / RHEL
sudo rpm -i sdw-redive-*.rpm

# Arch Linux
sudo pacman -U sdw-redive-*.pkg.tar.zst
```

## 文件布局（FHS）

安装后文件分布如下：

| 路径 | 说明 |
|------|------|
| `/usr/lib/sdw-redive/` | 应用程序文件（二进制、wwwroot 静态资源） |
| `/etc/sdw-redive/appsettings.yml` | 配置文件（YAML 格式，升级时保留用户修改） |
| `/var/lib/sdw-redive/downloads/` | 默认下载存储目录 |
| `/var/lib/sdw-redive/data-protection-keys/` | 网页保存的敏感配置所用持久加密密钥环 |
| `/usr/lib/systemd/system/sdw-redive.service` | systemd 服务单元 |

安装时自动创建 `sdw-redive` 系统用户和组用于运行服务。

## 配置

编辑 `/etc/sdw-redive/appsettings.yml`，填写必要配置项：

```yaml
# PostgreSQL 连接字符串（必填）
ConnectionStrings:
  sdw: "Host=localhost;Username=sdw;Password=YOUR_PASSWORD;Database=sdw"

# JWT 签名密钥（首次安装时自动生成，无需手动设置）
JwtSecret: "..."

Authentication:
  RefreshTokenReuseGraceSeconds: 3

DataProtection:
  KeyRingPath: /var/lib/sdw-redive/data-protection-keys

# qBittorrent Web API 地址（必填）
Torrent:
  Remote:
    Url: "http://localhost:8080"

# 下载文件存储路径
FileStore:
  Local: /var/lib/sdw-redive/downloads

# 现有媒体库扫描间隔与文件稳定等待时间
MediaLibrary:
  AllowedRoots:
    - /path/to/your/media
  ScanInterval: "00:05:00"
  SettlingPeriod: "00:00:30"
  MissingGracePeriod: "1.00:00:00"

# TMDB API 密钥（用于海报和元数据）
TmdbApiKey: "YOUR_TMDB_API_KEY"

# AI 推断配置（可选，留空 ApiKey 则禁用）
AI:
  Engine: BuiltIn
  Provider: OpenAI
  OpenAI:
    BaseUrl: https://api.openai.com/v1
    ApiMode: Responses
    ApiKey: ""
    Model: gpt-4o-mini
    MaxTokens: 1024
  CodexAppServer:
    Endpoint: ""
    BearerToken: ""
    Model: ""
    PermissionProfile: ":read-only"
    TimeoutSeconds: 300
Inference:
  RateLimitDelayMs: 1000

OutboundHttp:
  HappyEyeballsDelayMilliseconds: 250

# Valkey / Redis 分布式缓存（可选，留空则使用内存缓存）
# Valkey:
#   ConnectionString: "localhost:6379"
#   InstanceName: "sdw-redive:"
```

`DataProtection:KeyRingPath` 是运行时敏感设置的解密根密钥，不是普通缓存。请持久化并备份该目录，权限应仅允许应用服务账号读取。多副本连接同一个 PostgreSQL 数据库时，**所有副本必须挂载同一份共享密钥环**；否则一个副本写入的 API key/密码无法被其他副本解密。所有副本也必须保持应用内置的 Data Protection application name 一致（`SecondDimensionWatcherReDive`）。

> **注意**：配置文件在包升级时不会被覆盖（标记为 conffile / noreplace）。

安装脚本会把配置文件设为 `root:sdw-redive`、权限 `0640`，并把 Data Protection 密钥目录设为仅服务账户可访问的 `0700`（密钥文件 `0600`）。手动迁移或恢复备份后也应重新执行：

```bash
sudo chown root:sdw-redive /etc/sdw-redive/appsettings.yml
sudo chmod 0640 /etc/sdw-redive/appsettings.yml
sudo install -d -m 0700 -o sdw-redive -g sdw-redive /var/lib/sdw-redive/data-protection-keys
```

### 网页运行时设置

登录后可在「设置」中修改 AI/TMDB、qBittorrent、媒体库扫描、异常阈值和 NFS。网页值保存在 PostgreSQL并覆盖 YAML 默认值；API key 和密码使用 Data Protection 加密，接口不回显明文。请备份 `/var/lib/sdw-redive/data-protection-keys/`，丢失密钥环后已保存的敏感值无法恢复。

数据库、JWT、下载存储根目录、登录密码文件、Valkey 和 CORS 仍只允许由部署配置修改。NFS 监听配置需要重启服务，其余支持项对后续请求和新任务热生效。后台定时任务的间隔变更不会中断已经开始的等待，最迟会在当前等待周期结束后采用新值。

设置页提交的 API key 和密码会经过浏览器与服务端之间的连接；除严格的本机访问外，必须先为网页入口配置 HTTPS。带凭据的 AI 与 qBittorrent 上游同样应使用 TLS，或仅位于受信任的隔离网络中。

### 使用 Codex app-server

需要 Codex app-server 0.144.5 或兼容版本提供实验性的 `permissionProfile/list` 与 `permissions` 协议。应用默认选择 `:read-only`，并核验 thread 实际启用的 profile、`readOnly` sandbox 和 `networkAccess=false`；任一条件不满足都会失败关闭。当前 `:read-only` 仍可读取运行 app-server 的操作系统账号本来就能读取的文件，且 sandbox 的网络开关不阻止 app-server 自身访问模型 API，因此不能把它当作主机级隔离边界。

为 app-server 建立不同于 `sdw-redive`、管理员和日常开发账户的独立低权限用户。它的 `HOME`/`CODEX_HOME` 只保存专用 Codex 登录和最小配置，权限应为 `0700`；不要安装或启用个人 MCP servers、skills、plugins，也不要复用个人 Codex 配置。准备一个不含业务文件和密钥的空工作目录，从该目录启动：

```bash
sudo install -d -m 0700 -o sdw-codex -g sdw-codex \
  /var/lib/sdw-codex /var/lib/sdw-codex/.codex /var/lib/sdw-codex/empty-workspace
sudo -u sdw-codex env HOME=/var/lib/sdw-codex CODEX_HOME=/var/lib/sdw-codex/.codex \
  sh -c 'cd /var/lib/sdw-codex/empty-workspace && codex app-server --listen ws://127.0.0.1:4500'
```

随后在网页「设置 → AI」选择 Codex app-server 并填写该地址；模型留空时使用 app-server 默认模型。权限配置默认为 `:read-only`，也可填写管理员在 Codex 中定义的更严格 profile；无论选择哪个，应用都要求其最终结果为只读且 agent 网络关闭。应用按次创建临时 thread，固定使用 `approvalPolicy=never`，并通过 dynamic tools 转接现有业务能力。协议细节见 [OpenAI Codex app-server 文档](https://learn.chatgpt.com/docs/app-server)。

只读 sandbox 不是完整的信任边界：提示词注入仍可能读取该 profile 与操作系统账户权限允许的内容、调用已启用的能力；dynamic tools 也可能修改应用数据。不要把秘密放在 app-server 账户可读的位置，并把聊天、RSS、种子名和媒体元数据视为不受信任输入。

app-server WebSocket 目前是实验性接口。明文 `ws://` 只允许 loopback，并应只供本机受信任进程访问；跨主机时必须使用带 TLS 和认证的 `wss://` 反向代理并设置 Bearer token，不能把无认证端点暴露到公网。参见 [Codex App Server 官方文档](https://learn.chatgpt.com/docs/app-server)。

### 修改下载存储路径

如果需要将下载目录改为其他位置，修改 `FileStore: Local` 后确保 `sdw-redive` 用户有读写权限：

```bash
sudo mkdir -p /path/to/your/downloads
sudo chown sdw-redive:sdw-redive /path/to/your/downloads
```

同时需要在 systemd 服务中允许写入该路径。创建 override 文件：

```bash
sudo systemctl edit sdw-redive
```

添加以下内容：

```ini
[Service]
ReadWritePaths=/path/to/your/downloads
```

### 导入现有媒体库

确保 `sdw-redive` 用户对媒体目录拥有只读权限，并且必须至少将一个目录（或其父目录）加入
`MediaLibrary:AllowedRoots` 白名单；未配置白名单时不会接受任何导入源。导入路径不能与 `FileStore:Local` 管理的下载目录相同、互为父目录或以其他方式重叠。然后在网页「设置 → 现有媒体库导入」中填写服务器绝对路径。导入和后续对账只会修改数据库中的媒体记录和虚拟路径映射；即使扫描发现源条目消失，也绝不会移动、重命名或删除原文件。缺失条目会先撤下映射，并在 `MissingGracePeriod` 内保留观看与审核记录。若 systemd
加固策略限制了该路径，可创建 override 显式声明只读访问：

```ini
[Service]
ReadOnlyPaths=/path/to/your/media
```

### 修改监听端口

默认监听 `http://0.0.0.0:5097`。如需修改，创建 systemd override：

```bash
sudo systemctl edit sdw-redive
```

```ini
[Service]
Environment=ASPNETCORE_URLS=http://0.0.0.0:8080
```

## 服务管理

```bash
# 启动服务
sudo systemctl start sdw-redive

# 开机自启
sudo systemctl enable sdw-redive

# 查看状态
sudo systemctl status sdw-redive

# 查看日志
sudo journalctl -u sdw-redive -f

# 重启（修改配置后）
sudo systemctl restart sdw-redive

# 停止
sudo systemctl stop sdw-redive
```

## 反向代理（推荐）

生产环境建议在前面放置反向代理（Nginx / Caddy）处理 TLS 和域名。
同机 loopback 代理默认受信；应用会在限流前处理其 `X-Forwarded-For` 和
`X-Forwarded-Proto`。若代理位于另一台主机或容器，请在 YAML 中只加入代理自身的
精确地址或最小网段，切勿加入客户端网段：

```yaml
ReverseProxy:
  ForwardLimit: 1
  KnownProxies:
    - 10.20.0.5
  KnownNetworks: []
```

多级代理必须同时把 `ForwardLimit` 调整为实际受信跳数，并逐一限定受信代理。

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

### Caddy 示例

```
sdw.example.com {
    reverse_proxy 127.0.0.1:5097
}
```

## 升级

下载新版包后直接安装即可，包管理器会处理升级：

```bash
# Debian / Ubuntu
sudo dpkg -i sdw-redive_*.deb

# Fedora / RHEL
sudo rpm -U sdw-redive-*.rpm

# Arch Linux
sudo pacman -U sdw-redive-*.pkg.tar.zst
```

升级后重启服务：

```bash
sudo systemctl restart sdw-redive
```

配置文件 `/etc/sdw-redive/appsettings.yml` 不会被覆盖。数据库迁移在应用启动时自动执行。

## 卸载

```bash
# Debian / Ubuntu
sudo apt remove sdw-redive

# Fedora / RHEL
sudo dnf remove sdw-redive

# Arch Linux
sudo pacman -R sdw-redive
```

卸载前服务会自动停止并禁用。配置文件和数据目录不会被删除，如需完全清理：

```bash
sudo rm -rf /etc/sdw-redive /var/lib/sdw-redive
sudo userdel sdw-redive
sudo groupdel sdw-redive
```

## 安全加固

systemd 服务已启用以下安全措施：

- `NoNewPrivileges=true` — 禁止提权
- `ProtectSystem=strict` — 文件系统只读（仅 `/var/lib/sdw-redive` 可写）
- `ProtectHome=true` — 禁止访问 home 目录
- `PrivateTmp=true` — 独立 tmp 命名空间
- `ProtectKernelTunables=true` — 禁止修改内核参数
- `ProtectControlGroups=true` — 禁止修改 cgroup
- `RestrictSUIDSGID=true` — 禁止 SUID/SGID

数据库迁移在应用启动时自动执行。

## 卸载

```sh
sudo pkg remove sdw-redive
```

卸载前服务会自动停止。配置和数据目录不会被删除，如需完全清理：

```sh
sudo rm -rf /usr/local/etc/sdw-redive /var/db/sdw-redive
sudo pw userdel sdw-redive
sudo pw groupdel sdw-redive
```
