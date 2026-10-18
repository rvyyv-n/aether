# Pass 8.2: picture-in-picture hand-over, lifetime and ordering

One window (ADR-0022): the main window switches its `AppWindow` presenter to `CompactOverlay` and back. Nothing
in the engine is handed over: the mpv core, the render thread, the D3D11 device, the swap chain and the
`SwapChainPanel` all stay where they are. "Hand-over" here means the window changes shape under a running
render loop, so the work is ordering and state, not moving objects between windows. Size facts come from the
8.1 spike (`pass-8-pip-spike.md`).

## What stays put

| Object | Owner | In PiP |
|:---|:---|:---|
| mpv core, event thread | `PlayerSession` | untouched; playback, pause and seeks carry on |
| Render thread, device, swap chain, mpv render context | `MpvRenderLoop` (via `MpvVideoSurface`) | untouched; no stop, no restart, generation unchanged |
| `SwapChainPanel` | the page | stays loaded and bound; only its size changes |

The loop only sees the panel's `SizeChanged`, the same as a drag-resize: the UI thread posts the new size
(`SetTargetSize`, non-blocking) and the render thread resizes the back buffer between frames
(`ReleaseBackBuffer → ResizeBuffers → AcquireBackBuffer`). A presenter switch produces a short burst of sizes;
the loop coalesces them (only the newest is applied). A move to another monitor changes the composition scale,
which the loop already applies as a matrix transform or a resize. The frame loop allocates nothing on any of
these paths (existing zero-allocation test, extended in 8.3 to a shrink-and-grow burst).

Because there is no loop restart, the one-render-context-per-core rule and the bounded join on restart (R7,
`Stop(250)`) are not on the PiP path. They still guard engine restarts, which PiP never triggers.

## State machine (`Aether.Core.Playback.PictureInPicture`)

UI thread only, no WinUI types, unit tested.

- **States:** `Off`, `On`. The presenter switch is synchronous on the UI thread, so there are no in-between
  states to model; re-entrancy (a switch requested while `AppWindow.Changed` for the previous one is being
  handled) is refused by a `_switching` flag in the window, not queued.
- **Enter** (P, the menu entry): allowed only with a video open and the window not already in PiP. Remembers
  where to return: `Windowed` or `FullScreen` (a maximised window is `Windowed`; Windows keeps its maximised
  state with the overlapped presenter).
- **Leave** (Back button, P, double-click on the video, the close button, Alt+F4): returns the target
  remembered on enter. Esc never leaves.
- **Forced leave:** the video closes (status `Idle`; `Loading` between playlist files does not count), and
  anything that needs the full window: Settings, the Open URL dialog, the clip drawer, diagnostics, the tour,
  and F (goes straight to full screen, not back to the windowed size first).
- **Bounds:** the last PiP rectangle is kept in memory and in `window.json` (`pip` object, optional), saved when
  leaving PiP and when the app closes in PiP.

## Ordering

Enter (UI thread, inside `MainWindow.SetPictureInPicture(true)`):

1. Read the work area of the monitor the window is on **before** switching (in CompactOverlay the spike saw
   `DisplayArea` report a different, wrong work area).
2. Lower the aspect lock's minimum to the PiP minimum (256×144 DIPs). It must happen first: the lock answers
   `WM_GETMINMAXINFO` for every presenter and would otherwise hold the overlay at 640×400.
3. `SetPresenter(CompactOverlay)`, then `MoveAndResize` to the PiP bounds: the remembered ones fitted onto a
   connected monitor, or 400×225 client in the bottom-right corner of the work area, 16 px in from its edges.
   The size keeps the video's aspect at the same area as 400×225. Outer size = client + the frame measured from
   the window (`Size − ClientSize`), never `ResizeClient` (the spike showed it adds a title bar the extended
   window does not have).
4. Raise `PictureInPictureChanged`. The page then: turns the backdrop blur off (ADR-0013), hides the transport
   card, clip chip and drawer, rail, title text, gear, toasts and diagnostics, and shows the PiP chrome.

Leave:

1. Record the PiP bounds (outer, screen pixels).
2. `SetPresenter(Overlapped)` (Windows restores the size, place and maximised state it had), or
   `SetPresenter(FullScreen)` when that is the return target or F asked for it.
3. Put the aspect lock's minimum back to 640×400 **after** the switch, so the overlay is never stretched to it.
4. Re-apply Always on top (only the overlapped presenter has it; the overlay is always on top by itself).
5. Raise `PictureInPictureChanged`; the page restores the full chrome and the blur.

Close while in PiP: `AppWindow.Closing` is cancelled and the window leaves PiP instead; playback carries on.
The app has no other way to close itself (no programmatic `Close` or `Exit`), so it always exits from the full
window, which then saves its bounds together with the last PiP bounds.

Bounds tracking skips the overlay while a switch is under way: right after `SetPresenter` the overlay still has
the presenter's default size and the old position, and recording that would overwrite the remembered PiP place
before it is read.

## Edge cases (reviewed in 8.3)

| Case | Expected |
|:---|:---|
| Switch mid-seek (dragging the seek bar, or seeks coalescing) | The card collapses, so the bar loses pointer capture and ends the drag at the last position; queued seeks still complete. No stall: the loop never stopped. |
| Switch while paused | The frame stays on screen: a resize forces a redraw (`ApplyPendingSize` returns true) even with no new video frame. |
| Device loss in PiP | The loop recovers in place (ADR-0008); the new swap chain binds to the same panel. PiP state is untouched. |
| Close button / Alt+F4 in PiP | Back to the full window, still playing. |
| Video closed or failed in PiP | Leaves PiP; the home screen shows in the full window. |
| Next file in a folder playlist | Stays in PiP; the window takes the new video's aspect on the next drag. |
| Monitor unplugged while in PiP | Windows moves the window; the saved PiP bounds are refitted on the next enter. |
| P with a text field focused | Ignored (the text field gets the key). |
| Rapid P presses | Each press toggles once; a press during a switch is ignored. |
