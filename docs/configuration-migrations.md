# 配置版本迁移

配置顶层的 `Version` 表示配置结构版本，与应用发布版本、EF Core schema 版本和数据库 data migration 版本独立。当前结构为 `3.0.0`；未声明版本的配置按 `2.2.0` 处理。不要通过手工修改 `Version` 跳过迁移。文件和命令行使用 `Version`；环境覆盖层使用专用的 `SDW_CONFIG_VERSION`，通用 `VERSION`/`Version` 环境变量仅表示应用发布信息，不参与配置迁移。未声明 `SDW_CONFIG_VERSION` 的应用环境覆盖按旧结构处理。

```yaml
Version: "3.0.0"
StateDirectory: /var/lib/sdw-redive
```

## 使用 sdw-migrate

系统包、容器和 Linux 通用压缩包均携带 `sdw-cli`，`sdw-migrate` 是指向同一程序的软链接。程序根据入口名称选择命令，也支持直接运行 `sdw-cli migrate`。

```bash
# 升级外部 YAML；此例的底层只有随包基础 JSON
sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive \
  --inherit-config /usr/lib/sdw-redive/appsettings.json

# 迁移环境覆盖文件，保留基础文件中的显式路径与其他设置
sdw-migrate --config ./appsettings.Production.json --inherit-config ./appsettings.json

# 完整独立配置必须明确声明没有底层继承
sdw-migrate --config ./appsettings.json --standalone

# 自动化任务中不接受交互；继承链也不能需要尚未处理的选择
sdw-migrate --config /etc/sdw-redive/appsettings.yml --working-directory /usr/lib/sdw-redive \
  --inherit-config /usr/lib/sdw-redive/appsettings.json --non-interactive

# 没有创建软链接时直接选择子命令
sdw-cli migrate --config ./appsettings.json --standalone
```

Windows 压缩包提供 `sdw-cli.exe` 与 `install-clis.ps1`，解压后由脚本创建 `sdw-migrate.exe` 软链接。未创建链接时可直接执行 `./sdw-cli.exe migrate --config ./appsettings.json --standalone`，覆盖文件则使用相同的 `--inherit-config` 参数。

文件名以 `.json` 结尾时按 JSON 读取；其余后缀或无后缀均按 YAML 读取，保持外部 `Config` 对 `/run/secrets/sdw-config` 这类挂载路径的兼容。相同规则用于 CLI 目标和 `--inherit-config` 文件，迁移后的内容仍按选定格式写回。

`--config` 缺省时依次使用 `Config` 环境变量、已存在的 `/etc/sdw-redive/appsettings.yml` 或当前目录的 `appsettings.json`。`--working-directory` 指定旧应用进程的工作目录，用于保留密钥环、插件和缓存的旧相对状态路径；缺省为命令进程的当前目录。`--content-root` 指定旧主程序的 ContentRoot，用于读取相对 `PasswordFile` 或默认 `password.json`，缺省采用 `--working-directory`；相对值也以旧工作目录为基准。旧服务使用 `--contentRoot` 或 `ASPNETCORE_CONTENTROOT` 时，应把同一目录传给 CLI 的 `--content-root`。系统包服务的工作目录为 `/usr/lib/sdw-redive`，通用压缩包部署应填写原服务实际使用的目录。标准输入被重定向时也只允许静默迁移。

旧文件迁移必须明确其继承关系：重复的 `--inherit-config PATH` 按低到高优先级列出完整的底层来源；`--standalone` 则声明目标没有继承，不能与前者同时使用。CLI 不按文件名猜测基础/覆盖身份，也不读取其他进程的环境。没有上下文的旧覆盖文件会拒绝迁移；已经是当前版本且无需写入的文件仍可直接报告无变化。继承文件内容只读，在内存中按同一迁移链逐层计算，目标收到各层迁移后的有效值；底层若需要交互选择，应先用它自己的继承上下文迁移该层，再重试目标。提交前按规范化路径顺序取得目标及所有继承文件的迁移锁，持锁核对内容并原子替换目标；继承文件已变化或锁不可用时，目标不会写入。首次创建继承文件旁的锁需要该目录写权限；只读目录需由有权限的账户先创建兼容锁。已有继承锁只需读取权限，不要求改写继承配置。

