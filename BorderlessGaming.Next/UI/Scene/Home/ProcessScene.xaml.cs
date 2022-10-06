using BorderlessGaming.Next.UI.Scene.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BorderlessGaming.Next.UI.Scene.Home;

internal sealed partial class ProcessScene : Page
{
    public ProcessViewModel ViewModel => (ProcessViewModel)DataContext;
    public ProcessScene()
    {
        InitializeComponent();
    }

    private void SettingsClicked(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SettingsScene));
    }
}