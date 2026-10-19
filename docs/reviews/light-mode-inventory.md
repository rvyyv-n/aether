# Light Mode Inventory & Feasibility Review

## Overview

Aether is a native Windows 11 video player built with WinUI 3, .NET, and libmpv. Currently, the application forces dark mode via `RequestedTheme="Dark"` in `src/Aether.App/App.xaml`.

This review provides a comprehensive inventory and audit required to implement a live light theme that follows Windows light/dark system settings, alongside an Auto / Light / Dark user preference in Settings.

### Core Design Rules
1. **Light surfaces**: Home screen, Settings pages, title bar, caption buttons, window backdrop (Mica Alt), and dialogs over no video use near-white surfaces, dark ink text, and soft hairline borders.
2. **Text contrast**: All text colors target WCAG AA (minimum 4.5:1 for standard body text, 3:1 for large text) and WCAG AAA where possible.
3. **Surfaces over video stay dark**: Transport bar, clip pill/drawer, sidebar, flyouts (tracks, video, volume), toasts, phone pairing card over video, subtitles, and Open URL card over video stay dark in both themes.
4. **Out of scope**: Blur and backdrop render code (`MainPage.Backdrop.cs`, `BackdropSlot`).

---

## (a) DesignTokens.xaml Token Inventory

The tables below inventory every color, brush, and WinUI lightweight styling override in `src/Aether.App/Theme/DesignTokens.xaml`.

### 1. Core Aether Tokens (37 Colors / Brushes)

