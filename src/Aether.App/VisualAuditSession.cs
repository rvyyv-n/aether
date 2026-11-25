using Aether.Core.Playback.Diagnostics;

namespace Aether_App;

/// <summary>
/// Process-wide visual audit flag parsed once from the command line before <see cref="App"/> initializes its
/// static stores, so audit runs stay completely isolated from the user's real settings and single-instance lock.
/// </summary>
internal static class VisualAuditSession
{
    public static VisualAuditOptions? Current { get; } =
        VisualAuditOptions.TryParse(Environment.GetCommandLineArgs().Skip(1));

    public static bool IsActive => Current is not null;

    public static int ExitCode { get; set; }
}
