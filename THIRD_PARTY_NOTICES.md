# Third-Party Notices for SecondDimensionWatcher Re:Dive

This project is licensed under the **Apache License 2.0** (see [`LICENSE`](LICENSE)).
It bundles, redistributes, or links against the third-party software listed
below. This file exists to satisfy the notice / attribution requirements those
licenses impose on downstream distributors. Components are used as published.

If you ship our binaries or container images, you are also distributing some of
these components — please carry this notice along.

For dependencies specific to the Linux FUSE client (`sdwfuse`), see also
[`SecondDimensionWatcherReDive.FUSE/THIRD_PARTY_NOTICES.md`](SecondDimensionWatcherReDive.FUSE/THIRD_PARTY_NOTICES.md).

---

## Backend (NuGet)

The .NET backend, plugins, and analyzers reference the following packages.
Transitive dependencies are not enumerated; their notices flow through the
direct dependencies listed here.

| Package | License | Upstream |
| --- | --- | --- |
| AspSpaService | MIT | https://github.com/AntonyCorbett/AspSpaService |
| BCrypt.Net-Next | MIT | https://github.com/BcryptNet/bcrypt.net |
| BencodeNET | MIT | https://github.com/Krusen/BencodeNET |
| HtmlAgilityPack | MIT | https://github.com/zzzprojects/html-agility-pack |
| Microsoft.AspNetCore.Authentication.JwtBearer | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.AspNetCore.OpenApi | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.AspNetCore.SpaProxy | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.AspNetCore.SpaServices.Extensions | MIT | https://github.com/dotnet/aspnetcore |
| Microsoft.ClearScript.Complete / .Core / .V8 | MIT | https://github.com/microsoft/ClearScript |
| Microsoft.EntityFrameworkCore.Design | MIT | https://github.com/dotnet/efcore |
| Microsoft.Extensions.* (Hosting, Http, Logging, Options, DI, Configuration, Caching) | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Hosting.Systemd | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Caching.StackExchangeRedis | MIT | https://github.com/dotnet/aspnetcore |
| NetEscapades.Configuration.Yaml | MIT | https://github.com/andrewlock/NetEscapades.Configuration |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License | https://github.com/npgsql/efcore.pg |
| Swashbuckle.AspNetCore | MIT | https://github.com/domaindrivendev/Swashbuckle.AspNetCore |
| TMDbLib | MIT | https://github.com/LordMike/TMDbLib |

The PostgreSQL License (used by Npgsql) is a permissive license functionally
equivalent to the MIT/BSD family; full text:
https://opensource.org/license/postgresql.

### Microsoft.ClearScript.V8 — embedded V8 engine

`Microsoft.ClearScript.V8` ships native binaries of Google V8. The V8 engine
itself is **BSD-3-Clause** (with portions under other compatible licenses);
its license text travels inside the package. See
https://chromium.googlesource.com/v8/v8/+/refs/heads/main/LICENSE.

---

## Frontend (npm — `SecondDimensionWatcherReDive.Client/`)

The React/TypeScript SPA bundles the following packages into `dist/` at build
time. All are MIT-licensed unless noted.