| Token Name | Role | Current Dark Value | Proposed Light Value | Contrast & Design Reasoning (WCAG AA) | Sits Over Video (Stays Dark) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `AetherTextPrimary` | Titles, labels, and all primary text | `#FFFFFFFF` | `#FF0E1014` | Solid dark ink. Contrast on white is 17.5:1 (exceeds WCAG AAA 7:1). | No (Stays dark over video via dark subtrees) |
| `AetherTextSecondary` | Descriptions, metadata, hints (62% opacity) | `#9EFFFFFF` | `#9E0E1014` | 62% ink on white gives 5.5:1 contrast, exceeding WCAG AA (4.5:1). | No |
| `AetherTextTertiary` | Decorative or large text only (45% opacity) | `#73FFFFFF` | `#730E1014` | 45% ink on white gives 3.3:1 contrast, meeting WCAG AA for large text (3:1). | No |
| `AetherTextDisabled` | Disabled text (36% opacity) | `#5CFFFFFF` | `#5C0E1014` | 36% ink matches Fluent disabled text specification; exempt from contrast minimums. | No |
| `AetherSurfaceGlass` | Panels, cards, status strip, hero tile | `#730E1014` (45% ink) | `#CCFFFFFF` (80% white) | Near-white translucent card provides clean elevation against light Mica Alt. | No |
| `AetherSurfaceOsd` | Controls pill, badges, overlays over video | `#9E0E1014` (62% ink) | `#9E0E1014` (62% ink) | Must remain 62% dark ink in both themes; ensures 5.35:1 contrast against pure white video. | **Yes (STAYS DARK)** |
| `AetherSurfaceLetterbox` | Full-screen and windowed letterbox bars | `#FF000000` | `#FF000000` | Pillarbox and letterbox bars around video frames must remain pure black in all themes. | **Yes (STAYS DARK)** |
| `AetherSurfaceScrim` | Dimming layer behind dialogs (SmokeFill) | `#4D000000` (30% black) | `#4D000000` (30% black) | Universal 30% black dimming scrim cleanly dims both light and dark content. | No |
| `AetherSurfaceDialog` | In-app Acrylic dialog background (62% ink) | `#9E0E1014` (62% ink) | `#E6FFFFFF` (90% white) | Light translucent acrylic tint for dialogs over no video (e.g. Open URL on home). | No (Over video stays dark) |
| `AetherSurfaceDialogFallback` | Solid fallback for dialogs and menus | `#F20E1014` (95% ink) | `#FFF9F9F9` | Clean near-white solid background matching Fluent light flyouts (`SolidBackgroundFillColorQuarternary`). | No (Over video stays dark) |
| `AetherSurfaceTopScrim` | Title bar top shade over video only | `#80000000` (50% black) | `#80000000` (50% black) | Only exists over video; keeps white title text legible against bright video frames. | **Yes (STAYS DARK)** |
| `AetherSurfaceOverlayStrong` | Solid diagnostics overlay (95% ink) | `#F20E1014` (95% ink) | `#F20E1014` (95% ink) | Diagnostics panel floats over video; stays dark so secondary text holds 7:1 contrast. | **Yes (STAYS DARK)** |
| `AetherSurfaceCode` | Background for phone pairing QR code tile | `#FFFFFFFF` | `#FFFFFFFF` | Camera sensors scan black QR modules best against pure white; stays white in every theme. | No (Stays white) |
| `AetherFillEmphasisRest` | Primary pill, play button, active item (14%) | `#24FFFFFF` (14% white) | `#14000000` (8% ink) | Subtle dark tint replaces white fill; creates visible boundary against light background. | No |
| `AetherFillEmphasisHover` | Hover state of emphasis controls (18%) | `#2EFFFFFF` (18% white) | `#1F000000` (12% ink) | Noticeable darkening on hover for clear pointer interaction feedback. | No |
| `AetherFillEmphasisPressed` | Pressed state of emphasis controls (10%) | `#1AFFFFFF` (10% white) | `#0F000000` (6% ink) | Visual depression feedback on button press. | No |
| `AetherFillSubtleHover` | Icon button and list row hover state (6%) | `#0FFFFFFF` (6% white) | `#0F000000` (6% ink) | Standard Fluent subtle hover fill for light surfaces. | No |
| `AetherFillSubtlePressed` | Icon button and list row pressed state (4%) | `#0AFFFFFF` (4% white) | `#0A000000` (4% ink) | Standard Fluent subtle pressed fill for light surfaces. | No |
| `AetherFillDisabled` | Disabled control background | `#0BFFFFFF` (4% white) | `#0B000000` (4% ink) | Matches Fluent `ControlFillDisabled` light specification. | No |
| `AetherFillDropTarget` | Window background while dragging a file | `#0AFFFFFF` (4% white) | `#0A000000` (4% ink) | Subtle darkening over light HomeView during drag-and-drop. | No |
| `AetherFillRange` | In/out range on seek bar and volume rail | `#33FFFFFF` (20% white) | `#26000000` (15% ink) | Used on seek bar and sidebar peek. Stays `#33FFFFFF` over video; 15% ink in light mode. | **Yes (on video controls)** |
| `AetherFillInput` | Text box, combo, and segmented item fill | `#0FFFFFFF` (6% white) | `#0F000000` (6% ink) | Recessed subtle fill for input surfaces on light cards. | No |
| `AetherFillSelection` | Text selection highlight | `#33FFFFFF` (20% white) | `#33000000` (20% ink) | Readable selection highlight that does not obscure dark text. | No |
| `AetherFillProgressTrack` | Progress bar and ring track (12%) | `#1FFFFFFF` (12% white) | `#1F000000` (12% ink) | Subtle track for progress indicators on light cards. | No |
| `AetherFillProgressValue` | Progress bar filled value (no accent) | `#FFFFFFFF` | `#FF0E1014` | Matches primary text; ensures strong contrast against progress track. | No |
| `AetherStrokeSurface` | 1 px hairline border on glass surfaces | `#0FFFFFFF` (6% white) | `#14000000` (8% ink) | Soft hairline border delineates light cards against Mica Alt. | No |
| `AetherStrokeControl` | Outline pill and input border (12%) | `#1FFFFFFF` (12% white) | `#1F000000` (12% ink) | Crisp 1 px control boundary on light cards. | No |
| `AetherStrokeDivider` | Dividers inside a surface (8%) | `#14FFFFFF` (8% white) | `#14000000` (8% ink) | Subtle separator between rows in Settings groups. | No |
| `AetherStrokeDropTarget` | Inner window border during file drag (20%) | `#33FFFFFF` (20% white) | `#33000000` (20% ink) | Clear visual drop target border for file drag-and-drop. | No |
| `AetherStrokeInputFocus` | 2 px bottom edge of focused text fields | `#FFFFFFFF` | `#FF0E1014` | Solid ink focus edge exceeds WCAG 3:1 non-text contrast requirement. | No |
| `AetherCaptionGlyph` | Caption button and title bar glyphs (85%) | `#D9FFFFFF` (85% white) | `#D9000000` (85% ink) | High contrast (12:1) on light Mica title bar. | No |
| `AetherCaptionGlyphInactive`| Caption glyphs when window is inactive | `#73FFFFFF` (45% white) | `#73000000` (45% ink) | Muted caption glyphs when window loses focus. | No |
| `AetherCaptionHover` | Title bar button hover background (8%) | `#14FFFFFF` (8% white) | `#14000000` (8% ink) | Subtle hover background for title bar buttons in light mode. | No |
| `AetherCaptionPressed` | Title bar button pressed background (4%) | `#0AFFFFFF` (4% white) | `#0A000000` (4% ink) | Subtle pressed background for title bar buttons in light mode. | No |
| `AetherStatusReady` | Status dot: ready / success | `#FF4ADE80` (emerald) | `#FF15803D` (dark green) | `#FF4ADE80` fails WCAG AA on white (1.6:1); `#FF15803D` achieves 4.6:1. | No |
| `AetherStatusWarning` | Status dot: failed / unsupported / warning | `#FFF2A35E` (amber) | `#FFC2410C` (dark orange) | `#FFF2A35E` fails WCAG AA on white (1.7:1); `#FFC2410C` achieves 4.7:1. | No |
| `AetherStatusNeutral` | Status dot: starting / idle (50% opacity) | `#80FFFFFF` (50% white) | `#80000000` (50% ink) | Neutral gray dot with 4.5:1 contrast against light cards. | No |

