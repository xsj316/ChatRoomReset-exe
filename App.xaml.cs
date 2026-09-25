using ChatRoomReset.Services;
using Microsoft.UI.Xaml;

namespace ChatRoomReset;

public partial class App : Application
{
    public static Window? MainAppWindow { get; private set; }
    public static SocketService Socket { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}
