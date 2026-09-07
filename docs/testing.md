# Production-boundary test matrix

The default CI workflow exercises the boundaries that are easy to miss with mocked
unit tests:

- `dotnet test SecondDimensionWatcherReDive.slnx` starts a disposable PostgreSQL 17
  container, migrates an empty database, upgrades the previous schema, and exercises
  PostgreSQL advisory locks, escaped `LIKE`, raw SQL, unique indexes, concurrent writes,
  and transaction rollback. Testcontainers and its resource reaper own cleanup.
- `yarn test:unit` covers browser request boundaries such as concurrent JWT refresh and
  playback progress persistence. `yarn test:e2e` serves the prebuilt production `dist`
  artifact with SPA fallback and starts the mock API as a separate process; it never uses
  the Parcel development server. The Chromium journeys cover registration/login, download,
  VFS, playback state, subscription policy, metadata review, incident recovery, and chat
  SSE. Failed tests retain a screenshot, video, and Playwright trace.
- The FUSE test project covers the HTTP client, retry/range behavior, errno mapping,
  TTL cache, and concurrent file handles without root. CI also invokes
  `deployments/ci/fuse-mount-smoke.sh`; it performs a real read-only mount when the Linux
  runner exposes an accessible `/dev/fuse`, and emits an explicit skip notice otherwise.
  Set `SDW_REQUIRE_FUSE_MOUNT=1` on a privileged runner to turn missing FUSE support into
  a failure.
- The delivery smoke builds the final container and an amd64 Debian package. Both are
  started against disposable PostgreSQL databases, must serve the production frontend,
  and must apply EF migrations. The package is purged and all processes, containers,
  networks, temporary data, and test-only system accounts are removed by traps even on
  failure or cancellation.

## Local commands

```bash
dotnet test SecondDimensionWatcherReDive.slnx -c Release

cd SecondDimensionWatcherReDive.Client
yarn install --immutable
yarn test:unit
yarn build
yarn playwright install chromium
yarn test:e2e
```

For a local mount smoke, install the NativeAOT build prerequisites documented in the
FUSE README, publish the release client, and pass its executable to the script:

```bash
dotnet publish SecondDimensionWatcherReDive.FUSE/SecondDimensionWatcherReDive.FUSE.csproj \
  -c Release -r linux-x64 -p:StripSymbols=true \
  -o /tmp/sdw-fuse-smoke
deployments/ci/fuse-mount-smoke.sh /tmp/sdw-fuse-smoke/sdwfuse
```

CI publishes the raw Cobertura reports and an aggregate coverage summary. The current line
threshold is deliberately modest so it establishes a ratchet without discouraging incremental
coverage improvements; raise it as production-boundary coverage grows.
