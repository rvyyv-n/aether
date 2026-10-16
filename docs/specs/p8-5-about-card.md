# P8.5 About page: a closing card instead of the "author" row

Build spec for Sonnet. Page: `src/Aether.App/Settings/Pages/AboutPage.xaml(.cs)`. Model: the About block at the end of Rise's settings (`D:\Code\Claude\diet-tracker\src\Settings.jsx`, `AboutBlock`).

## What changes
- Remove the GitHub row (`GitHubButton`), its `Author` URI and the strings `Settings_About_GitHub` and `Settings_About_GitHubDescription` ("The author's projects at github.com/rvyyv-n"). Nothing else uses them.
- Remove the top `AppRow` (name and version): the card now carries both, and one page should not show the version twice (see question 1).
- Add a closing card as the last child of the page's `SettingsGroup`. Order becomes: Engine, Show the tour again, Licences, Report an issue, About card.

## The card
- Shell: `Border`, `AetherSurfaceGlassBrush`, 1 px `AetherStrokeSurfaceBrush`, `AetherOverlayCornerRadius` (8), padding 16, top margin 20 (24 from the row above with the group's 4 spacing), so it reads as an ending rather than another row. `SettingsGroup` only styles `Control` children, so set these on the Border directly.
- Inside, a `StackPanel` with spacing 4:
  1. Header: info glyph `&#xE946;` (16 px, `AetherTextSecondaryBrush`) and "About" (`AetherCaptionTextStyle`, secondary), spacing 8, bottom margin 8. Reuse `Settings_Page_About` ("About").
  2. Name line, one `TextBlock` with runs: "Aether" (`App_Name`, Body, SemiBold, primary) then " · v{version} · Alpha" (Body, secondary; version from the existing `_appVersion`, "Alpha" from `App_Badge`).
  3. "Everything stays on this device." (`Settings_About_Private`, Body, secondary, wrap).
  4. Network line (`Settings_About_Network`, Body, secondary, wrap): "No account and no tracking. Aether only goes online to play a link you open or to update yt-dlp."
  5. Link line: inline `Hyperlink` "Source on GitHub" (`Settings_About_Source`) to `https://github.com/rvyyv-n/aether` (the existing `Repository` URI; verified against `git remote`), then plain " · @rvyyv-n" in secondary. Only the words "Source on GitHub" are the link, as in Rise.
- Why the network line is accurate (checked in code): there is no update check, telemetry or `HttpClient` anywhere in the app. Network use is mpv and yt-dlp fetching a link the user opens (Open URL, paste, recents), and `yt-dlp -U` from the Update button on Settings › Streaming. The "Update yt-dlp automatically" setting is stored but nothing acts on it today; the wording "or to update yt-dlp" stays true if it is ever wired up. The Lan host project is referenced but never started. Links in Settings (GitHub, issues, yt-dlp releases, "Open in browser") open the user's browser, not Aether.

## Colour of the link
No accent. The design tokens say the app has no accent colour (accent brushes are overridden to white). The link is `AetherTextPrimaryBrush` with `UnderlineStyle="Single"`, sitting in secondary text, so it stands out by brightness and underline only. Override `HyperlinkForegroundPointerOver` (secondary) and `HyperlinkForegroundPressed` (disabled) in the page resources so hover does not fall back to a system blue; check it in the running app.

## Accessibility
- The Hyperlink is a tab stop with the system focus rectangle; Enter opens it. `AutomationProperties.HelpText` = "Opens the source code on GitHub in your browser" (`Settings_About_SourceHelp`).
- The glyph is `AccessibilityView="Raw"`. Text is real text, so Narrator reads the card in order; the card Border gets `AutomationProperties.Name` = "About Aether" (`Settings_About_CardName`).
- Contrast: secondary text (62%) on glass is the same pairing as every row description.

## Tests
- `ResourcesStringsTests` already covers new keys (unique, not empty). Add one case: no `Settings_About_*` value contains "author". Delete the removed keys from the resw (the strings test does not catch orphans, so grep for them).
- Manual: page at 640 wide (compact settings), keyboard Tab reaches the link, Narrator reads the card, link opens the repo.

## Open questions (default in brackets)
1. Keep the top name and version row as well? [No, remove it; the card shows both.]
2. Show "@rvyyv-n" as a second link to the profile? [No, plain text, like Rise.]
3. Header reads "About" on a page already titled About; use "Aether" instead? [Keep "About", matching Rise.]
4. Side finding: the Licences row opens the repository front page, not a licences list. Point it at `THIRD_PARTY.md` on GitHub? [Yes, in this change, as `.../blob/master/THIRD_PARTY.md`.]
