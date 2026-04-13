# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important Rules

- **NEVER use `npm` or `npx`**. This project uses Yarn Berry (PnP). Always use `yarn` for all frontend commands.

## Build & Development Commands

```bash
# Backend
dotnet build SecondDimensionWatcherReDive.slnx                                # Build entire solution
dotnet run --project SecondDimensionWatcherReDive                             # Run backend (http://localhost:5097)
dotnet test SecondDimensionWatcherReDive.slnx                                 # Run all tests

# Frontend (in SecondDimensionWatcherReDive.Client/)
yarn install        # Install dependencies (Yarn 4.2.2 Berry with PnP)
yarn start          # Dev server on http://localhost:1234
yarn build          # Production build to dist/
yarn mock           # Mock API server on http://localhost:5097
yarn dev            # Mock server + dev server together

# Docker
docker compose up                                 # Full stack: PostgreSQL + backend + frontend

# Container publish (alternative to Docker)
dotnet publish SecondDimensionWatcherReDive/SecondDimensionWatcherReDive.csproj /t:PublishContainer
```

## Architecture

This is an anime/animation download management system (二次元观测器 Re:Dive) with a .NET 10 backend, React 18 frontend, and PostgreSQL database.

### Solution Projects

- **SecondDimensionWatcherReDive** — Main ASP.NET Core web API. Controllers, background services, download/feed implementations, SPA hosting.
- **SecondDimensionWatcherReDive.Framework** — Shared abstractions (interfaces for plugins, file download, file storage, feeds, scheduled tasks). No implementations.
- **SecondDimensionWatcherReDive.Test** — MSTest unit tests with Moq. Covers plugin events, file renaming, feed parsing, auth.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler, Tailwind CSS, and Radix UI.
- **Plugins/SecondDimensionWatcherReDive.Inference.AI** — AI inference engine (OpenAI SDK + Anthropic SDK) for metadata extraction with TMDB tool calling.
- **Plugins/SecondDimensionWatcherReDive.Plugin.FileRenamer** — Post-download file renaming with S##E## format, including subtitle files.

### Backend Data Flow

The system uses **System.Threading.Channels** for async inter-service communication:

1. User triggers download via `AnimationInfoController`
2. `RemoteTorrentDownloadClient` submits torrent to qBittorrent API, writes to `RemoteTorrentTrackRequest` channel
3. `FetchRemoteTorrentBackgroundService` polls qBittorrent status, writes to `FileDownloadStatus` channel
4. `UpdateDownloadStatusBackgroundService` updates an in-memory cache with progress (finished items expire after 5 min)
5. On completion, `DownloadCompleteRequest` channel triggers `CompleteDownloadBackgroundService` to update the DB
6. `CompleteDownloadBackgroundService` fires `OnFileDownloadCompleted` plugin event, then runs `VideoFileRenamer`

### Provider/Strategy Pattern

Download clients and file stores use a provider pattern:
- `IFileDownloadClient` / `IFileDownloadClientProvider` — pluggable download backends (currently: qBittorrent remote)
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk)
- `IFileOperator` — file rename operations routed through qBittorrent API (`TorrentFileOperator`) so qBittorrent stays aware of renames

### Plugin System

Framework defines `IPlugin`/`PluginBase` with event hooks:
- `BeforeDownloadStarted` — triggered in `FileDownloadClientProxy` before download submission
- `OnFileDownloadCompleted` — triggered in `CompleteDownloadBackgroundService` after DB commit

Event infrastructure (`PluginEvent<T>`) is fully working. Plugin discovery/loading is not yet implemented. `IJavaScriptPluginLoader` (ClearScript) is scaffolded but unimplemented.

### Background Services & Scheduled Tasks

Scheduled tasks extend `ScheduledTaskBase` (Framework/Tasks/) which provides lock-free serial execution via `Channel<TaskCompletionSource>`. Each task is paired 1:1 with a generic `ScheduledTaskBackgroundService<TTask>` that drives its timer loop and queue processing. Tasks expose `IScheduledTask` for controller discovery.

- `IScheduledTask` — interface with `Id`, `Interval`, `IsEnabled`, `LastRunAt`, `IsRunning`, `RunNowAsync`, `Enqueue`
- `ScheduledTaskBase` — abstract base with Channel-based lock-free queuing (multiple `RunNowAsync`/`Enqueue` calls serialize without locks)
- `ScheduledTaskBackgroundService<TTask>` — generic BackgroundService that hosts a single ScheduledTaskBase, runs `ProcessQueueAsync` + timer loop

Registered scheduled tasks:
- **SyncFeed** — Syncs RSS feed subscriptions every 10 minutes
- **InferAnimationMetadata** — Runs offline AI inference on unprocessed AnimationInfo records every 30 minutes
- **ScrapeSeasonBangumi** — Scrapes mikanani.me for current season anime list every 7 days

Channel-driven event processors (always running, end with BackgroundService suffix):
- **FetchRemoteTorrentBackgroundService** — Polls qBittorrent every 500ms for download status
- **UpdateDownloadStatusBackgroundService** — Caches download progress in memory
- **CompleteDownloadBackgroundService** — Finalizes downloads, triggers plugins and file renaming

### AI Inference Pipeline

AI inference is decoupled from feed sync — runs offline as a background task (`InferAnimationMetadata`).

**Flow:**
1. `SyncFeed` creates raw `AnimationInfo` records (no AI metadata)
2. `InferAnimationMetadata` picks up records where `IsAiProcessed == false` and `AiRetryCount < 3`
3. AI extracts: `tmdb_id`, `group_name`, `season`, `episode` (TMDB-normalized)
4. Name, original name, description, and poster path are fetched from TMDB API in the server's locale
5. Records are updated with metadata; `IsAiProcessed = true`
6. Failed items increment `AiRetryCount`; users can reset via `POST /api/animationinfo/{id}/retry-inference`

