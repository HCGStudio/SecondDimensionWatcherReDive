# 发布质量门禁、重试与回滚

本项目把验证、制品构建和发布拆成三层。任何可变镜像标签或 GitHub Release 都只能由已经通过完整验证的提交产生。

## PR 与主分支门禁

`.github/workflows/verify.yml` 在所有指向 `main` 的 PR 和所有 `main` push 上运行，并且可被正式发布流程复用。它包含：

- 后端 `restore`、Release `build`、单元测试和集成测试；
- 前端不可变安装、测试、TypeScript 类型检查、Prettier 检查和生产构建；
- 最终 `Containerfile` 构建（不推送），随后用临时 PostgreSQL 启动容器，验证 EF Core 迁移、HTTP 可用性和 SPA 首页。

仓库 branch protection 应把 `Verify` workflow 中的 `Quality gate` 设为 `main` 的 required status check。这个汇总 job 只有在后端、前端和容器三项都成功时才成功，名称保持稳定，适合 branch protection 绑定。

## 主线预发布

`Publish verified mainline` 只响应成功的 `main` push 验证（手动运行时会先复用同一验证 workflow）。它对准确的已验证 commit SHA 执行以下操作：

1. 所有 Linux、Windows、portable 和 FUSE 制品使用同一版本与 commit 构建；
2. 多架构容器只先推送本次 run 专用的 `candidate-<commit>-<run>` 候选，并记录不可变 manifest digest；
3. 检查全部预期文件、SHA-256 校验和以及 `linux/amd64`、`linux/arm64` manifest；
4. 上传完整 draft prerelease，再从该 digest 推广 `pre-<version>` 与 `prerelease-latest`；
5. 最后发布 prerelease 并创建 `pre-<version>` Git tag。

测试或制品构建失败时，不会执行镜像推广和 release job。run 专用的候选标签只用于定位中间产物；推广始终按不可变 digest 执行，候选标签不是部署接口。

## 正式发布

版本变更必须先通过普通 PR 同时更新根目录 `VERSION` 以及主项目的 `AssemblyVersion`、`FileVersion`。合并后，从 `main` 手动运行 `Release` workflow；workflow 不再自行提交版本或提前创建 tag。

正式流程会再次运行完整门禁，构建并检查所有目标制品与多架构镜像。构建期间若 `main` 已前进，发布会失败，避免给旧提交打新 tag。`<version>` 与 `latest` 先从本次不可变 digest 推广；最后通过 GitHub refs API 以 create-only 操作把正式 `v<version>` tag 原子绑定到已验证提交，校验目标 SHA 后才使用 `--verify-tag` 创建包含全部制品的 Release。若同名 bare tag 在构建期间出现，原子创建会失败；若制品上传失败，本次创建的未完成 Release 和仍指向已验证提交的 tag 会被清理，以便安全重试。发布包附带 `SHA256SUMS`，release notes 记录容器 digest。

workflow 使用最小权限：验证只有 `contents: read`，构建候选镜像的 job 才有 `packages: write`，最终发布 job 才有 `contents: write` 和 `packages: write`。

## 失败后的重试

- **验证或构建失败**：修复原因后重新运行失败的 workflow，或者推送新提交。不会产生 `latest`、正式版本标签或公开的不完整 release。
- **`main` 在正式构建期间前进**：在最新 `main` 上重新运行 `Release`；不要给旧 workflow 强行放行。
- **最终发布前失败**：GitHub Release 会保留为 draft，公众不可见。确认其 commit、附件和容器 digest 后，删除该 draft，再重新运行 workflow；流程会重新校验完整产物。不要复用来自不同 run 的附件。
- **runner 临时故障**：可以 rerun 全部 jobs。候选镜像以 commit 标识，而实际推广始终使用当前 run 返回并复核的 digest。

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
