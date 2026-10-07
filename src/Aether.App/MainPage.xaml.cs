using Aether_App.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App;

/// <summary>Hosts the video surface and the player session behind it.</summary>
public sealed partial class MainPage : Page
{
    private PlayerSession? _session;

    public MainPage()
    {
        InitializeComponent();
        IsTabStop = true; // receives the frame-step keys
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PlayerSession? Session => _session;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _session ??= PlayerSession.TryCreate(VideoSurface, this, DispatcherQueue);
        Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _session?.Dispose();
        _session = null;
    }
}
