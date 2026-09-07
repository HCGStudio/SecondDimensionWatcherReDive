# Architecture and Configuration

Repository guidance and development commands are in [AGENTS.md](../AGENTS.md).

## Architecture

This is an anime/animation download management system (二次元观测器 Re:Dive) with a .NET 10 backend, React frontend, and PostgreSQL database.

### Solution Projects

- **SecondDimensionWatcherReDive** — Main ASP.NET Core web API. Internal controllers (`Controllers/`), external API DTOs (`Controllers/External/`), EF Core repository implementations (`Repositories/`), EF entity models (`Models/`), background services, download/feed implementations, SPA hosting.
- **SecondDimensionWatcherReDive.Framework** — Shared abstractions: domain records and repository interfaces (`DataRepository/`), plugin interfaces, file download/storage, feeds, scheduled tasks, inference. Also defines core AI tool contracts (`AI/`): `ITool` (static abstract `Definition` + `ExecuteAsync`), `IToolResult` (`object? Result` + `bool IsSuccess`), `ToolDefinition` (with `Create<TParams>` JSON Schema generation), and the `[Tool<TParam>]` attribute (`Attributes/`) consumed by the source generator.
- **SecondDimensionWatcherReDive.Test** — MSTest unit tests with Moq. Covers controllers, services, scheduled tasks, plugin events, feed parsing, auth.
- **SecondDimensionWatcherReDive.IntegrationTest** — MSTest integration tests via `Microsoft.AspNetCore.Mvc.Testing` (`WebDavWebApplicationFactory` boots the real app with fake repositories/file store from `Helpers/Fakes.cs` and seeded `TestData/WebDavMappingFixtures`). Covers WebDAV end-to-end (`Methods/` — OPTIONS, PROPFIND, GET/HEAD, advanced semantics, third-party `WebDav.Client` library compatibility), Basic-auth flow (`Auth/`), and the `/api/vfs` REST surface (`Vfs/` — stat/list/read/auth). `WebDavXmlAssertions` helps assert MultiStatus payloads.
- **SecondDimensionWatcherReDive.Client** — React/TypeScript SPA using Parcel bundler, Tailwind CSS, and Radix UI.
- **SecondDimensionWatcherReDive.FUSE** — Standalone Linux FUSE client (`sdwfuse` binary). Mounts the SDW virtual filesystem read-only by talking to the new `/api/vfs` REST endpoints over HTTP, authenticated with the same Basic per-device tokens used by WebDAV. Self-contained P/Invoke layer over `libfuse3.so.3` (no NuGet binding — `Native/LibFuse.cs` calls `fuse_main_real` directly; `Native/FuseOperations.cs` mirrors the 41-slot ops struct; `Native/LinuxStat.cs` mirrors the glibc `struct stat` for x86_64/arm64). Implements `getattr`, `readdir`, `open`, `read`, `release`, `access`. Layout: `Native/` (libfuse interop + errno/file mode constants), `Client/SdwClient.cs` (HTTP wrapper with Basic auth, range reads, retry on 5xx), `Fs/SdwFuseFs.cs` (`[UnmanagedCallersOnly]` callbacks bridging into the singleton instance), `Fs/AttrCache.cs` (TTL stat/list cache, default 5s), `Fs/FileHandleTable.cs`, `Configuration/FuseClientOptions.cs`, `Program.cs` (hand-rolled CLI). CLI: `sdwfuse mount <mountpoint> --server <url> --username <name> --password <token> [--foreground] [--debug] [--allow-other] [--cache-ttl 5]`. Env fallbacks: `SDW_FUSE_SERVER` / `SDW_FUSE_USERNAME` / `SDW_FUSE_PASSWORD`. Read-only: writes return `EROFS`. Linux-only (`RuntimeIdentifiers=linux-x64;linux-arm64`); requires the system `fuse3` package at runtime. Built with **NativeAOT** (`PublishAot=true`, `IsAotCompatible=true`) — libfuse3 is wired via `<DirectPInvoke Include="fuse3" />` + `<LinkerArg Include="-lfuse3" />`, and libc gets `<DirectPInvoke Include="libc" />` (no LinkerArg needed because the C runtime is mandatorily linked into every AOT binary), so the AOT image direct-calls `fuse_main_real` / `geteuid` / `getegid` instead of routing through `DllImportResolver`. `[LibraryImport]` is used everywhere instead of `[DllImport]` for source-generated marshalling. AOT publish must run on a Linux build host (cross-compile from macOS/Windows is not supported by the AOT toolchain).
- **Plugins/SecondDimensionWatcherReDive.AI** — AI engine abstraction with provider/engine split. Defines `IAIEngine` (streaming chat interface with tool-call support) and `IAIProvider` (provider-specific API call abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync`). `AIEngineRouter` selects the built-in `AIEngine` or the separate `CodexAppServerEngine` from `AI:Engine`. The built-in `AIEngine` implements the multi-round tool execution loop and delegates API calls to `IAIProvider`. Two provider implementations: `OpenAIProvider` (explicitly selectable Responses API or OpenAI-compatible Chat Completions; custom base URLs remain supported for Ollama/vLLM) and `AnthropicProvider` (SSE streaming via Anthropic HTTP API). Responses tool rounds use local opaque continuation state with `store:false`, replaying complete raw output items (including encrypted reasoning) rather than depending on server-side response storage. Persisted chats replay visible user/assistant text with assistant `phase`; old provider-neutral tool call/results are retained as labeled commentary records rather than protocol items because the database does not store the raw reasoning required to replay those items safely. Provides the tool execution layer: `IToolExecutor`/`IToolExecutorBuilder` dispatch tools by name, `ToolExecutorBuilder` registers `ITool` implementations, and `DefaultToolExecutor` handles result serialization. Result types: `ToolSuccessResult<T>` and `ToolFailureResult` (implement `IToolResult` from Framework) are returned by tool authors; `DefaultToolExecutor` serializes them into `ToolResult(IsSuccess, JsonElement)` — failures are wrapped as `{"error":"..."}` so the AI model can distinguish errors from successes.
- **Plugins/SecondDimensionWatcherReDive.Inference.AI** — AI inference pipeline for metadata extraction. `InferenceEngine` orchestrates system prompts, rate limiting (`SemaphoreSlim` + configurable delay), and JSON parsing. TMDB tools (`SearchTmdbTool`, `GetTmdbSeasonsTool`, `GetTmdbSeasonEpisodesTool`) are registered via `ToolExecutorBuilder`. Delegates all chat to `IAIEngine` from the AI plugin — no provider-specific code.
- **Plugins/SecondDimensionWatcherReDive.Chat** — Conversational AI chat plugin. `ChatController` exposes REST endpoints for conversation CRUD and SSE-streamed message responses. Includes 7 tools (`QueryAnimationsTool`, `ManageFeedsTool`, `QuerySeasonTool`, `SubscribeBangumiTool`, `ManageTasksTool`, `ManageDownloadsTool`, `QueryFilesTool`) that let the AI interact with the system on behalf of the user. `QueryFilesTool` browses the virtual filesystem via `IFileExplorer`. Depends on Framework repositories and the AI plugin's tool system.
- **Plugins/SecondDimensionWatcherReDive.NFS** — Read-only NFSv4.0 (RFC 7530) export of the same virtual filesystem WebDAV exposes. Self-hosted raw TCP server (default port 2049) — does NOT use ASP.NET MVC. Disabled by default; enable with `Nfs:Enabled=true`. Layers: `Xdr/` (XDR codec per RFC 4506 — `XdrReader` over `ReadOnlySpan<byte>`, `XdrWriter` over `IBufferWriter<byte>`, big-endian, 4-byte aligned strings/opaque/arrays), `Rpc/` (ONC RPC per RFC 5531 — record-marking framing, CALL/REPLY decode/encode, accepts AUTH_NONE and AUTH_SYS only), `Auth/` (AUTH_SYS pass-through — no real auth, security via the network layer), `Protocol/` (`NfsFileHandle` opaque encoding `[0xFE][kind][utf8(virtualPath)]`, `NfsStateId`, `NfsClientRegistry`/`NfsOpenStateRegistry` minimal lease/open state, `NfsAttributes` fattr4 bitmap encode/decode, `NfsCompoundDecoder`/`Encoder` for COMPOUND argarray/resarray), `Server/` (`NfsTcpServer` accept loop with semaphore-bounded concurrency, `NfsConnectionHandler` per-connection RPC loop, `NfsCompoundDispatcher` operation handlers), `Vfs/NfsVfsAdapter` (bridges `IFileExplorer`/`IFileMappingRepository`/`IFileStoreProvider` into NFS-shaped lookups). Implemented operations: NULL, PUTROOTFH, PUTFH, GETFH, SAVEFH, RESTOREFH, LOOKUP, LOOKUPP, GETATTR, ACCESS, READDIR, READ, OPEN/OPEN_CONFIRM/CLOSE, SETCLIENTID/SETCLIENTID_CONFIRM, RENEW, SECINFO, RELEASE_LOCKOWNER, DELEGRETURN. Write/lock ops return `NFS4ERR_ROFS` / `NFS4ERR_NOTSUPP`. `NfsServiceExtensions.AddNfs(IServiceCollection)` registers options + singleton state registries + the `NfsBackgroundService`.
- **Plugins/SecondDimensionWatcherReDive.WebDav** — WebDAV (RFC 4918) base primitives consumed by `WebDavController` in the main project. Provides: HTTP method attributes (`Http/`) — `HttpPropFindAttribute`, `HttpPropPatchAttribute`, `HttpMkcolAttribute`, `HttpCopyAttribute`, `HttpMoveAttribute`, `HttpLockAttribute`, `HttpUnlockAttribute`, all subclassing `WebDavHttpMethodAttribute : HttpMethodAttribute` so routing works automatically. XML schema types (`Xml/`) annotated with `System.Xml.Serialization` attributes bound to the `DAV:` namespace: `MultiStatus`, `DavResponse`, `PropStat`, `Prop` (with `[XmlAnyElement]` for dead-properties), `ResourceType`, `PropFindRequest` (allprop/propname/prop/include), `PropertyUpdate` (set/remove operations), `LockInfo`, `ActiveLock`, `LockDiscovery`, `SupportedLock`, `LockScope`/`LockType`, `LockToken`, `Owner`, `DavError`. `WebDavXml` static helper provides cached `XmlSerializer`-per-type with DTD prohibited and `d:` prefix for `DAV:`. Action results (`Results/`): `WebDavXmlResult<T>` (generic base), `MultiStatusResult` (207), `LockedResult` (423), `FailedDependencyResult` (424), `InsufficientStorageResult` (507). XML input/output formatters (`Formatters/`) restrict themselves to types in the WebDAV XML namespace. `WebDavServiceExtensions.AddWebDav(IMvcBuilder)` inserts formatters at position 0. Constants: `WebDavConstants` (DAV namespace, header names, Depth/Timeout tokens, XML MIME), `WebDavStatusCodes` (207/422/423/424/507 + `FormatStatusLine`).
- **Share/SecondDimensionWatcherReDive.Analyzers** — Roslyn incremental source generator. Finds partial classes with `[Tool<TParam>]` attribute (from `Framework.Attributes`) and generates a static `Definition` property (via `Framework.AI.ToolDefinition.Create<TParam>`) and an `ExecuteAsync` method that deserializes `JsonElement` arguments using `ToolJsonOptions.Options`, then delegates to the author's `ExecuteCoreAsync`. Deserialization failures return `ToolFailureResult`.

### Backend Data Flow

PostgreSQL holds authoritative attempts, reservations and durable workflow stages. **System.Threading.Channels** carry bounded progress updates and wake hints between services:

1. Manual, subscription, completion-plan and release-upgrade entry points persist the existing download-attempt/submission saga before calling `IFileDownloadClientProvider`.
2. With `DownloadCapacity:Enabled=true` (the default), `RemoteTorrentDownloadClient` records a durable capacity-queue row. When disabled, new submissions go directly to qBittorrent without capacity admission. `DownloadCapacityBackgroundService` serializes budget admission across replicas using a PostgreSQL advisory transaction lock, commits the reservation, then reconciles/submits that attempt to qBittorrent. Submission uncertainty retains capacity until remote reconciliation or acknowledged cancellation. Recovery requeues remotely missing attempts for fresh admission. Disabling capacity management drains existing unpaused work without capacity checks; remote tasks and completed rows leave the queue, while pause/resume/cancel remain available for queued work.
3. `FetchRemoteTorrentBackgroundService` periodically recovers tracked attempts from the database, skips capacity-waiting tasks, queries bounded hash batches at adaptive frequencies and backs off failed batches. Only queried hashes can be considered missing; meaningful progress updates use a bounded in-memory channel/cache.
4. On remote completion, `TryCompleteDownloadAsync` commits completion and its durable workflow together. `DownloadCompleteRequest` is only a best-effort wake hint; lost hints and restarts are recovered by database polling.
5. `CompleteDownloadBackgroundService` runs a configurable bounded worker pool (default 2). Each worker owns its DI scope, owner identity, job lease and renewal lifetime, and advances MapFiles → Notify → InvokePlugins → Done. Plugin callbacks are serialized within each application instance; deployments with multiple replicas already require cross-instance compatible/idempotent plugins. Files on disk are never renamed.
6. Mapping and release-upgrade activation precede notifications and plugin completion events. Stable event/job identifiers, stage persistence, retries and expired-lease takeover provide recovery rather than an exactly-once promise. Queue wait and execution duration are observable in the runtime meter.

Capacity configuration and actual-volume requirements are documented in [library workflows](library-workflows.md). Existing downloads are reconciled by attempt id; queued cancellation does not delete media.

### Provider/Strategy Pattern

Download clients and file stores use a provider pattern:
- `IFileDownloadClient` / `IFileDownloadClientProvider` — pluggable download backends (currently: qBittorrent remote). `IFileDownloadClientProvider` lives in Framework for cross-project reuse.
- `IFileStore` / `IFileStoreProvider` — pluggable storage backends (currently: local disk). Used for raw byte reads and directory walks, keyed by the `FileStore` string stored on each `FileMapping`. `FileStoreInfo` carries `IsDirectory`, `Path`, `FileName`, plus optional `Length` and `LastModifiedUtc` (populated for files; consumed by `WebDavController` for `getcontentlength`/`getlastmodified`/`getetag`).

### Virtual Filesystem (File Mapping)

Downloaded files are never renamed on disk. Instead, a `FileMapping` DB table records `{ VirtualPath, PhysicalPath, FileStore, AnimationInfoId }` rows and callers browse/stream via a virtual tree.

- **`IFileMapper`** (`Utils/FileStore/FileMapper.cs`) — invoked by `CompleteDownloadBackgroundService` after each completed download. Walks `StorePath` via `IFileStore`, computes virtual paths, resolves collisions, and persists rows via `IFileMappingRepository`. Uses `IInferenceEngine` for multi-episode torrents.
- **`IFileExplorer`** (`Framework/FileStore/IFileExplorer.cs`, impl in `Utils/FileStore/FileExplorer.cs`) — virtual-FS navigator. `EnumerateDirectoryAsync(DirectoryToken)` queries mappings by virtual-path prefix and emits `FileToken`/`DirectoryToken` children. `OpenReadStreamAsync(FileToken)` resolves the mapping and reads via `IFileStore`. Used by `FileController` and the Chat `QueryFilesTool`.
- **`IFileMappingRepository`** — CRUD over `FileMapping` rows plus exact-node and indexed direct-child hierarchy queries. Unique index on `VirtualPath`.
- **`FileSystemEntry` read model** — materialized file and synthetic-directory nodes keyed by virtual path. `(ParentPath, IsDirectory, Name)` serves direct-child listings; exact-path probes use the primary key. PostgreSQL triggers update directory reference counts atomically with mapping inserts/deletes, and the migration performs a transactional set-based backfill.

**Virtual path rules** (applied by `FileMapper`):
- Known single-episode (`Animation`, `Season`, `Episode` all present on `AnimationInfo`): largest video → `/{animeName}/{subGroup}/{animeName} S{season:D2}E{episode:D2}{ext}`. Matching subtitles inherit the same base with their language suffix preserved (e.g. `.zh.srt`). Other files fall through to the unknown rule.
- Known multi-episode (`Episode` null, `Animation` and `Season` set): each video is passed through `IInferenceEngine.InferAsync` to derive a per-file episode. On success → the same `SxxEyy` shape. On failure → unknown rule.
- Unknown (no `Animation` or no `Season`): `/unknown/{relativePathUnderStore}`, preserving torrent subdirectories.
- `subGroup` defaults to `Unknown` when `AnimationInfo.Group` is null. Path segments are sanitized (`Path.GetInvalidFileNameChars` + `/` replaced with `_`).
- Collisions: on virtual-path conflict (in-batch or against existing rows), suffix ` (n)` is inserted before the extension: `name.mkv` → `name (2).mkv`, incrementing until unique.

### Repository Pattern (Data Access)

The codebase uses a three-tier model architecture with repository interfaces for data access:

**1. EF Entity Classes** (`Models/`): Mutable classes mapped by EF Core (`ApplicationContext`). Only accessed inside `Repositories/`, `Program.cs`, and migrations.

**2. Domain Records** (`Framework/DataRepository/`): `AnimationInfo`, `AnimationInfoSummary`, `AnimationCatalogItem`, `Animation`, `AnimationGroup`, `Feed`, `SeasonBangumi`, `BangumiSubgroup`, `FileMapping`, `FileSystemEntry`, `WebDavToken`, `ChatConversationSummary`, `ChatConversationDetail`, `ChatMessageRecord` — immutable `sealed record` types with no EF Core dependency. Used by controllers, services, and plugin code. Catalog and episode results use stable cursor pages; `AnimationInfoSummary` intentionally excludes torrent payloads. Also includes `AnimeSeason` enum (Spring, Summer, Autumn, Winter).

**3. External DTOs** (`Controllers/External/`): API response types serialized to JSON. Separate from domain records to control the API surface. Converted from domain records via `Controllers/Converter.cs` extension methods (`ToExternal()`, `ToExternalResponseData()`).

**Conversions:**
- `Repositories/RepositoryConverter.cs` — EF entity <-> domain record (`ToRecord()`, `ToEntity()`, `ApplyTo()`)
- `Controllers/Converter.cs` — domain record -> external DTO (`ToExternal()`)

**Repository interfaces** (`Framework/DataRepository/`):
- `IAnimationInfoRepository` — paged queries, cursor-paged catalog summaries and lazy episodes, find by ID/title, add, update, pending inference, unfinished downloads
- `IAnimationRepository` — find by TMDB ID, add
- `IAnimationGroupRepository` — find by name, add
- `IFeedRepository` — ordered listing, URL queries, existence check, add, remove
- `ISeasonBangumiRepository` — ordered queries, find by MikanId, add/remove batch, save
- `IBangumiSubgroupRepository` — query by season bangumi, find by composite key, add, save
- `IChatRepository` — conversation CRUD (create, list, delete, update title), message persistence (add single/batch, list, count), full conversation retrieval with messages
- `IFileMappingRepository` — add/replace/remove mappings, exact hierarchy lookup, indexed direct-child query, existence checks, and compatibility prefix queries
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

The controlled platform in `PluginPlatform/` implements local package inspection, signed file manifests, explicit capability approval, dependency/lifecycle management and isolated ClearScript/V8 workers. Settings → Plugins and `/api/plugins` expose installation, enable/disable and health. API 1.0 connects notification and storage providers; arbitrary remote script installation and download/metadata provider adapters are unavailable. The legacy `IJavaScriptPluginLoader` placeholder is not the runtime implementation. See [plugin platform](plugin-platform.md).

### Background Services & Scheduled Tasks

Scheduled tasks extend `ScheduledTaskBase` (Framework/Tasks/). A bounded one-slot `Channel<byte>` coalesces local run requests; shared completion and force state use a lock. `IScheduledTaskLeaseManager` acquires and renews a PostgreSQL lease before execution, including forced runs, so replicas cannot own the same task concurrently. A generic `ScheduledTaskBackgroundService<TTask>` hosts each timer and queue loop. Tasks expose `IScheduledTask` for controller discovery.

- `IScheduledTask` — interface with `Id`, `Interval`, `IsEnabled`, `LastRunAt`, `IsRunning`, `RunNowAsync`, `Enqueue`
- `ScheduledTaskBase` — coalesces pending requests and coordinates execution through renewable distributed leases; cancelling one waiting HTTP request does not cancel the shared execution
- `ScheduledTaskBackgroundService<TTask>` — generic BackgroundService that hosts a single ScheduledTaskBase, runs `ProcessQueueAsync` + timer loop

Registered scheduled tasks:
- **SyncFeed** — Syncs RSS feed subscriptions every 10 minutes
- **InferAnimationMetadata** — Runs offline AI inference on unprocessed AnimationInfo records every 30 minutes
- **ScrapeSeasonBangumi** — Scrapes mikanani.me for current season anime list every 7 days

Always-running workflow processors:
- **DownloadCapacityBackgroundService** — Reconciles durable download attempts, reserves the shared capacity budget and submits admitted torrents.
- **FetchRemoteTorrentBackgroundService** — Recovers persisted tracked downloads, polls bounded hash batches at adaptive active/idle/paused intervals with failure backoff, and persists remote completion. It drains tracking/control messages between individual status batches and immediately continues the oldest overdue work; the idle delay applies only when no batch is due. `Torrent:Polling:StatusTimeoutSeconds` bounds the complete status request, including authentication and waiting for the shared request lock (default 30 seconds, clamped to 1–600). Increase it for a busy or distant downloader; request failures never imply a torrent is missing.
- **UpdateDownloadStatusBackgroundService** — Caches the bounded progress stream.
- **CompleteDownloadBackgroundService** — Claims durable jobs with independent worker scopes and advances mapping, notification and plugin stages; Channel messages only wake the workers sooner. Plugin contention defers the persisted job by three seconds and publishes a wake hint; idle workers wait until the earliest pending database attempt or the normal ten-second recovery poll, whichever comes first.
- **MultiSourceBackgroundService** — Evaluates linked-feed episode decisions every minute; persisted waiting deadlines and episode claims survive restarts.

See [runtime reliability](runtime-reliability.md) for scheduled leases, durable jobs, Outbox recovery and telemetry.

### Data Migration Tasks

One-shot data migrations (distinct from EF Core schema migrations) run once per database during startup, before the host begins serving requests.

- `IMigrationTask` (Framework/Tasks/) — `Key` + `ExecuteAsync(CancellationToken)`. Implementations are registered as singletons and discovered via DI (`IEnumerable<IMigrationTask>`).
- `Program.cs` acquires a dedicated-session PostgreSQL advisory lock before both EF schema and data migrations. Other replicas wait on the same lock; the lock is released automatically if the process/connection dies.
- `MigrationTaskRunner` (`MigrationTasks/`) records pending/running/failed/completed transitions, resumes stale running or failed attempts from their checkpoint, and only writes completed after the task returns successfully. Blocking failures abort before Kestrel and hosted services start.
- Current migrations: **MigrateFileMappings v2** — backfills `FileMapping` rows in stable keyset-ordered batches, checkpoints every batch, skips records still pending AI inference, and treats an unsuccessful `IFileMapper` result as a blocking failure.

### AI Inference Pipeline

AI inference is decoupled from feed sync — runs offline as a background task (`InferAnimationMetadata`).

**Flow:**
1. `SyncFeed` creates raw `AnimationInfo` records. `InferAnimationMetadata` evaluates explicitly scoped recognition rules before invoking AI; a unique valid rule can identify later matching releases without an AI request. Conflicts enter the manual review queue and rule hits retain the applied revision. Saving/editing a rule does not silently apply it to historical records.
2. `InferAnimationMetadata` picks up records where `IsAiProcessed == false` and `AiRetryCount < 3`
3. AI extracts: `tmdb_id`, `group_name`, `season`, `episode` (TMDB-normalized)
4. Name, original name, description, and poster path are fetched from TMDB API in the server's locale
5. Records are updated with metadata; `IsAiProcessed = true`
6. Failed items increment `AiRetryCount`; users can reset via `POST /api/animationinfo/{id}/retry-inference`

**Engine architecture (provider/engine split):**
- `IAIEngine` (in `SecondDimensionWatcherReDive.AI`) — streaming `IAsyncEnumerable<IChatUpdate>` chat interface routed to the built-in or Codex app-server execution backend, with tool-call support via `ChatOptions.ToolExecutor`. Update types: `TextDelta`, `ToolCallBegin`, `ToolCallDelta`, `ToolResultUpdate`, `Finished`
- `IAIProvider` — provider-specific API abstraction with `GetAvailableModelsAsync` and `StreamChatCompletionAsync` (single-round streaming plus an opaque continuation carried by `AIEngine` between tool rounds). Two implementations: `OpenAIProvider` (Responses or compatible Chat Completions, bearer token auth) and `AnthropicProvider` (Anthropic HTTP API, x-api-key auth)
- `AIEngine` — unified engine implementing `IAIEngine`, delegates API calls to `IAIProvider` and handles the multi-round tool execution loop
- Tool system: `ITool`/`IToolResult`/`ToolDefinition` live in Framework (`Framework.AI`), `[Tool<TParam>]` attribute in `Framework.Attributes`. Tool authors implement `ExecuteCoreAsync` returning `ToolSuccessResult<T>` or `ToolFailureResult`; the source generator (`Share/SecondDimensionWatcherReDive.Analyzers`) generates `Definition` and `ExecuteAsync`. `IToolExecutor`/`IToolExecutorBuilder` (in the AI plugin) handle dispatch and serialization — `DefaultToolExecutor` serializes success results to `JsonElement` and wraps failures as `{"error":"..."}`, returning `ToolResult(IsSuccess, JsonElement)` which implements `IToolResult`.
- `InferenceEngine` (in `SecondDimensionWatcherReDive.Inference.AI`) — provider-agnostic orchestrator with rate limiting (`SemaphoreSlim` + configurable delay), system prompt, tool dispatch (max 8 rounds), JSON parsing (handles markdown fences). Three TMDB tools registered via `ToolExecutorBuilder`: `SearchTmdbTool`, `GetTmdbSeasonsTool`, `GetTmdbSeasonEpisodesTool`
- TMDB season normalization handles: merged cours, absolute episode numbering, mismatched season labels
- Recognition rules that still need AI inference supply their TMDB target to `InferForTmdbAsync`; series search is disabled and results for a different ID are rejected. Historical previews recover the pre-offset episode from an unchanged rule hit, preserve manually finalized coordinates, and require explicit captures or manual correction when the applied rule revision is no longer available. Rules can be seeded from the current correction in Recent Operations. Historical rule previews persist the rule ID and revision; final application rechecks the enabled rule, its match and the absence of conflicting rules inside the metadata transaction, protecting the rule set from concurrent writes until commit.

### Controllers

All controllers are `internal` (discovered via `InternalControllerFeatureProvider` instead of the default ASP.NET Core provider, which only finds public classes). They inject repository interfaces (not `ApplicationContext`) and use `HttpContext.RequestAborted` as the CancellationToken for repository calls. API-specific DTOs live in `Controllers/External/`; some feature controllers return Framework domain records. `AppJsonSerializerContext` registers both for source-generated JSON serialization.

- `AnimationInfoController` (`/api/animationinfo`) — CRUD for animations, download/pause/resume/cancel, grouped listing by Animation, retry AI inference. Depends on `IAnimationInfoRepository`.
- `AuthController` (`/api/auth`) — register, login, refresh, verify
- `FileController` (`/api/file`) — virtual-FS browsing, playback link generation (returns full absolute URL via `Url.ActionLink`), streaming. Each animation's virtual root is derived from `Animation.Name` + `Group.Name` (known) or `/unknown` (otherwise); the controller delegates list/stream to `IFileExplorer`. Depends on `IAnimationInfoRepository`, `IFileExplorer`.
- `MediaTimelineController` (`/api/playback/timeline`) — Shared media-version-bound OP/ED ranges and chapters, with opt-in season defaults scoped to title/season/group. Resolving a timeline requires physical length and modification time; missing metadata or media yields 404. In-player auto-skip is opt-in per profile; progress sampling checks the active ending range before either manual or automatic skipping can enqueue a watched update. Timeline writes and season acceptance share a database transaction lock with mapping changes, including first saves. Logical playback exports/imports retain the AutoSkip preference; false is omitted from JSON to preserve checksums of older format-1 exports. Timeline mutations require `ContentWrite`.
- `NativeDownloadController` — JWT-authenticated `/api/vfs/download-link` issues a short-lived ticket; `/api/file/play/download/{resourceId}` validates the ticket cookie, live session and file fingerprint on GET/HEAD/Range.
- `FeedController` (`/api/feed`) — CRUD for RSS feed subscriptions. Depends on `IFeedRepository`.
- `SeasonController` (`/api/season`) — current season anime discovery from mikanani.me, subgroup browsing, one-click subscribe, supports browsing other seasons. Season scraping delegated to `ISeasonScraper`. Depends on `ISeasonBangumiRepository`, `IBangumiSubgroupRepository`, `IFeedRepository`, `ISeasonScraper`.
- `TasksController` (`/api/tasks`) — list background tasks with status, enqueue manual execution
- `ChatController` (`/api/chat`, in `SecondDimensionWatcherReDive.Chat` plugin) — AI chat with conversation CRUD and SSE-streamed message responses. Supports tool execution (7 tools for querying animations, managing feeds, browsing seasons, controlling downloads, etc.). Depends on `IChatRepository`, `IAIEngine`.
- `WebDavController` (`/webdav/{*path}`) — read-only WebDAV gateway over the virtual filesystem. Implements `OPTIONS` (advertises DAV class 1, `Allow: OPTIONS, PROPFIND, HEAD, GET`), `PROPFIND` (Depth: 0/1; infinite-depth on collections returns 403; empty body treated as allprop), and `GET`/`HEAD` with range support. Write methods return 405. Resource resolution and Depth:1 listings use the exact/direct-child `FileSystemEntry` read model; file stats are batched per store for a listing. Authenticated with the `Basic` scheme only.
- `VfsController` (`/api/vfs`) — flat read-only REST surface over the same virtual filesystem. `stat`, `list`, and `read` use exact/direct-child hierarchy queries; list metadata is fetched in storage batches. Path traversal (`..`) and missing leading `/` are rejected with 400. Accepts both Basic and Bearer authentication and backs the FUSE client and SPA files page.
- `WebDavTokenController` (`/api/webdav-tokens`, JWT-protected) — administrators list read-only device credentials; issue/revoke operations require recent administrator authentication. Each credential binds a household user, virtual-root scope and expiry. The random plaintext is returned once and stored using the peppered `IDeviceTokenHasher` HMAC scheme; Basic authentication upgrades legacy BCrypt hashes after verification. Credentials authorize both WebDAV and VFS, not household management APIs. Depends on `IWebDavTokenRepository`, `IIdentityRepository` and `IFileMappingRepository`.
- `LibraryController` (`/api/library`) and `LibraryCompletionController` (`/api/library/completion`) — search, integrity, existing upgrade/rollback and per-episode completion preview/submission. Completion submission requires `ContentWrite`.
- `MultiSourceSubscriptionsController` (`/api/multi-source-subscriptions`) — ordered source links, shared rules, persisted episode decisions, manual evaluation and `/episodes/{episode}/confirm`; mutations require `ContentWrite`.
- `WatchlistController` (`/api/watchlist`) — profile-owned lists and weekly release/unwatched summaries; mutations require `PlaybackWrite`.

### Feed Management

Feeds can be configured two ways (merged at sync time):
- Static: `MikananiFeeds` string array in config (`appsettings.example.json` / `appsettings.yml`)
- Dynamic: `Feed` entity in PostgreSQL, managed via `FeedController`

`SyncFeed` background service runs every 10 minutes, fetches all feed URLs, and creates `AnimationInfo` records. Linked multi-source feeds use their unified policy and defer automatic selection to `MultiSourceBackgroundService`; standalone feeds retain their original policy. For each reliably identified single episode, the coordinator preserves the earliest linked-source ingestion time, clamped to subscription creation, as its waiting origin; the release need not have matched quality rules when ingested. It chooses the primary immediately when eligible, or the highest-priority eligible fallback after the deadline, and uses the shared episode claim and download saga. Automatic upgrades run only in AutoDownload mode with upgrading enabled and a sufficient score improvement. A downloaded fallback only upgrades automatically within its current source; switching to a late primary or another source requires manual selection. The original per-feed rules remain stored and resume after unlinking. Unreliable season/episode metadata and batches remain manual.

### Broadcast-aware Completion

`EpisodeAirCalendarService` reads TMDB season episode dates and caches successful lookups for six hours, unsuccessful lookups for ten minutes. Integrity listings fetch calendars with at most four concurrent requests after database reads finish. It compares date-only values against the current UTC date; it does not infer a precise broadcast instant or equate that date with a group's `PublishTime`. Integrity excludes both future and unknown-date episodes from actionable missing counts, and reports those categories separately. Existing candidates remain selectable when the air date is unknown.

`LibraryCompletionService` scores collected reliable single-episode releases against the effective feed or shared-source policy. The preview shows one default candidate per episode, reasons, known/unknown size, source publication time and blocked states. Completed releases need live mappings to count as downloaded; completion awaiting mappings blocks a duplicate acquisition. Each batch item re-reads current state, claims `EpisodeAcquisitions` by TMDB/season/episode and uses the existing download submission/cancellation saga through `IFileDownloadClientProvider`. Responses are per item and failed items can be retried independently. The claim expires after five minutes if its owner is lost; persistent download state prevents an already queued episode from being submitted again. No external torrent search or forced batch/special matching is introduced.

While a completion plan is open, revalidation preserves skipped episodes and selected versions that remain eligible; opening a new plan initializes the current defaults. `mock-completion.mjs` uses the shared mock library with explicitly illustrative dates.

### Multi-source Anime Subscriptions

Feeds → Anime multi-source subscriptions binds existing feeds to one TMDB show/season with primary/fallback ordering and shared quality, waiting and notification/confirmation/download rules. `/api/multi-source-subscriptions` exposes the list, CRUD, manual evaluation and per-episode confirmation; mutations require `ContentWrite` (Admin/Member). A feed belongs to at most one subscription, and a show/season has at most one subscription. Shared rules override linked feed rules without replacing them; unlinking restores the original feed behavior and never removes downloaded files.

`SyncFeed` retains linked-source releases for evaluation and suppresses independent feed submission. `MultiSourceBackgroundService` evaluates reliable single episodes every minute. The persisted wait origin is the earliest linked-source ingestion time, clamped to subscription creation; the release need not have matched quality rules when first ingested. A suitable primary is selected immediately, or the highest-priority suitable fallback after the configured deadline. The UI shows each source's latest publication, the selected release, wait origin/deadline/remaining minutes and the selection reason. ManualConfirm exposes an explicit episode confirmation action; completion plans also support manual candidate adjustment.

The coordinator reuses episode claims and the existing download saga, so sources do not independently acquire the same episode. Automatic upgrading requires AutoDownload mode, enabled upgrading and the minimum quality-score improvement; a downloaded fallback only upgrades automatically within its incumbent source, so a late primary requires manual selection. Candidate enumeration, transaction-time score validation and the rollback period use the shared current policy. Ordinary automatic upgrade enumeration excludes both linked candidates and linked incumbents so source orchestration owns those decisions. Changing source membership or ordering clears pending decisions, and confirmation revalidates the selected source. Deleting a linked feed performs the same cleanup, renumbers remaining priorities and removes the subscription when its last source is deleted; downloaded files remain intact. Upgrade execution carries its manual or automatic origin separately from score eligibility, so an explicit manual cross-source upgrade remains available. Failed automatic decisions remain visible and are not retried by the background worker; the explicit Evaluate and retry failures action reopens them. Unreliable season/episode metadata and batches remain manual; no new external feed/search service is introduced. `mock-multi-source.mjs` supplies development source management using the existing completion mock helpers, explicit release-to-feed ownership, source priority and the same primary-or-expired-wait selection rule.

### Season Anime Discovery

`ScrapeSeasonBangumi` scrapes mikanani.me homepage for current season anime (HTML parsing via HtmlAgilityPack). Scraping logic is abstracted behind `ISeasonScraper` (implemented by `MikananiSeasonScraper`). Data cached in `SeasonBangumi` + `BangumiSubgroup` DB tables. `SeasonController` exposes:
- Browse current season (cached) or other seasons (on-demand scrape via `/Home/BangumiCoverFlowByDayOfWeek` endpoint)
- Subgroups per anime (on-demand scrape, cached 24h)
- One-click subscribe (creates `Feed` record with mikanani RSS URL)

### Personal Watchlist

`/watchlist` combines profile-owned tracking status, the Mikan weekday calendar, and unwatched library episodes through `IWatchlistRepository` and `/api/watchlist`. Saved TMDB IDs use canonical invariant decimal strings so links match library identities. Omitted link fields retain their current values, while explicit `null` clears a link; each entry must retain at least one TMDB or Mikan ID. Linking duplicate identities merges entries within the current profile. Playable mappings use the shared `MediaFileTypes` video extensions, and the notifications shortcut is visible only to administrators. `mock-watchlist-playback.mjs` mirrors the link update semantics for local development.

### SPA Proxy

In development, the main project proxies non-`/api` requests to the Parcel dev server (`http://localhost:1234`) via `AspSpaService`. In production, static files are served from `wwwroot` with fallback to `index.html`.

### Authentication

PostgreSQL stores household users, BCrypt password hashes, profiles and revocable login sessions. JWTs identify user + profile + session; refresh rotation and profile switching preserve those boundaries, while frontend cache invalidation prevents state from the previous profile leaking into the next. Admin manages system/identity/metadata/device credentials; Member manages content and personal playback; Viewer browses/plays. Physical media deletion requires recent administrator authentication. Shared media visibility is separate from profile-owned progress, watchlist and chat. Legacy password configuration is only an upgrade/bootstrap input, not the continuing authority.

`/webdav` explicitly uses the Basic scheme; `/api/vfs` accepts Basic or Bearer. Per-device credentials are read-only, belong to a household user and can have a virtual-root restriction and expiry; both protocols validate revocation and rewrite the visible namespace. Password verification uses the current peppered device-token scheme, with legacy BCrypt compatibility. JWTs do not authorize WebDAV. See [security boundaries](security-boundaries.md).

Browser playback and native downloads use a short-lived opaque resource ticket and an HttpOnly cookie bound to user/session/profile. Every new native download GET/HEAD/Range checks the live session and mapping/file fingerprint. The lifetime uses `Authentication:PlaybackLinkMinutes`; expiry, logout, revocation or a changed fingerprint requires a fresh link. Existing streams may finish after revocation. Resumable ranges and strong ETags require a seekable stream plus physical length and modification time; providers without version metadata serve full downloads. Long-lived Bearer credentials never appear in the URL.

### Frontend

React + TypeScript with Tailwind CSS for styling and Radix UI for accessible interactive primitives (Dialog, Toast, Progress). Uses SWR for data fetching, React Router for routing, lucide-react for icons, Artplayer for video playback, react-i18next for localization (zh-CN / en / ja). Design system follows DESIGN.md (warm parchment canvas, serif headlines, terracotta accents).

**Pages:**
- Main (`/`) — Anime card grid grouped by TMDB ID, with poster images; uncategorized section for unmatched items
- Anime Episodes (`/anime/:tmdbId`) — Episode list for a specific anime with poster header
- Downloading (`/downloading`) — Items currently being downloaded
- Downloaded (`/downloaded`) — Completed downloads with file browser
- Files (`/files?path=…`) — Top-level virtual filesystem explorer that calls `/api/vfs/{stat,list,read}` directly. Editorial breadcrumb + listing-card layout (Ivory surface, whisper shadow). Folders sort first; folder rows navigate (URL-synced via `?path=`); file rows show size + relative modified date and a Download button that creates a scoped ticket, checks its HEAD response and hands the stream to the browser download manager without buffering a full Blob. Path `/` is the implicit default. Backed by `useVfsList` SWR hook in `src/file/vfsHooks.ts` and `IVfsEntry` in `src/file/IVfsEntry.ts`. Mock server (`mock-server.mjs`) serves an in-memory `VFS_TREE` so `yarn dev` works standalone.
- Player (`/play/:animationId?file=`) — Video player page using Artplayer with fullscreen, PiP, speed control, screenshot, aspect ratio, flip, mini progress bar, and settings. Includes URL scheme buttons to open in local players (VLC, PotPlayer, IINA, mpv, nPlayer). Navigated to from FileBrowser play action.
- Chat (`/chat`) — Conversational AI interface with conversation sidebar, SSE-streamed responses, tool call display, and model picker
- Feeds (`/feeds`) — Subscription management, per-feed automation, multi-source orchestration and season discovery
- Search (`/search`) — Library search, release scoring, broadcast-aware completion plans and version upgrades
- Watchlist (`/watchlist`) — Current profile’s personal states and weekly expected/released/downloaded-unwatched view
- Metadata review (`/metadata-review`) — Administrator correction/preview/undo and scoped recognition-rule management
- Tasks (`/tasks`) — Background task dashboard with manual trigger
- Login (`/login`) — Login/register with form validation

**Key components:** `AppHeader` (top navigation bar with links to all pages and a user dropdown containing the language picker + logout), `AnimationInfo` (editorial row-style episode item with inline download controls, progress bar, AI retry button), `FileBrowser` (sheet/slide-over for browsing downloaded files; play action navigates to PlayerPage), `ExternalPlayerButtons` (URL scheme buttons for opening video in VLC, PotPlayer, IINA, mpv, nPlayer), `WebDavAccessSheet` (mounted on DownloadedPage; lists existing WebDAV tokens via `/api/webdav-tokens`, issues new ones, shows the plaintext token once with copy-to-clipboard, and revokes), `SeasonDiscovery` (season anime browser with day-of-week grouping and season selector), `ProtectedRoute` (auth guard), `ToastProvider` (Radix Toast notifications).
**Chat components:** `ChatSidebar` (conversation list with create/delete), `ChatMessageList`/`ChatMessage` (message rendering with markdown), `ChatInput` (text input with send), `ToolCallDisplay` (tool call and result rendering), `ModelPicker` (AI model selector). Chat module (`src/chat/`) provides `useStreamingChat` hook for SSE streaming with reducer-based state machine.
**UI primitives:** `src/components/ui/` — Button, Card, DropdownMenu, EmptyPrompt, FormRow, Input, Pagination, PasswordInput, Progress, Sheet, Spinner, Table.

### Internationalization (i18n)

Frontend UI strings are localized via **react-i18next** with bundled translation resources (no async HTTP loading). Supported languages: **zh-CN** (default/source), **en**, **ja**.

- `src/i18n/index.ts` — calls `i18next.init()` at module load. Uses `i18next-browser-languagedetector` with order `localStorage → navigator`, persisted under `localStorage["i18n.lng"]`. `lowerCaseLng` normalizes resource keys; explicit Chinese fallbacks resolve variants such as `zh-TW` to `zh-cn`, and `nonExplicitSupportedLngs` resolves regional English to `en`. Resources are bundled (no Suspense needed: `react.useSuspense: false`).
- `src/i18n/resources.ts` — statically imports the active feature locale JSON files and assembles the resources map. The initial `settings` namespace includes only an inline `system.reauthenticatePrompt`. Loading `SettingsPage.tsx` imports `src/i18n/settingsResources.ts`, which imports all three `locales/*/settings.json` files and merges them with `addResourceBundle(..., true, true)`, overwriting the inline prompt with the Settings bundle.
- `src/i18n/locales/{zh-CN,en,ja}/*.json` — feature/page namespaces including library, metadata review, settings, accounts, incidents, todos and watchlist as well as the original player/feed/file resources. To add or change a string, edit all three language files; Settings translations belong in `settings.json`. Keep the inline reauthentication fallback in sync when changing that prompt. To add a language, add matching JSON files, register ordinary namespaces in `resources.ts` and the Settings bundle in `settingsResources.ts`, and add it to `supportedLanguages` in `src/i18n/index.ts`.
- `src/App.tsx` — imports `./i18n` so init runs before `createRoot`, then bridges `i18n.on("languageChanged")` to `setDayjsLocale(lng)` and `document.documentElement.lang`.
- `src/utils/initDayjs.ts` exports `setDayjsLocale(lng)` which dynamically imports the matching dayjs locale module and calls `dayjs.locale(...)`. Plugins (`duration`, `relativeTime`) are extended once at module load.
- The language switcher lives inside the user dropdown in `AppHeader` (Radix DropdownMenu). It calls `i18n.changeLanguage(lng)` directly; persistence is automatic via the detector. Language labels are always rendered in their native form (`中文（简体）` / `English` / `日本語`) regardless of UI language — see `languageLabels` in `src/i18n/index.ts`.
- Components access translations via `useTranslation(<ns>)` from `react-i18next`, e.g. `const { t } = useTranslation("animation")`. For multiple namespaces use `useTranslation(["animation", "errors"])` and prefix keys: `t("errors:loadFailed")`. The `Trans` component handles inline elements (e.g. `<code>` placeholders in `WebDavAccessSheet`).
- Some constants in `src/season/SeasonDiscovery.tsx` (`SEASONS = ["冬","春","夏","秋"]`) are intentionally Chinese — they are upstream IDs that match the mikanani.me API, not user-facing strings; the UI converts them via `SEASON_KEY` to localized labels under the `season:seasons.*` keys.
- Task metadata (in `src/tasks/taskMetadata.ts`) is exposed via the `useTaskMetadata()` hook which reads from the `tasks:metadata.{id}.{name|description}` keys.

### Mock API Server

`mock-server.mjs` provides a zero-dependency mock backend for frontend development without the .NET backend, PostgreSQL, or qBittorrent. Run with `yarn dev` (starts both mock + dev server) or `yarn mock` (mock only, then `yarn start` separately).

Features include anime entries with TMDB poster paths and mixed download states, grouped listings, simulated progress, auth flow (any password), feed policies, season discovery, tasks, metadata review and file browsing. `mock-completion.mjs` and `mock-multi-source.mjs` supply local completion plans and multi-source CRUD/evaluation/confirmation using the same mock library; its dates and decisions are development examples rather than live TMDB or qBittorrent state. Listens on port 5097 (matching the Parcel proxy target).

## Key Configuration (appsettings.example.json)

- `ConnectionStrings:sdw` — PostgreSQL connection string
- `JwtSecret` — Required JWT signing key
- `Password:Value` — Legacy BCrypt bootstrap/upgrade input. Current registration/login authority is the household identity repository in PostgreSQL; registration is allowed only while no administrator identity exists and legacy bootstrap does not reserve it.
- `Torrent:Remote:Url` — qBittorrent API endpoint
- `FileStore:Local` — Download directory path
- `MikananiFeeds` — RSS feed URL array (static feeds)
- `TmdbApiKey` — TMDB API key (used for AI inference metadata, poster images, and season info)
- `AI:Engine` — `BuiltIn` or `CodexAppServer`, defaults to `BuiltIn`; the latter requires a separately isolated app-server deployment.
- `AI:Provider` — "OpenAI" or "Anthropic" for the built-in backend (defaults to OpenAI if omitted)
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

New feature repositories include `ILibraryCompletionRepository` (episode claims/candidates), `IMultiSourceSubscriptionRepository` (ordered feed links and decisions), `IMetadataRecognitionRuleRepository` (scoped rules and hit revisions), `IWatchlistRepository` (profile lists), `IMediaTimelineRepository` (media-bound ranges/chapters/templates) and `IDownloadCapacityRepository` (durable FIFO and budget transactions). Model configurations are discovered from the main assembly; database migrations and the snapshot remain the schema authority.

`DownloadCapacity:Enabled` defaults to true and `SafetyBytes` to 5 GiB. Disabled mode bypasses admission for new downloads and drains the existing unpaused durable queue. `LocalVolumePath` is an optional explicit shared mount of the actual downloader volume, not an application-root fallback. `Torrent:Polling` controls bounded refresh/backoff; `DownloadCompletion:Workers` defaults to 2 (clamped 1–16). See [feature configuration and boundaries](library-workflows.md).

HLS cache reservations remain independent of download admission. Waiting workers reuse atomically published shared manifests without reserving another write budget, and recheck again after acquiring a reservation before recreating a directory. Failure/cancellation cleanup only removes output created by that worker while its reservation still owns the directory; completed manifests and output belonging to another owner are preserved.

EF Core migrations run automatically on application startup.

Config migration: Users upgrading from pre-v2.2 (where AI config lived under `Inference:`) can run `deployments/migrate-config.sh` to automatically migrate to the new `AI:` config structure. For package installs, `postinstall.sh` runs this automatically.
