using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ChatRoomReset.Pages;

public class GroupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpper();
    public string InviteCode { get; set; } = "";
    public string OwnerName { get; set; } = "";
    // 未读角标
    public int Unread { get; set; }
    public string UnreadText => Unread > 0 ? (Unread > 99 ? "99+" : Unread.ToString()) : "";
    public Visibility UnreadVisible => Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class GroupsPage : Page
{
    private readonly ApiService _api = new();
    public ObservableCollection<GroupItem> Groups { get; } = new();

    public GroupsPage()
    {
        InitializeComponent();
        GroupList.ItemsSource = Groups;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Socket.GroupMessageReceived -= Socket_GroupMessageReceived;
        App.Socket.GroupMessageReceived += Socket_GroupMessageReceived;
        _ = LoadGroupsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Socket.GroupMessageReceived -= Socket_GroupMessageReceived;
    }

    private void Socket_GroupMessageReceived(int groupId, int fromId, string content, long fileId, long msgId, int replyTo, string fromName)
    {
        // 收到新群消息 → 刷新未读角标
        DispatcherQueue.TryEnqueue(() => _ = LoadUnreadAsync());
    }

    private async Task LoadGroupsAsync()
    {
        try
        {
            var res = await _api.GetAsync("/groups/list");
            Groups.Clear();
            foreach (var r in res.GetProperty("groups").EnumerateArray())
            {
                Groups.Add(new GroupItem
                {
                    Id = r.GetProperty("id").GetInt32(),
                    Name = r.GetProperty("name").GetString() ?? "",
                    InviteCode = r.GetProperty("invite_code").GetString() ?? "",
                    OwnerName = r.GetProperty("owner_name").GetString() ?? ""
                });
            }
            EmptyPanel.Visibility = Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
        await LoadUnreadAsync();
    }

    /// <summary>拉取群未读数，写入群列表角标。</summary>
    private async Task LoadUnreadAsync()
    {
        try
        {
            var res = await _api.GetAsync("/messages/unread");
            var unreads = new Dictionary<int, int>();
            foreach (var r in res.GetProperty("group").EnumerateArray())
            {
                var peerId = r.GetProperty("groupId").GetInt32();
                var n = r.GetProperty("unread").GetInt32();
                unreads[peerId] = n;
            }
            foreach (var g in Groups)
            {
                g.Unread = unreads.TryGetValue(g.Id, out var n) ? n : 0;
            }
        }
        catch { }
    }

    private void GroupList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GroupItem g)
        {
            MainWindow.Current?.Navigate(typeof(ChatPage), new ChatNavParams { Mode = "group", Id = g.Id, Name = g.Name });
        }
    }

    private async void CreateGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "群名称（1-30 字符）" };
        var dialog = new ContentDialog
        {
            Title = "创建群聊",
            Content = box,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = box.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var res = await _api.PostAsync("/groups/create", new { name });
            await LoadGroupsAsync();
            var inviteCode = res.TryGetProperty("inviteCode", out var ic) ? ic.GetString() ?? "" : "";
            await ShowDialogAsync("创建成功", $"群聊“{name}”已创建。\n群号（邀请码）：{inviteCode}");
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("创建失败", ex.Message);
        }
    }

    private async void JoinGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { PlaceholderText = "输入群号（邀请码）" };
        var dialog = new ContentDialog
        {
            Title = "加入群聊",
            Content = box,
            PrimaryButtonText = "加入",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var code = box.Text.Trim();
        if (string.IsNullOrEmpty(code)) return;
        try
        {
            await _api.PostAsync("/groups/join", new { inviteCode = code });
            await LoadGroupsAsync();
            await ShowDialogAsync("加入成功", "已加入该群聊");
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("加入失败", ex.Message);
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
