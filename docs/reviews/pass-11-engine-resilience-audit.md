# Pass 11.6: engine resilience audit

Scope: `MpvContext`, `MpvHandle`, `MpvEventLoop`, `MpvRenderContext`, `MpvRenderLoop`, `AngleMpvRenderBackend`,
`PlayerEventDispatcher`, and how `PlayerSession` and `MpvVideoSurface` attach, detach and dispose the core.

## Findings

| Area | Finding | Verdict |
| --- | --- | --- |
| Attach/detach ordering | The core is reference counted (`MpvHandle`); the event loop and every mpv render context hold their own reference, and the render context frees itself before dropping the core's. So the core can never be destroyed under `mpv_wait_event` or a live render context, whatever order the owners let go in. The render loop tears its stack down backend, presenter, device. | Sound, no change |
| Attach/detach ordering | `PlayerSession`'s constructor attached the surface and, if a later step threw, disposed the core without detaching it. The surface kept drawing a disposed core and could not rebuild its picture after a device loss. | **Fixed** |
| Device loss during disposal | Loss is latched, not thrown; teardown runs on the render thread through `DisposeQuietly`, so a throwing backend is reported and the presenter and device are still freed. Each object is freed once (fields are cleared before disposal). A lost context only makes ANGLE's GL calls no-ops, so freeing the mpv render context cannot hang on a dead device. | Sound, tests added |
| Queue overflow | The dispatcher only requested a UI refresh. mpv regenerates property changes itself, but the overflow can drop StartFile/FileLoaded/EndFile and command replies, so a lost FileLoaded left the player "loading" for good, and nothing re-read the state. Seeks and frame steps were already safe (both time out an unanswered command). Tracks are read on demand, never cached from events. | **Fixed** |
| Core destruction | `mpv_terminate_destroy` ran on whichever thread dropped the last reference. On session dispose or engine restart that was usually the UI thread (the event loop exits at once and the old render loop is joined first), so a slow shutdown (network stream, audio device) froze the window. Nothing waited for it at exit, so the process could end mid-teardown. | **Fixed** |
| Hot paths | No change to the frame loop or event pump. The overflow resync allocates only for its 19 requests, on a rare event. | Sound |

## What changed

- After a queue overflow the dispatcher clears the file-load flags and asks mpv for every observed property again
  (`mpv_get_property_async`); the replies are applied like ordinary property changes. Covers pause, position,
  duration, end of file, volume, idle state and the diagnostics values.
- `MpvContext.Dispose` drops its reference on a background thread ("Aether.MpvDestroy"), so destruction never runs on
  the caller's thread. Each disposed core is counted until `mpv_terminate_destroy` returns, and the app waits up to two
  seconds for them after the window closes (`MpvContext.WaitForDestroyedCores`).
- A failed session start detaches the surface before disposing the core.
- Tests: overflow resync (requests, stale values replaced, stuck loading cleared, shutdown race), background release
  and bounded exit wait (with a stand-in handle), render-loop teardown order and once-only frees, device loss with a
  throwing teardown, non-blocking loop disposal.

## Left as is

- `MpvVideoSurface.DetachPlayer` still waits up to 250 ms on the UI thread for the old render loop (existing hand-over
  design; teardown normally takes a few ms).
- Not verified against real libmpv here: the test machine has no `mpv-2.dll` beside the tests, so the libmpv-backed
  disposal test skips itself. Behaviour on a real GPU removal is covered only by simulated loss.
