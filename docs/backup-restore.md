# 备份、恢复与逻辑数据迁移

`sdw-backup` 提供可自动化的本地备份目标；系统包和应用容器均携带该命令。备份由 PostgreSQL custom-format dump、部署配置、`password.json`、Data Protection 密钥环和插件 manifest 组成。下载媒体、导入的原始媒体、qBittorrent 数据与 Valkey 缓存不在其中。

## 恢复目标与范围

- 建议 RPO：每日一次，重要升级前额外一次。默认 systemd timer 示例为每天 03:30，并随机延迟最多 30 分钟。
- 典型 RTO：数据库小于 10 GiB 时约 15–60 分钟；实际取决于 PostgreSQL、存储速度和重新核对媒体映射的时间。
- `downloads` 与外部媒体目录必须使用文件系统快照、NAS 快照或独立备份工具。数据库备份不能还原媒体内容。
- Data Protection 密钥环是恢复数据库内加密运行时凭据的必要条件，必须与数据库来自同一个恢复点。
- Valkey 中的会话和短期状态不恢复；恢复后用户可能需要重新登录。

每个归档包含时间戳、应用版本、最新 EF schema 版本、数据库实际字节数、最低临时空间估算和 SHA-256 文件清单；旁边的同名 `.sha256` 文件校验整个压缩或加密归档。manifest 和旁路校验文件均不包含数据库口令、JWT、上游 API key 或连接字符串。未加密归档仍包含配置和密钥文件，因此必须按秘密处理。

## 创建、列出和验证

命令读取标准 `PGHOST`、`PGPORT`、`PGUSER`、`PGPASSWORD`、`PGDATABASE`；在容器内也可直接解析已有的 `ConnectionStrings__sdw` 环境变量。创建期间会获取与应用迁移相同的 `SDWMIGR1` PostgreSQL advisory lease，并在 lease 内依次读取 schema、完成一致性 dump、再次核对 schema，然后复制 Data Protection 密钥环。密钥在数据库快照之后复制，因此归档包含 dump 内所有加密值所需密钥的安全超集。

```bash
export PGHOST=localhost PGPORT=5432 PGUSER=sdw PGDATABASE=sdw
read -rsp 'PostgreSQL password: ' PGPASSWORD && export PGPASSWORD

sudo -u sdw-redive --preserve-env=PGHOST,PGPORT,PGUSER,PGPASSWORD,PGDATABASE \
  sdw-backup create \
  --output /var/lib/sdw-redive/backups \
  --config /etc/sdw-redive/appsettings.yml \
  --password-file /var/lib/sdw-redive/password.json \
  --key-ring /var/lib/sdw-redive/data-protection-keys \
  --retention-days 14

sdw-backup list --output /var/lib/sdw-redive/backups
sdw-backup verify /var/lib/sdw-redive/backups/sdw-backup-YYYYMMDDTHHMMSSZ-PID.tar.gz
```

命令只输出最终归档路径或非敏感验证结果。最终文件及其 `.sha256` 以 `0600` 发布；失败的 `.partial` 不会被当作备份。保留清理只匹配目标目录中的 `sdw-backup-*.tar.gz[.age]` 及对应校验文件。

### age 加密

```bash
sdw-backup create ... --age-recipient 'age1...'
sdw-backup verify backup.tar.gz.age --age-identity /secure/backup-key.txt
```

私钥不写入归档、manifest 或日志。若使用 webhook，只会发送固定的 `sdw_backup_failed` 事件，不发送错误文本、路径或凭据。显式参数错误等 `exit` 路径同样通知；配置了 webhook 但没有 `curl` 时命令会立即失败：

```bash
export SDW_BACKUP_FAILURE_WEBHOOK=https://monitor.example/hooks/opaque-token
```

本地目录是当前内置 target driver。向对象存储扩展时，应在归档完成并通过本地 `verify` 后上传不可变文件；远端上传器不得读取或重新生成 manifest，也不得把 age identity 与备份存放在同一目标。