该列表必须覆盖实际使用的低优先级配置。外部 YAML 通常继承基础 JSON、存在的 `appsettings.Production.json`，然后才是环境变量和命令行来源。自定义环境或命令行覆盖须由管理员明确整理为使用当前配置结构、按实际优先级合并的 JSON/YAML 上下文快照，再通过 `--inherit-config` 提供；快照不能包含正在迁移的目标覆盖层。仅有部分新增覆盖值的快照应放在基础文件之后；包含全部底层有效值的快照可单独提供。机密值应保存在受保护的快照文件中，不放入命令行。CLI 在迁移任何层之前，按完整输入链与目标文件选定原来最终生效的 `PasswordFile`，保持凭据文件优先级及状态路径基准。

迁移需要读取配置、其引用的旧密码文件，并写入目标配置所在目录。协作迁移进程共用保留的锁文件；首次创建时使用配置的读写访问权限，在 Linux 还按下述规则保留账户及组的 POSIX ACL 访问，在 Windows 使用受保护的配置 DACL。已有锁直接复用，不要求后续账户修改该锁的权限，也不删除其他进程可能正在使用的锁 inode。完整链成功后，执行器把原始配置保存为同目录的 `<文件名>.<时间戳>-<随机标识>.bak`，再以同目录临时文件原子替换配置。JSON/YAML 会重新序列化，原注释与排版保留在备份中；JSON 支持注释和尾逗号输入。原配置软链接保留，更新其最终目标文件。Unix 保留配置文件权限。Linux 优先保留原所有者、组及 POSIX 访问 ACL；通过共享组或 named ACL 获得写权限的非属主账户不能转移新文件的所有权时，新文件归迁移账户所有，以 named ACL 保留原属主和组的有效权限，并把新属主权限设为该账户原有的有效访问权限。转换先应用旧 ACL mask，再加入原属主权限，避免扩大其他账户访问；不能等价保留组权限时迁移失败。写入前清除原文件不具备的目录继承 ACL；新备份在 Unix 使用 `0600`，不继承额外访问 ACL。macOS 在写入前复制并核对原属主、组、mode 和完整 ACL，写入后再次核对；新备份清除继承 ACL 并使用 `0600`。无法保留访问权限时迁移失败，原配置不被替换。其他尚无权限保留实现的 Unix 平台拒绝文件迁移，但已是当前版本的配置仍可使用。Windows 临时文件和备份从创建时即使用原配置的有效 DACL，并禁止继承目录中更宽的权限；最终替换保留原配置 ACL，无法保留元数据时迁移失败，不降级为普通覆盖。

同目录的 `<文件名>.migration.lock` 文件用于协调并发 CLI/启动迁移，执行结束后保留。写入前会核对配置是否仍与最初读取的内容一致；发生并发修改时退出并要求重试。

## 启动与交互行为

主程序在注册并启动应用服务之前检查配置版本。版本过旧时，迁移器按连续的 `Up` 链计算结果。整条链能够静默完成才允许继续启动；缺少迁移路径、需要用户决定或静默升级失败时，程序返回非零状态并报告错误，不会带着部分迁移的配置启动。

启动按配置源的原有优先级逐层处理。已读取 JSON 来源的值与同一份文件字节快照绑定；后续 appsettings 或外部 Config 写入前，同样锁定这些底层文件并验证快照。底层文件已变更、消失或无法取得锁时，当前覆盖文件不会写入。每个参与迁移的源独立读取自己的 `Version`，缺省按 `2.2.0` 处理，不会被随程序附带的 `Version: "3.0.0"` 遮蔽。低优先级源中已明确指定的 Provider、AI 协议、状态目录和具体状态路径会作为继承值传给后续迁移；仅覆盖密钥或日志的高优先级层不会重置这些值。启动在改写旧字段前，先按原来的默认配置源及最后加载的外部 `Config` 确定最终 `PasswordFile`（缺省为 `password.json`）。旧密码文件原本在所有这些源之后加载；每个旧层迁移时保持该文件的最终权威，仅把绝对路径写入 `Authentication:BootstrapCredentialsFile`，不把文件中的哈希复制进 appsettings。来自旧内联设置的哈希仍在原配置层转换。凭据内容按 ContentRoot 定位；默认状态目录仍保留原进程工作目录下的解析结果。配置键沿用 IConfiguration 的大小写不敏感语义；扁平键与嵌套对象可按任意顺序混用，互不冲突的对象分支会合并，重复叶子键或标量/对象冲突会拒绝。

