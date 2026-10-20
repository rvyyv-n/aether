using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class LanStreamPanelTests
{
    private static LanStreamPanel Panel(
        LanServerState server = LanServerState.Off,
        LanStreamSource source = LanStreamSource.File,
        bool hasAddress = true,
        int watchers = 0,
        bool addressChanged = false,
        bool publicNetwork = false) =>
        new(source, server, hasAddress, watchers, addressChanged, publicNetwork);

    [Fact]
    public void Starts_Off_With_The_Switch_Ready_For_A_Local_File()
    {
        LanStreamPanel panel = Panel();

        Assert.False(panel.SwitchOn);
        Assert.True(panel.SwitchEnabled);
        Assert.False(panel.ShowsCode);
        Assert.Equal("LanStream_StatusOff", panel.StatusKey);
        Assert.Equal(LanStreamTone.Neutral, panel.StatusTone);
        Assert.Null(panel.NoteKey);
    }

    [Theory]
    [InlineData(LanStreamSource.Nothing, "LanStream_NoteNoFile")]
    [InlineData(LanStreamSource.Link, "LanStream_NoteLink")]
    public void Without_A_Local_File_The_Switch_Is_Disabled_With_A_Reason(LanStreamSource source, string note)
    {
        LanStreamPanel panel = Panel(source: source);

        Assert.False(panel.SwitchEnabled);
        Assert.Equal(note, panel.NoteKey);
        Assert.False(panel.NoteIsWarning);
    }

    [Fact]
    public void Without_A_Network_Address_It_Cannot_Switch_On()
    {
        LanStreamPanel panel = Panel(hasAddress: false);

        Assert.False(panel.SwitchEnabled);
        Assert.Equal("LanStream_NoteNoNetwork", panel.NoteKey);
        Assert.True(panel.NoteIsWarning);
    }

    [Fact]
    public void Starting_Holds_The_Switch_On_And_Locked()
    {
        LanStreamPanel panel = Panel(LanServerState.Starting);

        Assert.True(panel.SwitchOn);
        Assert.False(panel.SwitchEnabled);
        Assert.False(panel.ShowsCode);
        Assert.Equal("LanStream_StatusStarting", panel.StatusKey);
    }

    [Theory]
    [InlineData(0, "LanStream_StatusWaiting")]
    [InlineData(1, "LanStream_StatusWatchingOne")]
    [InlineData(3, "LanStream_StatusWatchingMany")]
    public void On_Shows_The_Code_And_Counts_Phones(int watchers, string status)
    {
        LanStreamPanel panel = Panel(LanServerState.On, watchers: watchers);

        Assert.True(panel.SwitchOn);
        Assert.True(panel.SwitchEnabled);
        Assert.True(panel.ShowsCode);
        Assert.Equal(status, panel.StatusKey);
        Assert.Equal(LanStreamTone.Ready, panel.StatusTone);
    }

    [Fact]
    public void On_Can_Always_Be_Switched_Off_Even_When_The_Network_Goes()
    {
        LanStreamPanel panel = Panel(LanServerState.On, hasAddress: false);

        Assert.True(panel.SwitchEnabled);
        Assert.False(panel.ShowsCode);
        Assert.Equal("LanStream_NoteNoNetwork", panel.NoteKey);
    }

    [Fact]
    public void A_Changed_Address_Asks_For_A_New_Scan_Only_While_On()
    {
        Assert.Equal("LanStream_NoteAddressChanged", Panel(LanServerState.On, addressChanged: true).NoteKey);
        Assert.Null(Panel(addressChanged: true).NoteKey);
    }

    [Theory]
    [InlineData(LanServerState.Off)]
    [InlineData(LanServerState.On)]
    public void A_Public_Network_Warns_Before_And_While_Streaming(LanServerState server)
    {
        LanStreamPanel panel = Panel(server, publicNetwork: true);

        Assert.True(panel.SwitchEnabled);
        Assert.Equal("LanStream_NotePublicNetwork", panel.NoteKey);
        Assert.True(panel.NoteIsWarning);
    }

    [Fact]
    public void A_Failure_Or_A_New_Address_Outranks_The_Public_Network_Warning()
    {
        Assert.Equal("LanStream_NoteFailed", Panel(LanServerState.Failed, publicNetwork: true).NoteKey);
        Assert.Equal("LanStream_NoteAddressChanged", Panel(LanServerState.On, addressChanged: true, publicNetwork: true).NoteKey);
        Assert.Equal("LanStream_NoteLink", Panel(source: LanStreamSource.Link, publicNetwork: true).NoteKey);
    }

    [Theory]
    [InlineData(LanServerState.PortUnavailable, "LanStream_NotePortBusy")]
    [InlineData(LanServerState.Failed, "LanStream_NoteFailed")]
    public void A_Failed_Start_Is_Off_With_A_Warning_And_Can_Be_Tried_Again(LanServerState server, string note)
    {
        LanStreamPanel panel = Panel(server);

        Assert.False(panel.SwitchOn);
        Assert.True(panel.SwitchEnabled);
        Assert.Equal("LanStream_StatusOff", panel.StatusKey);
        Assert.Equal(note, panel.NoteKey);
        Assert.True(panel.NoteIsWarning);
    }

    [Fact]
    public void After_A_Failed_Start_A_Link_Explains_Itself_First()
    {
        Assert.Equal("LanStream_NoteLink", Panel(LanServerState.Failed, LanStreamSource.Link).NoteKey);
    }
}
