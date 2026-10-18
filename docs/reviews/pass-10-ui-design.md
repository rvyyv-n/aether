# Pass 10 UI design: clip export options, keyframe markers, Look filters

Covers 10.1, 10.5, 10.6, 10.7 and 10.8. The engine (10.2 to 10.4) is done. Owner choices: export options are one settings-style block at the top of the open drawer; size presets are No limit, 10, 25, 50 MB and Custom; LUTs ship built in, and the user can import their own; the filters are a "Look" section under the colour sliders. Reuse the existing tokens and styles. Add no new colours. All strings go in `Resources.resw` under the keys shown.

## 1. Clip drawer export options (10.1, 10.8)

**Placement.** A new `OptionsBlock` is the first child of `DrawerArea`, above `RowsScroller`. It has Padding `9,8,6,8` (the 9 lines up with `RangeText`), a `Grid` with RowSpacing 4, and a 1 px `AetherStrokeDividerBrush` divider under it (Margin `-3,0,-3,0`, as the existing one). Each row is 32 tall (`AetherSizeControl`). The label sits on the left (`AetherCaptionStrongTextStyle`) and the control on the right, 160 wide. The drawer stays 360 wide (`AetherSizeClipDrawerWidth`). The block shows even when the shelf is empty, as long as the drawer is open.

| Row | Label key / text | Control |
|:---|:---|:---|
| Mode | `Clips_Mode` "Cut" | `AetherSegmentedStyle` ListBox: `Clips_ModeFast` "Fast", `Clips_ModePrecise` "Precise" |
| Size | `Clips_SizeLimit` "Size limit" | `AetherSettingsComboStyle`: `Clips_SizeNone` "No limit", "10 MB", "25 MB", "50 MB", `Clips_SizeCustom` "Custom…" |
| Custom (only when Custom is picked) | (none, the field sits right-aligned under the combo) | `AetherSettingsTextBoxStyle`, 72 wide, digits only, 1–4000, with a trailing caption "MB" (`Clips_Megabytes`) |
| Look | `Clips_BakeLook` "Include Look" | `AetherSettingsToggleStyle` |
| Hint | one caption line across both columns, `AetherCaptionTextStyle` (secondary), wraps | see below |

Format the MB values with `CultureInfo.CurrentCulture`. 1 MB is 1,000,000 bytes, since Discord counts that way.

**States and the hint line** (the first match wins):
- Fast: the Size combo and the Look toggle are disabled (opacity from the styles; their values are kept). The hint reads `Clips_HintFast` "Fast cuts are instant and lossless, but start on the keyframe before In. Choose Precise to cut on the exact frame, fit a size limit or include the Look." When the keyframe offset is known (section 2), it appends `Clips_HintFastOffset` " This one starts {0} early." with {0} like "1.4 s".
- Precise, limit too small: before export, call `BitrateCalculator.Plan(bytes, out − in)`. If it throws `ClipTargetTooSmallException`, the hint turns `AetherStatusWarningBrush` and reads `Clips_HintTooSmall` "Too small for a clip this long. The smallest is {0} MB." ({0} = `MinimumSizeBytes` rounded up to 0.1 MB). Export clip is disabled, with that text as its tooltip. Re-check when the range, the preset or the custom value changes.
- Precise, Include Look on, and the Look is all off: `Clips_HintLookEmpty` "The Look is off, so the clip will look like the original."
- Precise otherwise: `Clips_HintPrecise` "Cuts on the exact frame. Takes longer, and uses the graphics card when it can."

**Remembering.** The mode, the preset (or custom MB) and Include Look are saved in settings (`ClipExportMode`, `ClipSizeLimitMb` where 0 means none, `ClipIncludeLook`). The defaults are Fast, No limit, and on. The chip does not change. Its Export clip tooltip says what will happen: `Clips_ExportTipPrecise` "Precise · {0}" where {0} is "no size limit" (`Clips_ExportTipNoLimit`) or "up to 25 MB" (`Clips_ExportTipLimit` "up to {0} MB"). Fast keeps no tooltip.

## 2. Saving rows: progress, cancel, encoder (10.1)

