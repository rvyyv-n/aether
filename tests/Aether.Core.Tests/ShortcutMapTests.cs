using System.Linq;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class ShortcutMapTests
{
    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public string? Read() => Content;
        public void Write(string content) => Content = content;
    }

    private static KeyCombination Key(string text)
    {
        Assert.True(KeyCombination.TryParse(text, out KeyCombination combination), text);
        return combination;
    }

    private static string[] Defaults(ShortcutAction action) =>
        [.. ShortcutMap.Defaults.Get(action).Select(k => k.ToString())];

    [Theory]
    [InlineData(ShortcutAction.OpenFile, "Ctrl+O")]
    [InlineData(ShortcutAction.OpenUrl, "Ctrl+Shift+O")]
    [InlineData(ShortcutAction.SkipBack, "Left")]
    [InlineData(ShortcutAction.SkipForward, "Right")]
    [InlineData(ShortcutAction.SkipBackFine, "Shift+Left")]
    [InlineData(ShortcutAction.SkipForwardFine, "Shift+Right")]
    [InlineData(ShortcutAction.SkipBackLong, "Ctrl+Left")]
    [InlineData(ShortcutAction.SkipForwardLong, "Ctrl+Right")]
    [InlineData(ShortcutAction.SetIn, "I")]
    [InlineData(ShortcutAction.SetOut, "O")]
    [InlineData(ShortcutAction.PlayPause, "Space")]
    [InlineData(ShortcutAction.Mute, "M")]
    [InlineData(ShortcutAction.Loop, "L")]
    [InlineData(ShortcutAction.AlwaysOnTop, "T")]
    [InlineData(ShortcutAction.PictureInPicture, "P")]
    [InlineData(ShortcutAction.Diagnostics, "Ctrl+I")]
    [InlineData(ShortcutAction.Settings, "Ctrl+Comma")]
    [InlineData(ShortcutAction.CopyFrame, "Ctrl+C")]
    [InlineData(ShortcutAction.SaveFrame, "Ctrl+Shift+S")]
    [InlineData(ShortcutAction.ExportClip, "Ctrl+E")]
    [InlineData(ShortcutAction.NextChapter, "PageDown")]
    [InlineData(ShortcutAction.PreviousChapter, "PageUp")]
    [InlineData(ShortcutAction.Sidebar, "B")]
    [InlineData(ShortcutAction.ToggleLanStream, "S")]
    public void Defaults_Are_Todays_Keys(ShortcutAction action, string expected)
    {
        Assert.Equal([expected], Defaults(action));
    }

    [Fact]
    public void Sidebar_Shortcut_Is_Configurable_Conflicts_And_Resets_On_Its_Own()
    {
        Assert.Equal(ShortcutAction.Sidebar, ShortcutMap.Defaults.Find(Key("B")));
        Assert.Equal("shortcuts.sidebar", ShortcutActions.SettingKey(ShortcutAction.Sidebar));

        ShortcutMap map = ShortcutMap.Defaults.With(ShortcutAction.Sidebar, [Key("M")]);
        Assert.Equal([ShortcutAction.Mute], map.ConflictsOf(ShortcutAction.Sidebar));
        Assert.True(map.HasConflict(ShortcutAction.Mute));
        Assert.Equal("M", map.Encode(ShortcutAction.Sidebar));

        ShortcutMap reset = map.Reset(ShortcutAction.Sidebar);
        Assert.True(reset.IsDefault(ShortcutAction.Sidebar));
        Assert.False(reset.HasConflict(ShortcutAction.Mute));
        Assert.Equal("", reset.Encode(ShortcutAction.Sidebar));
    }

    [Fact]
    public void A_Saved_Sidebar_Shortcut_Survives_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        ShortcutMap.Defaults.With(ShortcutAction.Sidebar, [Key("Ctrl+B")]).WriteTo(first);
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();

        Assert.Equal([Key("Ctrl+B")], ShortcutMap.From(second.Current).Get(ShortcutAction.Sidebar));
    }

    [Fact]
    public void Full_Screen_Defaults_To_F_And_F11()
    {
        Assert.Equal(["F", "F11"], Defaults(ShortcutAction.FullScreen));
    }

    [Fact]
    public void Every_Action_Has_A_Default_Setting_Key_And_No_Default_Conflicts()
    {
        foreach (ShortcutAction action in ShortcutActions.All)
        {
            Assert.NotEmpty(ShortcutMap.Defaults.Get(action));
            Assert.False(ShortcutMap.Defaults.HasConflict(action), action.ToString());
            Assert.Contains(ShortcutActions.SettingKey(action), SettingDefinitions.Keys);
            Assert.Equal(action, ShortcutActions.FromSettingKey(ShortcutActions.SettingKey(action)));
        }
    }

    [Theory]
    [InlineData("Ctrl+Shift+O")]
    [InlineData("Ctrl+Alt+Shift+F5")]
    [InlineData("Space")]
    [InlineData("Left")]
    [InlineData("Num5")]
    [InlineData("Slash")]
    [InlineData("Shift+PageUp")]
    public void Format_And_Parse_Round_Trip(string text)
    {
        KeyCombination combination = Key(text);

        Assert.Equal(text, combination.ToString());
        Assert.Equal(combination, Key(combination.ToString()));
    }

    [Fact]
    public void Parse_Ignores_Case_And_Modifier_Order_Is_Normalised()
    {
        Assert.Equal("Ctrl+Alt+Shift+K", Key("shift+ALT+ctrl+k").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+O")]
    [InlineData("Win+O")]
    [InlineData("Ctrl")]
    [InlineData("Banana")]
    [InlineData("Escape")]
    [InlineData("Comma")]
    [InlineData("Alt+Period")]
    public void Garbage_Or_Reserved_Text_Does_Not_Parse(string? text)
    {
        Assert.False(KeyCombination.TryParse(text, out _));
    }

    [Fact]
    public void Escape_With_A_Modifier_Is_Allowed_But_Plain_Escape_Is_Not()
    {
        Assert.True(new KeyCombination(KeyCombination.Escape, KeyModifiers.Shift).IsValid);
        Assert.False(new KeyCombination(KeyCombination.Escape).IsValid);
    }

    [Fact]
    public void Keycaps_Use_Symbols_For_Arrows_And_Short_Names()
    {
        Assert.Equal(["Ctrl", "Shift", "O"], Key("Ctrl+Shift+O").Keycaps);
        Assert.Equal(["←"], Key("Left").Keycaps);
        Assert.Equal(["Esc"], new KeyCombination(KeyCombination.Escape, KeyModifiers.None).Keycaps);
        Assert.Equal("Ctrl+Shift+O", Key("Ctrl+Shift+O").DisplayText);
        Assert.Equal("→", Key("Right").DisplayText);
    }

    [Fact]
    public void Find_Returns_The_Action_A_Combination_Triggers()
    {
        ShortcutMap map = ShortcutMap.Defaults;

        Assert.Equal(ShortcutAction.FullScreen, map.Find(Key("F11")));
        Assert.Equal(ShortcutAction.SetOut, map.Find(Key("O")));
        Assert.Equal(ShortcutAction.OpenFile, map.Find(Key("Ctrl+O")));
        Assert.Null(map.Find(Key("Ctrl+K")));
    }

    [Fact]
    public void Conflicts_Are_Found_On_Both_Actions()
    {
        ShortcutMap map = ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("L")]);

        Assert.True(map.HasConflict(ShortcutAction.Mute));
        Assert.True(map.HasConflict(ShortcutAction.Loop));
        Assert.Equal([ShortcutAction.Loop], map.ConflictsOf(ShortcutAction.Mute));
        Assert.False(map.HasConflict(ShortcutAction.PlayPause));
        Assert.Equal(ShortcutAction.Mute, map.Find(Key("L")));
    }

    [Fact]
    public void A_Shared_Secondary_Key_Is_A_Conflict_Too()
    {
        ShortcutMap map = ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("M"), Key("F11")]);

        Assert.Equal([ShortcutAction.FullScreen], map.ConflictsOf(ShortcutAction.Mute));
    }

    [Fact]
    public void Reset_Restores_One_Action_Only()
    {
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.Mute, [Key("N")])
            .With(ShortcutAction.Loop, [Key("K")]);

        ShortcutMap reset = map.Reset(ShortcutAction.Mute);

        Assert.True(reset.IsDefault(ShortcutAction.Mute));
        Assert.Equal(["K"], reset.Get(ShortcutAction.Loop).Select(k => k.ToString()));
        Assert.False(reset.IsDefault(ShortcutAction.Loop));
        Assert.True(map.ResetAll().IsDefault(ShortcutAction.Loop));
    }

    [Fact]
    public void Setting_Invalid_Or_Repeated_Keys_Drops_Them_And_Empty_Unbinds()
    {
        ShortcutMap map = ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("N"), Key("N"), new KeyCombination(0x10)]);

        Assert.Equal(["N"], map.Get(ShortcutAction.Mute).Select(k => k.ToString()));
        Assert.Empty(map.With(ShortcutAction.Mute, []).Get(ShortcutAction.Mute));
    }

    [Fact]
    public void Ctrl_L_Is_A_Different_Combination_From_Loops_L()
    {
        Assert.Equal(KeyCombination.Letter('L', KeyModifiers.Ctrl), ShortcutActions.OpenUrlAlternative);
        Assert.Equal("Ctrl+L", ShortcutActions.Alternative(ShortcutAction.OpenUrl)?.ToString());
        Assert.Null(ShortcutActions.Alternative(ShortcutAction.Mute));

        ShortcutMap map = ShortcutMap.Defaults.With(ShortcutAction.OpenUrl, [ShortcutActions.OpenUrlAlternative]);

        Assert.False(map.HasConflict(ShortcutAction.OpenUrl));
        Assert.False(map.HasConflict(ShortcutAction.Loop));
        Assert.Equal(ShortcutAction.OpenUrl, map.Find(Key("Ctrl+L")));
        Assert.Equal(ShortcutAction.Loop, map.Find(Key("L")));
    }

    [Fact]
    public void Default_Keys_Are_Stored_As_Empty_So_A_Changed_Default_Reaches_Existing_Users()
    {
        Assert.All(ShortcutActions.All, a => Assert.Equal("", ShortcutMap.Defaults.Encode(a)));
        Assert.Equal("N", ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("N")]).Encode(ShortcutAction.Mute));
        Assert.Equal("None", ShortcutMap.Defaults.With(ShortcutAction.Mute, []).Encode(ShortcutAction.Mute));
        Assert.Equal("F, F11", ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("F"), Key("F11")]).Encode(ShortcutAction.Mute));
    }

    [Fact]
    public void Changed_Shortcuts_Survive_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.OpenUrl, [Key("Ctrl+L")])
            .With(ShortcutAction.FullScreen, [Key("F11")])
            .With(ShortcutAction.Mute, []);
        map.WriteTo(first);
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();
        ShortcutMap loaded = ShortcutMap.From(second.Current);

        Assert.Equal(["Ctrl+L"], loaded.Get(ShortcutAction.OpenUrl).Select(k => k.ToString()));
        Assert.Equal(["F11"], loaded.Get(ShortcutAction.FullScreen).Select(k => k.ToString()));
        Assert.Empty(loaded.Get(ShortcutAction.Mute));
        Assert.True(loaded.IsDefault(ShortcutAction.Loop));
    }

    [Fact]
    public void Garbage_Saved_Values_Fall_Back_To_The_Default()
    {
        var storage = new FakeStorage
        {
            Content = """
                {
                  "version": 1,
                  "shortcuts.openUrl": "Ctrl+Banana",
                  "shortcuts.mute": "Escape",
                  "shortcuts.loop": 42,
                  "shortcuts.playPause": "Space, ???",
                  "shortcuts.setIn": "Shift+J"
                }
                """,
        };
        var store = new SettingsStore(storage);
        store.Load();

        ShortcutMap map = ShortcutMap.From(store.Current);

        Assert.True(map.IsDefault(ShortcutAction.OpenUrl));
        Assert.True(map.IsDefault(ShortcutAction.Mute));
        Assert.True(map.IsDefault(ShortcutAction.Loop));
        Assert.True(map.IsDefault(ShortcutAction.PlayPause));
        Assert.Equal(["Shift+J"], map.Get(ShortcutAction.SetIn).Select(k => k.ToString()));
    }

    [Fact]
    public void The_Store_Refuses_An_Unreadable_Shortcut_And_Raises_Changed_For_A_Good_One()
    {
        var store = new SettingsStore(new FakeStorage());
        var changed = new System.Collections.Generic.List<string>();
        store.Changed += (_, e) => changed.Add(e.Key);

        Assert.False(store.Set(SettingKeys.ShortcutMute, "Nonsense"));
        Assert.True(store.Set(SettingKeys.ShortcutMute, "N"));
        Assert.True(store.Reset(SettingKeys.ShortcutMute));

        Assert.Equal([SettingKeys.ShortcutMute, SettingKeys.ShortcutMute], changed);
        Assert.True(store.IsDefault(SettingKeys.ShortcutMute));
    }

    [Fact]
    public void Open_Url_Has_Ctrl_L_Alternative_And_Other_Actions_Have_None()
    {
        Assert.Equal(KeyCombination.Letter('L', KeyModifiers.Ctrl), ShortcutActions.Alternative(ShortcutAction.OpenUrl));
        foreach (ShortcutAction action in ShortcutActions.All.Where(a => a != ShortcutAction.OpenUrl))
        {
            Assert.Null(ShortcutActions.Alternative(action));
        }
    }

    [Fact]
    public void Fixing_A_Conflict_Clears_The_Flag_On_Both_Actions()
    {
        ShortcutMap conflicting = ShortcutMap.Defaults.With(ShortcutAction.Mute, [Key("L")]);
        Assert.True(conflicting.HasConflict(ShortcutAction.Mute));
        Assert.True(conflicting.HasConflict(ShortcutAction.Loop));
        Assert.Equal([ShortcutAction.Loop], conflicting.ConflictsOf(ShortcutAction.Mute));
        Assert.Equal([ShortcutAction.Mute], conflicting.ConflictsOf(ShortcutAction.Loop));

        // Clearing via Reset on the conflicting action
        ShortcutMap resetMute = conflicting.Reset(ShortcutAction.Mute);
        Assert.False(resetMute.HasConflict(ShortcutAction.Mute));
        Assert.False(resetMute.HasConflict(ShortcutAction.Loop));
        Assert.Empty(resetMute.ConflictsOf(ShortcutAction.Mute));
        Assert.Empty(resetMute.ConflictsOf(ShortcutAction.Loop));

        // Clearing via Rebind to an unused key
        ShortcutMap reboundMute = conflicting.With(ShortcutAction.Mute, [Key("N")]);
        Assert.False(reboundMute.HasConflict(ShortcutAction.Mute));
        Assert.False(reboundMute.HasConflict(ShortcutAction.Loop));
        Assert.Empty(reboundMute.ConflictsOf(ShortcutAction.Mute));
        Assert.Empty(reboundMute.ConflictsOf(ShortcutAction.Loop));

        // Clearing via unbinding
        ShortcutMap unboundMute = conflicting.With(ShortcutAction.Mute, []);
        Assert.False(unboundMute.HasConflict(ShortcutAction.Mute));
        Assert.False(unboundMute.HasConflict(ShortcutAction.Loop));
        Assert.Empty(unboundMute.ConflictsOf(ShortcutAction.Mute));
        Assert.Empty(unboundMute.ConflictsOf(ShortcutAction.Loop));

        // Clearing via modifying the other action instead
        ShortcutMap reboundLoop = conflicting.With(ShortcutAction.Loop, [Key("K")]);
        Assert.False(reboundLoop.HasConflict(ShortcutAction.Mute));
        Assert.False(reboundLoop.HasConflict(ShortcutAction.Loop));
        Assert.Empty(reboundLoop.ConflictsOf(ShortcutAction.Mute));
        Assert.Empty(reboundLoop.ConflictsOf(ShortcutAction.Loop));
    }

    [Fact]
    public void Multi_Action_Conflict_Clearing_Leaves_Remaining_Conflicts_Intact()
    {
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.SetIn, [Key("K")])
            .With(ShortcutAction.SetOut, [Key("K")])
            .With(ShortcutAction.Mute, [Key("K")]);

        Assert.True(map.HasConflict(ShortcutAction.SetIn));
        Assert.True(map.HasConflict(ShortcutAction.SetOut));
        Assert.True(map.HasConflict(ShortcutAction.Mute));

        // Fixing SetIn leaves SetOut and Mute conflicting with each other
        ShortcutMap fixedSetIn = map.Reset(ShortcutAction.SetIn);
        Assert.False(fixedSetIn.HasConflict(ShortcutAction.SetIn));
        Assert.True(fixedSetIn.HasConflict(ShortcutAction.SetOut));
        Assert.True(fixedSetIn.HasConflict(ShortcutAction.Mute));
        Assert.Equal([ShortcutAction.Mute], fixedSetIn.ConflictsOf(ShortcutAction.SetOut));
        Assert.Equal([ShortcutAction.SetOut], fixedSetIn.ConflictsOf(ShortcutAction.Mute));

        // Fixing SetOut clears the remaining conflict
        ShortcutMap fixedBoth = fixedSetIn.Reset(ShortcutAction.SetOut);
        Assert.False(fixedBoth.HasConflict(ShortcutAction.SetOut));
        Assert.False(fixedBoth.HasConflict(ShortcutAction.Mute));
    }

    [Fact]
    public void Clearing_A_Conflict_Redirects_Find_To_The_Remaining_Action()
    {
        // OpenFile precedes Mute in declaration order
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.OpenFile, [Key("Ctrl+M")])
            .With(ShortcutAction.Mute, [Key("Ctrl+M")]);

        Assert.Equal(ShortcutAction.OpenFile, map.Find(Key("Ctrl+M")));

        ShortcutMap resolved = map.Reset(ShortcutAction.OpenFile);
        Assert.Equal(ShortcutAction.Mute, resolved.Find(Key("Ctrl+M")));
    }

    [Fact]
    public void Reset_Restores_Single_Action_Without_Touching_Others()
    {
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.OpenFile, [Key("Ctrl+Shift+F")])
            .With(ShortcutAction.Mute, [Key("N")])
            .With(ShortcutAction.Loop, [Key("K")])
            .With(ShortcutAction.AlwaysOnTop, [Key("Alt+A")]);

        ShortcutMap resetMute = map.Reset(ShortcutAction.Mute);

        Assert.True(resetMute.IsDefault(ShortcutAction.Mute));
        Assert.Equal([Key("M")], resetMute.Get(ShortcutAction.Mute));

        // All other modified actions retain their custom bindings
        Assert.False(resetMute.IsDefault(ShortcutAction.OpenFile));
        Assert.Equal([Key("Ctrl+Shift+F")], resetMute.Get(ShortcutAction.OpenFile));
        Assert.False(resetMute.IsDefault(ShortcutAction.Loop));
        Assert.Equal([Key("K")], resetMute.Get(ShortcutAction.Loop));
        Assert.False(resetMute.IsDefault(ShortcutAction.AlwaysOnTop));
        Assert.Equal([Key("Alt+A")], resetMute.Get(ShortcutAction.AlwaysOnTop));
    }

    [Fact]
    public void Reset_All_Restores_Every_Default()
    {
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.OpenFile, [Key("Ctrl+Shift+F")])
            .With(ShortcutAction.OpenUrl, [])
            .With(ShortcutAction.FullScreen, [Key("F11")])
            .With(ShortcutAction.Mute, [Key("N")])
            .With(ShortcutAction.Loop, [Key("K")]);

        ShortcutMap reset = map.ResetAll();

        Assert.All(ShortcutActions.All, a =>
        {
            Assert.True(reset.IsDefault(a), $"Expected default for {a}");
            Assert.Equal(ShortcutActions.Defaults(a), reset.Get(a));
        });
    }

    [Theory]
    [InlineData("Comma")]
    [InlineData("Shift+Comma")]
    [InlineData("Alt+Comma")]
    [InlineData("Alt+Shift+Comma")]
    [InlineData("Period")]
    [InlineData("Shift+Period")]
    [InlineData("Alt+Period")]
    [InlineData("Alt+Shift+Period")]
    [InlineData("Escape")]
    public void Reserved_Combinations_Cannot_Be_Parsed_Or_Validated(string text)
    {
        Assert.False(KeyCombination.TryParse(text, out _));
        Assert.False(ShortcutMap.IsValidText(text));
    }

    [Fact]
    public void Comma_And_Period_Are_Reserved_Unless_Ctrl_Is_Held_While_Modified_Escape_Is_Valid()
    {
        KeyModifiers[] allModifiers =
        [
            KeyModifiers.None,
            KeyModifiers.Ctrl,
            KeyModifiers.Shift,
            KeyModifiers.Alt,
            KeyModifiers.Ctrl | KeyModifiers.Shift,
            KeyModifiers.Ctrl | KeyModifiers.Alt,
            KeyModifiers.Shift | KeyModifiers.Alt,
            KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt,
        ];

        foreach (KeyModifiers mod in allModifiers)
        {
            bool reserved = (mod & KeyModifiers.Ctrl) == 0;

            var comma = new KeyCombination(KeyCombination.OemComma, mod);
            Assert.Equal(reserved, comma.IsReserved);
            Assert.Equal(!reserved, comma.IsValid);

            var period = new KeyCombination(KeyCombination.OemPeriod, mod);
            Assert.Equal(reserved, period.IsReserved);
            Assert.Equal(!reserved, period.IsValid);
        }

        // Plain Escape is reserved, but modifier+Escape is allowed
        Assert.True(new KeyCombination(KeyCombination.Escape, KeyModifiers.None).IsReserved);
        Assert.False(new KeyCombination(KeyCombination.Escape, KeyModifiers.None).IsValid);
        Assert.False(new KeyCombination(KeyCombination.Escape, KeyModifiers.Ctrl).IsReserved);
        Assert.True(new KeyCombination(KeyCombination.Escape, KeyModifiers.Ctrl).IsValid);
        Assert.False(new KeyCombination(KeyCombination.Escape, KeyModifiers.Shift).IsReserved);
        Assert.True(new KeyCombination(KeyCombination.Escape, KeyModifiers.Shift).IsValid);
    }

    [Fact]
    public void ShortcutMap_With_Drops_Reserved_Combinations()
    {
        // Supplying only reserved combinations unbinds the action
        ShortcutMap unbound = ShortcutMap.Defaults.With(ShortcutAction.Mute,
        [
            new KeyCombination(KeyCombination.Escape, KeyModifiers.None),
            new KeyCombination(KeyCombination.OemComma, KeyModifiers.None),
            new KeyCombination(KeyCombination.OemPeriod, KeyModifiers.Alt),
        ]);
        Assert.Empty(unbound.Get(ShortcutAction.Mute));

        // Supplying a mix of valid and reserved keys keeps only the valid keys
        ShortcutMap filtered = ShortcutMap.Defaults.With(ShortcutAction.Mute,
        [
            Key("N"),
            new KeyCombination(KeyCombination.OemComma, KeyModifiers.None),
            new KeyCombination(KeyCombination.OemPeriod, KeyModifiers.Shift),
        ]);
        Assert.Equal([Key("N")], filtered.Get(ShortcutAction.Mute));
    }

    [Fact]
    public void Settings_Store_Rejects_Reserved_Combinations()
    {
        var store = new SettingsStore(new FakeStorage());

        Assert.False(store.Set(SettingKeys.ShortcutMute, "Escape"));
        Assert.False(store.Set(SettingKeys.ShortcutMute, "Comma"));
        Assert.False(store.Set(SettingKeys.ShortcutMute, "Period"));
        Assert.False(store.Set(SettingKeys.ShortcutMute, "Alt+Comma"));
        Assert.False(store.Set(SettingKeys.ShortcutMute, "Shift+Period"));
    }

    [Fact]
    public void Empty_Saved_Value_Or_Missing_Key_Restores_Defaults()
    {
        var storage = new FakeStorage
        {
            Content = """
                {
                  "version": 1,
                  "shortcuts.openFile": "",
                  "shortcuts.openUrl": "   "
                }
                """,
        };
        var store = new SettingsStore(storage);
        store.Load();

        ShortcutMap map = ShortcutMap.From(store.Current);

        Assert.True(map.IsDefault(ShortcutAction.OpenFile));
        Assert.Equal([Key("Ctrl+O")], map.Get(ShortcutAction.OpenFile));

        // Whitespace-only string safely falls back to default
        Assert.True(map.IsDefault(ShortcutAction.OpenUrl));
        Assert.Equal([Key("Ctrl+Shift+O")], map.Get(ShortcutAction.OpenUrl));

        // Unmentioned keys stay default
        Assert.True(map.IsDefault(ShortcutAction.PlayPause));
        Assert.Equal([Key("Space")], map.Get(ShortcutAction.PlayPause));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("  None  ")]
    public void None_Saved_Value_Gives_Unbound_Case_Insensitively(string text)
    {
        var storage = new FakeStorage
        {
            Content = $$"""
                {
                  "version": 1,
                  "shortcuts.mute": "{{text}}"
                }
                """,
        };
        var store = new SettingsStore(storage);
        store.Load();

        ShortcutMap map = ShortcutMap.From(store.Current);

        Assert.Empty(map.Get(ShortcutAction.Mute));
        Assert.False(map.IsDefault(ShortcutAction.Mute));
        Assert.Equal("None", map.Encode(ShortcutAction.Mute));
    }

    [Fact]
    public void Multi_Key_Custom_Binding_Survives_Save_And_Load_Round_Trip()
    {
        var storage = new FakeStorage();
        var store = new SettingsStore(storage);
        ShortcutMap map = ShortcutMap.Defaults
            .With(ShortcutAction.Mute, [Key("M"), Key("Ctrl+M")])
            .With(ShortcutAction.FullScreen, [Key("F11"), Key("F")]);

        map.WriteTo(store);
        store.Save();

        var reloadedStore = new SettingsStore(storage);
        reloadedStore.Load();
        ShortcutMap loaded = ShortcutMap.From(reloadedStore.Current);

        Assert.Equal([Key("M"), Key("Ctrl+M")], loaded.Get(ShortcutAction.Mute));
        Assert.Equal([Key("F11"), Key("F")], loaded.Get(ShortcutAction.FullScreen));
        Assert.Equal("M, Ctrl+M", loaded.Encode(ShortcutAction.Mute));
        Assert.Equal("F11, F", loaded.Encode(ShortcutAction.FullScreen));
    }

    [Fact]
    public void Comprehensive_Garbage_Values_Fall_Back_Safely_To_Defaults()
    {
        var storage = new FakeStorage
        {
            Content = """
                {
                  "version": 1,
                  "shortcuts.openFile": 12345,
                  "shortcuts.openUrl": true,
                  "shortcuts.skipBack": ["Left"],
                  "shortcuts.skipForward": { "key": "Right" },
                  "shortcuts.setIn": "Ctrl+Banana",
                  "shortcuts.setOut": "Ctrl+",
                  "shortcuts.playPause": "+Space",
                  "shortcuts.fullScreen": "F, UnknownKey",
                  "shortcuts.mute": "Ctrl+Ctrl+M",
                  "shortcuts.loop": "Escape",
                  "shortcuts.alwaysOnTop": "Alt+Comma"
                }
                """,
        };
        var store = new SettingsStore(storage);
        store.Load();

        ShortcutMap map = ShortcutMap.From(store.Current);

        // Every action with garbage falls back safely to its respective default
        Assert.True(map.IsDefault(ShortcutAction.OpenFile));
        Assert.True(map.IsDefault(ShortcutAction.OpenUrl));
        Assert.True(map.IsDefault(ShortcutAction.SkipBack));
        Assert.True(map.IsDefault(ShortcutAction.SkipForward));
        Assert.True(map.IsDefault(ShortcutAction.SetIn));
        Assert.True(map.IsDefault(ShortcutAction.SetOut));
        Assert.True(map.IsDefault(ShortcutAction.PlayPause));
        Assert.True(map.IsDefault(ShortcutAction.FullScreen));
        Assert.True(map.IsDefault(ShortcutAction.Mute));
        Assert.True(map.IsDefault(ShortcutAction.Loop));
        Assert.True(map.IsDefault(ShortcutAction.AlwaysOnTop));
    }

    [Fact]
    public void Direct_Settings_Instantiation_With_Garbage_Safely_Falls_Back()
    {
        var settings = new AetherSettings
        {
            ShortcutOpenFile = "Garbage",
            ShortcutMute = "Escape",
            ShortcutLoop = "Space, ???",
        };

        ShortcutMap map = ShortcutMap.From(settings);

        Assert.True(map.IsDefault(ShortcutAction.OpenFile));
        Assert.True(map.IsDefault(ShortcutAction.Mute));
        Assert.True(map.IsDefault(ShortcutAction.Loop));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("None", true)]
    [InlineData("none", true)]
    [InlineData("Ctrl+O", true)]
    [InlineData("F, F11", true)]
    [InlineData("Ctrl+O, Ctrl+Shift+O", true)]
    [InlineData("Banana", false)]
    [InlineData("Escape", false)]
    [InlineData("Comma", false)]
    [InlineData("Period", false)]
    [InlineData("Alt+Comma", false)]
    [InlineData("Ctrl+Comma", true)]
    [InlineData("Ctrl+Period", true)]
    [InlineData("   ", false)]
    [InlineData("Ctrl+O, Banana", false)]
    public void IsValidText_Validates_Shortcut_Strings_Accurately(string text, bool expected)
    {
        Assert.Equal(expected, ShortcutMap.IsValidText(text));
    }

    [Theory]
    [InlineData("Shift+Ctrl+O", "Ctrl+Shift+O", new[] { "Ctrl", "Shift", "O" })]
    [InlineData("Ctrl+Shift+O", "Ctrl+Shift+O", new[] { "Ctrl", "Shift", "O" })]
    [InlineData("Shift+Alt+Ctrl+O", "Ctrl+Alt+Shift+O", new[] { "Ctrl", "Alt", "Shift", "O" })]
    [InlineData("Alt+Shift+Ctrl+O", "Ctrl+Alt+Shift+O", new[] { "Ctrl", "Alt", "Shift", "O" })]
    [InlineData("Ctrl+Alt+Shift+O", "Ctrl+Alt+Shift+O", new[] { "Ctrl", "Alt", "Shift", "O" })]
    [InlineData("Shift+Alt+O", "Alt+Shift+O", new[] { "Alt", "Shift", "O" })]
    [InlineData("Alt+Shift+O", "Alt+Shift+O", new[] { "Alt", "Shift", "O" })]
    [InlineData("Ctrl+Alt+O", "Ctrl+Alt+O", new[] { "Ctrl", "Alt", "O" })]
    [InlineData("Alt+Ctrl+O", "Ctrl+Alt+O", new[] { "Ctrl", "Alt", "O" })]
    public void Display_Text_And_Keycaps_Maintain_Stable_Modifier_Order(
        string input,
        string expectedDisplay,
        string[] expectedKeycaps)
    {
        KeyCombination combination = Key(input);

        Assert.Equal(expectedDisplay, combination.DisplayText);
        Assert.Equal(expectedKeycaps, combination.Keycaps);
        Assert.Equal(expectedDisplay, combination.ToString());
    }

    [Fact]
    public void Display_Text_And_Keycaps_For_Directly_Constructed_Modifiers_Are_Stable()
    {
        var combo = new KeyCombination('O', KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt);

        Assert.Equal(["Ctrl", "Alt", "Shift", "O"], combo.Keycaps);
        Assert.Equal("Ctrl+Alt+Shift+O", combo.DisplayText);
        Assert.Equal("Ctrl+Alt+Shift+O", combo.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Left", "Ctrl+←", new[] { "Ctrl", "←" }, "Ctrl+Left")]
    [InlineData("Shift+Ctrl+Right", "Ctrl+Shift+→", new[] { "Ctrl", "Shift", "→" }, "Ctrl+Shift+Right")]
    [InlineData("Alt+Ctrl+Up", "Ctrl+Alt+↑", new[] { "Ctrl", "Alt", "↑" }, "Ctrl+Alt+Up")]
    [InlineData("Ctrl+Down", "Ctrl+↓", new[] { "Ctrl", "↓" }, "Ctrl+Down")]
    [InlineData("Shift+PageUp", "Shift+Page Up", new[] { "Shift", "Page Up" }, "Shift+PageUp")]
    [InlineData("Ctrl+Num5", "Ctrl+Num 5", new[] { "Ctrl", "Num 5" }, "Ctrl+Num5")]
    public void Display_Text_Uses_Symbols_While_ToString_Uses_Saved_Names_Both_With_Stable_Modifiers(
        string input,
        string expectedDisplay,
        string[] expectedKeycaps,
        string expectedSavedName)
    {
        KeyCombination combination = Key(input);

        Assert.Equal(expectedDisplay, combination.DisplayText);
        Assert.Equal(expectedKeycaps, combination.Keycaps);
        Assert.Equal(expectedSavedName, combination.ToString());
    }
}
