using System;
using System.Threading.Tasks;
using ChatRoomReset.Models;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

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

        // 自动更新检查（静默失败，不阻塞启动）
        if (SettingsService.Instance.AutoUpdate)
            _ = CheckUpdateQuietAsync();
    }

    private static async Task CheckUpdateQuietAsync()
    {
        var info = await UpdateService.CheckLatestAsync();
        if (info == null || string.IsNullOrEmpty(info.TagName)) return;
        if (MainAppWindow?.Content is FrameworkElement root && root.XamlRoot != null)
        {
            var dialog = new ContentDialog
            {
                Title = "发现新版本",
                Content = $"Chat Room 重置版 {info.TagName} 已发布（{info.Name}）。是否前往下载页？",
                PrimaryButtonText = "前往下载",
                CloseButtonText = "稍后",
                XamlRoot = root.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(info.HtmlUrl))
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new System.Uri(info.HtmlUrl));
            }
        }
    }
}
