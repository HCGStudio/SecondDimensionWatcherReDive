# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development Commands

```bash
# Backend
dotnet build SecondDimensionWatcherReDive.sln    # Build entire solution
dotnet run --project SecondDimensionWatcherReDive # Run backend (http://localhost:5097)
dotnet test                                       # Run all tests

# Frontend (in SecondDimensionWatcherReDive.Client/)
yarn install        # Install dependencies (Yarn 4.2.2 Berry with PnP)
yarn start          # Dev server on http://localhost:1234
yarn build          # Production build to dist/

# Docker
docker compose up                                 # Full stack: PostgreSQL + backend + frontend

# Container publish (alternative to Docker)
dotnet publish SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj /t:PublishContainer
```

## Architecture

This is an anime/animation download management system (二次元观测器 Re:Dive) with a .NET 10 backend, React 18 frontend, and PostgreSQL database.

### Solution Projects

- **SecondDimensionWatcherReDive** — Main ASP.NET Core web API. Controllers, background services, download/feed implementations, SPA hosting.
- **SecondDimensionWatcherReDive.Framework** — Shared abstractions (interfaces for plugins, file download, file storage, feeds). No implementations.
- **SecondDimensionWatcherReDive.Test** — MSTest unit tests with Moq. Covers plugin events, file renaming, feed parsing, auth.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler and Elastic UI.
- **Plugins/SecondDimensionWatcherReDive.Inference.AI** — AI inference engine (OpenAI/Anthropic compatible) for metadata extraction.
- **Plugins/SecondDimensionWatcherReDive.Plugin.FileRenamer** — Post-download file renaming with S##E## format.

### Backend Data Flow

The system uses **System.Threading.Channels** for async inter-service communication:

1. User triggers download via `AnimationInfoController`
2. `RemoteTorrentDownloadClient` submits torrent to qBittorrent API, writes to `RemoteTorrentTrackRequest` channel
3. `FetchRemoteTorrent` background service polls qBittorrent status, writes to `FileDownloadStatus` channel
4. `UpdateDownloadStatus` service updates an in-memory cache with progress (finished items expire after 5 min)
5. On completion, `DownloadCompleteRequest` channel triggers `CompleteDownload` service to update the DB
6. `CompleteDownload` fires `OnFileDownloadCompleted` plugin event, then runs `VideoFileRenamer`

### Provider/Strategy Pattern

Download clients and file stores use a provider pattern:
- `IFileDownloadClient` / `IFileDownloadClientProvider` — pluggable download backends (currently: qBittorrent remote)
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk)
- `IFileOperator` — file rename operations routed through qBittorrent API (`TorrentFileOperator`) so qBittorrent stays aware of renames

### Plugin System

Framework defines `IPlugin`/`PluginBase` with event hooks:
- `BeforeDownloadStarted` — triggered in `FileDownloadClientProxy` before download submission
- `OnFileDownloadCompleted` — triggered in `CompleteDownload` after DB commit

Event infrastructure (`PluginEvent<T>`) is fully working. Plugin discovery/loading is not yet implemented. `IJavaScriptPluginLoader` (ClearScript) is scaffolded but unimplemented.

### Controllers

- `AnimationInfoController` (`/api/animationinfo`) — CRUD for animations, download/pause/resume/cancel
- `AuthController` (`/api/auth`) — register, login, refresh, verify
- `FileController` (`/api/file`) — file listing, playback link generation, streaming
- `FeedController` (`/api/feed`) — CRUD for RSS feed subscriptions

### Feed Management

Feeds can be configured two ways (merged at sync time):
- Static: `MikananiFeeds` string array in `appsettings.json`
- Dynamic: `Feed` entity in PostgreSQL, managed via `FeedController`

`SyncFeed` background service runs every 10 minutes, fetches all feed URLs, and creates `AnimationInfo` records.

### SPA Proxy

In development, the main project proxies non-`/api` requests to the Parcel dev server (`http://localhost:1234`) via `AspSpaService`. In production, static files are served from `wwwroot` with fallback to `index.html`.

### Authentication

JWT Bearer tokens with BCrypt password hashing. Refresh token flow via `AuthController`. All API endpoints require authentication. Frontend uses `ProtectedRoute` component to redirect unauthenticated users to `/login`.

### Frontend

React 18 + TypeScript with Elastic UI (EUI) component library. Uses SWR for data fetching, React Router v6 for routing, Emotion for CSS-in-JS. EUI icons are tree-shaken via `appendIconComponentCache` in `App.tsx`.

**Pages:** Main (all animations), Downloading, Downloaded, Feeds (subscription management), Login.
**Key components:** `FileBrowser` (flyout for browsing downloaded files), `ProtectedRoute` (auth guard), `ToastProvider` (notifications).

## Key Configuration (appsettings.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array (static feeds)
- `TmdbApiKey` — TMDB API key
- `Inference:ApiKey` — AI inference API key (optional; enables metadata extraction)
- `Inference:Provider` — "OpenAI" or "Anthropic"
- `DisableCors` — Enable permissive CORS policy

EF Core migrations run automatically on application startup.
