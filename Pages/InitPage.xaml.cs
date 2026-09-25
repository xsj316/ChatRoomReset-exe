using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public sealed partial class InitPage : Page
{
    public string ServerText { get; set; }

    private readonly ApiService _api = new();

    public InitPage()
    {
        ServerText = SettingsService.Instance.ServerBase;
        InitializeComponent();
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerText.Trim();
        if (string.IsNullOrEmpty(url))
        {
            StatusText.Text = "请填写服务器地址";
            return;
        }

        ConnectBtn.IsEnabled = false;
        StatusText.Text = "正在连接...";
        var (ok, msg) = await _api.TestConnectionAsync(url);

        if (ok)
        {
            SettingsService.Instance.ServerBase = url;
            SettingsService.Instance.Save();
            StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StatusText.Text = "连接成功：" + msg;
            await Task.Delay(400);
            Frame.Navigate(typeof(LoginPage));
        }
        else
        {
            StatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            StatusText.Text = "连接失败：" + msg;
        }
        ConnectBtn.IsEnabled = true;
    }
}
