using System;
using System.Threading.Tasks;
using ChatRoomReset.Models;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ApiService _api = new();
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        var s = SettingsService.Instance;
        ServerBox.Text = s.ServerBase;
        ThemePicker.SelectedIndex = s.Theme switch
        {
            "Light" => 0,
            "Dark" => 1,
            _ => 2   // System
        };
        AutoUpdateSwitch.IsOn = s.AutoUpdate;
        UpdateAccountText();
    }

    private void UpdateAccountText()
    {
        var me = SettingsService.Instance.Me;
        AccountText.Text = me == null ? "未登录" : $"{me.Username}（ID {me.Id}，积分 {me.Points}）";
    }

    private async void SaveServerBtn_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = ServerBox.Text.Trim();
        if (string.IsNullOrEmpty(baseUrl)) return;

        var (ok, msg) = await _api.TestConnectionAsync(baseUrl);
        if (!ok)
        {
            ServerStatusText.Text = $"无法连接：{msg}";
            return;
        }

        SettingsService.Instance.ServerBase = baseUrl;
        SettingsService.Instance.Save();
        ServerStatusText.Text = "已保存，正在重连…";

        if (!string.IsNullOrEmpty(SettingsService.Instance.Token))
        {
            try
            {
                await App.Socket.ConnectAsync(SettingsService.Instance.ServerBase, SettingsService.Instance.Token);
                ServerStatusText.Text = "已保存并重连成功";
            }
            catch
            {
                ServerStatusText.Text = "已保存，但重连失败，请重新登录";
            }
        }
        else
        {
            ServerStatusText.Text = "已保存";
        }
    }

    private void SaveThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemePicker.SelectedIndex switch
        {
            0 => "Light",
            1 => "Dark",
            _ => "System"
        };
        SettingsService.Instance.Theme = theme;
        SettingsService.Instance.Save();

        if (MainWindow.Current?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
    }

    private void AutoUpdateSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.AutoUpdate = AutoUpdateSwitch.IsOn;
        SettingsService.Instance.Save();
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
        var info = await UpdateService.CheckLatestAsync();
        CheckUpdateBtn.IsEnabled = true;

        if (info == null || string.IsNullOrEmpty(info.TagName))
        {
            UpdateStatusText.Text = "检查失败或已是最新版本（当前 beta 1.0.0）";
            return;
        }
        UpdateStatusText.Text = $"发现 {info.TagName}（{info.Name}）";
        var dialog = new ContentDialog
        {
            Title = "发现新版本",
            Content = $"Chat Room 重置版 {info.TagName} 已发布（{info.Name}）。是否前往下载页？",
            PrimaryButtonText = "前往下载",
            CloseButtonText = "稍后",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrEmpty(info.HtmlUrl))
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(info.HtmlUrl));
        }
    }

    private async void RedeemBtn_Click(object sender, RoutedEventArgs e)
    {
        var code = RedeemBox.Text.Trim();
        if (string.IsNullOrEmpty(code)) return;
        try
        {
            var res = await _api.PostAsync("/points/redeem", new { code });
            var points = res.GetProperty("points").GetInt32();
            var amount = res.GetProperty("amount").GetInt32();
            RedeemStatusText.Text = $"兑换成功，+{amount} 积分，当前 {points} 积分";
            RedeemBox.Text = "";
            if (SettingsService.Instance.Me != null)
            {
                SettingsService.Instance.Me.Points = points;
                SettingsService.Instance.Save();
            }
            UpdateAccountText();
        }
        catch (ApiException ex)
        {
            RedeemStatusText.Text = $"兑换失败：{ex.Message}";
        }
    }

    private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "退出登录",
            Content = "确定要退出当前账号吗？",
            PrimaryButtonText = "退出",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        App.Socket.Disconnect();
        SettingsService.Instance.ClearSession();
        UpdateAccountText();
        MainWindow.Current?.Navigate(typeof(LoginPage));
    }
}
