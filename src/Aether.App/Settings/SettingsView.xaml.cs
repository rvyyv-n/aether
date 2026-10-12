using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace Aether_App.Settings;

public sealed partial class SettingsView : UserControl
{
    // Set while the list and the picker are being synced, so the second SelectionChanged doesn't re-navigate.
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        SectionList.ItemsSource = SettingsSection.All;
        SectionPicker.ItemsSource = SettingsSection.All;
        Show(SettingsSection.All[0]);
    }

    public void Show(SettingsSection section)
    {
        _syncing = true;
        SectionList.SelectedItem = section;
        SectionPicker.SelectedItem = section;
        _syncing = false;

        PageTitle.Text = section.Title;
        if (ContentFrame.CurrentSourcePageType != section.PageType)
        {
            ContentFrame.Navigate(section.PageType, null, new SuppressNavigationTransitionInfo());
            ContentScroller.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ((Selector)sender).SelectedItem is not SettingsSection section)
        {
            return;
        }

        Show(section);
    }
}
