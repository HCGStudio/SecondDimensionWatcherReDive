# 构建、发布、重试与回滚

`Verify`、备份恢复演练和发布流程独立运行。PR 与 `main` push 分别触发 `Verify` 和 `Backup Restore Drill`；`main` push 同时直接触发主线预发布。主线预发布和手动 `Release` 不调用或等待 `Verify`，其运行结果也不受 `Verify` 成功、失败或取消的影响。

## PR 与主分支门禁

`.github/workflows/verify.yml` 在所有指向 `main` 的 PR 和所有 `main` push 上运行，也支持手动运行。它不提供 `workflow_call` 入口，不调用其他 workflow；验证制品仅供本次 Verify 运行内的 job 使用。它包含：

- 后端 `restore`、Release `build`、单元测试、集成测试和覆盖率门禁；
- 前端不可变安装、测试、TypeScript 类型检查、Prettier 检查、生产构建、包体积预算和 Playwright E2E；
- 在原生 GitHub runner 上分别构建 `linux/amd64` 与 `linux/arm64` 最终 `Containerfile`（不推送、不使用 QEMU），并在 amd64 runner 上用临时 PostgreSQL 启动容器，验证 EF Core 迁移、HTTP 可用性和 SPA 首页；
- FUSE 挂载 smoke、安装与迁移等交付 smoke。

仓库 branch protection 应把 `Verify` workflow 中的 `Quality gate` 设为 `main` 的 required status check。这个汇总 job 只有在后端、前端、前端 E2E、容器、FUSE 挂载和交付 smoke 六项都成功时才成功，名称保持稳定，适合 branch protection 绑定。它只汇总 Verify 内部结果，不汇总其他 workflow。

`.github/workflows/backup-restore.yml` 独立响应指向 `main` 的 PR、`main` push 和手动运行，在 PostgreSQL 16、17 上执行备份恢复演练，并使用独立的并发取消分组。演练结果由 `Backup Restore Drill` 单独报告；若要将其作为合并条件，应单独配置对应的 required status checks。

## 共享制品构建

`.github/workflows/build.yml` 由主线预发布和 `Release` 共用，只执行构建、打包与制品汇总。前端使用 `yarn build`；Linux、Windows、portable 和 FUSE 包使用传入的同一版本与 commit。容器在 amd64 的 `ubuntu-latest` 和 arm64 的 `ubuntu-24.04-arm` 上分别按 digest 构建，再合并为 `candidate-<commit>-<run>-<attempt>` 多架构镜像。发布包汇总后生成 `SHA256SUMS`。

共享构建不执行输入验证、前端包体积预算、candidate 运行 smoke、预期文件清单或 manifest 检查，也不使用 Verify 的制品或运行结果。主线预发布在 `.github/workflows/container.yml` 中保留的发布检查如下。

## 主线预发布

`Publish mainline` 直接响应 `main` push，也支持从默认分支手动运行。它使用触发事件的 `github.sha` 固定源码，独立执行以下操作：

1. 调用共享构建，生成应用包、FUSE 包、多架构镜像和校验和文件；
2. 在发布阶段核对包的 SHA-256 校验和，检查 candidate digest、双架构 OCI index、attestation 以及镜像 config 的 source/version；
3. 检查 Git tag、Release 和 registry 版本标签的状态；
4. 通过 GitHub refs API 以 create-only 操作把 `pre-<version>` Git tag 绑定到本次提交，取得该版本的发布锁；
5. 创建或安全复用同名不可变镜像标签，上传附件并发布 prerelease；
6. 最后在串行 promotion job 中更新 `prerelease-latest`，并逐字节复核其 raw manifest；若默认分支已前进，则跳过此次 moving tag 更新。

制品构建或主线发布自身的检查失败时，不会继续镜像推广和 Release 发布；Verify 或备份恢复演练失败不会阻断此流程。run 专用的候选标签只用于定位中间产物；推广始终按不可变 digest 执行，候选标签不是部署接口。

## 版本发布与 RC

版本准备约定：先通过普通 PR 同时更新根目录 `VERSION` 以及主项目的 `Version`、`AssemblyVersion`、`FileVersion`。`VERSION` 和 `Version` 使用完整版本号，支持 `X.Y.Z` 和 `X.Y.Z-rcN`（N 从 1 开始）；程序集 `AssemblyVersion`、`FileVersion` 始终使用不带 RC 后缀的 `X.Y.Z`。这些是版本准备要求，`Release` 不再执行版本格式和程序集版本一致性检查。

建议合并后从 `main` 手动运行 `Release` workflow，也可以选择其他 ref。构建和发布固定使用启动本次运行时的 `github.sha` 及该提交中的 `VERSION`；分支在构建期间前进不会阻断发布。流程不自行提交版本，也不在构建前创建 tag。

