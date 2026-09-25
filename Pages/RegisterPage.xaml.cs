using System;
using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChatRoomReset.Pages;

public sealed partial class RegisterPage : Page
{
    private readonly ApiService _api = new();

    public RegisterPage()
    {
        InitializeComponent();
    }

    private void ConfirmBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) _ = DoRegisterAsync();
    }

    private void CodeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) _ = DoVerifyAsync();
    }

    private async void RegisterBtn_Click(object sender, RoutedEventArgs e) => await DoRegisterAsync();

    private async void VerifyBtn_Click(object sender, RoutedEventArgs e) => await DoVerifyAsync();

    private async void ResendBtn_Click(object sender, RoutedEventArgs e) => await DoResendAsync();

    private async Task DoRegisterAsync()
    {
        var username = UsernameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        var confirm = ConfirmBox.Password;

        if (username.Length < 2 || username.Length > 20)
        {
            StatusText.Text = "用户名需为 2-20 位";
            return;
        }
        if (string.IsNullOrEmpty(email) || !email.Contains('@') || !email.Contains('.'))
        {
            StatusText.Text = "请输入正确的邮箱地址";
            return;
        }
        if (password.Length < 6 || password.Length > 32)
        {
            StatusText.Text = "密码需为 6-32 位";
            return;
        }
        if (password != confirm)
        {
            StatusText.Text = "两次输入的密码不一致";
            return;
        }

        RegisterBtn.IsEnabled = false;
        StatusText.Text = "注册中...";
        try
        {
            await _api.PostAsync("/auth/register", new { username, password, email });
            // 注册成功：切到验证码确认面板，验证通过后账号才可登录
            _pendingEmail = email;
            VerifyHintText.Text = $"验证码已发送至 {email}，请查收后输入验证码完成邮箱确认。未验证的账号无法登录。";
            CodeBox.Text = "";
            VerifyStatusText.Text = "";
            FormPanel.Visibility = Visibility.Collapsed;
            VerifyPanel.Visibility = Visibility.Visible;
            CodeBox.Focus(FocusState.Programmatic);
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            RegisterBtn.IsEnabled = true;
        }
    }

    private async Task DoVerifyAsync()
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(_pendingEmail) || string.IsNullOrEmpty(code))
        {
            VerifyStatusText.Text = "请输入收到的验证码";
            return;
        }

        VerifyBtn.IsEnabled = false;
        VerifyStatusText.Text = "验证中...";
        try
        {
            await _api.PostAsync("/auth/verify-email", new { email = _pendingEmail, code });
            VerifyStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            VerifyStatusText.Text = "邮箱验证成功，即将跳转登录...";
            await Task.Delay(800);
            Frame.Navigate(typeof(LoginPage));
        }
        catch (ApiException ex)
        {
            VerifyStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            VerifyStatusText.Text = ex.Message;
        }
        finally
        {
            VerifyBtn.IsEnabled = true;
        }
    }

    private async Task DoResendAsync()
    {
        if (string.IsNullOrEmpty(_pendingEmail)) return;
        ResendBtn.IsEnabled = false;
        VerifyStatusText.Text = "重新发送中...";
        try
        {
            await _api.PostAsync("/auth/resend-code", new { email = _pendingEmail });
            VerifyStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            VerifyStatusText.Text = "验证码已重新发送，请查收";
        }
        catch (ApiException ex)
        {
            VerifyStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            VerifyStatusText.Text = ex.Message;
        }
        finally
        {
            ResendBtn.IsEnabled = true;
        }
    }

    private void BackToForm_Click(object sender, RoutedEventArgs e)
    {
        VerifyPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
        StatusText.Text = "";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private string _pendingEmail = "";
}