| 配置源 | 启动时的迁移方式 |
|--------|------------------|
| `appsettings.json` 及环境专用 `appsettings.*.json` | 更新对应文件并保留备份；环境专用文件按覆盖层处理 |
| UserSecrets 等其他 JSON 源 | 只在内存中补充迁移产生的变化，不改写全局机密文件 |
| 未加前缀的应用环境变量、命令行参数 | 只在各自源之后插入变化项，不修改进程环境或参数；`DOTNET_`、`ASPNETCORE_` 等带前缀的主机环境源不进入逐层继承或最终迁移快照 |
| `Config` 指定的外部 YAML/JSON | 更新文件并保留备份；仍按原有行为最后加载，优先于前述源 |

当前版本且没有变化的配置源不会被完整内存快照替换。UserSecrets、环境变量和命令行的迁移只对本次启动有效；若这些源存在需要用户决定的冲突，应修正对应源中的值后重试。`sdw-migrate` 只处理 `--config` 指定的文件，不会改写其他配置源。

主机配置源仍保留在实际宿主中，`ASPNETCORE_URLS`、`ASPNETCORE_CONTENTROOT` 等继续生效。即使可选的 appsettings 文件全部缺失，运行时的 `DOTNET_VERSION` 或 `ASPNETCORE_VERSION` 也不会成为应用配置的 `Version`；没有应用版本的环境配置仍按 `2.2.0` 迁移。

单独的环境来源只有包含 `Password:Value`、已知 AI/Inference 子项、专用 `SDW_CONFIG_VERSION` 等真实配置路径时才参与逐层迁移。普通的标量环境变量 `PASSWORD`、`AI`、`INFERENCE`、`AUTHENTICATION` 不视为这些配置节，也不会被迁移删除或遮蔽真实子项。

包安装使用 `--non-interactive`，显式传入随包基础 JSON 及存在的 Production JSON，遵循同样的规则；这些参数只代表标准随包来源。自定义 systemd 环境、命令行或其他来源需要管理员补充上述上下文快照。出现需要交互的错误后，先运行日志提示的 `sdw-migrate --config ...` 并带上完整继承输入，完成迁移后再重启服务或重试包配置。

只读挂载或配置目录缺少写入权限导致文件迁移失败时，主程序同样退出。先在具有写入权限的部署环境中执行迁移，再启动服务；系统包可使用上面的外部 YAML 命令，并按实际来源补充 Production 文件或上下文快照。其他部署将 `--working-directory` 换为原应用工作目录。

CLI 只呈现迁移必需的破坏性选择，例如同一设置同时存在互相冲突的新旧值时应保留哪一个。它不会询问普通新增功能的配置。迁移完成后，从网页「设置」配置 AI、下载、媒体库、通知与访问协议等功能；已有 PostgreSQL 运行时设置仍覆盖部署默认值。

## 2.2.0 → 2.3.0

前两层历史为：`2.2.0 → 2.2.1` 迁移 AI 字段，随后 `2.2.1 → 2.3.0` 迁移状态目录、初始密码哈希和 AI 协议默认值。

| 旧配置 | 迁移后的配置 |
|--------|--------------|
| `Inference:Provider` | `AI:Provider`；名称仅大小写不同视为同一 Provider，密钥、模型等其他字段仍按原值区分 |
| `Inference:ApiKey`、`BaseUrl`、`Model`、`MaxTokens` | 所选 `AI:OpenAI:*` 或 `AI:Anthropic:*` |
| `Inference:RateLimitDelayMs` | 保留原位置与原值 |
| 旧配置有效 Provider 为 OpenAI，或已包含 `AI:OpenAI` 子节 | 当前层和继承值均缺省 `ApiMode` 时显式保留 `ChatCompletions`；仅选择 Provider 而没有子节也适用，新配置的缺省协议为 `Responses` |
| `PasswordFile` | 取旧密码文件父目录作为 `StateDirectory`，保留密钥环、插件和转码缓存的默认位置 |
| 原配置中的 `Password:Value` | 在原层转换为 `Authentication:BootstrapPasswordHash` |
| 受保护旧密码文件中的 BCrypt 哈希 | 仅保存 `Authentication:BootstrapCredentialsFile` 绝对路径；启动读取后放入最终内存层 |

自 `2.3.0` 起（包括当前 `3.0.0`），配置中的 `Inference:Model` 表示自动推理的模型覆盖，与 `Inference:ProviderId` 和 `Inference:ReasoningEffort` 配合使用，迁移器保留它。旧版 `2.2.0`（含未声明版本）配置中的同名字段仍按上表迁移到 Provider 默认模型；使用新含义的环境覆盖须声明 `SDW_CONFIG_VERSION=3.0.0`。

