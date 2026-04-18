# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important Rules

- **NEVER use `npm` or `npx`**. This project uses Yarn Berry (PnP). Always use `yarn` for all frontend commands.
- **NEVER reference `ApplicationContext` directly** outside of `Repositories/` implementations, `Program.cs` (DI + migrations), and EF Core migration files. All data access goes through repository interfaces defined in `Framework/DataRepository/`.
- **Async method conventions in interfaces**: All interface methods returning `Task` or `Task<T>` must (1) have names ending with `Async`, (2) accept a `CancellationToken cancellationToken` parameter, and (3) must NOT have default values on `CancellationToken` in interface definitions. The parameter must be named `cancellationToken` (not `ct`).

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

# Podman / Container
cd deployments && podman-compose up -d             # Full stack: PostgreSQL + qBittorrent + app
podman build -f Containerfile -t sdw-redive .      # Build container image locally
```

## Architecture

This is an anime/animation download management system (二次元观测器 Re:Dive) with a .NET 10 backend, React 18 frontend, and PostgreSQL database.

### Solution Projects

- **SecondDimensionWatcherReDive** — Main ASP.NET Core web API. Internal controllers (`Controllers/`), external API DTOs (`Controllers/External/`), EF Core repository implementations (`Repositories/`), EF entity models (`Models/`), background services, download/feed implementations, SPA hosting.
- **SecondDimensionWatcherReDive.Framework** — Shared abstractions: domain records and repository interfaces (`DataRepository/`), plugin interfaces, file download/storage, feeds, scheduled tasks, inference. Also defines core AI tool contracts (`AI/`): `ITool` (static abstract `Definition` + `ExecuteAsync`), `IToolResult` (`object? Result` + `bool IsSuccess`), `ToolDefinition` (with `Create<TParams>` JSON Schema generation), and the `[Tool<TParam>]` attribute (`Attributes/`) consumed by the source generator.
- **SecondDimensionWatcherReDive.Test** — MSTest unit tests with Moq. Covers controllers, services, scheduled tasks, plugin events, feed parsing, auth.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler, Tailwind CSS, and Radix UI.
- **Plugins/SecondDimensionWatcherReDive.AI** — AI engine abstraction with provider/engine split. Defines `IAIEngine` (streaming chat interface with tool-call support) and `IAIProvider` (provider-specific API call abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync`). The unified `AIEngine` implements the multi-round tool execution loop and delegates API calls to `IAIProvider`. Two provider implementations: `OpenAIProvider` (SSE streaming via OpenAI-compatible HTTP API, supports custom base URLs for Ollama/vLLM) and `AnthropicProvider` (SSE streaming via Anthropic HTTP API). Provides the tool execution layer: `IToolExecutor`/`IToolExecutorBuilder` dispatch tools by name, `ToolExecutorBuilder` registers `ITool` implementations, and `DefaultToolExecutor` handles result serialization. Result types: `ToolSuccessResult<T>` and `ToolFailureResult` (implement `IToolResult` from Framework) are returned by tool authors; `DefaultToolExecutor` serializes them into `ToolResult(IsSuccess, JsonElement)` — failures are wrapped as `{"error":"..."}` so the AI model can distinguish errors from successes.
- **Plugins/SecondDimensionWatcherReDive.Inference.AI** — AI inference pipeline for metadata extraction. `InferenceEngine` orchestrates system prompts, rate limiting (`SemaphoreSlim` + configurable delay), and JSON parsing. TMDB tools (`SearchTmdbTool`, `GetTmdbSeasonsTool`, `GetTmdbSeasonEpisodesTool`) are registered via `ToolExecutorBuilder`. Delegates all chat to `IAIEngine` from the AI plugin — no provider-specific code.
- **Plugins/SecondDimensionWatcherReDive.Chat** — Conversational AI chat plugin. `ChatController` exposes REST endpoints for conversation CRUD and SSE-streamed message responses. Includes 7 tools (`QueryAnimationsTool`, `ManageFeedsTool`, `QuerySeasonTool`, `SubscribeBangumiTool`, `ManageTasksTool`, `ManageDownloadsTool`, `QueryFilesTool`) that let the AI interact with the system on behalf of the user. `QueryFilesTool` browses the virtual filesystem via `IFileExplorer`. Depends on Framework repositories and the AI plugin's tool system.
- **Plugins/SecondDimensionWatcherReDive.WebDav** — WebDAV (RFC 4918) base primitives. No controllers/resource logic yet. Provides: HTTP method attributes (`Http/`) — `HttpPropFindAttribute`, `HttpPropPatchAttribute`, `HttpMkcolAttribute`, `HttpCopyAttribute`, `HttpMoveAttribute`, `HttpLockAttribute`, `HttpUnlockAttribute`, all subclassing `WebDavHttpMethodAttribute : HttpMethodAttribute` so routing works automatically. XML schema types (`Xml/`) annotated with `System.Xml.Serialization` attributes bound to the `DAV:` namespace: `MultiStatus`, `DavResponse`, `PropStat`, `Prop` (with `[XmlAnyElement]` for dead-properties), `ResourceType`, `PropFindRequest` (allprop/propname/prop/include), `PropertyUpdate` (set/remove operations), `LockInfo`, `ActiveLock`, `LockDiscovery`, `SupportedLock`, `LockScope`/`LockType`, `LockToken`, `Owner`, `DavError`. `WebDavXml` static helper provides cached `XmlSerializer`-per-type with DTD prohibited and `d:` prefix for `DAV:`. Action results (`Results/`): `WebDavXmlResult<T>` (generic base), `MultiStatusResult` (207), `LockedResult` (423), `FailedDependencyResult` (424), `InsufficientStorageResult` (507). XML input/output formatters (`Formatters/`) restrict themselves to types in the WebDAV XML namespace. `WebDavServiceExtensions.AddWebDav(IMvcBuilder)` inserts formatters at position 0. Constants: `WebDavConstants` (DAV namespace, header names, Depth/Timeout tokens, XML MIME), `WebDavStatusCodes` (207/422/423/424/507 + `FormatStatusLine`).
- **Share/SecondDimensionWatcherReDive.Analyzers** — Roslyn incremental source generator. Finds partial classes with `[Tool<TParam>]` attribute (from `Framework.Attributes`) and generates a static `Definition` property (via `Framework.AI.ToolDefinition.Create<TParam>`) and an `ExecuteAsync` method that deserializes `JsonElement` arguments using `ToolJsonOptions.Options`, then delegates to the author's `ExecuteCoreAsync`. Deserialization failures return `ToolFailureResult`.

