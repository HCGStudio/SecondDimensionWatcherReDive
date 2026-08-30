# 备份、恢复与逻辑数据迁移

`sdw-backup` 提供可自动化的本地备份目标；系统包和应用容器均携带该命令。备份由 PostgreSQL custom-format dump、部署配置、`password.json`、Data Protection 密钥环和插件 manifest 组成。下载媒体、导入的原始媒体、qBittorrent 数据与 Valkey 缓存不在其中。

## 恢复目标与范围

- 建议 RPO：每日一次，重要升级前额外一次。默认 systemd timer 示例为每天 03:30，并随机延迟最多 30 分钟。
- 典型 RTO：数据库小于 10 GiB 时约 15–60 分钟；实际取决于 PostgreSQL、存储速度和重新核对媒体映射的时间。
- `downloads` 与外部媒体目录必须使用文件系统快照、NAS 快照或独立备份工具。数据库备份不能还原媒体内容。
- Data Protection 密钥环是恢复数据库内加密运行时凭据的必要条件，必须与数据库来自同一个恢复点。
- Valkey 中的会话和短期状态不恢复；恢复后用户可能需要重新登录。

每个归档包含时间戳、应用版本、最新 EF schema 版本、最低临时空间估算和 SHA-256 文件清单；旁边的同名 `.sha256` 文件校验整个压缩或加密归档。manifest 和旁路校验文件均不包含数据库口令、JWT、上游 API key 或连接字符串。未加密归档仍包含配置和密钥文件，因此必须按秘密处理。

## 创建、列出和验证

命令读取标准 `PGHOST`、`PGPORT`、`PGUSER`、`PGPASSWORD`、`PGDATABASE`；在容器内也可直接解析已有的 `ConnectionStrings__sdw` 环境变量。

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

私钥不写入归档、manifest 或日志。若使用 webhook，只会发送固定的 `sdw_backup_failed` 事件，不发送错误文本、路径或凭据：

```bash
export SDW_BACKUP_FAILURE_WEBHOOK=https://monitor.example/hooks/opaque-token
```

本地目录是当前内置 target driver。向对象存储扩展时，应在归档完成并通过本地 `verify` 后上传不可变文件；远端上传器不得读取或重新生成 manifest，也不得把 age identity 与备份存放在同一目标。

## systemd 定时执行

系统包安装 `/etc/sdw-redive/backup.env`、`sdw-backup.service` 和 `sdw-backup.timer`，但不会在口令仍为占位符时自动启用。安装 PostgreSQL client，编辑并保护环境文件后启用：

```bash
sudoedit /etc/sdw-redive/backup.env
sudo chown root:sdw-redive /etc/sdw-redive/backup.env
sudo chmod 0640 /etc/sdw-redive/backup.env
sudo systemctl enable --now sdw-backup.timer
sudo systemctl start sdw-backup.service
sudo journalctl -u sdw-backup.service
```

升级前可执行 `sudo systemctl start sdw-backup.service`，验证新归档后再升级。这样自动迁移数据库前已有明确恢复点。

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

1. 把目标应用安装为与备份相同的 major 版本。恢复脚本会在生成 safety dump 后删除并重建目标数据库，数据库登录角色必须拥有目标数据库并具有 `CREATEDB`（或使用 PostgreSQL 超级用户）；不要把 `PGDATABASE` 指向 `postgres`、`template0` 或 `template1`。默认的低权限应用账号不应长期获得 `CREATEDB`，恢复时请临时提供独立的数据库管理员凭据。
2. 预先挂载足够空间；脚本在任何数据库写入前验证路径、链接、所有 SHA-256、`pg_restore --list`、格式、major 版本、可选 schema 版本、临时空间和目标目录可写性。
3. 执行恢复。命令先在 safety directory 创建现有数据库 dump，旧配置、密码和密钥环也会重命名为 `.pre-restore-*`，可人工回退。
4. 修正文件所有者与权限，启动应用，检查 `/api/auth/allowRegister`、登录、订阅和文件浏览。

```bash
sudo systemctl stop sdw-redive
# /etc/sdw-redive 由 root 管理，因此完整恢复必须以 root 写入配置；
# 下列 PostgreSQL 变量应由 root 可读的凭据文件或当前管理会话提供。
sudo --preserve-env=PGHOST,PGPORT,PGUSER,PGPASSWORD,PGDATABASE,PGMAINTENANCEDATABASE \
  sdw-backup restore /var/lib/sdw-redive/backups/sdw-backup-....tar.gz \
  --confirm-replace \
  --expected-version 2.3.0 \
  --expected-schema 20260801000000_ExpectedMigration \
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
  --config-destination /app/backups/restored/podman-compose.yml \
  --password-destination /app/data/password.json \
  --key-ring-destination /app/data/data-protection-keys \
  --safety-directory /app/backups
# 检查并在宿主机替换 podman-compose.yml，然后：
podman-compose up -d
curl --fail http://127.0.0.1:5097/api/auth/allowRegister
```

如果版本、schema 或空间检查失败，数据库不会被写入。只有在经过人工评估后才能使用 `--allow-version-mismatch`；该开关仍不会跳过格式、校验和、dump 与空间验证。

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

导出 envelope 内的 SHA-256 在任何写入前验证。`skip` 可安全重复导入；`overwrite` 更新稳定键冲突项；`fail` 在首个冲突处返回 409，事务不会提交。订阅以 URL、规则以 TMDB id + pattern、人工修正以 release URL + title + publish time、播放进度以虚拟路径匹配。目标实例缺少对应 release 或虚拟文件时会明确计入 skipped，不会制造指向不存在媒体的记录。人工修正迁移当前元数据和审计操作，但不会覆盖物理路径；映射仍由目标实例的文件映射流程负责。

逻辑导出不含 JWT、登录密码、WebDAV token、Data Protection key、AI/qBittorrent 凭据、聊天内容或媒体文件。跨 major 格式、不匹配校验和、未知类别、非法数值与超大类别会在事务开始前拒绝。
