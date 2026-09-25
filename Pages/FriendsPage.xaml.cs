using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ChatRoomReset.Models;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public class FriendItem
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username.Substring(0, 1).ToUpper();
    public string StatusText { get; set; } = "离线";
    public Brush StatusColor { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
}

public sealed partial class FriendsPage : Page
{
    private readonly ApiService _api = new();
    private readonly HashSet<int> _online = new();
    public ObservableCollection<FriendItem> Friends { get; } = new();

    public FriendsPage()
    {
        InitializeComponent();
        FriendList.ItemsSource = Friends;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Socket.FriendOnlineChanged -= Socket_FriendOnlineChanged;
        App.Socket.FriendOnlineChanged += Socket_FriendOnlineChanged;
        _ = LoadFriendsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Socket.FriendOnlineChanged -= Socket_FriendOnlineChanged;
    }

    private void Socket_FriendOnlineChanged(int userId, bool online)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (online) _online.Add(userId); else _online.Remove(userId);
            foreach (var f in Friends)
            {
                if (f.Id == userId) ApplyStatus(f, online);
            }
        });
    }

    private static void ApplyStatus(FriendItem f, bool online)
    {
        f.StatusText = online ? "在线" : "离线";
        f.StatusColor = online
            ? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private async Task LoadFriendsAsync()
    {
        try
        {
            var res = await _api.GetAsync("/friends/list");
            var rows = res.GetProperty("friends");
            Friends.Clear();
            foreach (var r in rows.EnumerateArray())
            {
                var f = new FriendItem
                {
                    Id = r.GetProperty("id").GetInt32(),
                    Username = r.GetProperty("username").GetString() ?? ""
                };
                var online = _online.Contains(f.Id);
                ApplyStatus(f, online);
                Friends.Add(f);
            }
            EmptyPanel.Visibility = Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private void FriendList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FriendItem f)
        {
            MainWindow.Current?.Navigate(typeof(ChatPage), new ChatNavParams { Mode = "private", Id = f.Id, Name = f.Username });
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) _ = DoSearchAsync();
    }

    private async void SearchBtn_Click(object sender, RoutedEventArgs e) => await DoSearchAsync();

    private async Task DoSearchAsync()
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;
        try
        {
            var res = await _api.GetAsync($"/friends/search?q={Uri.EscapeDataString(q)}");
            var users = res.GetProperty("users").EnumerateArray().ToList();
            if (users.Count == 0)
            {
                await ShowDialogAsync("搜索结果", "未找到匹配的用户");
                return;
            }

            var list = new ListView { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single };
            list.ItemsSource = users.Select(u => new FriendItem
            {
                Id = u.GetProperty("id").GetInt32(),
                Username = u.GetProperty("username").GetString() ?? ""
            }).ToList();
            list.ItemTemplate = (DataTemplate)XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<TextBlock Text=\"{Binding Username}\" FontSize=\"15\" Padding=\"0,6\"/></DataTemplate>");

            var dialog = new ContentDialog
            {
                Title = "选择要添加的用户",
                Content = list,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && list.SelectedItem is FriendItem selected)
            {
                await _api.PostAsync("/friends/request", new { friendId = selected.Id });
                App.Socket.NotifyFriendRequest(selected.Id);
                await ShowDialogAsync("添加好友", $"已向 {selected.Username} 发送好友申请");
            }
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("添加失败", ex.Message);
        }
    }

    private async void RequestsBtn_Click(object sender, RoutedEventArgs e) => await RefreshRequestsAsync();

    private async Task RefreshRequestsAsync()
    {
        try
        {
            var res = await _api.GetAsync("/friends/requests");
            var rows = res.GetProperty("requests").EnumerateArray().ToList();
            RequestsList.ItemsSource = rows.Select(r => new FriendItem
            {
                Id = r.GetProperty("id").GetInt32(),
                Username = r.GetProperty("username").GetString() ?? ""
            }).ToList();
            RequestsEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private async void AcceptRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is FriendItem f)
        {
            try
            {
                await _api.PostAsync("/friends/accept", new { friendId = f.Id });
                await RefreshRequestsAsync();
                await LoadFriendsAsync();
            }
            catch (ApiException ex)
            {
                await ShowDialogAsync("操作失败", ex.Message);
            }
        }
    }

    private async Task ShowDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "好的",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