- **Pending row:** the `ProgressBar` starts indeterminate. On the first `ClipProgress` with Fraction > 0 it becomes determinate (0–1). Details reads `Clips_SavingProgress` "Saving · {0}% · {1} left", or "Saving · {0}%" while Remaining is null. Leave out speed: it means nothing to most people. Throttle UI updates to about 4 per second.
- **Cancel:** on a pending row the Actions panel is always visible (Opacity 1) and shows only one button: E711, tooltip and name `Clips_CancelExport` "Cancel export". Cancelling removes the row quietly with no toast (the engine deletes the partial file). Fast exports get the same button; they are usually done before anyone clicks it. When the drawer closes, exports keep running.
- **Failed row:** stays as it is today ("Couldn't save this clip"). If the error is `ClipTargetTooSmallException` (it can be thrown late), the row reads the too-small text instead, with no failure toast.
- **Finished row:** for Precise clips, details reads "12.4 MB · 0:18 · 14:02 · NVIDIA". The last part is the short name of `ClipResult.EncoderUsed`: `Clips_EncNvenc` "NVIDIA", `Clips_EncQsv` "Intel", `Clips_EncAmf` "AMD", `Clips_EncX264` "CPU". The tooltip on DetailsText gives the long form: `Clips_EncTip` "Encoded by {0}" with "NVIDIA NVENC", "Intel Quick Sync", "AMD AMF", "the processor (x264)". Store the encoder on the shelf entry as an optional field. Fast clips and older entries show nothing extra.
- **Accessibility:** set the ProgressBar's name to `Clips_ProgressName` "Export progress". Announce the finish once through a polite live region on the row ("Clip saved"), not every percent.

## 3. Keyframe markers in Fast mode (10.5)

**What is drawn.** The engine's Fast cut starts on the keyframe at or before In, and ends at Out. So only In gets a marker. If testing shows the end moves too, use the same marker at the keyframe at or after Out. In `PrecisionSeekBar`, add a `LeadIn` Border (Canvas.Top 6, Height `AetherSizeClipRange`, `AetherFillRangeBrush` at Opacity 0.5, radius 4 on the left corners only) from the keyframe to In. Also add a `KeyTick` Rectangle (1×24, Canvas.Top 4, `AetherTextTertiaryBrush`) at the keyframe. The solid band and the 2 px In tick stay as they are. Draw them only when the mode is Fast, there is an In point, the file is local, and the keyframe is known and more than 1 frame (about 0.04 s) before In. New API: `SetCutStart(double? seconds)`, where null hides both.

**How it is found.** Add a Core service, `KeyframeProbe.FindAtOrBeforeAsync(path, seconds, ct)`, next to the clipping code. It runs `ffprobe` beside `ffmpeg` if it is there: `-v error -select_streams v:0 -skip_frame nokey -show_entries frame=pts_time -of csv=p=0 -read_intervals {In−10}%{In+0.05}`, and takes the last pts ≤ In. If there is no ffprobe, it uses `ffmpeg -hide_banner -skip_frame nokey -ss {In−10} -i path -t 10.05 -map 0:v:0 -vf showinfo -f null -` and reads `pts_time` from stderr. libmpv does not expose a keyframe index, so it is not used. The window is 10 s before In; if nothing is found, widen once to 30 s, then give up. The probe runs 250 ms after the In point stops changing. A new In cancels the last probe, and results are cached per (path, In).

**Unknown or not applicable** (stream or link, probe failed, still probing, no ffmpeg): draw nothing extra and use the plain Fast hint without the offset. Don't add a spinner. The seek bar's automation help text gets `Seek_CutStartHelp` "Fast cut starts {0} before In" when the offset is known.

## 4. Look section in the Video panel (10.6, 10.7)

