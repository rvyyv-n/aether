using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Windowing;

namespace Aether.Core.Tests;

public class WindowPlacementTests
{
    private static readonly WindowRect Primary = new(0, 0, 1920, 1040);
    private static readonly WindowRect Second = new(1920, 0, 1920, 1040);
    private static readonly WindowRect[] Both = [Primary, Second];

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public bool Fail { get; set; }
        public string? Read() => Fail ? throw new IOException() : Content;
        public void Write(string content) => Content = Fail ? throw new IOException() : content;
    }

    [Fact]
    public void A_Window_Already_On_A_Monitor_Is_Left_Alone()
    {
        var saved = new WindowRect(200, 100, 1280, 720);

        Assert.Equal(saved, WindowPlacement.Fit(saved, Both, Primary));
    }

    [Fact]
    public void A_Window_On_The_Second_Monitor_Stays_There()
    {
        var saved = new WindowRect(2200, 200, 1000, 600);

        Assert.Equal(saved, WindowPlacement.Fit(saved, Both, Primary));
    }

    [Fact]
    public void A_Window_On_A_Removed_Monitor_Is_Centred_On_The_Primary()
    {
        var saved = new WindowRect(2200, 200, 1000, 600);

        Assert.Equal(new WindowRect(460, 220, 1000, 600), WindowPlacement.Fit(saved, [Primary], Primary));
    }

    [Fact]
    public void A_Window_Hanging_Off_An_Edge_Is_Pulled_Back_Inside()
    {
        var saved = new WindowRect(1500, 800, 900, 600);

        Assert.Equal(new WindowRect(1020, 440, 900, 600), WindowPlacement.Fit(saved, [Primary], Primary));
    }

    [Fact]
    public void A_Window_Bigger_Than_The_Monitor_Is_Shrunk_To_It()
    {
        var saved = new WindowRect(0, 0, 3000, 2000);

        Assert.Equal(Primary, WindowPlacement.Fit(saved, [Primary], Primary));
    }

    [Fact]
    public void A_Sliver_Showing_Is_Treated_As_Off_Screen()
    {
        var saved = new WindowRect(-980, 100, 1000, 600);

        WindowRect fitted = WindowPlacement.Fit(saved, [Primary], Primary);

        Assert.Equal(new WindowRect(460, 220, 1000, 600), fitted);
    }

    [Fact]
    public void A_Window_Straddling_Two_Monitors_Goes_To_The_One_It_Mostly_Covers()
    {
        var saved = new WindowRect(1800, 100, 1000, 600);

        Assert.Equal(new WindowRect(1920, 100, 1000, 600), WindowPlacement.Fit(saved, Both, Primary));
    }

    [Fact]
    public void Saved_Window_Round_Trips_Through_Storage()
    {
        var storage = new FakeStorage();
        var window = new SavedWindow(new WindowRect(-1500, 40, 1280, 720), true);
        new WindowStateStore(storage).Save(window);

        var reloaded = new WindowStateStore(storage);
        reloaded.Load();

        Assert.Equal(window, reloaded.Saved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"x":0,"y":0,"width":50,"height":50}""")]
    [InlineData("""{"x":0,"y":0,"width":"wide","height":600}""")]
    [InlineData("""{"x":0,"y":0,"width":99999,"height":600}""")]
    public void Unusable_Files_Give_No_Saved_Window(string? json)
    {
        var storage = new FakeStorage { Content = json };
        var store = new WindowStateStore(storage);

        store.Load();

        Assert.Null(store.Saved);
    }

    [Fact]
    public void Collapsed_Windows_Are_Not_Saved()
    {
        var storage = new FakeStorage();
        var store = new WindowStateStore(storage);

        store.Save(new SavedWindow(new WindowRect(0, 0, 10, 10), false));

        Assert.Null(store.Saved);
        Assert.Null(storage.Content);
    }

    [Fact]
    public void Storage_Errors_Do_Not_Escape()
    {
        var store = new WindowStateStore(new FakeStorage { Fail = true });
        var window = new SavedWindow(new WindowRect(0, 0, 800, 600), false);

        store.Load();
        store.Save(window);

        Assert.Equal(window, store.Saved);
    }

    [Fact]
    public void A_Missing_Maximized_Field_Means_Not_Maximised()
    {
        var store = new WindowStateStore(new FakeStorage { Content = """{"x":10,"y":20,"width":800,"height":600}""" });

        store.Load();

        Assert.Equal(new SavedWindow(new WindowRect(10, 20, 800, 600), false), store.Saved);
    }
}