### 2. Compatibility Aliases (12 Colors / Brushes)

| Token Name | Role / Target | Current Dark Value | Proposed Light Value | Design Reasoning | Sits Over Video (Stays Dark) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `AetherPanel` | Alias for `AetherSurfaceGlass` | `#730E1014` | `#CCFFFFFF` | Matches `AetherSurfaceGlass`. | No |
| `AetherOsd` | Alias for `AetherSurfaceOsd` | `#9E0E1014` | `#9E0E1014` | Over-video surface; stays dark. | **Yes (STAYS DARK)** |
| `AetherStroke` | Alias for `AetherStrokeSurface` | `#0FFFFFFF` | `#14000000` | Matches `AetherStrokeSurface`. | No |
| `AetherStrokeStrong` | Alias for `AetherStrokeControl` | `#1FFFFFFF` | `#1F000000` | Matches `AetherStrokeControl`. | No |
| `AetherSelected` | Selected row / item background | `#FF0B0C0E` | `#FFFFFFFF` | Crisp elevated white background for selected cards in light theme. | No |
| `AetherActive` | Alias for `AetherFillEmphasisRest` | `#24FFFFFF` | `#14000000` | Matches `AetherFillEmphasisRest`. | No |
| `AetherHover` | Alias for `AetherFillSubtleHover` | `#0FFFFFFF` | `#0F000000` | Matches `AetherFillSubtleHover`. | No |
| `AetherPressed` | Alias for `AetherFillSubtlePressed` | `#08FFFFFF` | `#0A000000` | Matches `AetherFillSubtlePressed`. | No |
| `AetherSuccess` | Alias for `AetherStatusReady` | `#FF4ADE80` | `#FF15803D` | Dark green for WCAG AA 4.5:1. | No |
| `AetherWarning` | Alias for `AetherStatusWarning` | `#FFF2A35E` | `#FFC2410C` | Dark orange for WCAG AA 4.5:1. | No |
| `AetherNeutralDot` | Alias for `AetherStatusNeutral` | `#80FFFFFF` | `#80000000` | Matches `AetherStatusNeutral`. | No |
| `AetherLetterboxFullScreen` | Alias for `AetherSurfaceLetterbox` | `#FF000000` | `#FF000000` | Fullscreen letterbox bars remain black. | **Yes (STAYS DARK)** |

### 3. Built-in WinUI Lightweight Styling Brushes (ThemeDictionaries)

These brushes customize default WinUI controls (ToggleSwitch, Slider, TextBox, MenuFlyout) to maintain Aether's accent-free aesthetic. In light theme, they must be added to a `<ResourceDictionary x:Key="Light">` block.

