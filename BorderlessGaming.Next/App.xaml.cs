using BorderlessGaming.Next.UI.Hosting;
using Microsoft.UI.Xaml;

namespace BorderlessGaming.Next;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        new MainWindow().Activate();
    }
}