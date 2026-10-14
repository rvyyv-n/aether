using Aether.Core.Playback;

namespace Aether_App;

/// <summary>
/// Lets code outside the player page (Settings, the clip drawer) ask for a toast: the page owns the
/// <see cref="ToastService"/> and shows what comes through here. UI thread only.
/// </summary>
internal static class AppToasts
{
    /// <summary>Raised for each request; the player page shows it.</summary>
    public static event Action<ToastRequest>? Requested;

    public static void Show(string key, string text, ToastTone tone = ToastTone.Neutral, string? actionLabel = null, Action? action = null) =>
        Requested?.Invoke(new ToastRequest(key, text, tone, actionLabel, action));
}
