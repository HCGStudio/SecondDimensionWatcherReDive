# 二次元观测器 Re:Dive

## 介绍

二次元观测器 Re:Dive 是一个动画下载管理系统，能够自动化或半自动化地从 RSS 订阅源获取番剧信息、通过 qBittorrent 下载，并在任何设备上通过 Web 界面或 WebDAV 浏览和播放。

系统通过 AI 推断自动识别动画元数据（季度、集数、TMDB ID），按动画分组展示，支持 TMDB 海报图片，并提供对话式 AI 助手让你用自然语言管理订阅、下载和文件。

下一正式版本的范围、实施顺序与升级边界见 **[3.0 发布目标](docs/release-3.0.md)**。

## 技术栈

- **后端**: .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL
- **前端**: React 19, TypeScript, Tailwind CSS, Radix UI, SWR, Parcel, Artplayer, react-i18next
- **下载**: qBittorrent Web API
- **AI**: OpenAI / Anthropic，或本地 Codex app-server（流式 SSE + 工具调用）+ TMDB API
- **包管理**: Yarn Berry (PnP)
- **测试**: MSTest + Moq、Testcontainers PostgreSQL、Playwright，以及 FUSE/交付制品 smoke tests

## 功能

以下能力描述当前源码；AI/TMDB、qBittorrent、HLS 转码、通知和协议服务需要对应部署配置。操作入口与边界见 [使用指南](docs/library-workflows.md)。

- [x] RSS 订阅源管理（静态配置 + 动态 CRUD）
- [x] 自动同步 RSS 源，创建动画信息记录
- [x] 通过 qBittorrent 进行下载 / 暂停 / 恢复 / 取消管理
- [x] 自适应下载进度追踪：活跃/暂停/无变化分频、分批查询与故障退避
- [x] 持久容量预留与可暂停/取消的等待队列，统一覆盖手动、订阅和版本升级下载
- [x] 虚拟文件系统：磁盘文件不重命名，按 `S##E##` 规则映射虚拟路径（含字幕语言后缀）
- [x] 现有媒体库原地导入（手动扫描或周期监控，不移动/删除原文件）
- [x] 多集种子通过 AI 推断逐文件拆分集数
- [x] HTTP 文件浏览和流媒体播放，支持外部播放器（VLC / PotPlayer / IINA / mpv / nPlayer）URL Scheme
- [x] WebDAV 只读网关（RFC 4918，按设备签发的 Basic 访问令牌，独立于 JWT）
- [x] JWT 认证 + 刷新令牌
- [x] 家庭账户与独立档案（Admin / Member / Viewer、PIN、会话撤销、独立播放/聊天状态）
- [x] AI 元数据推断（OpenAI / Anthropic）— 自动识别 TMDB ID、季度、集数、字幕组
- [x] 本地 Agent 执行模式（Codex app-server）— 同时用于元数据推断与对话助手
- [x] AI 对话助手：流式响应 + 7 个内置工具（动画 / 订阅 / 季度 / 下载 / 任务 / 文件查询）
- [x] 网页运行时设置：AI、TMDB、qBittorrent、媒体库、异常阈值和 NFS
- [x] TMDB 海报图片展示
- [x] AI 推断失败后手动重试、元数据人工审核/映射预览/撤销、显式长期识别规则
- [x] 订阅过滤、通知/确认/自动下载模式、番剧多来源优先级与等待回退
- [x] 全局搜索、播出感知的缺集补全计划、候选评分和版本升级/回滚
- [x] 档案独立的播放进度、个人追番清单与本周更新视图
- [x] 手动 OP/ED 跳过、命名章节、需按媒体版本确认的季默认与默认关闭的个人自动跳过
- [x] HLS 转码、音轨字幕选择、浏览器原生大文件下载与 Range 续传
- [x] 按动画分组的主页展示（卡片 + 剧集列表）
- [x] 当季番组发现（mikanani.me 爬取）+ 一键订阅
- [x] 后台任务仪表盘（查看状态、手动触发）
- [x] PostgreSQL 持久任务 / Outbox（崩溃恢复、指数重试、死信处理、多实例租约）
- [x] 存活与就绪探针、Prometheus 指标及 OpenTelemetry 链路
- [x] 可串行、可恢复的数据迁移（PostgreSQL advisory lock、版本状态、批次 checkpoint）
- [x] 插件事件系统（下载前 / 下载完成后钩子）
- [x] 多语言界面（简体中文 / English / 日本語）
- [x] Podman / Docker Compose 一键部署
- [x] 受控 JavaScript 插件管理：本地包检查、签名/能力确认、独立进程、启停与升级回滚（[API 1.0 边界](docs/plugin-platform.md)）
- [ ] WebDAV 写入支持
- [ ] 内置种子下载（替换 qBittorrent）

功能操作入口与已知边界见 [订阅、下载与观看指南](docs/library-workflows.md)。部署相关能力分别见 [运行可靠性](docs/runtime-reliability.md)、[安全边界](docs/security-boundaries.md)、[备份恢复](docs/backup-restore.md) 与 [架构说明](docs/architecture.md)。

## 快速开始

