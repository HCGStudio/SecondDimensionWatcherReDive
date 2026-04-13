# 二次元观测器 Re:Dive

## 介绍

二次元观测器 Re:Dive 是一个动画下载管理系统，能够自动化或半自动化地从 RSS 订阅源获取番剧信息、通过 qBittorrent 下载，并在任何设备上通过 Web 界面浏览和播放。

系统通过 AI 推断自动识别动画元数据（季度、集数、TMDB ID），按动画分组展示，支持 TMDB 海报图片显示。

## 技术栈

- **后端**: .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL
- **前端**: React 18, TypeScript, Tailwind CSS, Radix UI, SWR, Parcel
- **下载**: qBittorrent Web API
- **AI 推断**: OpenAI / Anthropic SDK + TMDB API
- **包管理**: Yarn Berry (PnP)

## 功能

- [x] RSS 订阅源管理（静态配置 + 动态 CRUD）
- [x] 自动同步 RSS 源，创建动画信息记录
- [x] 通过 qBittorrent 进行下载 / 暂停 / 恢复 / 取消管理
- [x] 实时下载进度追踪（速度、剩余时间）
- [x] 下载完成后自动重命名（S##E## 格式，含字幕文件）
- [x] HTTP 文件浏览和流媒体播放
- [x] JWT 认证 + 刷新令牌
- [x] AI 元数据推断（OpenAI / Anthropic）— 自动识别 TMDB ID、季度、集数、字幕组
- [x] TMDB 海报图片展示
- [x] AI 推断失败后手动重试
- [x] 按动画分组的主页展示（卡片 + 剧集列表）
- [x] 当季番组发现（mikanani.me 爬取）+ 一键订阅
- [x] 后台任务仪表盘（查看状态、手动触发）
- [x] 插件事件系统（下载前 / 下载完成后钩子）
- [x] Docker Compose 一键部署
- [x] Podman / 容器化部署
- [ ] 插件系统动态加载（JavaScript / ClearScript）
- [ ] WebDAV Server
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

编辑 `SecondDimensionWatcherReDive/appsettings.json`：

| 配置项 | 说明 |
|--------|------|
| `ConnectionStrings:sdw` | PostgreSQL 连接字符串 |
| `JwtSecret` | JWT 签名密钥 |
| `Torrent:Remote:Url` | qBittorrent API 地址 |
| `FileStore:Local` | 下载文件存储路径 |
| `MikananiFeeds` | RSS 源 URL 数组 |
| `TmdbApiKey` | TMDB API 密钥 |
| `Inference:ApiKey` | AI 推断 API 密钥（可选） |
| `Inference:Provider` | `OpenAI` 或 `Anthropic` |
| `Inference:Model` | 模型名称 |

## 项目结构

```
SecondDimensionWatcherReDive/           # ASP.NET Core 主项目
SecondDimensionWatcherReDive.Framework/ # 共享抽象层
SecondDimensionWatcherReDive.Test/      # 单元测试
SecondDimensionWatcherReDive.Client/    # React 前端
Plugins/
  SecondDimensionWatcherReDive.Inference.AI/     # AI 推断引擎
  SecondDimensionWatcherReDive.Plugin.FileRenamer/ # 文件重命名插件
```