同一份配置中的 `StateDirectory` 与旧密码文件父目录不一致，且仍有状态路径依赖隐式默认值时，迁移会要求选择。高优先级覆盖层显式设置 `StateDirectory` 时同样核验，不因已经继承 bootstrap 哈希或没有旧密码而跳过；未提供状态目录的覆盖层继续保留底层设置。`legacy` 保留已有 `StateDirectory`，并把尚未明确配置的 `DataProtection:KeyRingPath`、`PluginPlatform:RootPath`、`Transcoding:CachePath` 显式指向旧目录下的对应位置；当前层或继承层中已经明确设置的路径保持原值。`current` 让隐式路径采用 `StateDirectory`，需要在重启前自行迁移对应状态文件。迁移器只修改配置，不移动密钥环、插件或缓存文件。

迁移后的主程序只读取当前配置结构，不再隐式加载旧的 `Password`、`PasswordFile`、`Inference` 提供商字段或 `password.json`。只有显式设置 `Authentication:BootstrapCredentialsFile` 时，才读取该受保护 JSON 文件中的 `Authentication:BootstrapPasswordHash`（兼容旧 `Password:Value`），将哈希放入最终内存层；不会导入文件中的其他配置。凭据文件优先于内联哈希，空引用可显式禁用并遮蔽继承引用；交互选择当前内联哈希时会写入空引用。原凭据文件的内容和访问权限保持不变，迁移后需继续保留它。备份时将同一文件传给 `sdw-backup --password-file`，恢复到引用路径或同步调整引用。`Authentication:BootstrapPasswordHash` 只用于向数据库原子导入初始哈希；数据库已有密码时以数据库为准。新安装仍从网页注册管理员，无需填写此项。数据库中的旧家庭身份会在首次成功登录时继续迁移，此过程与配置结构迁移独立。

## 2.3.0 → 3.0.0

此步是独立新增的静默迁移，仅将配置结构版本推进到 `3.0.0`，不修改其他字段，不要求用户选择。已有 `2.2.0 → 2.2.1 → 2.3.0` 历史保留；旧配置依次经过完整的 `2.2.0 → 2.2.1 → 2.3.0 → 3.0.0` 链。即使这一步不改变其他字段，也应由迁移器更新版本并保存备份，不能手工修改 `Version` 跳过此前迁移。应用候选版本 `3.0.0-rc1` 使用配置结构版本 `3.0.0`。

## 添加下一个 Up

迁移接口和数据契约位于 `SecondDimensionWatcherReDive.Framework/ConfigurationMigration/`，具体实现位于 `SecondDimensionWatcherReDive.ConfigMigration`。Framework 不包含迁移执行器、文件读写或实现类。

1. 在实现项目中增加一个实现 `IConfigMigration` 的具体类，提供可访问的无参数构造函数。`Share/SecondDimensionWatcherReDive.Analyzers` 中的增量 Source Generator 会发现实现并生成 `GeneratedConfigMigrations` 注册表，无需维护手工注册清单或运行时反射扫描。
2. 通过 `Definition` 指定 `FromVersion`、`ToVersion`、说明和 `MayRequireUserIntervention`。每个 `Up` 都必须明确声明是否可能需要用户介入，版本应与已有历史连续衔接。
3. `GetRequiredChoices` 只检查当前文档并返回确实需要决定的破坏性选择，使用稳定的选项 key；无需决定时返回空集合。此方法不得修改文档或外部状态。
4. `Up` 使用已收集的选择修改内存中的 JSON 文档。不得直接写配置、修改其他文件或执行外部副作用；完整迁移链由执行器统一提交并更新版本。无破坏性的新增功能继续使用默认值。

`ConfigMigrationContext.WorkingDirectory` 保留旧进程工作目录，`ContentRootDirectory` 单独提供凭据文件的读取基准；启动时还会通过 `LegacyPasswordFile` 传入旧配置链最终选定的文件名。`ConfigMigrationContext.Configuration` 只包含当前层文档，`InheritedSettings` 提供低优先级源已经迁移后的扁平键值，供解析缺省设置时参考。`IsOverlay` 表示当前源属于覆盖层：不能把底层已有设置作为新默认值提升到当前层。旧凭据文件是原加载链的最终覆盖源，其受保护文件引用必须贯穿旧层迁移，不能把哈希复制到访问更宽的配置层，也不能因为已有继承值而丢失文件优先级。新增迁移应保持这些优先级规则。

不要改写已经发布的迁移来描述下一版本。像 EF Core 的正向迁移一样叠加新的 `Up`，使较旧配置能够依次经过所有版本。
