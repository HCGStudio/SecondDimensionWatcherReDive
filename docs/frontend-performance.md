# Frontend loading and bundle budgets

The SPA treats every page as an asynchronous route. The player route may be
preloaded when a play control receives hover or keyboard focus, but its MKV
probe and embedded-subtitle extraction are loaded only when needed. The
current fallback prepares HLS on the server; it does not download FFmpeg
WebAssembly to the browser.

MKV subtitle parsing and WebVTT conversion run in a dedicated worker. The
parser's browser bundle uses a raw Parcel asset pipeline so its global export
is preserved, and progress updates are throttled before reaching React.
Text subtitle extraction still reads the file sequentially to completion;
it can therefore use additional bandwidth while video playback uses Range
requests. Files without supported text subtitle tracks stop after metadata.
The worker and its fetch are terminated on cancellation.

The Yarn patch for `artplayer-proxy-mediabunny` limits scheduled audio to a
short buffer ahead of the playback clock, preventing the proxy from decoding
and retaining the entire audio track in advance. Pause, seek and speed changes
discard the previous audio schedule. Video decoding fills a bounded lookahead
queue of at most two frames backed by three reusable canvases, while
`requestAnimationFrame` presents at most one due frame using the audio clock. Future frames stay queued rather than being painted early within
the A/V tolerance window: painting several frames between browser refreshes
otherwise makes only the last one visible. Frames within the lateness tolerance
remain in order so audio-clock jitter does not discard normal 60 fps frames.
Catch-up reads stay bounded to eight frames per batch; an overdue final frame
is discarded before yielding instead of being queued for presentation.
Pause/resume preserves queued
frames; seek and teardown invalidate pending reads. Decoder errors use the
existing server-side HLS fallback. Keep these behaviors when updating the
proxy dependency.

## Historical baseline

Measured with `yarn build` on 2026-08-29:

| Production asset | Before | After |
| --- | ---: | ---: |
| Initial JavaScript | 1,785,214 bytes | 706,419 bytes |
| Player route | included in initial JS | 540,246 bytes, async |
| Chat route | included in initial JS | 213,092 bytes, async |
| Metadata review route | included in initial JS | 27,798 bytes, async |
| FFmpeg WASM | 32,232,419 bytes, emitted | 32,232,419 bytes, on-demand |

The initial JavaScript transfer was reduced by 60.4%. This baseline predates
the move from browser FFmpeg to server-side HLS; the FFmpeg asset sizes above
describe that earlier implementation.

Run `yarn build:budget` to build the production client, emit
`dist/bundle-report.{json,md}`, and enforce the checked-in budgets. CI publishes
the report with the frontend artifact and fails if the initial module, any
asynchronous JavaScript chunk, or the combined home-route JavaScript exceeds
its budget.

Production static files use Brotli/Gzip response compression. Parcel-hashed
assets are cached for one year with `immutable`; HTML revalidates on every
request so a deployment cannot strand clients on a stale import map.