RC 与正式版使用相同的构建和发布流程。RC 创建 `vX.Y.Z-rcN` tag、`X.Y.Z-rcN` 版本镜像和标记为 prerelease 的 GitHub Release，不推广稳定版 `latest`，也不改变 GitHub 的最新正式版。`prerelease-latest` 仍由自动主线发布管理；主线发布从 `VERSION` 去掉 RC 后缀后继续分配 `pre-X.Y.Z.N`。安装指定候选时使用完整 RC 镜像标签或 digest。

可在 `docs/releases/<完整版本号>.md` 中维护该版本发行说明。发布流程会把它与容器 digest、GitHub 自动生成的提交记录一起写入 Release；未提供该文件时保留自动生成的说明。

构建成功后，流程通过 GitHub refs API 以 create-only 操作把 `v<version>` tag 绑定到本次提交；同名 tag 已存在时，此创建操作直接失败，不会继续写入版本镜像。随后按构建输出的 digest 发布 `<version>` 镜像标签，使用 `gh release create --verify-tag --draft` 创建草稿，上传全部制品后公开 Release。只有正式版 Release 成功后，独立且串行的 promotion job 才按同一 digest 更新 `latest`。发布包附带 `SHA256SUMS`，release notes 记录容器 digest；Release 流程不再重复校验 checksum、manifest、镜像身份或 registry 状态。

workflow 按职责分配权限：验证和打包使用 `contents: read`，写入镜像的构建和 promotion job 使用 `packages: write`，创建 Git tag 与 Release 的发布 job 使用 `contents: write` 和 `packages: write`。

版本 Git tag 的 create-only 写入用于拒绝重复发布。版本镜像标签仍按发布约定保持不变，但 `Release` 对 registry 的写入不具备 create-only 保证，也不再验证并复用失败运行残留的同名镜像；重试行为见下文。主线预发布仍保留原有 registry 检查和已验证镜像复用逻辑。

## 失败后的重试

- **Verify 或备份恢复演练失败**：修复原因后重跑对应 workflow，或者推送新提交。验证失败或重跑本身不会触发、取消或阻断发布；新的 `main` push 仍会独立触发主线预发布。
- **构建或发布检查失败**：重跑对应发布 workflow 的失败 job，或者推送新提交。构建失败不会进入版本发布和 moving tag 更新。
- **`Release` 发布步骤失败**：退出处理只清理本次已成功创建的对象：尽力删除本次创建的 Release，并仅在本次创建的 tag 仍指向本次提交时删除 tag。硬取消、runner 故障或清理失败可能留下对象；重试前先处理残留同名 Git tag/Release，否则 Git tag 或 Release 创建会失败。版本镜像可能保留；下一次成功取得 Git tag 后会直接将同名镜像标签写为本次构建的 digest，不再验证并复用旧 digest。
- **主线预发布步骤失败**：保留已创建的 Git tag 作为已占用版本，只清理带本次运行标记的 Release。重新运行完整流程会重新分配可用版本；不要将已占用版本绑定到其他提交。若只存在同名镜像且尚无 Git tag，原发布逻辑仍要求其双架构 OCI index、attestation 和 source/version 身份符合本次发布后才复用。
- **moving tag 更新失败**：GitHub Release 与版本镜像仍然有效；只重跑失败的 promotion job，不要重建或覆盖版本制品。
- **runner 临时故障**：Git tag 尚未创建时可以 rerun 全部 jobs。artifact 使用覆盖式上传，候选镜像含 run attempt；Git tag 或 Release 已存在时，按上面对应流程的恢复规则处理。

## 回滚

版本 tag 和已经发布的 Release 是审计记录，不应移动或覆盖。应用回滚通过把部署固定到上一个已知良好的版本标签或 digest 完成；`latest` 只是便利标签，不应作为需要严格复现的部署依据。

```bash
# 推荐：直接固定不可变 digest
podman pull ghcr.io/hcgstudio/sdw-redive@sha256:<known-good-digest>

# 或使用先前的不可变版本标签
podman pull ghcr.io/hcgstudio/sdw-redive:<known-good-version>
```

若确实需要把 `latest` 回退给使用该便利标签的部署，由仓库管理员在核对目标 release 中记录的 digest 后执行：

```bash
docker buildx imagetools create \
  --tag ghcr.io/hcgstudio/sdw-redive:latest \
  ghcr.io/hcgstudio/sdw-redive@sha256:<known-good-digest>
```

容器回滚不会自动回滚 PostgreSQL schema。发布前应备份数据库和 Data Protection key ring；如果新版本包含不可逆数据迁移，先按该版本的迁移说明恢复数据库，再启动旧应用。
