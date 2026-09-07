# 数据库迁移运维手册

应用启动时先在专用 PostgreSQL 会话上取得全局 advisory lock，再依次运行备份钩子、EF Core schema migration 和版本化 data migration。Kestrel 与后台任务只会在所有阻断型迁移成功后启动，因此 `/health/ready` 在迁移完成前不可用，完成后返回 HTTP 200。多副本同时启动时只有持锁副本执行迁移，其余副本等待；等待可被 SIGINT/SIGTERM 安全取消。

## 发布前备份

生产升级前应生成经过恢复演练的 PostgreSQL 备份，并确认下载目录与 Data Protection key ring 也在备份策略内。可以由发布系统在启动应用前执行，也可以配置内置的 pre-migration hook：

```json
{
  "Migration": {
    "Timeout": "01:00:00",
    "BackupExecutable": "/usr/local/sbin/backup-sdw-before-migration",
    "BackupArguments": ["--database", "sdw", "--output", "/backups/sdw.dump"],
    "BackupTimeout": "00:30:00",
    "RequireBackup": true
  }
}
```

参数直接传给 executable，不经过 shell；需要时间戳、密钥管理或上传对象存储时应放在权限受限的 wrapper 中。hook 非零退出、超时或缺失（当 `RequireBackup=true`）都会在任何 migration 之前终止启动。备份程序应使用 PostgreSQL 一致性快照（例如 `pg_dump --format=custom`），并把凭据放入受保护的 `.pgpass`/secret，而不是参数或日志。

`Migration:Timeout` 覆盖等待全局锁、备份、schema migration 与 data migration 的总时长，默认一小时。超时与宿主关闭使用同一取消路径；若 data migration 已开始，其状态会保存为 `failed` 并保留最后 checkpoint。

## 状态与诊断

每个 data migration 以 `(Key, Version)` 保存一行：`Status`、`Checkpoint`、`StartedAt`、`FinishedAt`、`UpdatedAt`、`AttemptCount` 和 `LastErrorSummary`。状态值为 `0=pending`、`1=running`、`2=failed`、`3=completed`。

应用正常可用时，使用 JWT 调用：

```text
GET /api/migrations
POST /api/migrations/{key}/{version}/retry
GET /health/ready
```

阻断型 migration 失败时应用不会监听 HTTP。此时从日志取得完整异常，并使用只读 SQL 查看摘要：

```sql
SELECT "Key", "Version", "Status", "Checkpoint", "AttemptCount",
       "StartedAt", "FinishedAt", "LastErrorSummary"
FROM "MigrationMarkers"
ORDER BY "UpdatedAt" DESC;
```

schema migration 在状态表可用前失败时可能没有 data-migration 状态行，数据库日志与应用启动日志是诊断来源。

## 恢复与重试

1. 保留失败时的数据库备份和应用日志，不要先手工写入 `completed`。
2. 修复错误根因，例如缺失挂载、权限、磁盘空间或无效数据。
3. 重启阻断型失败的实例。runner 会把 `failed` 或进程中断遗留的 `running` 状态作为新 attempt，从最后一个持久 checkpoint 自动恢复。
4. 对允许应用继续启动的非阻断型 migration，可调用 retry API；只有当前为 `failed` 的已注册版本会被接受。重试也会取得同一个 PostgreSQL advisory lock。
5. 确认状态为 `completed`、readiness 为 200，再继续滚动发布剩余副本。

`MigrateFileMappings v2` 在每个稳定排序批次后保存 checkpoint。文件映射写入本身是幂等的；若进程恰好在数据提交后、checkpoint 提交前退出，恢复时最多重放一个批次，已存在映射会被安全跳过。取消或未接受的单项失败会记录为 `failed`，绝不会写 `completed`。

只有在已验证代码无法自动恢复且已有可还原备份时，才应手工修改 checkpoint。修改前停止所有副本，并同时记录变更原因、原值与操作者。不要删除或伪造 EF Core 的 `__EFMigrationsHistory` 行。

## 回滚

应用版本回滚不等于数据库回滚。先判断旧版本是否兼容新 schema；不兼容时停止所有副本，从发布前备份恢复数据库，再部署旧版本。不要在仍有实例读写数据库时运行破坏性的 `dotnet ef database update <old-migration>`。
