using Windows.UI.ViewManagement;

namespace Aether_App.Controls;

/// <summary>
/// Reduced motion: with Windows animation effects off, fades are instant (handoff, locked decisions).
/// Read on each fade, so a change in Settings applies without a restart. UI thread.
/// </summary>
internal static class Motion
{
    private static readonly UISettings Settings = new();

    public static bool AnimationsEnabled => Settings.AnimationsEnabled;
}