**Engine architecture:**
- `InferenceEngineBase` — common rate limiting (`SemaphoreSlim` + configurable delay), system prompt, tool dispatch, JSON parsing
- `OpenAiCompatibleEngine` — uses OpenAI .NET SDK with streaming (`CompleteChatStreamingAsync`), supports custom base URL
- `AnthropicCompatibleEngine` — uses Anthropic.SDK with typed message/tool APIs
- Two tool calls: `search_tmdb` (find TMDB ID) and `get_tmdb_seasons` (get season/episode structure for normalization)
- TMDB season normalization handles: merged cours, absolute episode numbering, mismatched season labels

### Controllers

- `AnimationInfoController` (`/api/animationinfo`) — CRUD for animations, download/pause/resume/cancel, grouped listing by Animation, retry AI inference
- `AuthController` (`/api/auth`) — register, login, refresh, verify
- `FileController` (`/api/file`) — file listing, playback link generation, streaming
- `FeedController` (`/api/feed`) — CRUD for RSS feed subscriptions
- `SeasonController` (`/api/season`) — current season anime discovery from mikanani.me, subgroup browsing, one-click subscribe, supports browsing other seasons
- `TasksController` (`/api/tasks`) — list background tasks with status, enqueue manual execution

### Feed Management

Feeds can be configured two ways (merged at sync time):
- Static: `MikananiFeeds` string array in `appsettings.json`
- Dynamic: `Feed` entity in PostgreSQL, managed via `FeedController`

`SyncFeed` background service runs every 10 minutes, fetches all feed URLs, and creates `AnimationInfo` records.

### Season Anime Discovery

`ScrapeSeasonBangumi` scrapes mikanani.me homepage for current season anime (HTML parsing via HtmlAgilityPack). Data cached in `SeasonBangumi` + `BangumiSubgroup` DB tables. `SeasonController` exposes:
- Browse current season (cached) or other seasons (on-demand scrape via `/Home/BangumiCoverFlowByDayOfWeek` endpoint)
- Subgroups per anime (on-demand scrape, cached 24h)
- One-click subscribe (creates `Feed` record with mikanani RSS URL)

### SPA Proxy

In development, the main project proxies non-`/api` requests to the Parcel dev server (`http://localhost:1234`) via `AspSpaService`. In production, static files are served from `wwwroot` with fallback to `index.html`.

### Authentication

JWT Bearer tokens with BCrypt password hashing. Refresh token flow via `AuthController`. All API endpoints require authentication. Frontend uses `ProtectedRoute` component to redirect unauthenticated users to `/login`. The HTTP client (`httpClient.ts`) handles automatic token refresh with deduplication on 401 responses, and throws on non-OK responses so SWR error boundaries work correctly.

### Frontend

React 18 + TypeScript with Tailwind CSS for styling and Radix UI for accessible interactive primitives (Dialog, Toast, Progress). Uses SWR for data fetching, React Router v6 for routing, lucide-react for icons. Design system follows DESIGN.md (warm parchment canvas, serif headlines, terracotta accents).

**Pages:**
- Main (`/`) — Anime card grid grouped by TMDB ID, with poster images; uncategorized section for unmatched items
- Anime Episodes (`/anime/:tmdbId`) — Episode list for a specific anime with poster header
- Downloading (`/downloading`) — Items currently being downloaded
- Downloaded (`/downloaded`) — Completed downloads with file browser
- Feeds (`/feeds`) — Subscription management + season discovery
- Tasks (`/tasks`) — Background task dashboard with manual trigger
- Login (`/login`) — Login/register with form validation

**Key components:** `AnimationInfo` (editorial row-style episode item with inline download controls, progress bar, AI retry button), `FileBrowser` (sheet/slide-over for browsing downloaded files), `SeasonDiscovery` (season anime browser with day-of-week grouping and season selector), `ProtectedRoute` (auth guard), `ToastProvider` (Radix Toast notifications).
**UI primitives:** `src/components/ui/` — Button, Card, EmptyPrompt, FormRow, Input, Pagination, PasswordInput, Progress, Sheet, Spinner, Table.

### Mock API Server

`mock-server.mjs` provides a zero-dependency mock backend for frontend development without the .NET backend, PostgreSQL, or qBittorrent. Run with `yarn dev` (starts both mock + dev server) or `yarn mock` (mock only, then `yarn start` separately).

Features: 25 anime entries with TMDB poster paths and mixed download states, grouped animations endpoint, simulated download progress, auth flow (any password), feed CRUD, season bangumi browsing, background task listing with enqueue, AI retry inference, mock file browser. Listens on port 5097 (matching the Parcel proxy target).

## Key Configuration (appsettings.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array (static feeds)
- `TmdbApiKey` — TMDB API key (used for AI inference metadata, poster images, and season info)
- `Inference:ApiKey` — AI inference API key (optional; enables metadata extraction)
- `Inference:Provider` — "OpenAI" or "Anthropic"
- `Inference:BaseUrl` — Custom API endpoint (supports OpenAI-compatible proxies)
- `Inference:Model` — Model name (e.g., "gpt-4o-mini", "claude-sonnet-4-20250514")
- `Inference:MaxTokens` — Max response tokens (default: 1024)
- `Inference:RateLimitDelayMs` — Min interval between API calls (default: 1000ms)
- `DisableCors` — Enable permissive CORS policy

EF Core migrations run automatically on application startup.
