namespace Aether.Core.Playback;

public enum ToastTone
{
    Neutral,
    Success,
    Failure,
}

/// <param name="Key">Identifies the message: a request whose key is already showing or waiting collapses into it.</param>
/// <param name="Text">What the toast says.</param>
/// <param name="Tone">Failure toasts jump the queue.</param>
/// <param name="ActionLabel">The one optional action; a toast with one stays longer and is never replaced.</param>
/// <param name="Action">Runs when the action is pressed.</param>
public sealed record ToastRequest(
    string Key,
    string Text,
    ToastTone Tone = ToastTone.Neutral,
    string? ActionLabel = null,
    Action? Action = null)
{
    public bool HasAction => ActionLabel is not null;
}

/// <summary>
/// The toast rules (screen 10): one toast at a time, the rest in order, failures ahead of the rest, a toast with an
/// action never replaced, repeats collapsed, 4 s (8 s with an action) that stop while the pointer or keyboard focus
/// is on the toast, and nothing shown while a dialog or flyout is open. The UI arms a single-shot timer for
/// <see cref="DueIn"/> and calls <see cref="Tick"/> when it fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class ToastQueue
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan DefaultActionDuration = TimeSpan.FromSeconds(8);

    private readonly TimeProvider _time;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _actionDuration;
    private readonly List<ToastRequest> _waiting = [];
    private ToastRequest? _current;
    private TimeSpan _remaining;
    private long _lastSync;
    private bool _hovered;
    private bool _focused;
    private bool _blocked;

    public ToastQueue(TimeProvider? time = null, TimeSpan? duration = null, TimeSpan? actionDuration = null)
    {
        _time = time ?? TimeProvider.System;
        _duration = duration ?? DefaultDuration;
        _actionDuration = actionDuration ?? DefaultActionDuration;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_duration, TimeSpan.Zero, nameof(duration));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_actionDuration, TimeSpan.Zero, nameof(actionDuration));
        _lastSync = _time.GetTimestamp();
    }

    /// <summary>Raised when <see cref="Showing"/> changes, or the toast shown is restarted or replaced by its repeat.</summary>
    public event EventHandler? Changed;

    /// <summary>The toast on screen: null when none is due or while a dialog or flyout is open.</summary>
    public ToastRequest? Showing => _blocked ? null : _current;

    public int WaitingCount => _waiting.Count;

    /// <summary>Time left on the toast on screen; null when there is none or its timer is held.</summary>
    public TimeSpan? DueIn
    {
        get
        {
            if (!IsRunning)
            {
                return null;
            }
            TimeSpan left = _remaining - _time.GetElapsedTime(_lastSync);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    private bool IsRunning => _current is not null && !_blocked && !_hovered && !_focused;

    public void Show(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Sync();
        ToastRequest? before = Showing;

        if (_current?.Key == request.Key)
        {
            _current = request;
            _remaining = DurationOf(request);
            Raise(changed: true);
            return;
        }

        int same = _waiting.FindIndex(w => w.Key == request.Key);
        if (same >= 0)
        {
            _waiting[same] = request;
            return;
        }

        if (request.Tone == ToastTone.Failure)
        {
            int after = _waiting.FindLastIndex(w => w.Tone == ToastTone.Failure);
            _waiting.Insert(after + 1, request);
        }
        else
        {
            _waiting.Add(request);
        }
        PromoteIfFree();
        Raise(before != Showing);
    }

    /// <summary>Closes the toast on screen (Dismiss, or its action having run). The next one follows.</summary>
    public void Dismiss()
    {
        Sync();
        if (_current is null || _blocked)
        {
            return;
        }
        ToastRequest? before = Showing;
        Advance();
        Raise(before != Showing);
    }

    /// <summary>The timer fired: closes the toast if its time has run out. True if it did.</summary>
    public bool Tick()
    {
        Sync();
        if (!IsRunning || _remaining > TimeSpan.Zero)
        {
            return false;
        }
        ToastRequest? before = Showing;
        Advance();
        Raise(before != Showing);
        return true;
    }

    public void SetHovered(bool hovered) => SetFlag(ref _hovered, hovered);

    public void SetFocused(bool focused) => SetFlag(ref _focused, focused);

    /// <summary>A dialog or flyout is open: the toast steps aside, its clock holds and the rest wait.</summary>
    public void SetBlocked(bool blocked)
    {
        if (_blocked == blocked)
        {
            return;
        }
        Sync();
        ToastRequest? before = Showing;
        _blocked = blocked;
        if (!blocked)
        {
            PromoteIfFree();
        }
        Raise(before != Showing);
    }

    private void SetFlag(ref bool flag, bool value)
    {
        if (flag != value)
        {
            Sync(); // the time up to now was spent under the old state
            flag = value;
        }
    }

    /// <summary>Takes the time that passed while the clock was running off the toast on screen.</summary>
    private void Sync()
    {
        long now = _time.GetTimestamp();
        if (IsRunning)
        {
            _remaining -= _time.GetElapsedTime(_lastSync, now);
        }
        _lastSync = now;
    }

    private void Advance()
    {
        _current = null;
        PromoteIfFree();
    }

    private void PromoteIfFree()
    {
        if (_current is not null || _blocked || _waiting.Count == 0)
        {
            return;
        }
        _current = _waiting[0];
        _waiting.RemoveAt(0);
        _remaining = DurationOf(_current);
        // The pointer and focus belonged to the toast that just closed; the host reports the new one afresh.
        _hovered = false;
        _focused = false;
    }

    private TimeSpan DurationOf(ToastRequest request) => request.HasAction ? _actionDuration : _duration;

    private void Raise(bool changed)
    {
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
