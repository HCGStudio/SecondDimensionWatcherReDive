# SecondDimensionWatcherReDive.FUSE (`sdwfuse`)

Linux FUSE client that mounts the SDW server's virtual filesystem read-only via the
`/api/vfs` REST endpoints. Authenticates with the same per-device Basic tokens used by
WebDAV (issue them from the WebDAV Access UI or `POST /api/webdav-tokens`).

## Requirements

- Linux (x86_64 or arm64), glibc.
- libfuse3 (runtime dependency — not bundled).
  - Debian/Ubuntu: `sudo apt install fuse3 libfuse3-3`
  - Fedora/RHEL: `sudo dnf install fuse3 fuse3-libs`
  - Arch: `sudo pacman -S fuse3`
- .NET 10 runtime to run, or use a self-contained publish.

## Build

NativeAOT is the recommended build mode — `sdwfuse` ships as a single-file native
executable with no managed runtime dependency. AOT publishing must run on Linux
(cross-compiling NativeAOT from macOS or Windows is not supported), and the build host
needs `clang`, `libfuse3-dev`, and zlib headers:

```bash
# Debian/Ubuntu build host
sudo apt install clang zlib1g-dev libfuse3-dev libssl-dev

dotnet publish SecondDimensionWatcherReDive.FUSE -c Release -r linux-x64 -p:StripSymbols=true
# Optional: -p:StripSymbols=true requires `binutils` (objcopy) — drop it if unavailable.
# Output: SecondDimensionWatcherReDive.FUSE/bin/Release/net10.0/linux-x64/publish/sdwfuse
```

Cross-compile is handled implicitly: build on the same architecture you're targeting,
or pass `-r linux-arm64` from a matching host.

The csproj wires libfuse3 through `<DirectPInvoke Include="fuse3" />` plus
`<LinkerArg Include="-lfuse3" />`, so the AOT image issues direct calls to
`fuse_main_real` instead of going through `DllImportResolver`. libfuse3 stays
dynamically linked — the .so on the deploy host must match the kernel's FUSE ABI.

If you need a managed-runtime build (e.g. for development on macOS), set `PublishAot=false`:

```bash
dotnet publish SecondDimensionWatcherReDive.FUSE -c Release -r linux-x64 \
    --self-contained false -p:PublishAot=false
```

## Usage

```bash
sdwfuse mount /mnt/sdw \
    --server http://sdw-server:5097 \
    --username sdw-AAAAAAAA \
    --password <token-from-webdav-access> \
    --foreground

# In another shell:
ls /mnt/sdw
mpv "/mnt/sdw/<anime>/<group>/<file>.mkv"

# Unmount when done:
fusermount3 -u /mnt/sdw
```

### Options

| Flag | Default | Notes |
| --- | --- | --- |
| `--server <url>` | (required) | HTTP/HTTPS base URL of the SDW server. |
| `--username <name>` | (required) | Username from the WebDAV access page. |
| `--password <token>` | (required) | Plaintext token shown once when issued. |
| `--cache-ttl <seconds>` | `5` | TTL for stat/list caches. `0` disables caching. |
| `--allow-other` | off | Add `allow_other` mount option (needs `user_allow_other` in `/etc/fuse.conf`). |
| `--foreground`, `-f` | off | Run in the foreground (do not daemonize). |
| `--debug`, `-d` | off | Enable libfuse debug logging plus verbose client logs. |
| `--user-agent <ua>` | `sdwfuse/<ver>` | Override the User-Agent header. |

Environment-variable fallbacks: `SDW_FUSE_SERVER`, `SDW_FUSE_USERNAME`, `SDW_FUSE_PASSWORD`.

## Behavior

- **Read-only.** All write operations return `EROFS`. The kernel page cache is enabled,
  so multiple readers of the same file share bytes for free.
- **Range requests** are issued for every `read()`, so streaming a large file does not
  pull the whole body. mpv/VLC seek-while-streaming works the same as it does over WebDAV.
- **Caching.** `stat` results and directory listings are cached for `--cache-ttl` seconds
  (default 5). Newly-uploaded files become visible after the TTL elapses or after the
  containing directory is re-listed by another process.
- **Permissions.** Files are advertised as `0444` and directories as `0555`, owned by the
  user that ran `sdwfuse` (effective uid/gid).
- **Auth failures** map to `EACCES`, network failures to `EIO`, missing paths to `ENOENT`.

## Troubleshooting

- `Failed to load library 'fuse3'` — install `libfuse3-3` (Debian/Ubuntu) or the
  equivalent package for your distro.
- `fusermount3: option allow_other only allowed if 'user_allow_other' is set in
  /etc/fuse.conf` — either drop `--allow-other` or edit `/etc/fuse.conf`.
- `Authentication failed` on startup — re-issue a token in the SDW UI; the plaintext
  token is shown once and cannot be recovered.
- Empty mount with no errors — verify your account can list the same paths via WebDAV at
  `<server>/webdav/`. If WebDAV works but FUSE does not, file an issue with
  `--debug` output attached.
