using Aether.Core.Playback.Input;

namespace Aether.Core.Playback.Tour;

/// <summary>The steps of the first-run tour (7.26), in the order they show.</summary>
public enum TourStep
{
    Open,
    Controls,
    Menu,
    Shortcuts,
    Settings,
}

/// <summary>Where a step's card sits against its target.</summary>
public enum TourPlacement
{
    Below,
    Above,
    Right,
    Centre,
}

/// <summary>
/// The first-run tour's state (7.26, spec in docs/reviews/pass-7-first-run-tour-spec.md): which step shows, moving
/// back and forth, and ending. It starts once on a launch that lands on an idle home screen and can be replayed from
/// Settings › About. Not thread-safe (UI thread only).
/// </summary>
public sealed class FirstRunTour
{
    public static IReadOnlyList<TourStep> Steps { get; } =
        [TourStep.Open, TourStep.Controls, TourStep.Menu, TourStep.Shortcuts, TourStep.Settings];

    /// <summary>The keys the Shortcuts step lists, with the user's current bindings.</summary>
    public static IReadOnlyList<ShortcutAction> Keys { get; } =
    [
        ShortcutAction.PlayPause,
        ShortcutAction.SkipBack,
        ShortcutAction.SkipForward,
        ShortcutAction.SetIn,
        ShortcutAction.SetOut,
        ShortcutAction.ExportClip,
        ShortcutAction.FullScreen,
    ];

    public bool IsActive { get; private set; }

    public int Index { get; private set; }

    public int Count => Steps.Count;

    public TourStep Current => Steps[Index];

    public bool IsFirst => Index == 0;

    public bool IsLast => Index == Count - 1;

    /// <summary>
    /// Whether the tour should open by itself: never seen, and the home screen is idle (the engine is up, no file is
    /// open or opening, nothing failed and nothing else covers it).
    /// </summary>
    public static bool ShouldStartOnLaunch(bool seen, bool homeIdle) => !seen && homeIdle;

    public static TourPlacement PlacementOf(TourStep step) => step switch
    {
        TourStep.Open => TourPlacement.Below,
        TourStep.Controls => TourPlacement.Above,
        TourStep.Menu => TourPlacement.Right,
        TourStep.Settings => TourPlacement.Below,
        _ => TourPlacement.Right,
    };

    /// <summary>Whether the step lights up a control; the Shortcuts step only has its card.</summary>
    public static bool HasSpotlight(TourStep step) => step is not (TourStep.Menu or TourStep.Shortcuts);

    /// <summary>Starts from the first step. False if it is already running.</summary>
    public bool Start()
    {
        if (IsActive)
        {
            return false;
        }
        IsActive = true;
        Index = 0;
        return true;
    }

    /// <summary>Moves on a step; on the last step it ends the tour. True while the tour is still running.</summary>
    public bool Next()
    {
        if (!IsActive)
        {
            return false;
        }
        if (IsLast)
        {
            End();
            return false;
        }
        Index++;
        return true;
    }

    /// <summary>Goes back a step. False on the first step or when not running.</summary>
    public bool Back()
    {
        if (!IsActive || IsFirst)
        {
            return false;
        }
        Index--;
        return true;
    }

    /// <summary>Ends the tour (Skip, Done, Esc, or the user going elsewhere). True if it was running.</summary>
    public bool End()
    {
        bool wasActive = IsActive;
        IsActive = false;
        Index = 0;
        return wasActive;
    }
}
