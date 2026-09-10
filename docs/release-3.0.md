# 3.0 发布目标

制定日期：2026-09-07。目标版本：`v3.0.0`。当前状态：本文列明的功能增量及 #55 文档同步已合并，正在交付首个候选 `v3.0.0-rc1`；正式版发布日期按候选验收完成情况确定，不预设日期。候选变化、升级与已知限制见 [3.0.0-rc1 发行说明](releases/3.0.0-rc1.md)。

执行跟踪：[GitHub 3.0.0 里程碑](https://github.com/HCGStudio/SecondDimensionWatcherReDive/milestone/1)。#55 已纳入该里程碑；升级与候选交付状态记录在里程碑描述中。

## 版本定位

将既有主线的订阅、下载、媒体整理、播放、家庭档案与运维能力，连同本文列出的增量功能，收敛为可长期使用的稳定大版本。用户能够完成从发现番剧、订阅下载到入库观看的现有流程，管理员能够部署、升级、诊断故障并恢复数据。

3.0 范围包括既有能力整合、本文列出的缺集补全/长期规则/容量编排等增量、阻断问题修复和发布交付。原先仅整合既有能力的冻结范围已由本次功能工作扩展；实际入口与边界见 [使用指南](library-workflows.md)。这不代表它们或既有功能已完成 3.0 候选的新装、升级、恢复及正式制品验收；P1 开发优先级不自动等于发布阻断级别。

## 当前基线

- 制定目标时的最新正式版本为 [v2.2.0](https://github.com/HCGStudio/SecondDimensionWatcherReDive/releases/tag/v2.2.0)，发布于 2026-04-26；制定目标时根目录 `VERSION` 与主项目程序集版本仍为 `2.2.0`。
- 本计划的源码基线为 [`9ca46bf`](https://github.com/HCGStudio/SecondDimensionWatcherReDive/commit/9ca46bf)。制定目标时最新主线预发布 [pre-2.2.0.130](https://github.com/HCGStudio/SecondDimensionWatcherReDive/releases/tag/pre-2.2.0.130) 指向此前的 [`8292d17`](https://github.com/HCGStudio/SecondDimensionWatcherReDive/commit/8292d17)，两者之间只有代理指引迁移的文档提交。以下能力已合并，属于 3.0 的现有基础，不再重复立项：

| 已有能力 | 已完成事项 |
| --- | --- |
| 通知与待办、AI 写操作确认与审计、请求及凭据边界 | [#7](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/7)、[#8](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/8)、[#10](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/10) |
| 大媒体库与 VFS 查询、全局搜索、缺集统计、版本升级、HLS 播放 | [#9](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/9)、[#11](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/11)、[#12](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/12) |
| 备份恢复、持久任务与 Outbox、可恢复迁移 | [#13](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/13)、[#14](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/14)、[#20](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/20) |
| 移动端与可访问性、家庭账户与独立档案、受控插件平台、前端按需加载 | [#15](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/15)、[#16](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/16)、[#17](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/17)、[#21](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/21) |

“已合并”只表示已有实现，不代表 3.0 发布候选已完成验证。README、TODOS 与架构说明由 #55 同步；功能实现和文档核对均不能代替候选发布验收。

## 必须完成的范围

以下目标覆盖既有主线与本次功能 PR。原基线能力已经合并，增量的合并状态和候选版本交付结果仍需分别确认；发现阻断问题时只修复实现与这些结果之间的差距，不借机扩大功能范围。

| 目标 | 3.0 交付结果 | 现有边界 |
| --- | --- | --- |
| **订阅到观看的完整交付** | 当前支持的订阅与自动下载策略、元数据推断及人工审核、媒体导入、搜索、版本升级、播放进度、HLS 与外部播放器入口能够协同使用；失败可从现有待办及任务入口定位。 | 沿用 qBittorrent；导入不改动原文件；播出资料未知时明确标注；缺集仅从已收录候选补全；容量准入要求可核对实际下载卷。 |
| **家庭使用与权限边界** | Admin / Member / Viewer、独立档案、播放与聊天状态、会话撤销和受限 WebDAV/VFS 设备凭据按已有规则工作，用户文档说明各角色可执行的操作。 | 媒体库共享；NFS 依赖受信网络；AI 写操作继续经过服务端确认与审计。 |
| **持续运行与受控扩展** | 发布持久任务、Outbox、重试与死信、健康探针、指标与日志的现有入口及恢复步骤；受控插件的安装、启停、故障隔离和已支持 Provider 有准确说明。 | 不承诺所有阶段恰好执行一次；插件 API 1.0 仅接入通知和存储 Provider，远程任意脚本安装不开放。 |
| **升级、恢复与可复现制品** | 提供受支持起点的 2.x → 3.0 升级及回退说明，记录候选的新装、升级和恢复结果；正式包、镜像与版本信息可追溯到同一已验证提交。 | 具体升级起点、备份范围、跨 major 限制和制品流程见下文；尚未执行的验证不能宣称完成。 |
| **[#55 文档与运行边界同步](https://github.com/HCGStudio/SecondDimensionWatcherReDive/issues/55)** | README、TODOS、架构及专题入口准确反映已实现能力，发布说明包含已知限制并指向实际操作入口。 | #55 虽标为 P2 文档工作，仍是本次发布必交付项。当前架构入口是 `AGENTS.md` 与 `docs/architecture.md`；issue 中旧 `CLAUDE.md` 引用按现结构处理。 |

## 实施顺序

1. 完成本次 P1 → P2 功能 PR 的合并与 #55 文档同步，再冻结包含这些增量的候选范围；同时整理升级、回退和 release notes。
2. 选定发布候选提交，完成现有验证及受支持部署方式的新装、升级、恢复操作，记录结果与已知限制。
3. 处理发现的发布阻断问题，并按受影响范围复用现有验证；没有新变更或未解决问题时不重复扩大验证范围。
4. 候选满足下述发布条件后，按现有发布流程交付 `v3.0.0`。新增增强另行排期，不在候选阶段追加。

## 发布条件与交付物

- #55 已完成，升级说明、release notes 与候选验证记录齐备；影响核心下载、播放、权限隔离或数据完整性的已知阻断问题已解决。文档 issue 关闭本身不等于版本可发布；未验证或被跳过的环境能力如实记录，不能计为通过。
- 发布候选复用现有 [Verify 工作流](../.github/workflows/verify.yml)、[测试矩阵](testing.md) 与 [发布流程](release-process.md)，完成现有构建、类型、格式、测试、备份恢复及交付制品验证。遵守 [AGENTS.md](../AGENTS.md)：不新增回归测试、测试专用脚本、工作流断言或同类检查。
- 发布说明覆盖新安装、升级、配置变化、已知限制与恢复方式；明确家庭档案各自保存播放/聊天状态，媒体库仍共享，NFS 依赖受信网络而非家庭账户认证。
- 制品沿用现有工作流支持的 Linux 系统包、Windows/portable 包、Linux FUSE 客户端及 `linux/amd64`、`linux/arm64` 容器；同次发布绑定同一已验证提交和版本，提供 `SHA256SUMS` 与容器 digest。
- 版本变更通过普通 PR 同步根目录 `VERSION` 和主项目 `Version`、`AssemblyVersion`、`FileVersion`；候选使用完整 `3.0.0-rcN` 版本与纯数字 `3.0.0` 程序集版本，由 `Release` workflow 创建 prerelease 和不可变候选镜像。正式发布创建 `v3.0.0` 与不可变版本镜像，并在成功后推广 `latest`；候选发布不推广 `latest`。

## 2.x 升级与恢复范围

基准升级来源定为正式版 `v2.2.0` 和制定目标时最新主线预发布 `pre-2.2.0.130`；其升级结果需在候选阶段记录，尚未完成演练。本计划不据此宣称所有历史预发布均可直接升级。

- 原地升级沿用 [迁移运维手册](migrations.md)：先保留数据库、部署配置与 Data Protection 密钥环，再启动新版本执行串行 schema/data migration。阻断失败会终止服务启动；排除故障后重启，版本化数据迁移从已保存的 checkpoint 恢复。
- 现有 `sdw-backup restore` 默认要求应用 major 相同，并核对精确 schema；逻辑 JSON 导入也拒绝跨 major。因此不承诺 2.x 归档或 JSON 直接恢复/导入 3.0。已有 2.x 归档先在匹配版本与 schema 的实例恢复，再通过应用升级迁移；若只有逻辑导出，先导入兼容的 2.x 实例，再升级该实例。升级成功后重新生成 3.0 备份与导出，详见 [备份恢复说明](backup-restore.md)。
- 备份归档不含下载媒体、导入原始媒体、qBittorrent 数据或完整插件运行数据；这些内容需要独立备份。数据库恢复不能替代媒体恢复。
- 回滚应用镜像不会回滚数据库。一旦新的家庭身份或受限设备凭据使旧 schema 无法表达其状态，降级可能被拒绝；回退到 2.x 应按升级前备份恢复相应数据库、密钥和配置，不强制删除新结构或移动已发布 tag。

## 候选功能实现状态

本次按 P1 → P2 实现以下增量，功能说明见 [订阅、下载与观看](library-workflows.md)。其代码验证记录随功能 PR 提供，3.0 发布验收仍按上述流程单独进行。

| 优先级 | 实现 |
| --- | --- |
| P1 | #46 播出感知缺集计划、#47 长期识别规则、#51 原生文件下载、#52 容量预留与等待队列 |
| P2 | #48 个人追番清单、#49 多来源订阅、#50 OP/ED 与章节、#53 自适应轮询、#54 完成处理并发 |

WebDAV 写入、内置种子下载引擎、外部种子搜索、推荐/第三方追番同步、任意远程脚本加载，以及插件下载/元数据 Provider 适配均不属于 3.0 首版范围。插件继续以 [API 1.0 已接入的通知与存储 Provider](plugin-platform.md) 为准。
