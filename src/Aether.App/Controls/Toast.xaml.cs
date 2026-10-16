using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Controls;

/// <summary>
/// One toast: message, optional status dot and action, and Dismiss. It only draws what it is given and
/// reports hover, focus and button presses; the queue rules live in <see cref="ToastQueue"/>. UI thread only.
/// </summary>
public sealed partial class Toast : UserControl
{
    private ToastRequest? _request;

    public Toast()
    {
        InitializeComponent();
        string dismiss = AppStrings.Get("Toast_Dismiss");
        AutomationProperties.SetName(DismissButton, dismiss);
        ToolTipService.SetToolTip(DismissButton, dismiss);
        GotFocus += (_, _) => FocusChanged?.Invoke(this, true);
        LosingFocus += OnLosingFocus;
    }

    public event EventHandler? ActionInvoked;

    public event EventHandler? DismissRequested;

    public event EventHandler<bool>? HoverChanged;

    public event EventHandler<bool>? FocusChanged;

    /// <summary>Draws <paramref name="request"/> and tells screen readers about it.</summary>
    public void Present(ToastRequest request)
    {
        _request = request;
        MessageText.Text = request.Text;
        AutomationProperties.SetName(this, request.Text);
        Dot.Visibility = request.Tone == ToastTone.Neutral ? Visibility.Collapsed : Visibility.Visible;
        Dot.Fill = (Brush)Application.Current.Resources[
            request.Tone == ToastTone.Failure ? "AetherStatusWarningBrush" : "AetherStatusReadyBrush"];
        ActionButton.Content = request.ActionLabel;
        ActionButton.Visibility = request.HasAction ? Visibility.Visible : Visibility.Collapsed;
        if (request.ActionLabel is { } label)
        {
            AutomationProperties.SetName(ActionButton, label);
        }

        // A live region only speaks when its text changes in place; a notification also covers a repeat of the same message.
        (FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this))
            ?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent,
                request.Text,
                "AetherToast");
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        _request?.Action?.Invoke();
        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => HoverChanged?.Invoke(this, true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HoverChanged?.Invoke(this, false);

    /// <summary>Focus moving from the action to Dismiss stays on the toast, so the timer must not restart in between.</summary>
    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs e)
    {
        for (DependencyObject? next = e.NewFocusedElement; next is not null; next = VisualTreeHelper.GetParent(next))
        {
            if (ReferenceEquals(next, this))
            {
                return;
            }
        }
        FocusChanged?.Invoke(this, false);
    }
}
