using Microsoft.UI.Xaml.Media;

namespace Aether_App;

/// <summary>
/// A backdrop that draws nothing. A flyout's popup window otherwise gets Windows' own acrylic, which shows the moment
/// the window does, before the flyout's content, and doubles the blur the render thread draws behind it (12.7).
/// </summary>
public sealed partial class NoBackdrop : SystemBackdrop;