| Brush Key | Current Dark Value (`Default`) | Proposed Light Value (`Light`) | Notes / Reasoning |
| :--- | :--- | :--- | :--- |
| `AccentFillColorDefaultBrush` | `#FFFFFFFF` | `#FF0E1014` | Aether uses ink instead of an accent color in light mode. |
| `AccentFillColorSecondaryBrush` | `#E6FFFFFF` | `#E60E1014` | Secondary ink tint (90%). |
| `AccentFillColorTertiaryBrush` | `#CCFFFFFF` | `#CC0E1014` | Tertiary ink tint (80%). |
| `AccentFillColorDisabledBrush` | `#5CFFFFFF` | `#5C0E1014` | Disabled ink tint (36%). |
| `AccentTextFillColorPrimaryBrush` | `#FFFFFFFF` | `#FF0E1014` | Primary text for accent elements. |
| `TextOnAccentFillColorPrimaryBrush` | `#FF0E1014` | `#FFFFFFFF` | White text on dark ink accent fill. |
| `ToggleSwitchFillOn` | `#FFFFFFFF` | `#FF0E1014` | Toggle ON track: dark ink in light mode. |
| `ToggleSwitchFillOnPointerOver` | `#E6FFFFFF` | `#E60E1014` | Toggle ON track hover. |
| `ToggleSwitchFillOnPressed` | `#CCFFFFFF` | `#CC0E1014` | Toggle ON track pressed. |
| `ToggleSwitchFillOnDisabled` | `#5CFFFFFF` | `#5C0E1014` | Toggle ON track disabled. |
| `ToggleSwitchKnobFillOn` | `#FF0E1014` | `#FFFFFFFF` | Toggle ON knob: white knob over dark track in light mode. |
| `ToggleSwitchKnobFillOnPointerOver` | `#FF0E1014` | `#FFFFFFFF` | Toggle ON knob hover. |
| `ToggleSwitchKnobFillOnPressed` | `#FF0E1014` | `#FFFFFFFF` | Toggle ON knob pressed. |
| `ToggleSwitchKnobFillOnDisabled` | `#FF0E1014` | `#FFFFFFFF` | Toggle ON knob disabled. |
| `ToggleSwitchStrokeOff` | `#9EFFFFFF` | `#9E0E1014` | Toggle OFF track border: 62% ink. |
| `ToggleSwitchKnobFillOff` | `#9EFFFFFF` | `#9E0E1014` | Toggle OFF knob: 62% ink. |
| `SliderTrackValueFill` | `#FFFFFFFF` | `#FF0E1014` | Slider active track value fill. |
| `SliderTrackValueFillPointerOver` | `#FFFFFFFF` | `#FF0E1014` | Slider active track value fill hover. |
| `SliderTrackValueFillPressed` | `#E6FFFFFF` | `#CC0E1014` | Slider active track value fill pressed. |
| `SliderTrackFill` | `#33FFFFFF` | `#26000000` | Slider inactive track fill (15% ink). |
| `SliderThumbBackground` | `#FFFFFFFF` | `#FF0E1014` | Slider thumb fill: solid ink. |
| `SliderThumbBackgroundPointerOver` | `#FFFFFFFF` | `#FF0E1014` | Slider thumb hover. |
| `SliderThumbBackgroundPressed` | `#E6FFFFFF` | `#CC0E1014` | Slider thumb pressed. |
| `TextControlBorderBrushFocused` | `#FFFFFFFF` | `#FF0E1014` | Focused 2 px bottom border on text fields. |
| `TextControlSelectionHighlightColor` | `#33FFFFFF` | `#33000000` | Selection highlight in text boxes (20% ink). |
| `ProgressBarForeground` | `#FFFFFFFF` | `#FF0E1014` | Active bar value fill. |
| `ProgressBarBackground` | `#1FFFFFFF` | `#1F000000` | Inactive bar track (12% ink). |
| `ProgressRingForegroundThemeBrush` | `#FFFFFFFF` | `#FF0E1014` | Progress ring stroke in light mode. |
| `MenuFlyoutItemBackgroundPointerOver` | `#0FFFFFFF` | `#0F000000` | Menu item hover background (6% ink). |
| `MenuFlyoutItemBackgroundPressed` | `#0AFFFFFF` | `#0A000000` | Menu item pressed background (4% ink). |
| `ToggleMenuFlyoutItemBackgroundPointerOver` | `#0FFFFFFF` | `#0F000000` | Toggle menu item hover background. |
| `ToggleMenuFlyoutItemBackgroundPressed` | `#0AFFFFFF` | `#0A000000` | Toggle menu item pressed background. |
| `MenuFlyoutItemKeyboardAcceleratorTextForeground` | `#9EFFFFFF` | `#9E0E1014` | Shortcut text in menu items (62% ink). |
| `MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver` | `#9EFFFFFF` | `#9E0E1014` | Shortcut text hover. |
| `MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed` | `#9EFFFFFF` | `#9E0E1014` | Shortcut text pressed. |
| `MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled` | `#5CFFFFFF` | `#5C0E1014` | Shortcut text disabled (36% ink). |

---

## (b) Hard-Coded Colors in XAML and C#

This table lists every hard-coded hex color, `Colors.*`, `SolidColorBrush`, and manual color construction found across `src/Aether.App`.