## systemd 定时执行

系统包依赖 PostgreSQL client 与 `curl`，并安装 `/etc/sdw-redive/backup.env`、`sdw-backup.service` 和 `sdw-backup.timer`，但不会在口令仍为占位符时自动启用。编辑并保护环境文件后启用：

```bash
sudoedit /etc/sdw-redive/backup.env
sudo chown root:sdw-redive /etc/sdw-redive/backup.env
sudo chmod 0640 /etc/sdw-redive/backup.env
sudo systemctl enable --now sdw-backup.timer
sudo systemctl start sdw-backup.service
sudo journalctl -u sdw-backup.service
```

升级前可执行 `sudo systemctl start sdw-backup.service`，验证新归档后再升级。应用的 pre-migration backup hook 已经持有 `SDWMIGR1` lease 时，必须把 `create --migration-lock-held` 作为 hook 参数；该选项只供持锁父进程使用，普通定时任务不得设置，否则会绕过迁移串行化。

## 容器部署

模板把 `./backups` 挂载到 `/app/backups`，并以只读方式挂载 Compose 配置。创建与验证：

```bash
mkdir -p backups && chmod 0700 backups
podman-compose exec sdw-redive sdw-backup create \
  --output /app/backups \
  --config /app/deployment/podman-compose.yml \
  --password-file /app/data/password.json \
  --key-ring /app/data/data-protection-keys
podman-compose exec sdw-redive sdw-backup verify /app/backups/sdw-backup-....tar.gz
```

生产环境可用宿主机 systemd timer 或 cron 调用上述命令。不要把数据库口令作为命令行参数；脚本使用容器已有的连接字符串环境变量。

## 灾难恢复

恢复是替换操作。先停止所有应用副本和后台任务；仅停止应用，不要停止 PostgreSQL。

1. 把目标应用安装为与备份相同的 major 版本。数据库恢复登录角色必须是超级用户，或同时具有 `CREATEDB` 且是目标数据库 owner 的成员；脚本通过 `pg_restore --role=<owner>` 保证恢复对象仍归应用 owner。不要把 `PGDATABASE` 指向 `postgres`、`template0` 或 `template1`，也不要长期提升低权限应用账号。
2. 预先挂载足够空间。脚本先验证路径、链接、所有 SHA-256、`pg_restore --list`、格式、major、精确 schema、临时空间和目标目录。必须用 `--expected-schema`（或 `SDW_EXPECTED_SCHEMA_VERSION`）传入当前应用构建所要求的完整 migration id，不能只依赖同 major 或目标库碰巧已有的版本。对于远程 PostgreSQL，脚本无法从客户端可靠读取服务端文件系统剩余字节，必须用 `--postgres-available-bytes` 提供数据库主机或监控刚采集的 default tablespace 可用量；本地可见的 tablespace 会直接用 `df` 测量。
3. 执行恢复。命令先创建 safety dump，再把归档以 `--single-transaction --exit-on-error --role=<owner>` 恢复到候选数据库；schema、对象 owner、索引和应用角色访问检查全部通过后，才用可回滚的数据库 rename 切换。任何候选恢复失败都删除候选库，目标库不变。成功后原数据库以 `sdw_previous_*` 名称保留，确认应用和备份后再由管理员删除。
4. 配置、密码、密钥环和插件 manifest 在切换前同文件系统 staging；切换失败时自动恢复原状态。旧状态仍以 `.pre-restore-*` 保留。
5. 修正文件所有者与权限，启动应用，检查 `/api/auth/allowRegister`、登录、加密运行时设置、订阅和文件浏览。

