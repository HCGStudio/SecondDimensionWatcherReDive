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

# qBittorrent Web API 地址（必填）
Torrent:
  Remote:
    Url: "http://localhost:8080"

# 下载文件存储路径
FileStore:
  Local: /var/lib/sdw-redive/downloads

# TMDB API 密钥（用于海报和元数据）
TmdbApiKey: "YOUR_TMDB_API_KEY"

# AI 推断配置（可选，留空 ApiKey 则禁用）
Inference:
  Provider: OpenAI
  BaseUrl: https://api.openai.com/v1
  ApiKey: ""
  Model: gpt-4o-mini

# Valkey / Redis 分布式缓存（可选，留空则使用内存缓存）
# Valkey:
#   ConnectionString: "localhost:6379"
#   InstanceName: "sdw-redive:"
```

> **注意**：配置文件在包升级时不会被覆盖（标记为 conffile / noreplace）。

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
