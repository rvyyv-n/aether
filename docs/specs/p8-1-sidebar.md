# P8.1 Sidebar: a small floating card with the bar's blur

Build spec for Sonnet. Replaces the 7.21 hover rail's look (`Rail` in `MainPage.xaml`, `MainPage.Rail.cs`); the hover logic in `HoverRail` stays.

## Shape and size
- A vertical pill, not a strip: 48 wide, padding 6, four 36 px items 4 apart (Player, Clips, Stream, Settings; no spacer, Settings is simply last). Height 6 + 4×36 + 3×4 + 6 = 168.
- Radius 24 (`AetherTransportCornerRadius`), the slim bar's radius; items are 36 circles (radius 18), so 6 + 18 = 24 is concentric, exactly like Play inside the slim pill.
- Placed 16 from the left edge (`AetherSizeOsdSideMargin`), centred vertically in the space between the title bar (or the top in full screen) and the window bottom. If that would bring it within 8 of the bar's top edge, it moves up to sit 8 above the bar; if the window is too short for that under the title bar, it doesn't open.
- New tokens: `AetherSizeSidebarWidth` 48, `AetherSizeSidebarItem` 36, `AetherSizeSidebarPadding` 6, `AetherSidebarItemCornerRadius` 18. Retire `AetherSizeRailWidth` and `AetherSizeRailItem` and point their comments at the new keys. `AetherSizeRailHotZone` stays.

