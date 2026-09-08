# 配置版本迁移

配置顶层的 `Version` 表示配置结构版本，与应用发布版本、EF Core schema 版本和数据库 data migration 版本独立。当前结构为 `2.3.0`；未声明版本的配置按 `2.2.0` 处理。不要通过手工修改 `Version` 跳过迁移。

```yaml
Version: "2.3.0"
StateDirectory: /var/lib/sdw-redive
```

## 使用 sdw-migrate

系统包、容器和 Linux 通用压缩包均携带 `sdw-cli`，`sdw-migrate` 是指向同一程序的软链接。程序根据入口名称选择命令，也支持直接运行 `sdw-cli migrate`。

```bash
# 升级 YAML 配置；只在破坏性变更需要决定时提问
sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive

# JSON 配置也使用相同入口
sdw-migrate --config ./appsettings.json

# 自动化任务中不接受交互；需要决定时返回非零状态
sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive --non-interactive

# 没有创建软链接时直接选择子命令
sdw-cli migrate --config ./appsettings.json
```

Windows 压缩包提供 `sdw-cli.exe` 与 `install-clis.ps1`，解压后由脚本创建 `sdw-migrate.exe` 软链接。未创建链接时可直接执行 `./sdw-cli.exe migrate --config ./appsettings.json`。

`--config` 缺省时依次使用 `Config` 环境变量、已存在的 `/etc/sdw-redive/appsettings.yml` 或当前目录的 `appsettings.json`。`--working-directory` 指定旧应用工作目录，用于解析相对 `PasswordFile` 或默认 `password.json`；缺省为命令进程的当前目录。系统包服务的工作目录为 `/usr/lib/sdw-redive`，通用压缩包部署应填写原服务实际使用的目录。标准输入被重定向时也只允许静默迁移。

迁移需要读取配置、其引用的旧密码文件，并写入目标配置所在目录。完整链成功后，执行器把原始配置保存为同目录的 `<文件名>.<时间戳>-<随机标识>.bak`，再以同目录临时文件原子替换配置。JSON/YAML 会重新序列化，原注释与排版保留在备份中；JSON 支持注释和尾逗号输入。原配置软链接保留，更新其最终目标文件。Unix 保留配置文件权限，Linux 还保留原所有者和组；新备份在 Unix 使用 `0600`。

同目录的 `<文件名>.migration.lock` 文件用于协调并发 CLI/启动迁移，执行结束后保留。写入前会核对配置是否仍与最初读取的内容一致；发生并发修改时退出并要求重试。

## 启动与交互行为

主程序在注册并启动应用服务之前检查配置版本。版本过旧时，迁移器按连续的 `Up` 链计算结果。整条链能够静默完成才允许继续启动；缺少迁移路径、需要用户决定或静默升级失败时，程序返回非零状态并报告错误，不会带着部分迁移的配置启动。

启动按配置源的原有优先级逐层处理。每个参与迁移的源独立读取自己的 `Version`，缺省按 `2.2.0` 处理，不会被随程序附带的 `Version: "2.3.0"` 遮蔽。低优先级源中已明确指定的 Provider、AI 协议、状态目录和具体状态路径会作为继承值传给后续迁移；仅覆盖密钥或日志的高优先级层不会重置这些值。配置键沿用 IConfiguration 的大小写不敏感语义。

| 配置源 | 启动时的迁移方式 |
|--------|------------------|
| `appsettings.json` 及环境专用 `appsettings.*.json` | 更新对应文件并保留备份；环境专用文件按覆盖层处理 |
| UserSecrets 等其他 JSON 源 | 只在内存中补充迁移产生的变化，不改写全局机密文件 |
| 环境变量、命令行参数 | 只在各自源之后插入变化项，不修改进程环境或参数 |
| `Config` 指定的外部 YAML/JSON | 更新文件并保留备份；仍按原有行为最后加载，优先于前述源 |

当前版本且没有变化的配置源不会被完整内存快照替换。UserSecrets、环境变量和命令行的迁移只对本次启动有效；若这些源存在需要用户决定的冲突，应修正对应源中的值后重试。`sdw-migrate` 只处理 `--config` 指定的文件，不会改写其他配置源。

包安装使用 `--non-interactive`，遵循同样的规则。出现需要交互的错误后，先运行日志提示的 `sdw-migrate --config ...`，完成迁移后再重启服务或重试包配置。