**Layout.** After `ResetColoursButton`, add a 1 px divider (Margin `-16,4,-16,4`), then a heading `Video_Look` "Look" in `AetherCaptionStrongTextStyle`, then these blocks, each a StackPanel with Spacing 4 in the panel's existing 12 spacing:
1. `Video_LookGrade` "Grade": a full-width `AetherSettingsComboStyle` list: `Video_GradeNone` "None", "Warm", "Cool", "Vivid", "Teal and orange", "Faded film", "Black and white" (keys `Video_Grade*`). Then a divider item with the user's looks by file name (no extension, A to Z), then `Video_AddLook` "Add a look…" and `Video_OpenLooksFolder` "Open looks folder".
2. `Video_Sharpen` "Sharpen": a slider from 0 to 100, step 5, with the value readout on the right like Zoom (0 shows "Off" via `Video_Off`).
3. `Video_Grain` "Grain": the same slider.
4. `Video_Denoise` "Denoise": `AetherSegmentedStyle` with "Off", "Light", "Medium", "Strong" (`Video_Denoise*`). Under it a caption: `Video_DenoiseHint` "Uses more power. Best with a dedicated graphics card."
5. `Video_ResetLook` "Reset look" (`AetherSettingsTextButtonStyle`, left-aligned). It sets everything above back to None, 0, 0, Off.

Everything applies live on change. Moving a slider waits 150 ms before rebuilding the chain, so a drag doesn't flood mpv.

**LUT files.** The built-in grades are 33-point `.cube` files in `Assets/Looks/` (installed next to the app). They can be generated by a small script, and that script is checked in. User looks live in `%LOCALAPPDATA%\Aether\Looks\`. "Add a look…" opens a file picker for `.cube` and checks the header (`LUT_3D_SIZE` from 2 to 65). If the check passes, it copies the file into the folder (adding " (2)" on a name clash) and selects it. If not, a toast says `Toast_LookInvalid` "That file isn't a 3D LUT (.cube)". Read the folder each time the panel opens. If the selected look has gone missing, fall back to None.

**Model.** Add `VideoLook(string Grade = "none", int Sharpen = 0, int Grain = 0, DenoiseLevel Denoise = Off)` beside `VideoAdjustments` (or as fields on it). It has one pure `FilterChain(lutPath)` that returns the lavfi chain, in this order: `hqdn3d` → `lut3d` → `cas` → `noise`. Denoise Light `hqdn3d=2:1.5:3:2.25`, Medium `4:3:6:4.5`, Strong `8:6:12:9`. Grade `lut3d=file='<escaped path>'`. Sharpen `cas=strength={v/100}`. Grain `noise=alls={v*0.3}:allf=t`. Off parts are left out, and all-off gives an empty chain. Playback sets mpv `vf` to `lavfi=[chain]` or clears it. The Look is kept across files and saved as last used. It is off by default.

**Performance.** These are CPU filters. While the chain is not empty, switch hwdec to its copy-back form (for example `d3d11va-copy`), and put it back when the chain empties, so playback with no Look is untouched. 10.7 requires checking dropped frames with Strong denoise on 1080p and 4K before shipping.

## 5. Baking the Look into Precise exports (10.8)

With Precise and Include Look on, the request carries the same `FilterChain` plus an `eq` filter for the four colour sliders. The `eq` mapping is approximate: brightness b/100·0.5, contrast 1+c/100, gamma 1+g/100 (clamped from 0.1), saturation 1+s/100. Tune it against a frame grab, and pin it with a test. Zoom, rotation and aspect are not baked (out of scope). Fast never passes a chain; the hint in section 1 explains why.

## Keyboard and accessibility

Tab order in the open drawer: Cut → Size limit → custom MB → Include Look → rows → Show all → chip. Arrow keys move inside the segmented controls. Esc closes the drawer as today. Every control's name is its visible label, and the hint is attached as help text to Cut. Disabled controls drop out of the tab order. The hint line already says why, so they need no tooltip of their own. In the Video panel, the Look controls follow the colour sliders in tab order, and each slider's name is its label.

## Details for the builder

- New fields go on `ClipRequest` (`VideoFilterChain string? = null`), and the Precise argument builder adds `-vf` when it is set. The x264 and hardware paths both take it.
- Files: `ClipDrawer.xaml(.cs)`, `ClipRow.xaml(.cs)` (a progress setter, cancel, encoder text), `PrecisionSeekBar.xaml(.cs)`, `VideoPanel.xaml(.cs)`, `ClipShelf` (optional encoder), the settings keys, `Resources.resw`.
- Escape `lut3d` paths for lavfi: backslashes become `/`, the drive colon becomes `\:`, and the path is wrapped in single quotes.
- 10.9 tests: `FilterChain` for each combination, the `eq` mapping, too-small detection, the Fast-mode hint choice, and the keyframe output parsing for both ffprobe and ffmpeg.
