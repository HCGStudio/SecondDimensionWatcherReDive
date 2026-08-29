# 二次元观测器 Re:Dive

## 介绍

二次元观测器 Re:Dive 是一个动画下载管理系统，能够自动化或半自动化地从 RSS 订阅源获取番剧信息、通过 qBittorrent 下载，并在任何设备上通过 Web 界面或 WebDAV 浏览和播放。

系统通过 AI 推断自动识别动画元数据（季度、集数、TMDB ID），按动画分组展示，支持 TMDB 海报图片，并提供对话式 AI 助手让你用自然语言管理订阅、下载和文件。

## 技术栈

- **后端**: .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL
- **前端**: React 18, TypeScript, Tailwind CSS, Radix UI, SWR, Parcel, Artplayer, react-i18next
- **下载**: qBittorrent Web API
- **AI**: OpenAI / Anthropic，或本地 Codex app-server（流式 SSE + 工具调用）+ TMDB API
- **包管理**: Yarn Berry (PnP)
- **测试**: MSTest + Moq、Testcontainers PostgreSQL、Playwright，以及 FUSE/交付制品 smoke tests

## 功能

- [x] RSS 订阅源管理（静态配置 + 动态 CRUD）
- [x] 自动同步 RSS 源，创建动画信息记录
- [x] 通过 qBittorrent 进行下载 / 暂停 / 恢复 / 取消管理
- [x] 实时下载进度追踪（速度、剩余时间）
- [x] 虚拟文件系统：磁盘文件不重命名，按 `S##E##` 规则映射虚拟路径（含字幕语言后缀）
- [x] 现有媒体库原地导入（手动扫描或周期监控，不移动/删除原文件）
- [x] 多集种子通过 AI 推断逐文件拆分集数
- [x] HTTP 文件浏览和流媒体播放，支持外部播放器（VLC / PotPlayer / IINA / mpv / nPlayer）URL Scheme
- [x] WebDAV 只读网关（RFC 4918，按设备签发的 Basic 访问令牌，独立于 JWT）
- [x] JWT 认证 + 刷新令牌
- [x] AI 元数据推断（OpenAI / Anthropic）— 自动识别 TMDB ID、季度、集数、字幕组
- [x] 本地 Agent 执行模式（Codex app-server）— 同时用于元数据推断与对话助手
- [x] AI 对话助手：流式响应 + 7 个内置工具（动画 / 订阅 / 季度 / 下载 / 任务 / 文件查询）
- [x] 网页运行时设置：AI、TMDB、qBittorrent、媒体库、异常阈值和 NFS
- [x] TMDB 海报图片展示
- [x] AI 推断失败后手动重试
- [x] 按动画分组的主页展示（卡片 + 剧集列表）
- [x] 当季番组发现（mikanani.me 爬取）+ 一键订阅
- [x] 后台任务仪表盘（查看状态、手动触发）
- [x] 一次性数据迁移框架（`MigrationMarkers` 表幂等记录）
- [x] 插件事件系统（下载前 / 下载完成后钩子）
- [x] 多语言界面（简体中文 / English / 日本語）
- [x] Podman / Docker Compose 一键部署
- [ ] 插件系统动态加载（JavaScript / ClearScript）
- [ ] WebDAV 写入支持
- [ ] 内置种子下载（替换 qBittorrent）

## 快速开始

### 前置条件

- .NET 10 SDK
- Node.js 18+
- Yarn (`corepack enable`)
- PostgreSQL
- qBittorrent（开启 Web API）

### 开发

```bash
# 后端
dotnet run --project SecondDimensionWatcherReDive

# 前端（另开终端）
cd SecondDimensionWatcherReDive.Client
yarn install
yarn dev    # Mock 服务器 + 开发服务器

# 或连接真实后端
yarn start  # 仅前端开发服务器（代理到 localhost:5097）
```

完整测试矩阵与本地命令见 **[生产边界测试指南](docs/testing.md)**。

