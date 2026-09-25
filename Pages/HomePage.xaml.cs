using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public class CodeItem
{
    public string Code { get; set; } = "";
    public string StatusText { get; set; } = "";
}

public class TxnItem
{
    public string TypeLabel { get; set; } = "";
    public string Note { get; set; } = "";
    public string AmountText { get; set; } = "";
}

public sealed partial class HomePage : Page
{
    private readonly ApiService _api = new();
    public ObservableCollection<CodeItem> Codes { get; } = new();
    public ObservableCollection<TxnItem> Txns { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        CodeList.ItemsSource = Codes;
        TxnList.ItemsSource = Txns;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var me = SettingsService.Instance.Me;
        if (me == null) return;

        UsernameText.Text = me.Username;
        AvatarText.Text = string.IsNullOrEmpty(me.Avatar) ? me.Username.Substring(0, 1).ToUpper() : me.Avatar.Substring(0, 1).ToUpper();
        PointsText.Text = $"当前积分：{me.Points}";
        RoleText.Text = me.Role == "admin" ? "管理员" : "普通用户";
        AdminPanel.Visibility = me.Role == "admin" ? Visibility.Visible : Visibility.Collapsed;

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await Task.WhenAll(LoadSignStatusAsync(), LoadTransactionsAsync());
        if (SettingsService.Instance.Me?.Role == "admin") await LoadCodesAsync();
    }

    private async Task LoadSignStatusAsync()
    {
        try
        {
            var res = await _api.GetAsync("/points/sign-status");
            var signed = res.GetProperty("signed").GetBoolean();
            SignStatusText.Text = signed ? "今日已签到" : "今日尚未签到";
            SignBtn.IsEnabled = !signed;
            SignBtn.Content = signed ? "今日已签到" : "立即签到 +10 积分";
        }
        catch { /* 忽略 */ }
    }

    private async Task LoadCodesAsync()
    {
        try
        {
            var res = await _api.GetAsync("/codes/list");
            Codes.Clear();
            foreach (var c in res.GetProperty("codes").EnumerateArray().Take(50))
            {
                var code = c.GetProperty("code").GetString() ?? "";
                var amount = c.GetProperty("amount").GetInt32();
                var used = c.GetProperty("used_count").GetInt32();
                var max = c.GetProperty("max_uses").GetInt32();
                Codes.Add(new CodeItem { Code = code, StatusText = $"{amount} 积分 · 已用 {used}/{max}" });
            }
        }
        catch { /* 忽略 */ }
    }

    private async Task LoadTransactionsAsync()
    {
        try
        {
            var res = await _api.GetAsync("/points/transactions");
            Txns.Clear();
            foreach (var t in res.GetProperty("transactions").EnumerateArray().Take(100))
            {
                var type = t.GetProperty("type").GetString() ?? "";
                var amount = t.GetProperty("amount").GetInt32();
                var note = t.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
                var time = t.TryGetProperty("created_at", out var ct) ? ct.GetString() ?? "" : "";

                var typeLabel = type switch
                {
                    "sign" => "签到",
                    "redeem" => "兑换",
                    "redpacket" => amount >= 0 ? "收红包" : "发红包",
                    _ => type
                };
                Txns.Add(new TxnItem
                {
                    TypeLabel = typeLabel,
                    Note = $"{note}  {time}",
                    AmountText = amount >= 0 ? $"+{amount}" : amount.ToString()
                });
            }
        }
        catch { /* 忽略 */ }
    }

    private async void SignBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var res = await _api.PostAsync("/points/sign");
            var points = res.GetProperty("points").GetInt32();
            PointsText.Text = $"当前积分：{points}";
            if (SettingsService.Instance.Me != null)
            {
                SettingsService.Instance.Me.Points = points;
                SettingsService.Instance.Save();
            }
            SignStatusText.Text = "今日已签到";
            SignBtn.IsEnabled = false;
            SignBtn.Content = "今日已签到";
            await LoadTransactionsAsync();
        }
        catch (ApiException ex)
        {
            SignStatusText.Text = ex.Message;
            await LoadSignStatusAsync();
        }
    }

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var amount = (int)CodeAmountBox.Value;
        var count = (int)CodeCountBox.Value;
        try
        {
            GenResultText.Text = "";
            var generated = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++)
            {
                var res = await _api.PostAsync("/codes/generate", new { amount, maxUses = 1 });
                generated.Add(res.GetProperty("code").GetString() ?? "");
            }
            GenResultText.Text = $"已生成 {generated.Count} 个兑换码：" + string.Join("  ", generated);
            await LoadCodesAsync();
        }
        catch (ApiException ex)
        {
            GenResultText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            GenResultText.Text = ex.Message;
        }
    }
}
