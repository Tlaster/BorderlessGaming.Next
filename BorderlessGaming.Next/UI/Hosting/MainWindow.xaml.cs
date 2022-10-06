using BorderlessGaming.Next.UI.Scene.Home;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BorderlessGaming.Next.UI.Hosting;

public sealed partial class MainWindow : ModernWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        TrySetSystemBackdrop();
        RootFrame.Navigate(typeof(ProcessScene));
    }
}