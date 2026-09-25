using System;
using System.Text.Json;
using System.Threading.Tasks;
using ChatRoomReset.Models;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public sealed partial class LoginPage : Page
{
    private readonly ApiService _api = new();
    private readonly SocketService _socket = new();

    public LoginPage()
    {
        InitializeComponent();
        ServerLabel.Text = "服务器：" + SettingsService.Instance.ServerBase;
        Loaded += LoginPage_Loaded;
    }

    private void LoginPage_Loaded(object sender, RoutedEventArgs e)
    {
        UsernameBox.Focus(FocusState.Programmatic);
    }

    private void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) _ = DoLoginAsync();
    }

    private async void LoginBtn_Click(object sender, RoutedEventArgs e) => await DoLoginAsync();

    private async Task DoLoginAsync()
    {
        var account = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "请输入用户名或邮箱和密码";
            return;
        }

        LoginBtn.IsEnabled = false;
        StatusText.Text = "登录中...";
        try
        {
            var res = await _api.PostAsync("/auth/login", new { account, password });
            var token = res.GetProperty("token").GetString() ?? "";
            var me = res.GetProperty("user").Deserialize<UserInfo>();

            SettingsService.Instance.Token = token;
            SettingsService.Instance.Me = me;
            SettingsService.Instance.Save();

            try { await _socket.ConnectAsync(SettingsService.Instance.ServerBase, token); }
            catch { /* Socket 连不上不阻塞登录 */ }

            Frame.Navigate(typeof(HomePage));
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = "登录失败：" + ex.Message;
        }
        finally
        {
            LoginBtn.IsEnabled = true;
        }
    }

    // 忘记密码：输入邮箱 → 服务端发送验证码 → 输入验证码+新密码 → 重置成功
    private async void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var emailBox = new TextBox
        {
            Header = "注册邮箱",
            PlaceholderText = "请输入注册时使用的邮箱",
            Width = 300
        };
        var step1 = new ContentDialog
        {
            Title = "忘记密码",
            Content = emailBox,
            PrimaryButtonText = "发送验证码",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await step1.ShowAsync() != ContentDialogResult.Primary) return;

        var email = emailBox.Text.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            StatusText.Text = "邮箱格式不正确";
            return;
        }

        try
        {
            StatusText.Text = "验证码发送中...";
            await _api.PostAsync("/auth/forgot-password", new { email });
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        var codeBox = new TextBox { Header = "邮箱验证码", PlaceholderText = "请输入收到的 6 位验证码", Width = 300 };
        var newPassBox = new PasswordBox { Header = "新密码", PlaceholderText = "6-32 位", Width = 300 };
        var step2Content = new StackPanel { Spacing = 12, Width = 300 };
        step2Content.Children.Add(new TextBlock
        {
            Text = "验证码已发送至 " + email + "（若该邮箱已注册），请查收后设置新密码。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 12
        });
        step2Content.Children.Add(codeBox);
        step2Content.Children.Add(newPassBox);

        var step2 = new ContentDialog
        {
            Title = "重置密码",
            Content = step2Content,
            PrimaryButtonText = "确认重置",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await step2.ShowAsync() != ContentDialogResult.Primary) return;

        var code = codeBox.Text.Trim();
        var newPassword = newPassBox.Password;
        if (string.IsNullOrEmpty(code) || newPassword.Length < 6)
        {
            StatusText.Text = "验证码不能为空，新密码至少 6 位";
            return;
        }

        try
        {
            await _api.PostAsync("/auth/reset-password", new { email, code, newPassword });
            StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StatusText.Text = "密码重置成功，请使用新密码登录";
        }
        catch (ApiException ex)
        {
            StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            StatusText.Text = ex.Message;
        }
    }

    private void GoRegister_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(RegisterPage));
    }
}