### 前置条件

- .NET 10 SDK
- Node.js 24（与仓库 CI 一致）
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

详见 **[服务器部署指南](docs/server-deployment.md)** | **[容器部署指南](docs/container-deployment.md)** | **[发布流程](docs/release-process.md)**

### 配置

编辑 `SecondDimensionWatcherReDive/appsettings.json`（完整示例见 [appsettings.example.json](SecondDimensionWatcherReDive/appsettings.example.json)）：

| 配置项 | 说明 |
|--------|------|
| `Version` | 配置结构版本，当前为 `2.3.0`；与应用版本独立，缺省按 `2.2.0` 迁移 |
| `StateDirectory` | 持久状态目录；密钥环、插件和转码缓存的默认路径位于此目录下 |
| `ConnectionStrings:sdw` | PostgreSQL 连接字符串 |
| `Migration:BackupExecutable` / `BackupArguments` / `BackupTimeout` / `RequireBackup` | schema/data migration 前的可选备份钩子与强制策略；详见[迁移运维手册](docs/migrations.md) |
| `JwtSecret` | JWT 签名密钥 |
| `Authentication:BootstrapPasswordHash` | 配置迁移保留的初始 BCrypt 哈希；启动时原子导入 PostgreSQL，之后以数据库为准。新安装通过网页注册，无需设置此项 |
| `DataProtection:KeyRingPath` | 网页保存的 API key/密码所用加密密钥环；必须位于持久化目录 |
| `Torrent:Remote:Url` | qBittorrent API 地址 |
| `FileStore:Local` | 下载文件存储根目录 |
| `DownloadCapacity:Enabled` / `SafetyBytes` / `LocalVolumePath` | 默认启用容量准入；安全余量默认 5 GiB；本地卷路径仅能指向下载器实际写入卷的共享挂载，详见[容量边界](docs/library-workflows.md#容量与等待队列) |
| `Torrent:Polling` / `DownloadCompletion:Workers` | 自适应轮询周期/分批/退避；完成处理默认 2 个独立 worker |
| `MediaLibrary:AllowedRoots` / `ScanInterval` / `SettlingPeriod` / `MissingGracePeriod` | 必须显式配置的导入根目录白名单、监控间隔、文件写入稳定等待时间与缺失记录保留期 |
| `MikananiFeeds` | RSS 源 URL 数组 |
| `TmdbApiKey` | TMDB API 密钥 |
| `AI:Engine` | `BuiltIn` 或 `CodexAppServer`（默认 `BuiltIn`） |
| `AI:Provider` | 内置引擎使用的 `OpenAI` 或 `Anthropic`（默认 `OpenAI`） |
| `AI:OpenAI:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` | OpenAI 或 OpenAI 兼容端点配置 |
| `AI:OpenAI:ApiMode` | `Responses`（默认，官方 OpenAI）或 `ChatCompletions`（Ollama / vLLM 等兼容端点）；迁移会显式保留旧配置采用的协议 |
| `AI:Anthropic:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` / `ApiVersion` | Anthropic 端点 |
| `AI:CodexAppServer:Endpoint` / `BearerToken` / `Model` / `PermissionProfile` / `TimeoutSeconds` | Codex app-server WebSocket 端点；空模型使用服务端默认模型；权限配置默认 `:read-only`，也可填写管理员定义的 profile id |
| `Inference:RateLimitDelayMs` | 推断 API 调用最小间隔（毫秒，默认 1000） |
| `Health:ValkeyRequired` / `QbittorrentRequired` / `StorageRequired` / `AIRequired` | `/health/ready` 的依赖要求；PostgreSQL 始终必需，AI 默认不阻塞就绪 |
| `OpenTelemetry:OtlpEndpoint` | 可选 OTLP Collector 地址；Prometheus `/metrics` 无需配置即启用 |
| `Notifications:Webhook:Enabled` / `Url` | 通用 Webhook 通知渠道；完整 URL 按敏感配置处理，建议从网页设置中保存；远端地址必须使用 HTTPS |
| `Notifications:WebPush:Enabled` / `Subject` / `VapidPublicKey` / `VapidPrivateKey` | 浏览器 Web Push；首次在网页启用时可由服务端生成 VAPID 密钥对，私钥加密保存；浏览器订阅需要 HTTPS 或 localhost 安全来源 |
| `Notifications:Events` / `QuietHours` | 允许投递的领域事件与可选免打扰时段；启用且选中的事件会在核心操作完成后尽力写入持久化 Outbox，再异步重试投递 |
| `OutboundHttp:AllowedPrivateHosts` / `AllowedPrivateNetworks` | RSS、Webhook 与 Web Push 端点默认拒绝 loopback/私网目的地；确有需要时仅精确放行目标主机或 CIDR |
| `Valkey:ConnectionString` | Valkey / Redis 连接（单副本可选；多副本必须共享同一实例） |
| `ReverseProxy:KnownProxies` / `KnownNetworks` | 非 loopback 反向代理的受信地址/CIDR；仅填写代理，不填写客户端网段 |

> 使用现有媒体库导入前，必须至少配置一个 `MediaLibrary:AllowedRoots`。导入源必须位于白名单内，且不能与 `FileStore:Local` 管理的下载目录相同、互为父目录或以其他方式重叠。导入与后续对账只会修改数据库中的媒体记录和虚拟路径映射；系统绝不会移动、重命名或删除原文件。短暂缺失的条目会先撤下映射并保留观看/审核记录，超过 `MissingGracePeriod`（默认 24 小时）后才清理数据库记录。

> 系统包升级配置使用 `sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive`，并通过重复的 `--inherit-config` 按优先级提供基础 JSON、存在的 Production 文件及自定义环境/命令行上下文快照；无继承的独立配置使用 `--standalone`。JSON 配置同样支持。迁移按版本依次执行 `Up`；只有需要决定的破坏性变更才会提问，其他新增功能保持默认并在网页设置中配置。主程序启动和包安装只尝试静默升级。完整参数与恢复步骤见[配置版本迁移](docs/configuration-migrations.md)。

运行探针、持久任务恢复、死信操作和遥测标签约束详见 [运行可靠性与可观测性](docs/runtime-reliability.md)。
升级前的备份、失败诊断、checkpoint 恢复和多副本发布流程见 **[数据库迁移运维手册](docs/migrations.md)**。

### 网页运行时设置

管理员登录后打开「设置」，可修改 AI 执行模式与 Provider、AI/TMDB 密钥、qBittorrent、媒体库扫描、异常检测、通知和 NFS。保存值存入 PostgreSQL，并覆盖部署文件或环境变量中的默认值；密钥、密码、Webhook URL、VAPID 私钥及浏览器 PushSubscription 能力凭据使用持久化 Data Protection 密钥环加密，API 不会回显明文。可对单个敏感项选择保留、替换、清除或恢复部署默认值。

启用且订阅的通知会在核心操作完成后，以唯一去重键尽力写入 PostgreSQL Outbox，再由后台服务按至少一次语义投递。Webhook 和每个 Web Push 浏览器订阅拥有独立投递行、租约与重试状态，一个渠道失败不会重复投递另一个渠道；Webhook 请求带有稳定的 `X-SDW-Event-Id`，Web Push 也使用同一事件 ID 作为通知标签，接收端仍应按事件 ID 幂等。5xx、408、429 和网络错误会指数退避重试，失效的浏览器订阅会在 404/410 后撤销，永久失败可在「设置 → 通知」查看，且任何投递或入队失败都不会回滚订阅、下载、推断或异常处理。顶栏「待办中心」会按风险汇总待确认下载、异常、低置信度/失败元数据和磁盘预警，并支持已读、稍后提醒及无副作用批量操作。

数据库连接、JWT、下载存储根目录、持久状态目录、CORS 和 Valkey 仍属于启动/基础设施配置，不允许从网页修改。NFS 监听地址、端口和启用状态会保存，但需要重启应用才能切换；其余上述设置对后续请求和新任务热生效。后台定时任务的间隔变更不会中断已经开始的等待，最迟会在当前等待周期结束后采用新值。

设置页提交的 API key 和密码会经过浏览器与服务端之间的连接；除严格的本机访问外，必须为网页入口配置 HTTPS。配置带凭据的 AI 或 qBittorrent 端点时也应使用 TLS，或将明文 HTTP 严格限制在受信任的隔离网络内。

### 家庭账户、档案与设备访问

首次安装由注册页创建管理员和默认档案；旧实例的密码哈希会先经配置迁移写入 `Authentication:BootstrapPasswordHash`，再于启动时导入数据库。已有密码时注册入口保持关闭，使用用户名 `admin` 和原密码首次登录即可完成家庭账户迁移。主程序不再读取旧 `Password` 配置或 `password.json` 文件。右上角档案菜单可即时切换档案，「账户与档案」页可管理名称、头像、可选 PIN、家庭用户和登录会话。档案切换会轮换访问/刷新令牌，并清除浏览器中上一档案的播放、聊天等缓存；多个标签页通过 Web Locks 与浏览器消息同步轮换结果。

角色权限由服务端强制执行：Admin 可管理全局设置、用户、任务、元数据和设备凭据；Member 可管理订阅、下载任务和播放状态，但删除已下载文件仍需近期管理员验证；Viewer 仅可浏览和播放。敏感管理操作在超过近期验证窗口后会要求再次输入账户密码，无需退出登录。

管理员可在「设置 → 访问协议」为指定家庭用户签发 WebDAV/VFS 设备凭据。每个凭据固定为只读，可限制虚拟根路径并设置到期时间；撤销、到期和路径边界同时由 WebDAV 与 VFS 强制执行。路径 `/Anime` 不会授权 `/Anime2`，客户端看到的根目录和 WebDAV href 会重写到所授权的命名空间。

升级迁移会把旧播放进度、偏好和聊天记录归入默认 `Home` 档案。旧数据库结构无法表达多用户、档案归属、token 根路径、到期或撤销状态；因此一旦创建了新身份数据或受限设备凭据，向该迁移之前降级会在删除任何列之前明确失败并保持数据库原样，避免静默合并历史或扩大已撤销凭据权限。

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
