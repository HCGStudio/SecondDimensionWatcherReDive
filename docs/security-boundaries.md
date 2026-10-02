# 网络、令牌与 NFS 安全边界

## 出站 RSS 与 torrent

应用只允许绝对 HTTP(S) URL。RSS 与 torrent 请求在保存、每次重定向以及实际建连时都会重新解析 DNS；只要任一地址属于 loopback、private、link-local、共享地址、文档地址、组播或保留网段，请求即失败。因此 DNS 在校验后重新绑定到内网地址也不能绕过限制。

响应默认受 30 秒总超时、10 秒连接超时、15 秒首字节超时、3 次重定向、4 个并发请求、4 MiB RSS、8 MiB torrent 和 1000 个 RSS item 限制。RSS 会在对象反序列化前以低内存 XmlReader 预扫 item 数与嵌套深度；torrent 会先以迭代扫描器校验深度、节点、字符串、canonical integer/string 及字典 key 的严格排序/唯一性，再进入第三方解析器。info hash 直接取已验证的原始 `info` 字节范围。双栈目标会交错尝试所有已验证地址（默认间隔 250 ms），因此 IPv6 不可达时仍可回退 IPv4。可在 `OutboundHttp` 中调整。

确需访问家庭网络 RSS 时，应只加入所需的精确主机或最小 CIDR：

```yaml
OutboundHttp:
  AllowedPrivateHosts:
    - rss.home.example
  AllowedPrivateNetworks:
    - 192.168.50.20/32
```

不要把整个 RFC1918 网段加入白名单。HTTP 代理被禁用，以免目标校验与实际连接目标脱节。

应用会 fail closed 拒绝可识别的 IPv4-compatible、NAT64 well-known/local-use、6to4、Teredo 与 ISATAP 地址。6rd 和运营方自定义 NAT64 NSP 无法仅凭一个 IPv6 地址可靠识别；生产环境仍应使用主机/容器 egress ACL，只允许必要的公网目的端口，并阻断内网、链路本地和云 metadata 网段。

## JWT 与 refresh token 升级

JWT 现在强制校验签名算法、`exp`、issuer 与 audience。Refresh token family 有绝对期限，轮换在存储层以单个原子操作完成；Valkey/Redis 模式不会因请求落到不同副本而重复消费。成功刷新后旧 token 立即失效；为吸收浏览器多标签页的同一轮并发，默认 3 秒内对相同旧 token 与 JWT 返回同一后继结果，窗口结束后再次重放会撤销同一 family 的后续 token。窗口可通过 `Authentication:RefreshTokenReuseGraceSeconds` 缩短或设为 `0`。注销接口也会服务端撤销 family。

升级前签发的无期限 refresh token 使用旧缓存键，升级后会失效，用户需要重新登录一次。这是预期的 fail-closed 迁移。默认 access token 为 10 分钟、refresh family 为 30 天，可在 `Authentication` 中调整。多副本必须共享同一个 Valkey/Redis；内存回退仅适合单副本。

匿名播放资源票据默认 15 分钟过期，使用共享 Data Protection key ring 加密并绑定到虚拟路径、用户和当前 access-token 会话；URL 中的票据必须与单独的 HttpOnly、SameSite cookie 配对，单独复制 URL 不能播放。视频和字幕可并发签发并共用同一会话 cookie，负载均衡后的任意副本都能验证。所有副本必须挂载同一个 `DataProtection:KeyRingPath`；官方容器配置已将它放在应用数据持久卷中。响应禁止缓存与 Referer 传播，日志只会看到不可单独授权、不可还原路径的加密资源标识。

网页播放器使用响应中的到期时间，在票据过期前通过需 JWT 认证的生成接口重新签发视频和字幕链接，并恢复播放位置及状态。该过程不改变旧票据的绝对期限；退出、撤销或切换用户/档案后不能继续为旧身份续签。文件页的原生下载仍沿用独立的短期入口与受限续传规则。

注销成功时浏览器播放 cookie 会被删除。由于视频 Range 请求需要在有效期内重复读取，自包含票据不能做一次性消费；已经复制出的完整 URL+cookie 组合仍可能使用到其各自的最早过期时间（默认不超过 15 分钟），随后 fail closed。需要更短窗口时可降低 `Authentication:PlaybackLinkMinutes`。

## Passkey 与无密码登录