### 一键部署

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/HCGStudio/SecondDimensionWatcherReDive/main/deployments/setup.sh)
```

交互式引导，支持三种部署方式：
- **系统包安装**（推荐）— 自动检测发行版，下载安装 deb/rpm/pacman 包
- **容器部署** — Podman / Docker，含 qBittorrent 和 PostgreSQL
- **通用 tar.gz** — 适用于任意 Linux 发行版

详见 **[服务器部署指南](docs/server-deployment.md)** | **[容器部署指南](docs/container-deployment.md)**

### 配置

编辑 `SecondDimensionWatcherReDive/appsettings.json`（完整示例见 `appsettings.example.json`）：

| 配置项 | 说明 |
|--------|------|
| `ConnectionStrings:sdw` | PostgreSQL 连接字符串 |
| `JwtSecret` | JWT 签名密钥 |
| `Password:Value` | 登录密码的 BCrypt 哈希；为空时允许首次注册写入 `password.json` |
| `DataProtection:KeyRingPath` | 网页保存的 API key/密码所用加密密钥环；必须位于持久化目录 |
| `Torrent:Remote:Url` | qBittorrent API 地址 |
| `FileStore:Local` | 下载文件存储根目录 |
| `MediaLibrary:AllowedRoots` / `ScanInterval` / `SettlingPeriod` / `MissingGracePeriod` | 必须显式配置的导入根目录白名单、监控间隔、文件写入稳定等待时间与缺失记录保留期 |
| `MikananiFeeds` | RSS 源 URL 数组 |
| `TmdbApiKey` | TMDB API 密钥 |
| `AI:Engine` | `BuiltIn` 或 `CodexAppServer`（默认 `BuiltIn`） |
| `AI:Provider` | 内置引擎使用的 `OpenAI` 或 `Anthropic`（默认 `OpenAI`） |
| `AI:OpenAI:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` | OpenAI 或 OpenAI 兼容端点配置 |
| `AI:OpenAI:ApiMode` | `Responses`（官方 OpenAI）或 `ChatCompletions`（Ollama / vLLM / 旧兼容端点）；旧配置缺省为后者 |
| `AI:Anthropic:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` / `ApiVersion` | Anthropic 端点 |
| `AI:CodexAppServer:Endpoint` / `BearerToken` / `Model` / `PermissionProfile` / `TimeoutSeconds` | Codex app-server WebSocket 端点；空模型使用服务端默认模型；权限配置默认 `:read-only`，也可填写管理员定义的 profile id |
| `Inference:RateLimitDelayMs` | 推断 API 调用最小间隔（毫秒，默认 1000） |
| `Valkey:ConnectionString` | Valkey / Redis 连接（可选；为空则使用内存缓存） |

> 使用现有媒体库导入前，必须至少配置一个 `MediaLibrary:AllowedRoots`。导入源必须位于白名单内，且不能与 `FileStore:Local` 管理的下载目录相同、互为父目录或以其他方式重叠。导入与后续对账只会修改数据库中的媒体记录和虚拟路径映射；系统绝不会移动、重命名或删除原文件。短暂缺失的条目会先撤下映射并保留观看/审核记录，超过 `MissingGracePeriod`（默认 24 小时）后才清理数据库记录。

> 从 v2.2 之前升级：旧的 `Inference:ApiKey/Provider/Model` 已迁移到 `AI:` 前缀。运行 `deployments/migrate-config.sh` 自动迁移；包管理器安装时 `postinstall.sh` 会自动执行。

### 网页运行时设置

登录后打开「设置」，可修改 AI 执行模式与 Provider、AI/TMDB 密钥、qBittorrent、媒体库扫描、异常检测和 NFS。保存值存入 PostgreSQL，并覆盖部署文件或环境变量中的默认值；密钥和密码使用持久化 Data Protection 密钥环加密，API 不会回显明文。可对单个敏感项选择保留、替换、清除或恢复部署默认值。

数据库连接、JWT、下载存储根目录、登录密码文件、CORS 和 Valkey 仍属于启动/基础设施配置，不允许从网页修改。NFS 监听地址、端口和启用状态会保存，但需要重启应用才能切换；其余上述设置对后续请求和新任务热生效。后台定时任务的间隔变更不会中断已经开始的等待，最迟会在当前等待周期结束后采用新值。

设置页提交的 API key 和密码会经过浏览器与服务端之间的连接；除严格的本机访问外，必须为网页入口配置 HTTPS。配置带凭据的 AI 或 qBittorrent 端点时也应使用 TLS，或将明文 HTTP 严格限制在受信任的隔离网络内。

### 使用本地 Codex app-server

需要 Codex app-server 0.144.5 或兼容版本提供实验性的 `permissionProfile/list` 与 `permissions` 协议。应用默认请求 `:read-only` 权限配置，并在每次创建 thread 后核验服务端实际返回 `readOnly` 且 agent network access 为 `false`；服务端不支持该协议、配置不可用或结果更宽松时会拒绝执行。当前 `:read-only` **不会把主机文件读取范围收窄到空目录**，而 agent sandbox 的网络开关也不限制 app-server 自身访问模型 API，所以仍必须把进程当作能够读取其操作系统账号可读文件的服务来隔离。

为 app-server 创建独立的低权限操作系统用户、仅含 Codex 登录与最小配置的隔离 `CODEX_HOME`，并从不含任何业务文件或密钥的空工作目录启动。不要复用管理员、开发者桌面或 `sdw-redive` 服务账户，也不要在该 `CODEX_HOME` 中配置个人 MCP servers、skills 或 plugins。可在设置中选择管理员定义的更严格权限配置，但应用仍会要求最终 sandbox 为只读且 agent 网络关闭。

下面的命令假定专用账户和目录已经按上述要求准备并完成登录：

```bash
sudo -u sdw-codex env HOME=/var/lib/sdw-codex CODEX_HOME=/var/lib/sdw-codex/.codex \
  sh -c 'cd /var/lib/sdw-codex/empty-workspace && codex app-server --listen ws://127.0.0.1:4500'
