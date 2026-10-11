namespace Aether.Core.Playback.Recents;

/// <summary>Which date label a recents row shows.</summary>
public enum RecentDay
{
    Today,
    Yesterday,
    Earlier,
}

/// <summary>The parts of a recents row (2.10, 2.18) that do not depend on the strings file.</summary>
public static class RecentDisplay
{
    /// <summary>Compares calendar days in <paramref name="now"/>'s offset (the local time zone in the app).</summary>
    public static RecentDay Day(DateTimeOffset openedAt, DateTimeOffset now)
    {
        DateTime opened = openedAt.ToOffset(now.Offset).Date;
        DateTime today = now.Date;
        return opened >= today ? RecentDay.Today
            : opened == today.AddDays(-1) ? RecentDay.Yesterday
            : RecentDay.Earlier;
    }

    /// <summary>The folder for files ("D:\Captures"), the site for links ("youtube.com").</summary>
    public static string Source(RecentItem item) =>
        item.IsLink ? WebLink.Host(item.Location) ?? item.Location : Path.GetDirectoryName(item.Location) ?? item.Location;

    /// <summary>A file that is no longer on disk. Links are never missing. <paramref name="fileExists"/> may touch the disk.</summary>
    public static bool IsMissing(RecentItem item, Func<string, bool> fileExists) => !item.IsLink && !fileExists(item.Location);
}