| File & Line | Code Snippet | What It Is | Sits Over Video? | Proposed Token or Fix |
| :--- | :--- | :--- | :--- | :--- |
| `src/Aether.App/Controls/PrecisionSeekBar.xaml:79` | `Stroke="#33000000"` | 20% black hairline border on the seek bar `Thumb` ellipse | **Yes** | Keep as-is. It provides subtle boundary separation for the white thumb against bright video. Sits over video and stays dark. |
| `src/Aether.App/Settings/Pages/SubtitlesPage.xaml:70` | `Background="#FF1B1B1F"` | Dark container border simulating a video background for subtitle previews | No (Settings page) | Keep dark background or bind to an explicit semantic token `AetherSubtitlePreviewBackground`. Subtitles are white/yellow and need a dark surface for legible preview in both themes. |
| `src/Aether.App/Settings/Pages/SubtitlesPage.xaml.cs:181` | `new SolidColorBrush(ParseColour(...))` | Dynamic foreground brush for subtitle preview text | No | No fix needed. Parses user-configured subtitle color string dynamically. |
| `src/Aether.App/Settings/Pages/SubtitlesPage.xaml.cs:182` | `new SolidColorBrush(ParseColour(...))` | Dynamic background brush for subtitle preview box | No | No fix needed. Parses user-configured subtitle background color string dynamically. |
| `src/Aether.App/Settings/Pages/SubtitlesPage.xaml.cs:190` | `Color.FromArgb(alpha, ...)` | Bitwise RGB color parser for subtitle settings | No | Keep as-is. Pure utility function for parsing hex subtitle colors. |
| `src/Aether.App/MainPage.xaml:234` | `Fill="#99000000"` | `DropScrim` rectangle dimming video during file drag | **Yes** | Keep dark or replace with `AetherSurfaceScrimBrush` (`#4D000000`) or a dedicated `#99000000` scrim token. Sits over video. |
| `src/Aether.App/MainPage.xaml:590` | `Color="#00000000"` | Transparent gradient stop in `TopScrim` linear gradient | **Yes** | Keep as-is. Standard transparent endpoint for gradient fade over video. |
| `src/Aether.App/MainWindow.xaml.cs:121` | `titleBar.ButtonBackgroundColor = Colors.Transparent;` | Transparent caption button background | Title bar | Keep as-is. Transparent caption button background allows Mica Alt to show through in both themes. |
| `src/Aether.App/MainWindow.xaml.cs:122` | `titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;` | Transparent inactive caption button background | Title bar | Keep as-is. Correct for both themes. |
| `src/Aether.App/MainWindow.xaml.cs:489` | `titleBar.ButtonHoverForegroundColor = Colors.White;` | Hard-coded white caption glyph on button hover | Title bar | **Fix required**. In light mode, a white glyph on hover is unreadable on light title bars. Update dynamically to `Colors.Black` (or `_captionForeground`) in light theme. |
| `src/Aether.App/MainWindow.xaml.cs:490` | `titleBar.ButtonPressedForegroundColor = Colors.White;` | Hard-coded white caption glyph on button press | Title bar | **Fix required**. In light mode, update dynamically to `Colors.Black` (or `_captionForeground`). |
| `src/Aether.App/Controls/ClipRow.xaml.cs:201` | `new SolidColorBrush(Microsoft.UI.Colors.Transparent)` | Unhovered row background brush allocation | **Yes** | Replace with cached `TransparentBrush` or `Colors.Transparent` to avoid per-event brush allocations. |

---

## (c) Audit of Dark Theme Assumptions

This section details all components, styles, and code paths that assume a dark theme.

