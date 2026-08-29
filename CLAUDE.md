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
- **SecondDimensionWatcherReDive.IntegrationTest** — MSTest integration tests via `Microsoft.AspNetCore.Mvc.Testing` (`WebDavWebApplicationFactory` boots the real app with fake repositories/file store from `Helpers/Fakes.cs` and seeded `TestData/WebDavMappingFixtures`). Covers WebDAV end-to-end (`Methods/` — OPTIONS, PROPFIND, GET/HEAD, advanced semantics, third-party `WebDav.Client` library compatibility), Basic-auth flow (`Auth/`), and the `/api/vfs` REST surface (`Vfs/` — stat/list/read/auth). `WebDavXmlAssertions` helps assert MultiStatus payloads.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler, Tailwind CSS, and Radix UI.
- **SecondDimensionWatcherReDive.FUSE** — Standalone Linux FUSE client (`sdwfuse` binary). Mounts the SDW virtual filesystem read-only by talking to the new `/api/vfs` REST endpoints over HTTP, authenticated with the same Basic per-device tokens used by WebDAV. Self-contained P/Invoke layer over `libfuse3.so.3` (no NuGet binding — `Native/LibFuse.cs` calls `fuse_main_real` directly; `Native/FuseOperations.cs` mirrors the 41-slot ops struct; `Native/LinuxStat.cs` mirrors the glibc `struct stat` for x86_64/arm64). Implements `getattr`, `readdir`, `open`, `read`, `release`, `access`. Layout: `Native/` (libfuse interop + errno/file mode constants), `Client/SdwClient.cs` (HTTP wrapper with Basic auth, range reads, retry on 5xx), `Fs/SdwFuseFs.cs` (`[UnmanagedCallersOnly]` callbacks bridging into the singleton instance), `Fs/AttrCache.cs` (TTL stat/list cache, default 5s), `Fs/FileHandleTable.cs`, `Configuration/FuseClientOptions.cs`, `Program.cs` (hand-rolled CLI). CLI: `sdwfuse mount <mountpoint> --server <url> --username <name> --password <token> [--foreground] [--debug] [--allow-other] [--cache-ttl 5]`. Env fallbacks: `SDW_FUSE_SERVER` / `SDW_FUSE_USERNAME` / `SDW_FUSE_PASSWORD`. Read-only: writes return `EROFS`. Linux-only (`RuntimeIdentifiers=linux-x64;linux-arm64`); requires the system `fuse3` package at runtime. Built with **NativeAOT** (`PublishAot=true`, `IsAotCompatible=true`) — libfuse3 is wired via `<DirectPInvoke Include="fuse3" />` + `<LinkerArg Include="-lfuse3" />`, and libc gets `<DirectPInvoke Include="libc" />` (no LinkerArg needed because the C runtime is mandatorily linked into every AOT binary), so the AOT image direct-calls `fuse_main_real` / `geteuid` / `getegid` instead of routing through `DllImportResolver`. `[LibraryImport]` is used everywhere instead of `[DllImport]` for source-generated marshalling. AOT publish must run on a Linux build host (cross-compile from macOS/Windows is not supported by the AOT toolchain).
- **Plugins/SecondDimensionWatcherReDive.AI** — AI engine abstraction with provider/engine split. Defines `IAIEngine` (streaming chat interface with tool-call support) and `IAIProvider` (provider-specific API call abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync`). The unified `AIEngine` implements the multi-round tool execution loop and delegates API calls to `IAIProvider`. Two provider implementations: `OpenAIProvider` (explicitly selectable Responses API or OpenAI-compatible Chat Completions; custom base URLs remain supported for Ollama/vLLM) and `AnthropicProvider` (SSE streaming via Anthropic HTTP API). Responses tool rounds use local opaque continuation state with `store:false`, replaying complete raw output items (including encrypted reasoning) rather than depending on server-side response storage. Persisted chats replay visible user/assistant text with assistant `phase`; old provider-neutral tool call/results are retained as labeled commentary records rather than protocol items because the database does not store the raw reasoning required to replay those items safely. Provides the tool execution layer: `IToolExecutor`/`IToolExecutorBuilder` dispatch tools by name, `ToolExecutorBuilder` registers `ITool` implementations, and `DefaultToolExecutor` handles result serialization. Result types: `ToolSuccessResult<T>` and `ToolFailureResult` (implement `IToolResult` from Framework) are returned by tool authors; `DefaultToolExecutor` serializes them into `ToolResult(IsSuccess, JsonElement)` — failures are wrapped as `{"error":"..."}` so the AI model can distinguish errors from successes.
- **Plugins/SecondDimensionWatcherReDive.Inference.AI** — AI inference pipeline for metadata extraction. `InferenceEngine` orchestrates system prompts, rate limiting (`SemaphoreSlim` + configurable delay), and JSON parsing. TMDB tools (`SearchTmdbTool`, `GetTmdbSeasonsTool`, `GetTmdbSeasonEpisodesTool`) are registered via `ToolExecutorBuilder`. Delegates all chat to `IAIEngine` from the AI plugin — no provider-specific code.
- **Plugins/SecondDimensionWatcherReDive.Chat** — Conversational AI chat plugin. `ChatController` exposes REST endpoints for conversation CRUD and SSE-streamed message responses. Includes 7 tools (`QueryAnimationsTool`, `ManageFeedsTool`, `QuerySeasonTool`, `SubscribeBangumiTool`, `ManageTasksTool`, `ManageDownloadsTool`, `QueryFilesTool`) that let the AI interact with the system on behalf of the user. `QueryFilesTool` browses the virtual filesystem via `IFileExplorer`. Depends on Framework repositories and the AI plugin's tool system.
- **Plugins/SecondDimensionWatcherReDive.NFS** — Read-only NFSv4.0 (RFC 7530) export of the same virtual filesystem WebDAV exposes. Self-hosted raw TCP server (default port 2049) — does NOT use ASP.NET MVC. Disabled by default; enable with `Nfs:Enabled=true`. Layers: `Xdr/` (XDR codec per RFC 4506 — `XdrReader` over `ReadOnlySpan<byte>`, `XdrWriter` over `IBufferWriter<byte>`, big-endian, 4-byte aligned strings/opaque/arrays), `Rpc/` (ONC RPC per RFC 5531 — record-marking framing, CALL/REPLY decode/encode, accepts AUTH_NONE and AUTH_SYS only), `Auth/` (AUTH_SYS pass-through — no real auth, security via the network layer), `Protocol/` (`NfsFileHandle` opaque encoding `[0xFE][kind][utf8(virtualPath)]`, `NfsStateId`, `NfsClientRegistry`/`NfsOpenStateRegistry` minimal lease/open state, `NfsAttributes` fattr4 bitmap encode/decode, `NfsCompoundDecoder`/`Encoder` for COMPOUND argarray/resarray), `Server/` (`NfsTcpServer` accept loop with semaphore-bounded concurrency, `NfsConnectionHandler` per-connection RPC loop, `NfsCompoundDispatcher` operation handlers), `Vfs/NfsVfsAdapter` (bridges `IFileExplorer`/`IFileMappingRepository`/`IFileStoreProvider` into NFS-shaped lookups). Implemented operations: NULL, PUTROOTFH, PUTFH, GETFH, SAVEFH, RESTOREFH, LOOKUP, LOOKUPP, GETATTR, ACCESS, READDIR, READ, OPEN/OPEN_CONFIRM/CLOSE, SETCLIENTID/SETCLIENTID_CONFIRM, RENEW, SECINFO, RELEASE_LOCKOWNER, DELEGRETURN. Write/lock ops return `NFS4ERR_ROFS` / `NFS4ERR_NOTSUPP`. `NfsServiceExtensions.AddNfs(IServiceCollection)` registers options + singleton state registries + the `NfsBackgroundService`.
- **Plugins/SecondDimensionWatcherReDive.WebDav** — WebDAV (RFC 4918) base primitives consumed by `WebDavController` in the main project. Provides: HTTP method attributes (`Http/`) — `HttpPropFindAttribute`, `HttpPropPatchAttribute`, `HttpMkcolAttribute`, `HttpCopyAttribute`, `HttpMoveAttribute`, `HttpLockAttribute`, `HttpUnlockAttribute`, all subclassing `WebDavHttpMethodAttribute : HttpMethodAttribute` so routing works automatically. XML schema types (`Xml/`) annotated with `System.Xml.Serialization` attributes bound to the `DAV:` namespace: `MultiStatus`, `DavResponse`, `PropStat`, `Prop` (with `[XmlAnyElement]` for dead-properties), `ResourceType`, `PropFindRequest` (allprop/propname/prop/include), `PropertyUpdate` (set/remove operations), `LockInfo`, `ActiveLock`, `LockDiscovery`, `SupportedLock`, `LockScope`/`LockType`, `LockToken`, `Owner`, `DavError`. `WebDavXml` static helper provides cached `XmlSerializer`-per-type with DTD prohibited and `d:` prefix for `DAV:`. Action results (`Results/`): `WebDavXmlResult<T>` (generic base), `MultiStatusResult` (207), `LockedResult` (423), `FailedDependencyResult` (424), `InsufficientStorageResult` (507). XML input/output formatters (`Formatters/`) restrict themselves to types in the WebDAV XML namespace. `WebDavServiceExtensions.AddWebDav(IMvcBuilder)` inserts formatters at position 0. Constants: `WebDavConstants` (DAV namespace, header names, Depth/Timeout tokens, XML MIME), `WebDavStatusCodes` (207/422/423/424/507 + `FormatStatusLine`).
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
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk). Used for raw byte reads and directory walks, keyed by the `FileStore` string stored on each `FileMapping`. `FileStoreInfo` carries `IsDirectory`, `Path`, `FileName`, plus optional `Length` and `LastModifiedUtc` (populated for files; consumed by `WebDavController` for `getcontentlength`/`getlastmodified`/`getetag`).

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

**2. Domain Records** (`Framework/DataRepository/`): `AnimationInfo`, `Animation`, `AnimationGroup`, `Feed`, `SeasonBangumi`, `BangumiSubgroup`, `FileMapping`, `WebDavToken`, `ChatConversationSummary`, `ChatConversationDetail`, `ChatMessageRecord` — immutable `sealed record` types with no EF Core dependency. Used by controllers, services, and plugin code. Result types: `PagedResult<T>`, `AnimationGroupedResult`, `AnimationWithEpisodesResult`. Also includes `AnimeSeason` enum (Spring, Summer, Autumn, Winter).

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
- `IWebDavTokenRepository` — list all tokens (newest first), find by username, existence check by username, add, remove by id. Backs the per-device WebDAV Basic-auth flow
- `IMigrationStateRepository` — durable versioned migration lifecycle (`pending/running/failed/completed`), checkpoint, timestamps, attempts, and last error over the `MigrationMarkers` table

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
- `Program.cs` acquires a dedicated-session PostgreSQL advisory lock before both EF schema and data migrations. Other replicas wait on the same lock; the lock is released automatically if the process/connection dies.
- `MigrationTaskRunner` (`MigrationTasks/`) records pending/running/failed/completed transitions, resumes stale running or failed attempts from their checkpoint, and only writes completed after the task returns successfully. Blocking failures abort before Kestrel and hosted services start.
- Current migrations: **MigrateFileMappings v2** — backfills `FileMapping` rows in stable keyset-ordered batches, checkpoints every batch, skips records still pending AI inference, and treats an unsuccessful `IFileMapper` result as a blocking failure.

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
- `IAIProvider` — provider-specific API abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync` (single-round streaming plus an opaque continuation carried by `AIEngine` between tool rounds). Two implementations: `OpenAIProvider` (Responses or compatible Chat Completions, bearer token auth) and `AnthropicProvider` (Anthropic HTTP API, x-api-key auth)
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
- `WebDavController` (`/webdav/{*path}`) — read-only WebDAV gateway over the virtual filesystem. Implements `OPTIONS` (advertises DAV class 1, `Allow: OPTIONS, PROPFIND, HEAD, GET`), `PROPFIND` (Depth: 0/1; infinite-depth on collections returns 403 to avoid full-table scans; empty body treated as allprop), and `GET`/`HEAD` with range support. Write methods (`PROPPATCH`, `MKCOL`, `COPY`, `MOVE`, `LOCK`, `UNLOCK`, `PUT`, `DELETE`) return 405. Resource resolution: exact `FileMapping` match → file; prefix match → synthetic collection; root `/` is always a collection. `getcontentlength`/`getlastmodified`/`getetag`/`creationdate` come from `IFileStore.FileInfoAsync` (`FileStoreInfo.Length` + `LastModifiedUtc`). Authenticated with the `Basic` scheme only — does NOT use JWT. Excluded from the dev-mode SPA proxy. Depends on `IFileExplorer`, `IFileMappingRepository`, `IFileStoreProvider`, `IContentTypeProvider`.
- `VfsController` (`/api/vfs`) — flat read-only REST surface over the same virtual filesystem. Three endpoints: `GET /api/vfs/stat?path=/...` returns `VfsEntry { name, isDirectory, size?, lastModifiedUtc? }` (or 404), `GET /api/vfs/list?path=/...` returns `VfsEntry[]` of immediate children (404 on missing, 400 on a file path), `GET /api/vfs/read?path=/...` streams bytes with `Range` support. Resolution mirrors `WebDavController` (FileMapping hit → file; prefix match → synthetic directory; root `/` is always a directory). Path traversal (`..`) and missing leading `/` are rejected with 400. Accepts both `Basic` (used by FUSE/WebDAV-style clients) and `Bearer` (used by the SPA's logged-in JWT session); the `Basic` scheme is listed first so the 401 challenge still emits `WWW-Authenticate: Basic`. Designed to back the FUSE client (`SecondDimensionWatcherReDive.FUSE`) and the SPA's `/files` page. Depends on `IFileExplorer`, `IFileMappingRepository`, `IFileStoreProvider`, `IContentTypeProvider`.
- `WebDavTokenController` (`/api/webdav-tokens`, JWT-protected) — manages per-device Basic-auth credentials for WebDAV. `GET` lists tokens (no plaintext), `POST` issues a new `(username, plaintext token)` pair (auto-generates `sdw-XXXXXXXX` username if not supplied; usernames must match `^[A-Za-z0-9._-]{3,32}$`; plaintext is a 32-byte URL-safe base64 string returned ONCE), `DELETE /{id}` revokes. Plaintext is BCrypt-hashed before persisting. Depends on `IWebDavTokenRepository`.

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

JWT Bearer tokens with BCrypt password hashing. Refresh token flow via `AuthController`. All `/api` endpoints require JWT authentication. Frontend uses `ProtectedRoute` component to redirect unauthenticated users to `/login`. The HTTP client (`httpClient.ts`) handles automatic token refresh with deduplication on 401 responses, and throws on non-OK responses so SWR error boundaries work correctly.

`/webdav` uses a separate HTTP Basic scheme (`BasicAuthenticationHandler` in `Auth/`, scheme name `"Basic"`, registered alongside JWT in `Program.cs`). Credentials are per-device tokens issued via `WebDavTokenController` and stored in the `WebDavTokens` table — the handler looks up the row by username through `IWebDavTokenRepository.FindByUsernameAsync` and BCrypt-verifies the supplied password against the stored `TokenHash`. There is no fixed username and no shared password in configuration. On challenge it emits `WWW-Authenticate: Basic realm="SecondDimensionWatcher WebDAV", charset="UTF-8"`. The WebDAV controller opts into this scheme explicitly via `[Authorize(AuthenticationSchemes = BasicAuthenticationHandler.SchemeName)]` — JWT clients cannot reach WebDAV and Basic clients cannot reach `/api`.

### Frontend

React 18 + TypeScript with Tailwind CSS for styling and Radix UI for accessible interactive primitives (Dialog, Toast, Progress). Uses SWR for data fetching, React Router v6 for routing, lucide-react for icons, Artplayer for video playback, react-i18next for localization (zh-CN / en / ja). Design system follows DESIGN.md (warm parchment canvas, serif headlines, terracotta accents).

**Pages:**
- Main (`/`) — Anime card grid grouped by TMDB ID, with poster images; uncategorized section for unmatched items
- Anime Episodes (`/anime/:tmdbId`) — Episode list for a specific anime with poster header
- Downloading (`/downloading`) — Items currently being downloaded
- Downloaded (`/downloaded`) — Completed downloads with file browser
- Files (`/files?path=…`) — Top-level virtual filesystem explorer that calls `/api/vfs/{stat,list,read}` directly. Editorial breadcrumb + listing-card layout (Ivory surface, whisper shadow). Folders sort first; folder rows navigate (URL-synced via `?path=`); file rows show size + relative modified date and a Download icon button that streams the file via `Authorization: Bearer …` into a Blob + temporary `<a download>`. Path `/` is the implicit default. Backed by `useVfsList` SWR hook in `src/file/vfsHooks.ts` and `IVfsEntry` in `src/file/IVfsEntry.ts`. Mock server (`mock-server.mjs`) serves an in-memory `VFS_TREE` so `yarn dev` works standalone.
- Player (`/play/:animationId?file=`) — Video player page using Artplayer with fullscreen, PiP, speed control, screenshot, aspect ratio, flip, mini progress bar, and settings. Includes URL scheme buttons to open in local players (VLC, PotPlayer, IINA, mpv, nPlayer). Navigated to from FileBrowser play action.
- Chat (`/chat`) — Conversational AI interface with conversation sidebar, SSE-streamed responses, tool call display, and model picker
- Feeds (`/feeds`) — Subscription management + season discovery
- Tasks (`/tasks`) — Background task dashboard with manual trigger
- Login (`/login`) — Login/register with form validation

**Key components:** `AppHeader` (top navigation bar with links to all pages and a user dropdown containing the language picker + logout), `AnimationInfo` (editorial row-style episode item with inline download controls, progress bar, AI retry button), `FileBrowser` (sheet/slide-over for browsing downloaded files; play action navigates to PlayerPage), `ExternalPlayerButtons` (URL scheme buttons for opening video in VLC, PotPlayer, IINA, mpv, nPlayer), `WebDavAccessSheet` (mounted on DownloadedPage; lists existing WebDAV tokens via `/api/webdav-tokens`, issues new ones, shows the plaintext token once with copy-to-clipboard, and revokes), `SeasonDiscovery` (season anime browser with day-of-week grouping and season selector), `ProtectedRoute` (auth guard), `ToastProvider` (Radix Toast notifications).
**Chat components:** `ChatSidebar` (conversation list with create/delete), `ChatMessageList`/`ChatMessage` (message rendering with markdown), `ChatInput` (text input with send), `ToolCallDisplay` (tool call and result rendering), `ModelPicker` (AI model selector). Chat module (`src/chat/`) provides `useStreamingChat` hook for SSE streaming with reducer-based state machine.
**UI primitives:** `src/components/ui/` — Button, Card, DropdownMenu, EmptyPrompt, FormRow, Input, Pagination, PasswordInput, Progress, Sheet, Spinner, Table.

### Internationalization (i18n)

Frontend UI strings are localized via **react-i18next** with bundled translation resources (no async HTTP loading). Supported languages: **zh-CN** (default/source), **en**, **ja**.

- `src/i18n/index.ts` — calls `i18next.init()` at module load. Uses `i18next-browser-languagedetector` with order `localStorage → navigator`, persisted under `localStorage["i18n.lng"]`. `fallbackLng` is `zh-CN`. `nonExplicitSupportedLngs: true` so `en-US` → `en`, `zh-TW` → `zh-CN`. Resources are bundled (no Suspense needed: `react.useSuspense: false`).
- `src/i18n/resources.ts` — static `import` of every locale JSON, assembled into the resources map.
- `src/i18n/locales/{zh-CN,en,ja}/{common,auth,errors,animation,files,chat,feeds,season,tasks,player}.json` — 10 namespaces grouped by feature/page surface. To add a string, edit all three language files. To add a language, drop a folder of JSON files matching the structure and add it to `supportedLanguages` in `src/i18n/index.ts`.
- `src/App.tsx` — imports `./i18n` so init runs before `createRoot`, then bridges `i18n.on("languageChanged")` to `setDayjsLocale(lng)` and `document.documentElement.lang`.
- `src/utils/initDayjs.ts` exports `setDayjsLocale(lng)` which dynamically imports the matching dayjs locale module and calls `dayjs.locale(...)`. Plugins (`duration`, `relativeTime`) are extended once at module load.
- The language switcher lives inside the user dropdown in `AppHeader` (Radix DropdownMenu). It calls `i18n.changeLanguage(lng)` directly; persistence is automatic via the detector. Language labels are always rendered in their native form (`中文（简体）` / `English` / `日本語`) regardless of UI language — see `languageLabels` in `src/i18n/index.ts`.
- Components access translations via `useTranslation(<ns>)` from `react-i18next`, e.g. `const { t } = useTranslation("animation")`. For multiple namespaces use `useTranslation(["animation", "errors"])` and prefix keys: `t("errors:loadFailed")`. The `Trans` component handles inline elements (e.g. `<code>` placeholders in `WebDavAccessSheet`).
- Some constants in `src/season/SeasonDiscovery.tsx` (`SEASONS = ["冬","春","夏","秋"]`) are intentionally Chinese — they are upstream IDs that match the mikanani.me API, not user-facing strings; the UI converts them via `SEASON_KEY` to localized labels under the `season:seasons.*` keys.
- Task metadata (in `src/tasks/taskMetadata.ts`) is exposed via the `useTaskMetadata()` hook which reads from the `tasks:metadata.{id}.{name|description}` keys.

### Mock API Server

`mock-server.mjs` provides a zero-dependency mock backend for frontend development without the .NET backend, PostgreSQL, or qBittorrent. Run with `yarn dev` (starts both mock + dev server) or `yarn mock` (mock only, then `yarn start` separately).

Features: 25 anime entries with TMDB poster paths and mixed download states, grouped animations endpoint, simulated download progress, auth flow (any password), feed CRUD, season bangumi browsing, background task listing with enqueue, AI retry inference, mock file browser. Listens on port 5097 (matching the Parcel proxy target).

## Key Configuration (appsettings.example.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Password:Value` — BCrypt hash of the JWT login password. When empty, `/api/auth/register` is open and writes the first user's hash to the path in `PasswordFile` (default `password.json`); once set, `/api/auth/login` BCrypt-verifies against this value. Unrelated to WebDAV.
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array (static feeds)
- `TmdbApiKey` — TMDB API key (used for AI inference metadata, poster images, and season info)
- `AI:Provider` — "OpenAI" or "Anthropic" (defaults to OpenAI if omitted)
- `AI:OpenAI:ApiKey` — OpenAI API key (leave empty to disable AI inference)
- `AI:OpenAI:BaseUrl` — OpenAI-compatible API endpoint (default: `https://api.openai.com/v1`; supports Ollama, vLLM, etc.)
- `AI:OpenAI:ApiMode` — wire protocol: `Responses` for official OpenAI, or `ChatCompletions` for Ollama/vLLM/legacy compatible endpoints. Missing values default to `ChatCompletions` for backward compatibility
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