### Backend Data Flow

The system uses **System.Threading.Channels** for async inter-service communication:

1. User triggers download via `AnimationInfoController`
2. `RemoteTorrentDownloadClient` submits torrent to qBittorrent API with savepath `{FileStore:Local}/{torrentHash}` so concurrent downloads never collide on disk, then writes to `RemoteTorrentTrackRequest` channel
3. `FetchRemoteTorrentBackgroundService` polls qBittorrent status, writes to `FileDownloadStatus` channel
4. `UpdateDownloadStatusBackgroundService` updates an in-memory cache with progress (finished items expire after 5 min)
5. On completion, `DownloadCompleteRequest` channel triggers `CompleteDownloadBackgroundService` to update the DB (`IsDownloadFinished`, `FileStore`, `StorePath`)
6. `CompleteDownloadBackgroundService` invokes `IFileMapper` to build virtual-path mappings, then fires the `OnFileDownloadCompleted` plugin event. Files on disk are never renamed.

### Provider/Strategy Pattern

Download clients and file stores use a provider pattern:
- `IFileDownloadClient` / `IFileDownloadClientProvider` — pluggable download backends (currently: qBittorrent remote). `IFileDownloadClientProvider` lives in Framework for cross-project reuse.
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk). Used for raw byte reads and directory walks, keyed by the `FileStore` string stored on each `FileMapping`.

### Virtual Filesystem (File Mapping)

Downloaded files are never renamed on disk. Instead, a `FileMapping` DB table records `{ VirtualPath, PhysicalPath, FileStore, AnimationInfoId }` rows and callers browse/stream via a virtual tree.