| Area / Component | File & Line | Current Behavior & Assumption | Problem in Light Theme | Required Remediation |
| :--- | :--- | :--- | :--- | :--- |
| **Application Theme Enforcement** | `App.xaml:7` | `RequestedTheme="Dark"` on `<Application>` | Forces the entire visual tree into dark theme regardless of Windows system theme. | Remove `RequestedTheme="Dark"` from `App.xaml`. Manage theme dynamically via `Application.RequestedTheme` or root element `RequestedTheme`. |
| **Design Tokens Structure** | `DesignTokens.xaml:13-58` | Lightweight styling brushes only exist under `<ResourceDictionary x:Key="Default">`. | WinUI looks for `Light` dictionary in light mode. Without it, defaults or missing resources occur. | Add a `<ResourceDictionary x:Key="Light">` dictionary with light values. Duplicate `Default` into `Dark` as noted by existing comments. |
| **Design Tokens Scoping** | `DesignTokens.xaml:60-140` | All 37 core tokens and 12 legacy aliases are defined at the root of `ResourceDictionary`, outside of `ThemeDictionaries`. | Tokens are static and do not switch when the application or system theme switches. | Move all `Aether*Color` and `Aether*Brush` tokens inside `ThemeDictionaries` under `Default`, `Dark`, and `Light`. |
| **Resource Binding Mode** | `DesignTokens.xaml:240-970` | Shared control styles use `{StaticResource Aether...Brush}` for foregrounds, backgrounds, and strokes. | `{StaticResource}` evaluates once at parse time. Controls do not update dynamically when `ActualTheme` changes at runtime. | Change all token bindings inside styles and templates to `{ThemeResource Aether...Brush}`. |
| **Caption Button Cache** | `MainWindow.xaml.cs:110-113` | Reads caption colors once in constructor into `readonly Color` fields: `_captionForeground`, `_captionInactiveForeground`, etc. | When theme changes at runtime, title bar caption buttons retain stale dark-theme colors. | Re-read caption colors from resources on theme change events and apply to `AppWindowTitleBar`. |
| **Caption Button Hover/Press** | `MainWindow.xaml.cs:489-490` | `ButtonHoverForegroundColor = Colors.White` and `ButtonPressedForegroundColor = Colors.White` | White glyph on hover/press is invisible against light hover backgrounds in light theme. | Set caption button hover and pressed glyph foregrounds according to active theme (dark ink in light theme, white in dark theme). |
| **System Theme Listener** | `MainWindow.xaml.cs:480` | `OnSystemColoursChanged` listens to `UISettings.ColorValuesChanged` but only calls `ApplyTaskbarIcon()`. | Changes in Windows light/dark mode do not trigger title bar or UI updates. | Extend listener to trigger title bar caption refresh and app theme synchronization when theme setting is "Auto". |
| **Title Bar Button Brushes** | `MainPage.xaml.cs:216-218` | Sets `SettingsButton.Foreground` and `SettingsBackButton.Foreground` by looking up `Application.Current.Resources[...]` in `OnWindowActivationChanged()`. | If resources are theme-dependent, querying `Application.Current.Resources` directly must reflect the active theme. | Ensure title bar buttons update foreground on theme change as well as window activation change. |
| **Settings Group Cards** | `SettingsGroup.cs:32-33, 58-59` | Programmatically assigns `Background = Resource<Brush>("AetherSurfaceGlassBrush")` and `BorderBrush = Resource<Brush>("AetherStrokeSurfaceBrush")`. | Existing cards retain dark brushes when theme switches live; do not automatically re-evaluate. | Refresh brushes on `ActualThemeChanged`, or use XAML styling with `{ThemeResource}` instead of code-behind assignment. |
| **Settings Expanders** | `SettingsExpander.cs:104` | Sets `row.BorderBrush = (Brush)Application.Current.Resources["AetherStrokeDividerBrush"]` imperatively. | Stale border brush after live theme change. | Re-apply divider brush on theme change or define divider border via XAML style with `{ThemeResource}`. |
| **Keycap Strips** | `KeycapStrip.cs:39`, `SettingsPageBinder.cs:240` | Sets `Foreground = SettingsGroup.Resource<Brush>("AetherTextPrimaryBrush")` imperatively. | Stale primary text brush on shortcut keycaps after live theme change. | Re-apply text brush on theme change or bind in XAML template. |
| **Settings Nav Fills** | `SettingsView.xaml:18-50` | `SectionItemStyle` defines hover and selected states using `{StaticResource AetherFillEmphasisRestBrush}` and subtle fills. | Selected item highlight does not update on live theme change without `{ThemeResource}`. | Convert `{StaticResource}` to `{ThemeResource}` throughout `SettingsView.xaml`. |
| **Reset ContentDialog** | `AdvancedPage.xaml.cs:121` | Imperatively sets dialog `Background`, `BorderBrush`, and `RequestedTheme = RequestedTheme`. | `RequestedTheme` on page defaults to `ElementTheme.Default`; dialog resolves correctly once `App.xaml` dark lock is removed. | Ensure dialog uses current effective theme from `XamlRoot.Content.ActualTheme`. |
| **ToolTip Theme Inheritance** | `DesignTokens.xaml:1062` | `<Style TargetType="ToolTip"><Setter Property="IsHitTestVisible" Value="False" /></Style>`. Popups inherit theme from placement target. | If a video control is in a dark subtree, its tooltip is dark. Tooltips in Settings or Home take light theme. | Verify tooltips over video controls inherit `RequestedTheme="Dark"` from their parent containers. |

---

## (d) Checklist of UI Surfaces to Hand-Check

Use this checklist to visually verify both Light and Dark themes:

### 1. Window Frame & Title Bar
- [ ] **Mica Alt backdrop**: Off-white light tint in Light theme; dark smoky charcoal in Dark theme.
- [ ] **Title text**: "Aether · Alpha" or filename renders in dark ink (`#FF0E1014`) in Light; white (`#FFFFFFFF`) in Dark.
- [ ] **Title bar tools**: Settings gear and Back buttons render correct glyph color and hover/pressed states.
- [ ] **System caption buttons**: Minimize, Maximize, and Close glyphs legible in rest, hover, and pressed states in both themes.
- [ ] **Inactive window state**: Title text and caption glyphs dim to secondary/inactive opacity (`45%`).
- [ ] **Taskbar preview & icon**: Correct app icon (pale vs graphite) and taskbar thumbnail buttons.

### 2. Home Screen & Empty State
- [ ] **Hero tile**: Centered glass tile, 16:9 film frame glyph with AA contrast, progress ring during stream fetch.
- [ ] **Status typography**: "No video loaded" title and description legible against Mica Alt.
- [ ] **Action pill buttons**: "Open file" (Emphasis pill) and "Open URL" (Outline pill) show clear boundaries and hover fills.
- [ ] **Recent files list**: `RecentRow` cards show correct text primary/secondary hierarchy; disabled state on missing files.
- [ ] **Status strip**: Bottom dock strip shows correct ready dot color (`#FF15803D` in Light), version string, and startup progress bar.
- [ ] **Drag-over state**: `DropOutline` border and `DropScrim` visible and distinct during file drag.