只读挂载或配置目录缺少写入权限导致文件迁移失败时，主程序同样退出。先在具有写入权限的部署环境中执行迁移，再启动服务；系统包可执行 `sudo sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive`。其他部署将 `--working-directory` 换为原应用工作目录。

CLI 只呈现迁移必需的破坏性选择，例如同一设置同时存在互相冲突的新旧值时应保留哪一个。它不会询问普通新增功能的配置。迁移完成后，从网页「设置」配置 AI、下载、媒体库、通知与访问协议等功能；已有 PostgreSQL 运行时设置仍覆盖部署默认值。

## 2.2.0 → 2.3.0

当前历史包含两层：`2.2.0 → 2.2.1` 迁移 AI 字段，随后 `2.2.1 → 2.3.0` 迁移状态目录、初始密码哈希和 AI 协议默认值。

| 旧配置 | 迁移后的配置 |
|--------|--------------|
| `Inference:Provider` | `AI:Provider` |
| `Inference:ApiKey`、`BaseUrl`、`Model`、`MaxTokens` | 所选 `AI:OpenAI:*` 或 `AI:Anthropic:*` |
| `Inference:RateLimitDelayMs` | 保留原位置与原值 |
| 旧 OpenAI 配置未指定有效 `AI:OpenAI:ApiMode` | 当前层和继承值均缺省时显式保留 `ChatCompletions`；新配置的缺省协议为 `Responses` |
| `PasswordFile` | 取旧密码文件父目录作为 `StateDirectory`，保留密钥环、插件和转码缓存的默认位置 |
| `Password:Value` 或旧密码文件中的 BCrypt 哈希 | `Authentication:BootstrapPasswordHash` |

同一份配置中的 `StateDirectory` 与旧密码文件父目录不一致，且仍有状态路径依赖隐式默认值时，迁移会要求选择。`legacy` 保留已有 `StateDirectory`，并把尚未明确配置的 `DataProtection:KeyRingPath`、`PluginPlatform:RootPath`、`Transcoding:CachePath` 显式指向旧目录下的对应位置；当前层或继承层中已经明确设置的路径保持原值。`current` 让隐式路径采用 `StateDirectory`，需要在重启前自行迁移对应状态文件。迁移器只修改配置，不移动密钥环、插件或缓存文件。

迁移后的主程序只读取当前配置结构，不再加载 `password.json` 或旧的 `Password`、`PasswordFile`、`Inference` 提供商字段。`Authentication:BootstrapPasswordHash` 只用于向数据库原子导入初始哈希；数据库已有密码时以数据库为准。新安装仍从网页注册管理员，无需填写此项。数据库中的旧家庭身份会在首次成功登录时继续迁移，此过程与配置结构迁移独立。

## 添加下一个 Up

迁移接口和数据契约位于 `SecondDimensionWatcherReDive.Framework/ConfigurationMigration/`，具体实现位于 `SecondDimensionWatcherReDive.ConfigMigration`。Framework 不包含迁移执行器、文件读写或实现类。

1. 在实现项目中增加一个实现 `IConfigMigration` 的具体类，提供可访问的无参数构造函数。`Share/SecondDimensionWatcherReDive.Analyzers` 中的增量 Source Generator 会发现实现并生成 `GeneratedConfigMigrations` 注册表，无需维护手工注册清单或运行时反射扫描。
2. 通过 `Definition` 指定 `FromVersion`、`ToVersion`、说明和 `MayRequireUserIntervention`。每个 `Up` 都必须明确声明是否可能需要用户介入，版本应与已有历史连续衔接。
3. `GetRequiredChoices` 只检查当前文档并返回确实需要决定的破坏性选择，使用稳定的选项 key；无需决定时返回空集合。此方法不得修改文档或外部状态。
4. `Up` 使用已收集的选择修改内存中的 JSON 文档。不得直接写配置、修改其他文件或执行外部副作用；完整迁移链由执行器统一提交并更新版本。无破坏性的新增功能继续使用默认值。

`ConfigMigrationContext.Configuration` 只包含当前层文档，`InheritedSettings` 提供低优先级源已经迁移后的扁平键值，供解析缺省设置时参考。`IsOverlay` 表示当前源属于覆盖层：没有显式 `PasswordFile` 时，不应再次查找隐式 `password.json`，也不能把底层已有设置作为新默认值提升到当前层。新增迁移应保持这些优先级规则。

不要改写已经发布的迁移来描述下一版本。像 EF Core 的正向迁移一样叠加新的 `Up`，使较旧配置能够依次经过所有版本。
