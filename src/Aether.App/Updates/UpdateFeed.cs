namespace Aether_App.Updates;

/// <summary>Where releases live. Change the repository here and nowhere else (the release workflow uses the repository it runs in).</summary>
internal static class UpdateFeed
{
    public const string RepositoryUrl = "https://github.com/rvyyv-n/aether";

    /// <summary>The Velopack pack ID. Must not be "Aether": Velopack installs to <c>%LOCALAPPDATA%\&lt;id&gt;</c> and deletes that folder on uninstall, which is where settings and resume data live.</summary>
    public const string PackId = "AetherPlayer";
}