设置中的账户安全页面支持通过浏览器 WebAuthn 添加 Passkey，服务端使用固定版本的 [Fido2 .NET 验证库](https://github.com/passwordless-lib/fido2-net-lib/tree/4.1.0) 校验注册证明、签名、challenge、RP ID、origin 和用户验证标志。私钥由验证器保管；数据库仅保存凭据 ID、公钥、签名计数和显示信息。同一个凭据 ID 不能重复注册；每个账户最多保存 20 个 Passkey。

所有 Passkey 端点和密码删除操作只接受 HTTPS，HTTP localhost 也不例外。RP ID 来自当前 HTTPS 请求的主机名，origin 精确绑定协议、主机及端口，不接受客户端自行指定 origin；不允许跨 origin 嵌入式认证。代理后的 HTTPS 识别仍仅信任下文列明的 `ReverseProxy` 地址配置，并要求代理保留外部 Host。建议长期保持访问域名不变，变更主机名后旧 Passkey 不能用于新域名。

注册与认证 challenge 存在 PostgreSQL，五分钟过期，绑定用户、用途、适用时的登录会话，以及 `Secure` / `HttpOnly` / `SameSite=Strict` 浏览器 cookie；数据库原子消费阻止跨副本或并发重放。每次开始新操作会替换浏览器绑定，未完成的旧操作应重新发起。注册要求最近五分钟内认证；删除密码必须单独使用已有 Passkey 完成一次新的用户验证和签名，不会仅凭已登录状态删除。密码删除与凭据确认在事务中完成，保留当前会话并撤销该用户的其他会话。删除后通过持久标志禁止重新启用旧 bootstrap 密码；账户锁使并发的旧密码登录或重新认证不能绕过删除。

匿名登录选项统一使用可发现凭据，不返回账户的凭据 ID 或数量；用户名只绑定在服务端 challenge 中，选择其他账户的 Passkey 仍会被拒绝。会话创建时若账户已被禁用，登录返回 `401`。

Passkey 登录及敏感操作的重新认证支持无密码账户。当前不提供 Passkey 删除或密码恢复入口；删除密码前应确认验证器及其同步/备份方式在后续设备上仍可使用。

## WebDAV/FUSE 设备 token

新设备 token 使用带 pepper 的 HMAC-SHA-256，不再为每次 Range 请求执行 BCrypt。旧 BCrypt token 仍可使用，并会在第一次成功鉴权后原地迁移。请长期保存 `WebDavTokens:Pepper`；未配置时会回退到 `JwtSecret`。更换 pepper 会使已经迁移的设备 token 失效，需要重新签发。

登录/注册/refresh、logout、Basic 认证失败（以及迁移期 legacy BCrypt 校验）和 AI 接口分别有按来源 IP 的独立固定窗口限流。成功的现代 HMAC Basic 认证不消耗失败额度，VFS/WebDAV 的 Range 数据面也不受固定请求数限流。阈值位于 `RateLimit`。配置 Valkey/Redis 时，Basic 失败额度由所有副本共享，存储不可用时会 fail closed；内存模式只适合单副本。应用只接受来自 loopback 或 `ReverseProxy:KnownProxies` / `KnownNetworks` 明确信任代理的 `X-Forwarded-For` 与 `X-Forwarded-Proto`，并在认证限流前还原客户端地址。不要信任客户端所在网段；代理跨容器或跨主机时，只配置代理自身的精确地址或最小网段。

## NFS

NFS 仍默认关闭；启用时默认只监听 `127.0.0.1`，只接受 `AllowedNetworks` 中的客户端，每个 RPC 请求（包括读取、执行和写回）必须在默认 120 秒内完成。无资源访问能力的 NFS NULL procedure 始终允许标准 `AUTH_NONE` 探测；COMPOUND 默认仍要求 `AUTH_SYS`，只有显式启用 `AllowAnonymous` 才接受 `AUTH_NONE`。如果确需通过局域网导出，必须同时设置明确的监听地址和最小客户端 CIDR。例如：

```yaml
Nfs:
  Enabled: true
  BindAddress: 192.168.50.10
  AllowedNetworks:
    - 192.168.50.0/24
  AllowAnonymous: false
```

AUTH_SYS 不提供密码学身份保证；应继续使用主机防火墙或可信 VLAN 隔离 NFS 端口。
