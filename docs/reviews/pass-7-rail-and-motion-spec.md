# Pass 7: hover rail (7.21) and the motion overhaul

Design brief for 7.21 and a refit of every animation in the app. The owner gave full creative freedom; the
redline (handoff screen 6 and "Reduced motion") and the 7.15 decisions still hold where they apply.

## 1. Hover rail (screen 6)

**Where:** the player and the home screen. Not in Settings (it has its own section list), not under the Open URL
dialog, never during picture in picture (Pass 8).

**States**

| State | What shows | How it gets there |
|:---|:---|:---|
| Collapsed (6.1) | Nothing. A 4 px hot zone along the left edge, from below the title bar to the bottom. | Default. |
| Peek (6.2) | A 4 px FillRange strip in the hot zone, the rail's height, square on the window edge and round (2) on the inside. | The pointer is in the hot zone. |
| Open (6.3, 6.4) | The rail: 56 wide, 8 from the left, from 8 below the title bar (40, or 8 in full screen) to 8 above the pill (follows the pill as it grows); 24 from the bottom when there is no pill. | 150 ms resting in the hot zone, or a click on it. |

**Surface:** the solid SurfaceOverlayStrong (#F20E1014) with the StrokeSurface hairline, radius 8, padding 8. In-app
Acrylic cannot see the swap chain (ADR-0013), so the redline's fallback is the build, not a spike.

**Items:** 40×40, radius 8, gap 4, 20 px glyphs, tooltips to the right with the label and shortcut.

| Item | Glyph | Does | Enabled |
|:---|:---|:---|:---|
| Player | Video `E714` | Closes the rail (it is where you are). Shown active: FillEmphasisRest + StrokeControl. | Always |
| Clips | `E8F1` (as in the menu) | Opens the clip drawer. | Same rule as the menu: a video is open and a clip is saved |
| Stream | `E774` (as in the menu) | Nothing yet (Pass 11). | Never, tooltip "Stream (coming later)" |
| spacer | | | |
| Settings | `E713` | Opens Settings. | Always |

**Behaviour**

- Opens after 150 ms of rest in the hot zone (passing through does not open it) or at once on a click there.
- Closes 300 ms after the pointer has left both the rail and the hot zone, on Esc (first in the Esc order), after
  an item is used, and when the chrome hides.
- While the pointer is on it, the rail keeps the chrome up (a new `Rail` hold) but does not grow the slim pill: the
  bar only grows for the controls themselves.
- Never opens or peeks during a drag: a file dragged over the window, a seek-bar drag, a clip dragged out of the
  drawer, or any held mouse button.
- The logic is a small state machine in Aether.Core (`HoverRail`), like the volume flyout's, with unit tests.

## 2. Motion: one vocabulary, two profiles

Everything moves in one of four ways. Each has a token set; both profiles share durations for leaving.

| Move | Used by | Quick | Springy |
|:---|:---|:---|:---|
| **Chrome fade** | title bar, top scrim, pill, clip chip, caption glyphs, blur | 150 in / 250 out, Fluent ease-out | same (it is in step with the render thread's blur and the system caption glyphs) |
| **Enter / exit** (fade + short slide) | toasts, clip drawer rows, rail, Settings, settings sections, diagnostics, Open URL dialog | in: 200 ms ease-out from 8 px; out: 150 ms ease-in by 4 px, then collapsed | in: the damped spring (0.85, 350 ms period) from 10 px, a little overshoot; out: as Quick |
| **Bar grow / shrink** | the transport card | 260 / 200 ms eased | the same spring, keeps its speed when reversed |
| **State crossfade** | hover and press fills on every Aether button, rail item and settings row | 83 ms (the Windows control value) | 83 ms, plus a press squash to 92% that springs back on release (transport and rail buttons) |

Slide directions follow where the thing comes from: up for things above the pill (toasts, clips, dialog), down for
the diagnostics panel at the top, in from the left for the rail. Settings and its sections rise up by the slide.

**Reduced motion** (Windows "Animation effects" off): every fade, slide, spring and squash is instant; delays and
timers are unchanged. Read live, so flipping the setting applies at once. The 83 ms hover crossfade is a XAML
brush transition, as in the stock Windows controls.

**Kept as they are:** right-click menus and the tracks, video and volume flyouts keep the system's own popup
animation (7.15 decision); it already honours reduced motion and matches Windows.

**What changes from today**

- The clip drawer, Settings, diagnostics and the Open URL dialog used to appear and vanish at once; they now enter
  and exit like toasts. Exits collapse the element when the fade ends (or at once with reduced motion), so nothing
  invisible takes clicks.
- Settings sections switch with an entrance instead of a hard cut.
- Hover and press fills crossfade instead of snapping.
- Springy gains the press squash, so the two profiles differ in feel, not only in the bar.
- The curve and spring maths move into Aether.Core so they are unit tested; the unused 200 ms transition on the
  home view (it never changed opacity) goes.

## 3. Libraries considered

- **Composition (built in):** key-frame, expression and spring animations on the compositor thread. Used for every
  fade, slide and squash. Springs are the app's own spring sampled into key frames, so slides and the press squash
  match the UI-thread spring of the bar exactly; the squash's frames start from wherever the button is, so a release
  picks up a press midway. Native compositor springs were considered, but Microsoft only documents key-frame and
  expression animations on XAML element properties such as Scale.
- **XAML implicit transitions** (`BrushTransition`): used for the hover crossfades inside control templates.
- **Connected animations:** no use here; nothing moves between two pages.
- **CommunityToolkit.WinUI.Animations:** a friendlier builder over the same Composition calls. Not added: the app's
  needs fit in two small classes, and it would be a new dependency for no new capability.
- **Lottie / AnimatedIcon:** heavy for a near-monochrome UI with Segoe glyphs. Not added.
