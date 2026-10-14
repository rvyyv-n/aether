using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Aether_App.Controls;

/// <summary>The diagnostics overlay (Ctrl+I): playback facts from mpv as six rows, with a Copy button. UI thread only.</summary>
public sealed partial class DiagnosticsPanel : UserControl
{
    private DiagnosticsRows _rows = DiagnosticsRows.From(PlayerState.Initial);

    public DiagnosticsPanel()
    {
        InitializeComponent();
        TitleText.Text = AppStrings.Get("Diagnostics_Title");
        CopyButton.Content = AppStrings.Get("Diagnostics_Copy");
        DecoderLabel.Text = AppStrings.Get("Diagnostics_Decoder");
        VideoLabel.Text = AppStrings.Get("Diagnostics_Video");
        FrameRateLabel.Text = AppStrings.Get("Diagnostics_FrameRate");
        DisplayLabel.Text = AppStrings.Get("Diagnostics_Display");
        DroppedLabel.Text = AppStrings.Get("Diagnostics_Dropped");
        CacheLabel.Text = AppStrings.Get("Diagnostics_Cache");
        Show(_rows);
    }

    public void Update(in PlayerState state)
    {
        DiagnosticsRows rows = DiagnosticsRows.From(state);
        if (rows != _rows)
        {
            Show(rows);
        }
    }

    private void Show(DiagnosticsRows rows)
    {
        _rows = rows;
        DecoderValue.Text = rows.Decoder;
        VideoValue.Text = rows.Video;
        FrameRateValue.Text = rows.FrameRate;
        DisplayValue.Text = rows.Display;
        DroppedValue.Text = rows.Dropped;
        CacheValue.Text = rows.Cache;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        string[] labels =
        [
            DecoderLabel.Text, VideoLabel.Text, FrameRateLabel.Text,
            DisplayLabel.Text, DroppedLabel.Text, CacheLabel.Text,
        ];
        var package = new DataPackage();
        package.SetText(_rows.ToText(TitleText.Text, labels));
        Clipboard.SetContent(package);
    }
}
