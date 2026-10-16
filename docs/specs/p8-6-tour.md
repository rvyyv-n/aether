# P8.6 First-run tour: fits any window, no sidebar, livelier, web links

Build spec for Sonnet. Updates the 7.26 tour (`docs/reviews/pass-7-first-run-tour-spec.md`). Code: `src/Aether.App/MainPage.Tour.cs`, the `TourLayer` block in `MainPage.xaml`, `src/Aether.Core.Playback/Tour/FirstRunTour.cs` and `TourLayout.cs`. Runs alongside P8.1 (sidebar); see Risks.

## Root causes
- **(a) Card 2 runs off the bottom.** On Next, the card exits first; `ChromeFader.Exit` sets it `Collapsed`, and only then calls `FillTourCard` → `PlaceTourCard`, which measures the card. A collapsed element measures 0 × 0, so `cardHeight` is 0. Step 2 is placed Above the bar: `y = target.Y − 16 − 0`, so the card's top sits just over the bar and its whole body hangs below it, off the window, with Next (the last row) lost first. Resizing calls `RefreshTourLayout` while the card is visible, so the measure is right and it jumps into place. Below-placed steps (1, 6) hide the bug because they never need the height.
- **(e) Keys card over the recents** is the same bug: Centre computes `y = (H − 0) / 2`, so the card's top lands at the middle of the window and hangs down over the recent-files list.
- **Also in `TourLayout.Clamp`:** it keeps 16 from y = 0, not from below the 32 px title bar, so a card can sit under the title bar; and when a card is taller than the space, `Max(Margin, …)` wins and the bottom overflows.
- **(b) "Skip tour" clipped.** The Skip pill has `Margin="0,-6,-8,-6"`: a 32 px pill squeezed into a ~20 px header row and pushed 8 px past the right edge of the card's content column. It is drawn bigger than the slot layout gives it, and the overhang is trimmed. Confirm in Live Visual Tree (layout slot narrower and shorter than the button's actual size); the fix below removes the overhang either way.

## Fixes
- **Measure while visible.** In `FillTourCard`, call `_fader.Enter(TourCard, …)` (sets composition opacity 0, then `Visible`) *before* `PlaceTourCard`; `PlaceTourCard` measures with `new Size(TourCard.Width, ∞)`. `RefreshTourLayout` skips the card while it is collapsed (mid-change; the fill that follows places it).
- **Header row.** Two columns (`*`, `Auto`), height `AetherSizeControl` (32), no negative margins; progress text and Skip both `VerticalAlignment="Center"`; Skip keeps its transparent border, padding 12,0. `TourContent` top margin 12 → 4 so the card keeps its rhythm.
- **Scroll safety net.** Wrap `TourContent` in a `ScrollViewer` (vertical auto, horizontal disabled). `PlaceTourCard` sets the card's `MaxHeight` from the layout result, so the header and the Back/Next row are always on screen and only the middle scrolls.

## Placement (pure, in `TourLayout`, unit tested)
`PlaceCard(target, cardW, cardH, areaW, areaH, topInset, placement)` returns `(X, Y, MaxHeight)`.
1. Bounds: left 16, right `areaW − 16`, top `topInset + 16` (`topInset` = title bar height, 0 in full screen), bottom `areaH − 16`. `MaxHeight = bottom − top`; `h = min(cardH, MaxHeight)`.
2. No target or Centre: centre in the bounds (so below the title bar, not the whole layer).
3. Otherwise try sides in order, taking the first where the card fits wholly inside the bounds without covering the lit rect: Below → [Below, Above]; Above → [Above, Below]; Right → [Right, Left, Below, Above]. Gap 16 from the light.
4. No side fits: use the preferred side, then clamp. Clamp: `x` into `[left, right − cardW]`, `y` into `[top, bottom − h]`; when `h == MaxHeight`, `y = top`.
This keeps every card fully inside down to the 640 × 400 minimum window (with and without the title bar).

## Steps (6)
`Steps = [Open, Links, Controls, Menu, Shortcuts, Settings]`; `TourStep.Rail` goes.

| # | Step | Light | Card |
|:---|:---|:---|:---|
| 1 | Open a video | `OpenActions` on home; the bar's Open button with a video | Below |
| 2 | Play web links (new) | `OpenUrlButton` on home (the light narrows from both buttons to this one); with a video open, no light | Below; Centre with no light |
| 3 | The controls | Real bar, or the dashed slot | Above |
| 4 | Right-click for more | Real menu at (24, title bar + 24), no light | Right of the menu; Centre if it does not fit |
| 5 | Keys worth knowing | None | Centre of the content area; card 440 wide (`AetherSizeTourCardWide`), keys in two columns (4 + 3 rows) so it fits at 400 tall |
| 6 | Settings | The gear | Below (right-clamped); Centre in full screen |

- **Menu width.** The menu is now about 220 wide. `TourLayout.MenuWidth` 276 → 224 and `AetherSizeContextMenu` 276 → 224 (comment: room for the ~220 menu). `MenuFits`: 24 + 224 + 16 + 320 + 16 = 600, so the side-by-side menu step now works at the 640 minimum. Better still: on the menu's `Opened`, read the real `MenuFlyoutPresenter.ActualWidth` (via `VisualTreeHelper.GetOpenPopupsForXamlRoot`) and re-place the card; fall back to 224.
- **Strings.** New `Tour_Links_Title` "Play web links"; `Tour_Links_Body` "Play YouTube and other web links right in Aether. Choose Open URL or press {0}, or copy a link and press Ctrl+V."; `Tour_Links_BodyNoKey` the same without "or press {0}". `{0}` is the user's current Open URL binding (`App.Shortcuts.Get(ShortcutAction.OpenUrl)`, default Ctrl+Shift+O, shown as text); unbound uses NoKey. `Tour_Open_Body` → "Choose Open file, or drop a video anywhere on the window." Delete `Tour_Rail_Title/Body`. `Settings_About_TourDescription` → "A quick look at opening videos and links, the controls, the right-click menu and the keys". No tour text mentions the sidebar or rail.

