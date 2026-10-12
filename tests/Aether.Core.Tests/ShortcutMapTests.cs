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
    [InlineData(ShortcutAction.SetIn, "I")]
    [InlineData(ShortcutAction.SetOut, "O")]
    [InlineData(ShortcutAction.PlayPause, "Space")]
    [InlineData(ShortcutAction.Mute, "M")]
    [InlineData(ShortcutAction.Loop, "L")]
    [InlineData(ShortcutAction.AlwaysOnTop, "T")]
    public void Defaults_Are_Todays_Keys(ShortcutAction action, string expected)
    {
        Assert.Equal([expected], Defaults(action));
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
    [InlineData("Ctrl+Period")]
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
}
