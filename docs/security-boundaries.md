# 网络、令牌与 NFS 安全边界

## 出站 RSS 与 torrent

应用只允许绝对 HTTP(S) URL。RSS 与 torrent 请求在保存、每次重定向以及实际建连时都会重新解析 DNS；只要任一地址属于 loopback、private、link-local、共享地址、文档地址、组播或保留网段，请求即失败。因此 DNS 在校验后重新绑定到内网地址也不能绕过限制。

响应默认受 30 秒总超时、10 秒连接超时、15 秒首字节超时、3 次重定向、4 个并发请求、4 MiB RSS、8 MiB torrent 和 1000 个 RSS item 限制。可在 `OutboundHttp` 中调整。

确需访问家庭网络 RSS 时，应只加入所需的精确主机或最小 CIDR：

```yaml
OutboundHttp:
  AllowedPrivateHosts:
    - rss.home.example
  AllowedPrivateNetworks:
    - 192.168.50.20/32
```

不要把整个 RFC1918 网段加入白名单。HTTP 代理被禁用，以免目标校验与实际连接目标脱节。

## JWT 与 refresh token 升级

JWT 现在强制校验签名算法、`exp`、issuer 与 audience。Refresh token family 有绝对期限，成功刷新后旧 token 立即失效；重放旧 token 会撤销同一 family 的后续 token。注销接口也会服务端撤销 family。

升级前签发的无期限 refresh token 使用旧缓存键，升级后会失效，用户需要重新登录一次。这是预期的 fail-closed 迁移。默认 access token 为 10 分钟、refresh family 为 30 天，可在 `Authentication` 中调整。

匿名播放链接默认 15 分钟过期，限定到生成时的虚拟路径，响应禁止缓存与 Referer 传播。服务端日志不再记录完整播放 URL。

## WebDAV/FUSE 设备 token

新设备 token 使用带 pepper 的 HMAC-SHA-256，不再为每次 Range 请求执行 BCrypt。旧 BCrypt token 仍可使用，并会在第一次成功鉴权后原地迁移。请长期保存 `WebDavTokens:Pepper`；未配置时会回退到 `JwtSecret`。更换 pepper 会使已经迁移的设备 token 失效，需要重新签发。

登录/注册/refresh、Basic 文件访问和 AI 接口分别有按来源 IP 的固定窗口限流。阈值位于 `RateLimit`。

## NFS

NFS 仍默认关闭；启用时默认只监听 `127.0.0.1`，只接受 `AllowedNetworks` 中的客户端，120 秒无请求会关闭连接，并拒绝 `AUTH_NONE`。如果确需通过局域网导出，必须同时设置明确的监听地址和最小客户端 CIDR。例如：

```yaml
Nfs:
  Enabled: true
  BindAddress: 192.168.50.10
  AllowedNetworks:
    - 192.168.50.0/24
  AllowAnonymous: false
```

AUTH_SYS 不提供密码学身份保证；应继续使用主机防火墙或可信 VLAN 隔离 NFS 端口。
