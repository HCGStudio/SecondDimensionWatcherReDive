# Frontend loading and bundle budgets

The SPA treats every page as an asynchronous route. The player route may be
preloaded when a play control receives hover or keyboard focus, but its MKV
probe, embedded-subtitle parser, and FFmpeg fallback are separate dynamic
imports. The 32 MB FFmpeg WebAssembly asset is therefore requested only after
native playback and the lightweight Matroska proxy have both been ruled out.

## Baseline

Measured with `yarn build` on 2026-08-29:

| Production asset | Before | After |
| --- | ---: | ---: |
| Initial JavaScript | 1,785,214 bytes | 706,419 bytes |
| Player route | included in initial JS | 540,246 bytes, async |
| Chat route | included in initial JS | 213,092 bytes, async |
| Metadata review route | included in initial JS | 27,798 bytes, async |
| FFmpeg WASM | 32,232,419 bytes, emitted | 32,232,419 bytes, on-demand |

The initial JavaScript transfer was reduced by 60.4%. Emitted FFmpeg assets
appear in Parcel's import map but are not fetched until the transcoder module
calls `ffmpeg.load()`.

Run `yarn build:budget` to build the production client, emit
`dist/bundle-report.{json,md}`, and enforce the checked-in budgets. CI publishes
the report with the frontend artifact and fails if the initial module, any
asynchronous JavaScript chunk, or the FFmpeg WASM asset exceeds its budget.

Production static files use Brotli/Gzip response compression. Parcel-hashed
assets are cached for one year with `immutable`; HTML revalidates on every
request so a deployment cannot strand clients on a stale import map.