```

然后在「设置 → AI」选择「Codex app-server」，填写 `ws://127.0.0.1:4500`。模型可留空以使用 app-server 默认值。本实现为每次 AI 操作创建临时 thread，使用 `approvalPolicy=never` 和经核验的只读权限配置，并将本系统的业务工具通过 app-server dynamic tools 转接；该 WebSocket 协议目前仍是实验性接口。协议细节见 [OpenAI Codex app-server 文档](https://learn.chatgpt.com/docs/app-server)。

只读 sandbox 不是完整的信任边界：提示词注入仍可能读取该 profile 与操作系统账户权限允许的内容、调用已启用的能力；本系统转接的业务工具也可能修改应用数据。不要把秘密放在 app-server 账户可读的位置，并按不受信任输入处理聊天内容、RSS、种子名和媒体元数据。

明文 `ws://` 只允许 loopback；loopback 端点也必须只供本机受信任进程访问。容器或远程部署应在 app-server 前配置 TLS 与认证，填写 `wss://` 地址和 Bearer token，切勿把无认证的 app-server 暴露到公网。协议和认证方式以 [Codex App Server 官方文档](https://learn.chatgpt.com/docs/app-server) 为准。

## 项目结构

```
SecondDimensionWatcherReDive/             # ASP.NET Core 主项目（控制器、后台服务、SPA 托管）
SecondDimensionWatcherReDive.Framework/   # 共享抽象（仓储接口、领域记录、AI / 插件 / 文件存储抽象）
SecondDimensionWatcherReDive.Test/        # 单元测试（MSTest + Moq）
SecondDimensionWatcherReDive.IntegrationTest/ # 集成测试（WebDAV / Basic Auth 端到端）
SecondDimensionWatcherReDive.Client/      # React 前端（Parcel + Tailwind + Radix）
Plugins/
  SecondDimensionWatcherReDive.AI/            # 统一 AI 引擎（OpenAI / Anthropic Provider + 工具执行器）
  SecondDimensionWatcherReDive.Inference.AI/  # 离线元数据推断流水线（含 TMDB 工具）
  SecondDimensionWatcherReDive.Chat/          # 对话式 AI 插件（ChatController + 7 个工具）
  SecondDimensionWatcherReDive.WebDav/        # WebDAV (RFC 4918) 基础类型与序列化
Share/
  SecondDimensionWatcherReDive.Analyzers/     # Roslyn 源生成器（生成 [Tool<T>] 的 Definition / ExecuteAsync）
```