| Package | License | Upstream |
| --- | --- | --- |
| react / react-dom | MIT | https://github.com/facebook/react |
| react-router | MIT | https://github.com/remix-run/react-router |
| swr | MIT | https://github.com/vercel/swr |
| @radix-ui/react-dialog, react-dropdown-menu, react-progress, react-toast | MIT | https://github.com/radix-ui/primitives |
| tailwindcss + @tailwindcss/postcss + @tailwindcss/typography | MIT | https://github.com/tailwindlabs/tailwindcss |
| postcss | MIT | https://github.com/postcss/postcss |
| artplayer | MIT | https://github.com/zhw2590582/ArtPlayer |
| artplayer-proxy-mediabunny | MIT | https://github.com/zhw2590582/ArtPlayer |
| mediabunny | **MPL-2.0** | https://github.com/Vanilagy/mediabunny |
| media-captions | MIT | https://github.com/vidstack/media-captions |
| matroska-subtitles | MIT | https://github.com/mathiasvr/matroska-subtitles |
| hls.js | Apache-2.0 | https://github.com/video-dev/hls.js |
| clsx | MIT | https://github.com/lukeed/clsx |
| dayjs | MIT | https://github.com/iamkun/dayjs |
| i18next + react-i18next + i18next-browser-languagedetector | MIT | https://github.com/i18next/i18next |
| lucide-react | **ISC** | https://github.com/lucide-icons/lucide |
| react-markdown | MIT | https://github.com/remarkjs/react-markdown |
| remark-gfm | MIT | https://github.com/remarkjs/remark-gfm |
| tailwind-merge | MIT | https://github.com/dcastil/tailwind-merge |

Build-only / development dependencies (Parcel, Prettier, TypeScript, etc.)
are listed in `SecondDimensionWatcherReDive.Client/package.json` —
they are not embedded in shipping artifacts and are not enumerated here.

### Browser media support

`mediabunny` is distributed under MPL-2.0; modifications to MPL-covered files
must remain available under that license. This project does not modify its
sources. `hls.js` provides Media Source Extensions playback for the server-side
HLS fallback on browsers without native HLS support.

### FFmpeg / ffprobe runtime dependency

Server-side media probing, remuxing, transcoding, segmentation, and WebVTT
conversion invoke the separately installed FFmpeg command-line tools. Official
container images install their base distribution's FFmpeg package; Linux system
packages list FFmpeg as a runtime dependency. FFmpeg's effective
license depends on the codecs enabled by the distributor (the current container
build is GPL-licensed); downstream redistributors must carry the corresponding
distro package notices and source offer. The application does not statically link
FFmpeg or copy its libraries into the .NET binaries.

---

## Container base images

Official container builds (`Containerfile`, published to GHCR) are layered on
top of:

- `mcr.microsoft.com/dotnet/sdk:10.0` (build stage) — MIT licensed,
  https://github.com/dotnet/dotnet-docker
- `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime stage) — MIT licensed,
  https://github.com/dotnet/dotnet-docker
- `node:24` (frontend build stage) — MIT licensed,
  https://github.com/nodejs/docker-node

These images themselves bundle further OS-vendor packages (Debian/Alpine
userspace, OpenSSL, glibc/musl, ICU, etc.) under their own licenses; consult
the upstream image repositories for the full breakdown.

---

## Native dependencies of `sdwfuse` (the FUSE client)

The Linux-only FUSE client at `SecondDimensionWatcherReDive.FUSE/` has its
own compliance considerations that are documented in detail in
[`SecondDimensionWatcherReDive.FUSE/THIRD_PARTY_NOTICES.md`](SecondDimensionWatcherReDive.FUSE/THIRD_PARTY_NOTICES.md).
The short version:

- **libfuse3** — LGPL-2.1-only, dynamically linked at runtime against the
  user's distro-installed `libfuse3.so.3`. Never bundled.
- **.NET 10 NativeAOT runtime** — MIT, statically linked into the
  `sdwfuse` binary by the AOT compiler.

---

## External services we talk to but do not redistribute

Listed for transparency only; nothing from these is bundled with our binaries
or container images, and they impose no notice obligation on us:

- **The Movie Database (TMDB) API** — used for anime metadata lookups via
  `TMDbLib`. Subject to TMDB's own terms of use; an API key is required and
  the user supplies their own.
- **qBittorrent Web API** — used for download orchestration. qBittorrent
  runs as a separate process / container and is not redistributed by this
  project.
- **mikanani.me** — RSS feeds and HTML scraping for season discovery.

---

If you spot a package that should be on this list and isn't, please file an
issue at https://github.com/mahoshojoHCG/SecondDimensionWatcherReDive/issues.