- **`IFileMapper`** (`Utils/FileStore/FileMapper.cs`) — invoked by `CompleteDownloadBackgroundService` after each completed download. Walks `StorePath` via `IFileStore`, computes virtual paths, resolves collisions, and persists rows via `IFileMappingRepository`. Uses `IInferenceEngine` for multi-episode torrents.
- **`IFileExplorer`** (`Framework/FileStore/IFileExplorer.cs`, impl in `Utils/FileStore/FileExplorer.cs`) — virtual-FS navigator. `EnumerateDirectoryAsync(DirectoryToken)` queries mappings by virtual-path prefix and emits `FileToken`/`DirectoryToken` children. `OpenReadStreamAsync(FileToken)` resolves the mapping and reads via `IFileStore`. Used by `FileController` and the Chat `QueryFilesTool`.
- **`IFileMappingRepository`** — CRUD + prefix query over `FileMapping` rows. Unique index on `VirtualPath`.

**Virtual path rules** (applied by `FileMapper`):
- Known single-episode (`Animation`, `Season`, `Episode` all present on `AnimationInfo`): largest video → `/{animeName}/{subGroup}/{animeName} S{season:D2}E{episode:D2}{ext}`. Matching subtitles inherit the same base with their language suffix preserved (e.g. `.zh.srt`). Other files fall through to the unknown rule.
- Known multi-episode (`Episode` null, `Animation` and `Season` set): each video is passed through `IInferenceEngine.InferAsync` to derive a per-file episode. On success → the same `SxxEyy` shape. On failure → unknown rule.
- Unknown (no `Animation` or no `Season`): `/unknown/{relativePathUnderStore}`, preserving torrent subdirectories.
- `subGroup` defaults to `Unknown` when `AnimationInfo.Group` is null. Path segments are sanitized (`Path.GetInvalidFileNameChars` + `/` replaced with `_`).
- Collisions: on virtual-path conflict (in-batch or against existing rows), suffix ` (n)` is inserted before the extension: `name.mkv` → `name (2).mkv`, incrementing until unique.

### Repository Pattern (Data Access)

The codebase uses a three-tier model architecture with repository interfaces for data access:

**1. EF Entity Classes** (`Models/`): Mutable classes mapped by EF Core (`ApplicationContext`). Only accessed inside `Repositories/`, `Program.cs`, and migrations.

**2. Domain Records** (`Framework/DataRepository/`): `AnimationInfo`, `Animation`, `AnimationGroup`, `Feed`, `SeasonBangumi`, `BangumiSubgroup`, `FileMapping`, `ChatConversationSummary`, `ChatConversationDetail`, `ChatMessageRecord` — immutable `sealed record` types with no EF Core dependency. Used by controllers, services, and plugin code. Result types: `PagedResult<T>`, `AnimationGroupedResult`, `AnimationWithEpisodesResult`. Also includes `AnimeSeason` enum (Spring, Summer, Autumn, Winter).

**3. External DTOs** (`Controllers/External/`): API response types serialized to JSON. Separate from domain records to control the API surface. Converted from domain records via `Controllers/Converter.cs` extension methods (`ToExternal()`, `ToExternalResponseData()`).

**Conversions:**
- `Repositories/RepositoryConverter.cs` — EF entity <-> domain record (`ToRecord()`, `ToEntity()`, `ApplyTo()`)
- `Controllers/Converter.cs` — domain record -> external DTO (`ToExternal()`)

**Repository interfaces** (`Framework/DataRepository/`):
- `IAnimationInfoRepository` — paged queries, grouped view, find by ID/title, add, update, pending inference, unfinished downloads
- `IAnimationRepository` — find by TMDB ID, add
- `IAnimationGroupRepository` — find by name, add
- `IFeedRepository` — ordered listing, URL queries, existence check, add, remove
- `ISeasonBangumiRepository` — ordered queries, find by MikanId, add/remove batch, save
- `IBangumiSubgroupRepository` — query by season bangumi, find by composite key, add, save
- `IChatRepository` — conversation CRUD (create, list, delete, update title), message persistence (add single/batch, list, count), full conversation retrieval with messages
- `IFileMappingRepository` — add batch of mappings, find by virtual path, prefix query, existence check (per-path and per-`AnimationInfoId`), remove by `AnimationInfoId`
- `IMigrationMarkerRepository` — `ExistsAsync(key)` / `SetAsync(key)` over the `MigrationMarkers` table; one-shot data migrations gate themselves on this

**Repository implementations** (`Repositories/`): EF Core implementations registered as scoped services, sharing the same `ApplicationContext` per request.

