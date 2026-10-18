# Pass 8.1 spike: CompactOverlay at 400×225

Measured on the dev machine (Windows 11 Pro 26200, one monitor, display scale 100% as the app reads it) by
switching the main window to the `CompactOverlay` presenter and asking for a range of sizes with
`AppWindow.Resize`. The code was a throwaway hook in `MainWindow`; it is not in the repo.

## Results

| Asked for (outer) | Got (outer) | Client | Notes |
|:---|:---|:---|:---|
| (switch only) | 400×225 | 384×217 | the presenter's own default size; position kept |
| 400×225 | 400×225 | 384×217 | |
| 100×56 | 136×56 | 120×48 | the real minimum: about 120×48 client |
| 200×113 | 200×113 | 184×105 | |
| 300×169 | 300×169 | 284×161 | |
| 1200×675 | 1200×675 | 1184×667 | |
| 2000×1125 | 1940×1100 | 1924×1092 | capped near the monitor size |
| switch back to Overlapped | 656×408 | 640×400 | Windows restores the size and place it had before |

- **Minimum:** far below 400×225, so 400×225 is fine. The frame is the same as the normal window's: 8 px of
  invisible resize border left, right and bottom (outer = client + 16×8). For a 400×225 picture, ask for
  416×233 outer. `ResizeClient` is not usable here: it adds a title bar height the extended window does not have
  (asked 400×225, got 400×256).
- **Caption buttons:** the system draws only Close in CompactOverlay. `TitleBar.RightInset` drops from 138 to 46
  and the title bar height stays 32 (Standard). So "Back to full window" (32 px) sits immediately left of a
  46 px Close, inside our extended title bar, with the drag region covering the rest of the top strip.
- **Our minimum size fights it:** the aspect lock's 640×400 `WM_GETMINMAXINFO` minimum applies to every
  presenter, so it must drop to the PiP minimum while in PiP and go back on return. The lock already allows
  changing the minimum at any time; the "only one lock per process" rule does not matter with one window.
- **Display oddity:** in the Overlapped presenter `DisplayArea.WorkArea` read 1600×900 (matching Windows Forms'
  view of the screen); while in CompactOverlay the same call read 1920×1080. Place the PiP window from the work
  area read *before* switching presenter, and clamp to it.

## Decision

Build as planned: one window that switches to CompactOverlay and back (scout Option A). Open at 400×225 client
in the bottom-right of the work area, remember the last PiP bounds, and let Windows restore the full window's
own size and place on return.
