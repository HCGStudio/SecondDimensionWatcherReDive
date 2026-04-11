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

# Container publish
dotnet publish SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj /t:PublishContainer
```

## Architecture

This is an anime/animation download management system (二次元观测器 Re:Dive) with a .NET 10 backend, React 18 frontend, and PostgreSQL database.

### Solution Projects

- **SecondDimensionWatcherReDive** — Main ASP.NET Core web API. Controllers, background services, download/feed implementations, SPA hosting.
- **SecondDimensionWatcherReDive.Framework** — Shared abstractions (interfaces for plugins, file download, file storage, feeds). No implementations.
- **SecondDimensionWatcherReDive.Test** — MSTest unit tests.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler and Elastic UI.

### Backend Data Flow

The system uses **System.Threading.Channels** for async inter-service communication:

1. User triggers download via `AnimationInfoController`
2. `RemoteTorrentDownloadClient` submits torrent to qBittorrent API, writes to `RemoteTorrentTrackRequest` channel
3. `FetchRemoteTorrent` background service polls qBittorrent status, writes to `FileDownloadStatus` channel
4. `UpdateDownloadStatus` service updates an in-memory cache with progress
5. On completion, `DownloadCompleteRequest` channel triggers `CompleteDownload` service to update the DB

### Provider/Strategy Pattern

Download clients and file stores use a provider pattern:
- `IFileDownloadClient` / `IFileDownloadClientProvider` — pluggable download backends (currently: qBittorrent remote)
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk)

### Plugin System

Framework defines `IPlugin`/`PluginBase` with event hooks (`BeforeDownloadStarted`, `OnFileDownloadCompleted`). `Microsoft.ClearScript` is included for JavaScript plugin execution. The plugin system is partially implemented.

### SPA Proxy

In development, the main project proxies non-`/api` requests to the Parcel dev server (`http://localhost:1234`) via `AspSpaService`. In production, static files are served from `wwwroot` with fallback to `index.html`.

### Authentication

JWT Bearer tokens with BCrypt password hashing. Refresh token flow via `AuthController`. All API endpoints require authentication.

### Frontend

React 18 + TypeScript with Elastic UI (EUI) component library. Uses SWR for data fetching, React Router v6 for routing, Emotion for CSS-in-JS. EUI icons are tree-shaken via `appendIconComponentCache` in `App.tsx`.

## Key Configuration (appsettings.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array
- `TmdbApiKey` — TMDB API key
- `DisableCors` — Enable permissive CORS policy

EF Core migrations run automatically on application startup.
