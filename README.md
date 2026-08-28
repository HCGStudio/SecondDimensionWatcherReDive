# 二次元观测器 Re:Dive

## 介绍

二次元观测器 Re:Dive 是一个动画下载管理系统，能够自动化或半自动化地从 RSS 订阅源获取番剧信息、通过 qBittorrent 下载，并在任何设备上通过 Web 界面或 WebDAV 浏览和播放。

系统通过 AI 推断自动识别动画元数据（季度、集数、TMDB ID），按动画分组展示，支持 TMDB 海报图片，并提供对话式 AI 助手让你用自然语言管理订阅、下载和文件。

## 技术栈

- **后端**: .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL
- **前端**: React 18, TypeScript, Tailwind CSS, Radix UI, SWR, Parcel, Artplayer, react-i18next
- **下载**: qBittorrent Web API
- **AI**: OpenAI / Anthropic（流式 SSE + 工具调用）+ TMDB API
- **包管理**: Yarn Berry (PnP)
- **测试**: MSTest + Moq（单元）/ `Microsoft.AspNetCore.Mvc.Testing`（集成，覆盖 WebDAV 与 Basic Auth）

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
- [x] AI 对话助手：流式响应 + 7 个内置工具（动画 / 订阅 / 季度 / 下载 / 任务 / 文件查询）
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
| `Torrent:Remote:Url` | qBittorrent API 地址 |
| `FileStore:Local` | 下载文件存储根目录 |
| `MediaLibrary:AllowedRoots` / `ScanInterval` / `SettlingPeriod` / `MissingGracePeriod` | 必须显式配置的导入根目录白名单、监控间隔、文件写入稳定等待时间与缺失记录保留期 |
| `MikananiFeeds` | RSS 源 URL 数组 |
| `TmdbApiKey` | TMDB API 密钥 |
| `AI:Provider` | `OpenAI` 或 `Anthropic`（默认 `OpenAI`） |
| `AI:OpenAI:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` | OpenAI 或 OpenAI 兼容端点配置 |
| `AI:OpenAI:ApiMode` | `Responses`（官方 OpenAI）或 `ChatCompletions`（Ollama / vLLM / 旧兼容端点）；旧配置缺省为后者 |
| `AI:Anthropic:ApiKey` / `BaseUrl` / `Model` / `MaxTokens` / `ApiVersion` | Anthropic 端点 |
| `Inference:RateLimitDelayMs` | 推断 API 调用最小间隔（毫秒，默认 1000） |
| `Valkey:ConnectionString` | Valkey / Redis 连接（可选；为空则使用内存缓存） |

> 使用现有媒体库导入前，必须至少配置一个 `MediaLibrary:AllowedRoots`。导入源必须位于白名单内，且不能与 `FileStore:Local` 管理的下载目录相同、互为父目录或以其他方式重叠。导入与后续对账只会修改数据库中的媒体记录和虚拟路径映射；系统绝不会移动、重命名或删除原文件。短暂缺失的条目会先撤下映射并保留观看/审核记录，超过 `MissingGracePeriod`（默认 24 小时）后才清理数据库记录。

> 从 v2.2 之前升级：旧的 `Inference:ApiKey/Provider/Model` 已迁移到 `AI:` 前缀。运行 `deployments/migrate-config.sh` 自动迁移；包管理器安装时 `postinstall.sh` 会自动执行。

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
