namespace Aether.Core.Tests.TestSupport;

/// <summary>
/// Finds repository directories by walking up from the test execution directory to the solution root.
/// </summary>
public static class RepoPath
{
    private static readonly Lazy<string> RootLazy = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AetherPlayer.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate AetherPlayer.sln starting from AppContext.BaseDirectory: '{AppContext.BaseDirectory}'.");
    });

    public static string Root => RootLazy.Value;

    public static string Get(params string[] relativeParts) =>
        Path.Combine([Root, .. relativeParts]);
}