## No sidebar during the tour (c)
Use P8.1's gate: `SyncRail` adds `&& !_tour.IsActive` to `available` (add it here if P8.1 has not landed). `StartTour` calls `SyncRail()` after the tour is active (replaces `CloseRail()`), so the hot zone collapses and an open sidebar closes before the first card; `EndTour` calls `SyncRail()` after `_tour.End()` to bring it back. Remove the Rail branch in `ShowTourStep`, `_rail.SetPinned` in `LeaveTourStep`, the Rail case in `TourTargetOf`, and the "rail just became visible" comment. Nothing animates the sidebar while the tour runs.

## Motion (d); all off with reduced motion
| Move | How | Timing |
|:---|:---|:---|
| Change step | Card fades and slides out against the direction of travel, new card in from the other side: Next exits left, enters from right; Back the reverse. First card and tour open: in from its target's side, as now | Out `AetherMotionQuickOutMilliseconds` 150 ease-in; in `AetherMotionQuickInMilliseconds` 200 ease-out (spring in Springy); slide 16 px (new token `AetherMotionTourSlide`; add an optional distance to `ChromeFader.Enter/Exit`) |
| Light moves | Unchanged glide | `AetherMotionQuickGrowMilliseconds` 260 ease-out |
| Pulse | New `TourPulse` rectangle under `TourRing`, same rect, radius and dash. When the light settles (tween `Value` reaches 1): composition scale out by 8 px a side (scale `1 + 16/w`, `1 + 16/h`, centre point mid-rect) while opacity goes 0.5 → 0, 3 times, then it stays hidden. Stopped (opacity 0) when the light moves, on steps with no light, on resize and on EndTour | 900 ms ease-out per pulse, 1600 ms apart (new tokens `AetherMotionTourPulseMilliseconds` 900, `…TourPulseIntervalMilliseconds` 1600) |
| Progress dots | Build the 6 dots once per tour, all `AetherTextPrimaryBrush`. A `MotionTween` per step change: old dot 16 → 6 wide, opacity 1 → 0.45 (the tertiary value); new dot 6 → 16, 0.45 → 1. Total row width never changes, so nothing jitters | 260 ms ease-out (`QuickGrow`) |

Reduced motion (`Motion.AnimationsEnabled` false): card swaps in place, light jumps, no pulse at all, dots jump. `ChromeFader` and `MotionTween` already honour it; the pulse must check it before starting. Dots and pulse are decorative (`AccessibilityView="Raw"`); the announcement and focus-on-Next stay as they are.

## Files
`FirstRunTour.cs` (steps, placement, `HasSpotlight`), `TourLayout.cs` (new `PlaceCard`, `MenuWidth`; optional pure `DotWidth(i, from, to, t)`), `MainPage.Tour.cs`, `MainPage.xaml` (header, ScrollViewer, `TourPulse`), `MainPage.Rail.cs` (gate, if needed), `Controls/ChromeFader.cs` (slide distance), `Theme/DesignTokens.xaml` (tokens above), `Strings/en-us/Resources.resw`, `tests/Aether.Core.Tests/FirstRunTourTests.cs`, and the 7.26 spec's step table (one-line pointer to this file).

## Tests
- Steps list and count 6, no Rail; `PlacementOf` and `HasSpotlight` per step.
- Placement theory over 640 × 400 (title bar 32 and 0), 800 × 600, 1920 × 1080, each step's real target and card heights (about 210 for steps 1-4 and 6, 280 for the keys card, and an oversize 500): card always within the bounds, never under the title bar; `MaxHeight` used when oversize.
- Regression for (a): Above target at the bottom of 640 × 400 with height 210 → `y + 210 ≤ 384`. Centre puts the card's middle at the middle of the content area.
- `MenuFits(640, 320)` is true; dot widths always sum to 5 × 6 + 16.
- Strings: `Tour_Links_Body` has `{0}`, `Tour_Links_BodyNoKey` has none; no `Tour_*` value contains "rail" or "sidebar".
- Manual: every step at 640 × 400, Next visible without resizing; replay from About with a video open; full screen; Windows animation effects off; Narrator.

## Risks
- P8.1 touches the same files (it also drops the Rail step). Land P8.1 first and rebase this onto it; if this lands first, P8.1 skips its tour section.
- P8.7 stutter: the pulse and dot tween run while a video may be playing under the scrim (replay). The pulse is composition-only and stops after 3 pulses; check dropped frames on a replay over a playing video.
- Reading the menu presenter's width depends on WinUI's popup tree; keep the 224 fallback.
- Longer translations wrap; the measure-while-visible fix and the scroll net cover them.

## Open questions (default in brackets)
1. Steps 1 and 2 both light the home buttons; fold links into step 1 instead? [No, own step; it is the news people miss.]
2. With a video open, light the bar's Open button for the links step? [No, centre card with no light.]
3. Mention that YouTube needs yt-dlp? [No; the Open URL dialog already says so when it is missing.]
4. Pulse forever or a few times? [3 pulses then still; endless motion distracts and runs past the 5-second guideline.]
5. Slide by direction of travel, or from the target's side as now? [Direction of travel.]
6. Roadmap says "progress bar", report says dots: [animated dots.]
7. Keys card: two columns 440 wide, or one column with scrolling at small sizes? [Two columns.]