## Look (tokens)
- Card: `AetherSurfaceOsdBrush` (62% ink, the bar's tint), 1 px `AetherStrokeSurfaceBrush`, radius 24. No solid `SurfaceOverlayStrong` any more.
- Items: new `AetherSidebarItemStyle` based on `AetherTransportIconButtonStyle` (36, 14 px glyphs, hover `FillSubtleHover`, press `FillSubtlePressed`) with radius 18. Active item (Player while the player shows): `FillEmphasisRest` + `StrokeControl`, like the Play button. Disabled Stream: `TextDisabled`.
- Over the home screen (no video) there is nothing to blur: the tint sits on Mica, the same as any card there.

## Blur (same as the bar, ADR-0013 / P7.6)
- Reuse the render-thread blur; do not try in-app Acrylic. Today `BackdropSource` holds one region with one fade. Give it a fixed set of two slots (`BackdropSlot.Bar`, `BackdropSlot.Sidebar`), each with its own region, active flag and fade (a small struct array, no allocation per frame). `NeedsFrame` is true while either slot is fading. `TryGetFrame` becomes `TryGetFrames(timestamp, Span<BackdropFrame>)` returning the count.
- `BackdropBlurPass.Draw` loops over the frames: save GL state once, then copy, downsample, blur and composite per region, restore once. Cost scales with area: the sidebar is about 48×168 plus the 24 px copy margin, roughly a fifth of the bar's area, so expect about +0.1 ms a frame while it is shown and nothing while it is hidden (slot inactive or opacity 0 means no work).
- Keep the cached-frame rule: redraws between video frames copy the cached picture (`VideoFrameCache`) and only redo the blur. Add no per-frame UI-thread work: the page posts the sidebar's rectangle only when it is placed or the window or scale changes (as `PostBackdropRegion` does for the bar: compare with the last posted value, post on change).
- `MpvVideoSurface.PostBackdrop`, `SetBackdropActive` and `FadeBackdrop` take a slot. The sidebar slot is active only while a video is open, the sidebar is open, and not in picture in picture (PiP turns both slots off), full screen is fine.
- Shadow: the bar's shadow (0,8 / 24 / 32%) is drawn per region; keep it for the sidebar too.

## Show, hide and motion
- Keep `HoverRail` as is: 4 px hot zone on the left edge, peek, opens after 150 ms or on click, closes 300 ms after the pointer leaves, on Esc, after an item is used, and with the chrome; never during a drag.
- Hot zone stays full height (easy to find), but the peek strip (`RailPeek`) is drawn only alongside where the card will be (168 tall, centred the same way), radius 2.
- Enter and exit: opacity only, no slide, on the chrome fade timing (`AetherMotionChromeShowMilliseconds` / `HideMilliseconds`, ease-out), with `FadeBackdrop(Sidebar, …)` given the same duration so blur and tint stay together. A slide would need the blur rectangle to move every frame, which is the per-frame cost we're avoiding. Springy profile: same as Quick here.
- Reduced motion (`Motion.AnimationsEnabled` false): appears and disappears at once, blur included (duration 0).

## Tour (P8.6 c)
- The sidebar must not exist during the tour. In `SyncRail`, add `&& !_tour.IsActive` to `available`, so the hot zone is collapsed and an open sidebar closes the moment the tour starts. Drop the tour's Rail step (`TourStep.Rail`, its strings, `SetPinned` use and the `FirstRunTourTests` cases); `HoverRail.SetPinned` can go if nothing else uses it. If the owner wants the sidebar mentioned, the Controls step's text can say "Rest the pointer on the left edge for Clips and Settings".

## Accessibility
- Card: `AutomationProperties.Name` = `Rail_Name` ("Navigation"), `AutomationProperties.LandmarkType="Navigation"`. Items keep their names and tooltips (placement Right); Settings' tooltip keeps its shortcut.
- Focus visuals: system focus ring, 3 px out (`FocusVisualMargin -3`), which still fits inside the 16 px margin.
- Esc closes it and returns focus to the page.

## Files
- `src/Aether.App/MainPage.xaml`: `Rail`, `RailPeek` markup.
- `src/Aether.App/MainPage.Rail.cs`: `PlaceRail` (centre and bar clearance), post the sidebar's rectangle, tour gate.
- `src/Aether.App/MainPage.Backdrop.cs`: post per slot.
- `src/Aether.App/MainPage.xaml.cs`: `ApplyChrome` and the video-open path call the slotted backdrop methods.
- `src/Aether.App/Controls/MpvVideoSurface.cs`: slotted API.
- `src/Aether.Core.Interop/Rendering/BackdropSource.cs`, `BackdropGeometry.cs`, `BackdropBlurPass.cs`, `AngleMpvRenderBackend.cs`, `MpvRenderLoop.cs` (`NeedsFrame` use).
- `src/Aether.App/Theme/DesignTokens.xaml`: tokens and item style.
- `src/Aether.App/MainPage.Tour.cs`, `src/Aether.Core.Playback/Tour/FirstRunTour.cs`, strings.
- Tests: `BackdropTests` (two slots fade on their own, `NeedsFrame` with either fading, inactive slot draws nothing), `BackdropBlurPassTests`, `FirstRunTourTests`, `HoverRailTests` if `SetPinned` goes.

## Risks
- GL state: the pass restores state once after all regions; check that mpv's next frame is unchanged (the existing state-restore test covers this, extend it to two regions).
- Overlap: at short windows the sidebar and bar could overlap and blur twice; the placement rule above stops that.
- Stutter (P8.7 is still open): measure dropped frames with the sidebar open and fading before and after; if the blur pass shows up, use the P8.7 fix for both slots.
- Hit testing: the card is much smaller, so the 300 ms close delay matters more; check that moving from the hot zone to the card doesn't close it (the hot zone spans the full height, so the path is covered).

## Open design questions (with the default to build if nobody answers)
1. Vertical position: centred, or near the top under the title bar? Default: centred.
2. Keep Player as an item, given the sidebar only shows over the player? Default: keep it, as the active marker.
3. Slide on enter? Default: no, fade only (keeps the blur in step at no cost).
4. Should it also open from a keyboard shortcut? Default: no new key for now.
5. Blur over the home screen too (no video)? Default: no, tint on Mica, the same as the bar.
6. Labels next to icons on hover (an expanding card)? Default: no, tooltips only.