```bash
sudo systemctl stop sdw-redive
# /etc/sdw-redive 由 root 管理，因此完整恢复必须以 root 写入配置；
# 下列 PostgreSQL 变量应由 root 可读的凭据文件或当前管理会话提供。
sudo --preserve-env=PGHOST,PGPORT,PGUSER,PGPASSWORD,PGDATABASE,PGMAINTENANCEDATABASE \
  sdw-backup restore /var/lib/sdw-redive/backups/sdw-backup-....tar.gz \
  --confirm-replace \
  --expected-version 2.3.0 \
  --expected-schema 20260801000000_ExpectedMigration \
  --postgres-available-bytes 21474836480 \
  --config-destination /etc/sdw-redive/appsettings.yml \
  --password-destination /var/lib/sdw-redive/password.json \
  --key-ring-destination /var/lib/sdw-redive/data-protection-keys
sudo chown root:sdw-redive /etc/sdw-redive/appsettings.yml
sudo chmod 0640 /etc/sdw-redive/appsettings.yml
sudo chown -R sdw-redive:sdw-redive /var/lib/sdw-redive
sudo systemctl start sdw-redive
curl --fail http://127.0.0.1:5097/api/auth/allowRegister
```

容器恢复时停止应用，然后用临时容器运行 backup entrypoint。Compose 文件在容器内只读，所以先把恢复出的配置放到宿主机可写的 backups 目录，再由管理员检查并替换：

```bash
podman-compose stop sdw-redive
podman-compose run --rm --no-deps --entrypoint sdw-backup sdw-redive \
  restore /app/backups/sdw-backup-....tar.gz \
  --confirm-replace --expected-version 2.3.0 \
  --expected-schema 20260801000000_ExpectedMigration \
  --postgres-available-bytes "$MEASURED_DATABASE_FREE_BYTES" \
  --config-destination /app/backups/restored/podman-compose.yml \
  --password-destination /app/data/password.json \
  --key-ring-destination /app/data/data-protection-keys \
  --safety-directory /app/backups
# 检查并在宿主机替换 podman-compose.yml，然后：
podman-compose up -d
curl --fail http://127.0.0.1:5097/api/auth/allowRegister
```

如果版本、schema 或空间检查失败，候选库也不会创建。有效归档在 `pg_restore`、owner 或可用性检查中失败时，正式目标库仍保持原名和原内容。只有在经过人工评估后才能使用 `--allow-version-mismatch`；该开关不会跳过精确 schema、格式、校验和、dump 与空间验证。

## 逻辑 JSON 导出与导入

JWT 管理员可按类别迁移非秘密业务数据：`feeds`、`automation-policies`、`filename-rules`、`metadata-corrections`、`playback`，或 `all`。

```bash
curl --fail -H "Authorization: Bearer $TOKEN" \
  'https://sdw.example/api/data-transfer/export?categories=feeds,automation-policies,playback' \
  -o logical-export.json

jq '. + {conflictStrategy:"skip"}' logical-export.json > logical-import.json
curl --fail -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  --data-binary @logical-import.json \
  https://sdw.example/api/data-transfer/import
```

导出在一个 repeatable-read 快照内读取所有类别；导出与导入共同限制为每类 10,000 条、完整导入请求 10 MiB，因此不会生成自身无法重新导入的文件。envelope 内的 SHA-256 在任何写入前验证。`skip` 可安全重复导入；`overwrite` 更新稳定键冲突项；`fail` 在首个冲突处返回 409，事务不会提交。生产 Npgsql 重试的每个 attempt 都使用全新 scope、DbContext 和 mapper 状态。

订阅以 URL、规则以 TMDB id + pattern、人工修正以 release URL + title + publish time、播放进度以虚拟路径匹配。目标实例缺少对应 release 或虚拟文件时会明确计入 skipped，不会制造指向不存在媒体的记录。人工修正不会覆盖目标中已有同 TMDB Animation 的全局名称、原名或海报，也不会改动共享该 Animation 的其他 release；物理路径由目标实例的映射预览与事务性替换流程处理。

逻辑导出不含 JWT、登录密码、WebDAV token、Data Protection key、AI/qBittorrent 凭据、聊天内容或媒体文件。跨 major 格式、不匹配校验和、未知类别、非法数值与超大类别会在事务开始前拒绝。