**Design rules:**
- Mutating methods (`AddAsync`, `RemoveAsync`, `UpdateAsync`) call `SaveChangesAsync` internally
- `SeasonBangumiRepository` and `BangumiSubgroupRepository` expose `void Add()`/`void RemoveRange()` + explicit `SaveChangesAsync()` for batch operations
- Background services resolve repositories via `IServiceScopeFactory.CreateAsyncScope()`
- Namespace collision: `Feed` and `Animation` entity names collide with Framework namespaces — use `FeedEntity`/`AnimationEntity` aliases where needed

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
- **CompleteDownloadBackgroundService** — Finalizes downloads, invokes `IFileMapper` to build virtual-FS mappings, then fires the `OnFileDownloadCompleted` plugin event

### Data Migration Tasks

One-shot data migrations (distinct from EF Core schema migrations) run once per database during startup, before the host begins serving requests.

- `IMigrationTask` (Framework/Tasks/) — `Key` + `ExecuteAsync(CancellationToken)`. Implementations are registered as singletons and discovered via DI (`IEnumerable<IMigrationTask>`).
- `MigrationTaskRunner` (`MigrationTasks/`) — iterates registered migrations, skips any whose `Key` is already in `MigrationMarkers`, runs the rest, and writes the marker on success. Failures abort startup so a half-migrated DB never serves traffic.
- Invoked from `Program.cs` after `context.Database.MigrateAsync()` and before `app.RunAsync()`.
- Current migrations: **MigrateFileMappings** — backfills `FileMapping` rows for previously-completed downloads (pages through `IAnimationInfoRepository.GetDownloadedPagedAsync`, skips records still pending AI inference to avoid racing `InferAnimationMetadata`'s mapping pass, calls `IFileMapper.MapDownloadAsync` per item).

### AI Inference Pipeline

AI inference is decoupled from feed sync — runs offline as a background task (`InferAnimationMetadata`).

**Flow:**
1. `SyncFeed` creates raw `AnimationInfo` records (no AI metadata)
2. `InferAnimationMetadata` picks up records where `IsAiProcessed == false` and `AiRetryCount < 3`
3. AI extracts: `tmdb_id`, `group_name`, `season`, `episode` (TMDB-normalized)
4. Name, original name, description, and poster path are fetched from TMDB API in the server's locale
5. Records are updated with metadata; `IsAiProcessed = true`
6. Failed items increment `AiRetryCount`; users can reset via `POST /api/animationinfo/{id}/retry-inference`

**Engine architecture (provider/engine split):**
- `IAIEngine` (in `SecondDimensionWatcherReDive.AI`) — streaming `IAsyncEnumerable<IChatUpdate>` chat interface with tool-call support via `ChatOptions.ToolExecutor`. Update types: `TextDelta`, `ToolCallBegin`, `ToolCallDelta`, `ToolResultUpdate`, `Finished`
- `IAIProvider` — provider-specific API abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync` (single-round streaming). Two implementations: `OpenAIProvider` (OpenAI-compatible HTTP API, bearer token auth) and `AnthropicProvider` (Anthropic HTTP API, x-api-key auth)
- `AIEngine` — unified engine implementing `IAIEngine`, delegates API calls to `IAIProvider` and handles the multi-round tool execution loop
- Tool system: `ITool`/`IToolResult`/`ToolDefinition` live in Framework (`Framework.AI`), `[Tool<TParam>]` attribute in `Framework.Attributes`. Tool authors implement `ExecuteCoreAsync` returning `ToolSuccessResult<T>` or `ToolFailureResult`; the source generator (`Share/SecondDimensionWatcherReDive.Analyzers`) generates `Definition` and `ExecuteAsync`. `IToolExecutor`/`IToolExecutorBuilder` (in the AI plugin) handle dispatch and serialization — `DefaultToolExecutor` serializes success results to `JsonElement` and wraps failures as `{"error":"..."}`, returning `ToolResult(IsSuccess, JsonElement)` which implements `IToolResult`.
- `InferenceEngine` (in `SecondDimensionWatcherReDive.Inference.AI`) — provider-agnostic orchestrator with rate limiting (`SemaphoreSlim` + configurable delay), system prompt, tool dispatch (max 8 rounds), JSON parsing (handles markdown fences). Three TMDB tools registered via `ToolExecutorBuilder`: `SearchTmdbTool`, `GetTmdbSeasonsTool`, `GetTmdbSeasonEpisodesTool`
- TMDB season normalization handles: merged cours, absolute episode numbering, mismatched season labels

### Controllers

All controllers are `internal` (discovered via `InternalControllerFeatureProvider` instead of the default ASP.NET Core provider, which only finds public classes). They inject repository interfaces (not `ApplicationContext`) and use `HttpContext.RequestAborted` as the CancellationToken for repository calls. API response types live in `Controllers/External/`, with `AppJsonSerializerContext` providing source-generated JSON serialization.

- `AnimationInfoController` (`/api/animationinfo`) — CRUD for animations, download/pause/resume/cancel, grouped listing by Animation, retry AI inference. Depends on `IAnimationInfoRepository`.
- `AuthController` (`/api/auth`) — register, login, refresh, verify
- `FileController` (`/api/file`) — virtual-FS browsing, playback link generation (returns full absolute URL via `Url.ActionLink`), streaming. Each animation's virtual root is derived from `Animation.Name` + `Group.Name` (known) or `/unknown` (otherwise); the controller delegates list/stream to `IFileExplorer`. Depends on `IAnimationInfoRepository`, `IFileExplorer`.
- `FeedController` (`/api/feed`) — CRUD for RSS feed subscriptions. Depends on `IFeedRepository`.
- `SeasonController` (`/api/season`) — current season anime discovery from mikanani.me, subgroup browsing, one-click subscribe, supports browsing other seasons. Season scraping delegated to `ISeasonScraper`. Depends on `ISeasonBangumiRepository`, `IBangumiSubgroupRepository`, `IFeedRepository`, `ISeasonScraper`.
- `TasksController` (`/api/tasks`) — list background tasks with status, enqueue manual execution
- `ChatController` (`/api/chat`, in `SecondDimensionWatcherReDive.Chat` plugin) — AI chat with conversation CRUD and SSE-streamed message responses. Supports tool execution (7 tools for querying animations, managing feeds, browsing seasons, controlling downloads, etc.). Depends on `IChatRepository`, `IAIEngine`.

### Feed Management

Feeds can be configured two ways (merged at sync time):
- Static: `MikananiFeeds` string array in config (`appsettings.example.json` / `appsettings.yml`)
- Dynamic: `Feed` entity in PostgreSQL, managed via `FeedController`

`SyncFeed` background service runs every 10 minutes, fetches all feed URLs, and creates `AnimationInfo` records.

### Season Anime Discovery

`ScrapeSeasonBangumi` scrapes mikanani.me homepage for current season anime (HTML parsing via HtmlAgilityPack). Scraping logic is abstracted behind `ISeasonScraper` (implemented by `MikananiSeasonScraper`). Data cached in `SeasonBangumi` + `BangumiSubgroup` DB tables. `SeasonController` exposes:
- Browse current season (cached) or other seasons (on-demand scrape via `/Home/BangumiCoverFlowByDayOfWeek` endpoint)
- Subgroups per anime (on-demand scrape, cached 24h)
- One-click subscribe (creates `Feed` record with mikanani RSS URL)

### SPA Proxy

In development, the main project proxies non-`/api` requests to the Parcel dev server (`http://localhost:1234`) via `AspSpaService`. In production, static files are served from `wwwroot` with fallback to `index.html`.

### Authentication

JWT Bearer tokens with BCrypt password hashing. Refresh token flow via `AuthController`. All API endpoints require authentication. Frontend uses `ProtectedRoute` component to redirect unauthenticated users to `/login`. The HTTP client (`httpClient.ts`) handles automatic token refresh with deduplication on 401 responses, and throws on non-OK responses so SWR error boundaries work correctly.

### Frontend

React 18 + TypeScript with Tailwind CSS for styling and Radix UI for accessible interactive primitives (Dialog, Toast, Progress). Uses SWR for data fetching, React Router v6 for routing, lucide-react for icons, Artplayer for video playback. Design system follows DESIGN.md (warm parchment canvas, serif headlines, terracotta accents).

**Pages:**
- Main (`/`) — Anime card grid grouped by TMDB ID, with poster images; uncategorized section for unmatched items
- Anime Episodes (`/anime/:tmdbId`) — Episode list for a specific anime with poster header
- Downloading (`/downloading`) — Items currently being downloaded
- Downloaded (`/downloaded`) — Completed downloads with file browser
- Player (`/play/:animationId?file=`) — Video player page using Artplayer with fullscreen, PiP, speed control, screenshot, aspect ratio, flip, mini progress bar, and settings. Includes URL scheme buttons to open in local players (VLC, PotPlayer, IINA, mpv, nPlayer). Navigated to from FileBrowser play action.
- Chat (`/chat`) — Conversational AI interface with conversation sidebar, SSE-streamed responses, tool call display, and model picker
- Feeds (`/feeds`) — Subscription management + season discovery
- Tasks (`/tasks`) — Background task dashboard with manual trigger
- Login (`/login`) — Login/register with form validation

**Key components:** `AppHeader` (top navigation bar with links to all pages), `AnimationInfo` (editorial row-style episode item with inline download controls, progress bar, AI retry button), `FileBrowser` (sheet/slide-over for browsing downloaded files; play action navigates to PlayerPage), `ExternalPlayerButtons` (URL scheme buttons for opening video in VLC, PotPlayer, IINA, mpv, nPlayer), `SeasonDiscovery` (season anime browser with day-of-week grouping and season selector), `ProtectedRoute` (auth guard), `ToastProvider` (Radix Toast notifications).
**Chat components:** `ChatSidebar` (conversation list with create/delete), `ChatMessageList`/`ChatMessage` (message rendering with markdown), `ChatInput` (text input with send), `ToolCallDisplay` (tool call and result rendering), `ModelPicker` (AI model selector). Chat module (`src/chat/`) provides `useStreamingChat` hook for SSE streaming with reducer-based state machine.
**UI primitives:** `src/components/ui/` — Button, Card, DropdownMenu, EmptyPrompt, FormRow, Input, Pagination, PasswordInput, Progress, Sheet, Spinner, Table.

### Mock API Server

`mock-server.mjs` provides a zero-dependency mock backend for frontend development without the .NET backend, PostgreSQL, or qBittorrent. Run with `yarn dev` (starts both mock + dev server) or `yarn mock` (mock only, then `yarn start` separately).

Features: 25 anime entries with TMDB poster paths and mixed download states, grouped animations endpoint, simulated download progress, auth flow (any password), feed CRUD, season bangumi browsing, background task listing with enqueue, AI retry inference, mock file browser. Listens on port 5097 (matching the Parcel proxy target).

## Key Configuration (appsettings.example.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array (static feeds)
- `TmdbApiKey` — TMDB API key (used for AI inference metadata, poster images, and season info)
- `AI:Provider` — "OpenAI" or "Anthropic" (defaults to OpenAI if omitted)
- `AI:OpenAI:ApiKey` — OpenAI API key (leave empty to disable AI inference)
- `AI:OpenAI:BaseUrl` — OpenAI-compatible API endpoint (default: `https://api.openai.com/v1`; supports Ollama, vLLM, etc.)
- `AI:OpenAI:Model` — Model name (e.g., "gpt-4o-mini")
- `AI:OpenAI:MaxTokens` — Max response tokens (default: 1024)
- `AI:Anthropic:ApiKey` — Anthropic API key
- `AI:Anthropic:BaseUrl` — Anthropic API endpoint (default: `https://api.anthropic.com`)
- `AI:Anthropic:Model` — Model name (e.g., "claude-sonnet-4-20250514")
- `AI:Anthropic:MaxTokens` — Max response tokens (default: 1024)
- `AI:Anthropic:ApiVersion` — Anthropic API version (default: "2023-06-01")
- `Inference:RateLimitDelayMs` — Min interval between API calls (default: 1000ms)
- `DisableCors` — Enable permissive CORS policy
- `Valkey:ConnectionString` — Valkey/Redis connection string (optional; uses in-memory cache if empty)
- `Valkey:InstanceName` — Cache key prefix (default: "sdw-redive:")

EF Core migrations run automatically on application startup.

Config migration: Users upgrading from pre-v2.2 (where AI config lived under `Inference:`) can run `deployments/migrate-config.sh` to automatically migrate to the new `AI:` config structure. For package installs, `postinstall.sh` runs this automatically.