### 3. Settings View & Pages
- [ ] **Navigation sidebar**: List items show proper rest, hover, pressed, and selected emphasis fills in Light mode.
- [ ] **Compact picker**: Responsive navigation ComboBox below 640 px window width.
- [ ] **Settings cards**: `SettingsGroup` cards have clean white/near-white surface, soft hairline border, and row dividers.
- [ ] **Toggle switches**: Clear distinction between On (ink track, white knob) and Off (hairline ring, ink knob).
- [ ] **Sliders**: Inactive track, active value fill, and thumb handle contrast well against card backgrounds.
- [ ] **Combo boxes & text fields**: Input surfaces clearly delineated with 1 px border; 2 px solid focus line on focus.
- [ ] **Segmented controls**: Clear container border, inactive items, and selected emphasis item plate.
- [ ] **Keycap strips**: Shortcut badges in Shortcuts page clearly legible.
- [ ] **Subtitles page preview**: Subtitle preview frame retains dark background (`#FF1B1B1F`) so white/yellow subtitles stay readable.
- [ ] **Advanced page dialog**: "Reset all settings" `ContentDialog` renders with correct light theme styling and button fills.
- [ ] **About page links**: GitHub and website row links show proper chevrons and hover feedback.

### 4. Dialogs Over No Video
- [ ] **Open URL dialog (Home screen)**: Card, text box, "Play" emphasis button, and "Cancel" button adapt to Light theme.
- [ ] **First-run tour card**: Tour card, step counter, title, body, and "Next" / "Skip" buttons render in Light theme.

### 5. Floating Over-Video Surfaces (MUST STAY DARK in both themes)
- [ ] **Transport bar card (`Osd`)**: 62% dark ink tint with hairline border; buttons, timecodes, and played bar stay dark.
- [ ] **Transport slim pill**: 48 px compact pill while playing stays dark.
- [ ] **Seek bar & thumb**: White played fill, dark thumb hairline border (`#33000000`), hover timecode bubble.
- [ ] **Transport flyouts**: Open menu, Speed menu, Quality menu, More menu, and Volume flyout remain dark.
- [ ] **Right-click context menu**: Attached context menu over video stays dark.
- [ ] **Tracks panel flyout**: Audio and subtitle selection panels stay dark.
- [ ] **Video panel flyout**: Aspect ratio, rotation, crop, and looks panels stay dark.
- [ ] **Sidebar (`Rail`)**: 4 px peek strip and 48x168 floating sidebar pill stay dark.
- [ ] **Clip drawer (`ClipDrawer`)**: Export chip and drawer rows stay dark.
- [ ] **Chapter skip pill**: Bottom-right chapter prompt stays dark.
- [ ] **Toasts**: In-app toast notifications stay dark.
- [ ] **Diagnostics overlay**: Ctrl+I technical overlay stays dark.
- [ ] **Picture-in-picture chrome**: Center play button and thin progress line stay dark.
- [ ] **Open URL over video**: When opened while video is active, dialog stays dark.
- [ ] **Phone pairing overlay**: Scrim, card, and QR module tile stay dark.
- [ ] **Letterbox bars**: Pillarbox and letterbox bars remain pure black (`#FF000000`).
- [ ] **TopScrim**: Linear gradient below title bar remains dark over video.

---

## Subtree Theme Isolation (Keeping Over-Video Surfaces Dark)

To allow the home screen and settings to switch to Light mode while guaranteeing that all surfaces over video remain dark, specific XAML elements must set `RequestedTheme="Dark"`.

The table below lists every XAML file in `src/Aether.App` and the exact root or subtree requiring `RequestedTheme="Dark"`.

