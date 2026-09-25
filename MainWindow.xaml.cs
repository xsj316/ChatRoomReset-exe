using System;
using ChatRoomReset.Pages;
using ChatRoomReset.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ChatRoomReset;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }
    public Frame MainFrame => ContentFrame;

    public MainWindow()
    {
        InitializeComponent();
        Current = this;

        // 应用启动时应用保存的主题
        var theme = SettingsService.Instance.Theme;
        if (this.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme == "Dark" ? ElementTheme.Dark : ElementTheme.Light;
        }

        // 决定起始页
        var s = SettingsService.Instance;
        Type start;
        if (!string.IsNullOrEmpty(s.Token) && s.Me != null)
            start = typeof(HomePage);
        else if (!string.IsNullOrEmpty(s.ServerBase))
            start = typeof(LoginPage);
        else
            start = typeof(InitPage);

        ContentFrame.Navigate(start);

        Title = "Chat Room 重置版 beta 1.0.0";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            switch (tag)
            {
                case "home": Navigate(typeof(HomePage)); break;
                case "friends": Navigate(typeof(FriendsPage)); break;
                case "groups": Navigate(typeof(GroupsPage)); break;
                case "settings": Navigate(typeof(SettingsPage)); break;
            }
        }
    }

    /// <summary>框架内页面导航（ChatPage 等非菜单页会清除菜单选中）。</summary>
    public void Navigate(Type pageType, object? parameter = null)
    {
        // 非菜单页：取消 NavigationView 选中，避免高亮残留
        if (pageType != typeof(HomePage) && pageType != typeof(FriendsPage) &&
            pageType != typeof(GroupsPage) && pageType != typeof(SettingsPage))
        {
            NavView.SelectedItem = null;
        }
        ContentFrame.Navigate(pageType, parameter);
    }
}
