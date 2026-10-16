# Pass 7: first-run tour (7.26)

Design brief for 7.26. The owner gave full creative freedom; it follows the look of the rest of the app (quiet,
near-monochrome, solid surfaces over video) and reuses the motion vocabulary in `pass-7-rail-and-motion-spec.md`.

## 1. What it is

A spotlight tour: the window dims, a lit window opens around one real control, and a small card next to it says
what the control does. Next moves the light to the next control; the card steps aside while the light travels and
comes back beside its new target. Nothing is a picture of the app: every step lights up the real thing.

| # | Step | What is lit | Card |
|:---|:---|:---|:---|
| 1 | Open a video | The Open file and Open URL buttons on the home screen (the bar's Open button when a video is open) | Below |
| 2 | The controls | The real bar when a video is open; otherwise a dashed outline where the bar will sit (520 × 88, 24 from the bottom) | Above |
| 3 | The side rail | The real rail, held open for the step | Right |
| 4 | Right-click for more | The real right-click menu, opened at the left edge without taking the keyboard; nothing else lit | Right of the menu |
| 5 | Keys worth knowing | No light; the card lists the user's current keys as keycaps: Play/pause, Back 5 s, Forward 5 s, Set In, Set Out, Export clip, Full screen | Centre |
| 6 | Settings | The gear in the title bar; the card says the tour can be replayed from Settings › About | Below |

Where a target is not on screen (full screen hides the gear; a narrow window has no room for the menu and the card
side by side) the step shows its card in the centre with no light, so the tour never points at nothing.

## 2. Look

- **Scrim:** SurfaceScrim (the dialog scrim) from below the title bar to the bottom. The title bar stays live, so the
  window can still be moved, resized and closed, and the gear can be clicked.
- **Light:** a hole in the scrim, 6 px larger than the target on every side, with the target's radius plus 6.
- **Ring:** a 2 px StrokeInputFocus (white) line round the hole: the Windows focus ring, read as "this one". Dashed
  when it marks where the bar will be rather than the bar itself.
- **Card:** 320 wide (`AetherSizeTourCard`), SurfaceDialogFallback with the StrokeSurface hairline, radius 8,
  padding 16, 16 from the light. Top line: "2 of 6" (Caption, secondary) and Skip tour on the right. Then the title
  (Subtitle) and one or two sentences (Body, secondary). Bottom: progress dots on the left (6 px, the current one a
  16 px pill), Back (outline pill, hidden on the first step) and Next (emphasis pill; Done on the last) on the right.
- Strings are in Resources.resw (`Tour_*`).

## 3. Motion

Reuses the shared tokens and helpers; nothing new.

| Move | How | Quick | Springy |
|:---|:---|:---|:---|
| Tour opens | Scrim fades in (enter/exit fade, no slide); the card enters from its side | 200 ms ease-out, 8 px | spring from 10 px |
| Light moves | The hole and ring travel and resize between targets (the bar's grow tween) | 260 ms ease-out | the bar's spring, slight overshoot |
| Card changes step | Exits towards its side, gets its new text and place, enters again | out 150 ms, in 200 ms | in on the spring |
| Light goes away | It shrinks to a point at its centre (steps 4 and 5) | as "Light moves" | as "Light moves" |
| Tour closes | Card and scrim exit, then collapse so nothing invisible takes clicks | 150 ms ease-in | same |

The rail in step 3 enters and leaves with its own slide; the menu in step 4 keeps its system animation.
**Reduced motion** (Windows animation effects off): every fade, slide and glide is instant, as everywhere else.

## 4. Behaviour

- **When:** once, on the first launch that lands on an idle home screen (engine ready, no file opening, nothing
  failed). Launched with a file, it waits for a later launch. It starts 400 ms after the home screen is ready so it
  does not land on top of the window opening.
- **Seen:** `general.tourSeen` is saved as soon as the tour starts, so closing the window halfway does not bring it
  back. Reset all settings clears it, so the tour shows again after a full reset.
- **Replay:** Settings › About › Show the tour again closes Settings and starts the tour over whatever is open.
  With a video open the steps light the real bar and its Open button.
- **Ends** on Skip tour, Done, Esc, or when the user goes somewhere else: a file opens or is dropped, a link
  starts loading, Open URL or Settings opens (the gear stays clickable).
- **While it runs:** clicks under the scrim do nothing (the lit controls are shown, not live; the menu in step 4
  is live, as it is the real menu). Configurable shortcuts are off, right-click does not open a second menu, toasts
  wait, and the chrome stays up (a `Tour` hold, so the bar shows in full).
- **Keyboard:** focus moves to Next at every step; Tab cycles Skip, Back and Next inside the card; ← and → go Back
  and Next; Enter or Space presses the focused button; Esc skips (first in the Esc order). Each step is announced
  to screen readers ("2 of 6. The controls. …").

## 5. Code

- `Aether.Core.Playback.Tour`: `FirstRunTour` (step order, active, next, back, end, when to start on launch,
  which keys step 5 lists) and `TourLayout` (where the card goes next to its target, flipping and clamping to the
  window; whether the menu and card fit side by side). Unit tested.
- `HoverRail.SetPinned`: holds the rail open for step 3 whatever the pointer does.
- `MainPage.Tour.cs` and a `TourLayer` at the top of `MainPage.xaml`; the About page asks the window to replay it.