| XAML File | Target Root / Subtree Element | Line Number | Action / Strategy |
| :--- | :--- | :--- | :--- |
| `src/Aether.App/App.xaml` | `<Application>` root | Line 7 | **Remove** `RequestedTheme="Dark"` so the app can support light theme. |
| `src/Aether.App/MainWindow.xaml` | `<Window>` root | Line 2 | Inherits system or user setting. No static dark override. |
| `src/Aether.App/MainPage.xaml` | `<controls:TracksPanel x:Name="TracksPanel">` | Line 45 | Add `RequestedTheme="Dark"` to `TracksPanel` or `TracksFlyout.FlyoutPresenterStyle`. |
| `src/Aether.App/MainPage.xaml` | `<controls:VideoPanel x:Name="VideoPanel">` | Line 67 | Add `RequestedTheme="Dark"` to `VideoPanel` or `VideoFlyout.FlyoutPresenterStyle`. |
| `src/Aether.App/MainPage.xaml` | `<MenuFlyout x:Name="ContextMenu">` | Line 83 | Add `RequestedTheme="Dark"` to `AetherMenuFlyoutPresenterStyle` or context menu root. |
| `src/Aether.App/MainPage.xaml` | `<Rectangle x:Name="DropScrim">` | Line 233 | Stays `#99000000` (dark dimming scrim over video). |
| `src/Aether.App/MainPage.xaml` | `<Rectangle x:Name="TopScrim">` | Line 580 | Gradient uses `AetherSurfaceTopScrimColor` (`#80000000`), always dark over video. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="PipChrome">` | Line 599 | Add `RequestedTheme="Dark"` on `PipChrome` grid (Picture-in-Picture controls). |
| `src/Aether.App/MainPage.xaml` | `<controls:DiagnosticsPanel x:Name="Diagnostics">` | Line 725 | Add `RequestedTheme="Dark"` on `DiagnosticsPanel` element. |
| `src/Aether.App/MainPage.xaml` | `<Border x:Name="Rail">` | Line 756 | Add `RequestedTheme="Dark"` on `Rail` sidebar container. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="ClipLayer">` | Line 798 | Add `RequestedTheme="Dark"` on `ClipLayer` grid (hosts `ClipDrawer`). |
| `src/Aether.App/MainPage.xaml` | `<Border x:Name="Osd">` | Line 819 | Add `RequestedTheme="Dark"` on `Osd` border (encompasses entire transport bar and its attached flyouts: `OpenMenu`, `SpeedMenu`, `QualityMenu`, `VolumeFlyout`, `MoreMenu`). |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="SkipChapterLayer">` | Line 1148 | Add `RequestedTheme="Dark"` on `SkipChapterLayer` grid. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="ToastLayer">` | Line 1184 | Add `RequestedTheme="Dark"` on `ToastLayer` grid (hosts `ToastCard`). |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="OpenUrlOverlay">` | Line 1198 | **Dynamic**: Set `RequestedTheme="Dark"` in code when a video is active; inherit app theme when opened from home screen. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="LanStreamOverlay">` | Line 1280 | Add `RequestedTheme="Dark"` on `LanStreamOverlay` (phone pairing card sits over video). |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="HomeView">` | Line 242 | **Inherits Light**: No dark override. Follows app theme. |
| `src/Aether.App/MainPage.xaml` | `<Border x:Name="SettingsHost">` | Line 640 | **Inherits Light**: No dark override. Follows app theme. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="TitleBar">` | Line 649 | **Inherits Light**: Adapts to active app theme. Over video, sits atop `TopScrim`. |
| `src/Aether.App/MainPage.xaml` | `<Grid x:Name="TourLayer">` | Line 1419 | **Inherits Light**: First-run tour over home screen adapts to active app theme. |
| `src/Aether.App/Controls/ClipDrawer.xaml` | `<UserControl>` root | Line 2 | Add `RequestedTheme="Dark"` on root `<UserControl>`. |
| `src/Aether.App/Controls/ClipRow.xaml` | `<UserControl>` root | Line 2 | Inherits `RequestedTheme="Dark"` from `ClipDrawer`. |
| `src/Aether.App/Controls/DiagnosticsPanel.xaml` | `<UserControl>` root | Line 2 | Add `RequestedTheme="Dark"` on root `<UserControl>`. |
| `src/Aether.App/Controls/PrecisionSeekBar.xaml` | `<UserControl>` root | Line 2 | Inherits `RequestedTheme="Dark"` from `Osd`. |
| `src/Aether.App/Controls/RecentRow.xaml` | `<UserControl>` root | Line 2 | **Inherits Light**: Sits in `HomeView` recents list; follows app theme. |
| `src/Aether.App/Controls/Toast.xaml` | `<UserControl>` root | Line 2 | Add `RequestedTheme="Dark"` on root `<UserControl>`. |
| `src/Aether.App/Controls/TracksPanel.xaml` | `<UserControl>` root | Line 2 | Add `RequestedTheme="Dark"` on root `<UserControl>`. |
| `src/Aether.App/Controls/VideoPanel.xaml` | `<UserControl>` root | Line 2 | Add `RequestedTheme="Dark"` on root `<UserControl>`. |
| `src/Aether.App/Settings/SettingsView.xaml` | `<UserControl>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/AboutPage.xaml` | `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/AdvancedPage.xaml`| `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/AudioPage.xaml` | `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/GeneralPage.xaml` | `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. Hosts new Theme selector. |
| `src/Aether.App/Settings/Pages/PlaybackPage.xaml`| `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/ShortcutsPage.xaml`| `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/StreamingPage.xaml`| `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
| `src/Aether.App/Settings/Pages/SubtitlesPage.xaml`| `<Page>` root | Line 2 | **Inherits Light**: Follows app theme (preview box interior stays dark). |
| `src/Aether.App/Settings/Pages/VideoPage.xaml` | `<Page>` root | Line 2 | **Inherits Light**: Follows app theme. |
