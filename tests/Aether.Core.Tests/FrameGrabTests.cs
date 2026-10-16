using Aether.Core.Playback.Video;
using Xunit;

namespace Aether.Core.Tests;

public class FrameGrabTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "aether-framegrab-" + Guid.NewGuid().ToString("N"));

    public FrameGrabTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_Configured_Folder_Wins()
    {
        Assert.Equal(@"D:\Shots", FrameGrab.SaveFolder(@"  D:\Shots ", @"C:\Users\me\Pictures"));
    }

    [Fact]
    public void Without_A_Setting_Frames_Go_To_Pictures_Aether()
    {
        Assert.Equal(Path.Combine(@"C:\Users\me\Pictures", "Aether"), FrameGrab.SaveFolder("", @"C:\Users\me\Pictures"));
        Assert.Equal("", FrameGrab.SaveFolder(" ", ""));
    }

    [Theory]
    [InlineData("~/Shots", true)]
    [InlineData("~~desktop/", true)]
    [InlineData(@"D:\Shots", false)]
    public void Mpv_Home_Paths_Are_Spotted(string folder, bool expected) => Assert.Equal(expected, FrameGrab.IsMpvPath(folder));

    [Fact]
    public void Two_Copies_Never_Share_A_Temp_File()
    {
        string a = FrameGrab.TempFile(@"C:\Temp", Guid.NewGuid());
        string b = FrameGrab.TempFile(@"C:\Temp", Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.EndsWith(".png", a);
    }

    [Fact]
    public void The_Newest_Image_Since_The_Start_Is_Found()
    {
        DateTime start = DateTime.UtcNow;
        string old = Touch("old.png", start.AddMinutes(-5));
        string first = Touch("first.png", start.AddSeconds(1));
        string second = Touch("second.jpg", start.AddSeconds(2));
        Touch("notes.txt", start.AddSeconds(3));

        Assert.Equal(second, FrameGrab.FindSaved(_folder, start));
        Assert.NotEqual(old, FrameGrab.FindSaved(_folder, start));
        Assert.NotEqual(first, FrameGrab.FindSaved(_folder, start));
    }

    [Fact]
    public void Nothing_Is_Found_When_No_Image_Is_New()
    {
        Touch("old.png", DateTime.UtcNow.AddMinutes(-5));

        Assert.Null(FrameGrab.FindSaved(_folder, DateTime.UtcNow));
    }

    [Fact]
    public void A_Missing_Folder_Finds_Nothing() =>
        Assert.Null(FrameGrab.FindSaved(Path.Combine(_folder, "gone"), DateTime.MinValue));

    private string Touch(string name, DateTime writtenUtc)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }
}
