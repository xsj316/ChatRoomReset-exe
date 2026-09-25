using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace ChatRoomReset.Pages;

public class ChatNavParams
{
    public string Mode { get; set; } = "private";   // private / group
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ChatItem
{
    public bool IsMine { get; set; }
    public bool ShowSender { get; set; }
    public string FromName { get; set; } = "";
    public string Content { get; set; } = "";
    public bool ShowContent => !string.IsNullOrEmpty(Content);
    public long FileId { get; set; }
    public string FileLabel { get; set; } = "";
    public bool ShowFile => FileId > 0;
    public string TimeText { get; set; } = "";
}

/// <summary>群友面板成员项：头像（无头像用首字母占位）+ 名称 + 在线状态 + 群内角色。</summary>
public class GroupMemberItem
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Avatar { get; set; } = "";
    public bool HasAvatar => !string.IsNullOrEmpty(Avatar);
    public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username.Substring(0, 1).ToUpper();
    public string Role { get; set; } = "member";   // owner / member
    public bool IsOwner => Role == "owner";
    public bool Online { get; set; }
    public string StatusText => Online ? "在线" : "离线";
    public Brush OnlineBrush => Online
        ? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
        : new SolidColorBrush(Microsoft.UI.Colors.Gray);
    public string SelfTag { get; set; } = "";
}

public class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MyTemplate { get; set; }
    public DataTemplate? TheirTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
        => item is ChatItem c && c.IsMine ? MyTemplate! : TheirTemplate!;
}

public sealed partial class ChatPage : Page
{
    private readonly ApiService _api = new();
    private readonly FileTransferService _files = new();
    private ChatNavParams? _nav;
    public ObservableCollection<ChatItem> Messages { get; } = new();
    public ObservableCollection<GroupMemberItem> Members { get; } = new();

    public ChatPage()
    {
        InitializeComponent();
        MsgList.ItemsSource = Messages;
        MemberList.ItemsSource = Members;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _nav = e.Parameter as ChatNavParams;
        if (_nav == null) return;

        TitleText.Text = _nav.Name;
        if (_nav.Mode == "group")
        {
            App.Socket.JoinGroup(_nav.Id);
            App.Socket.GroupMessageReceived -= Socket_GroupMessage;
            App.Socket.GroupMessageReceived += Socket_GroupMessage;
            App.Socket.GroupMembersUpdated -= Socket_GroupMembersUpdated;
            App.Socket.GroupMembersUpdated += Socket_GroupMembersUpdated;
        }
        else
        {
            App.Socket.PrivateMessageReceived -= Socket_PrivateMessage;
            App.Socket.PrivateMessageReceived += Socket_PrivateMessage;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Socket.PrivateMessageReceived -= Socket_PrivateMessage;
        App.Socket.GroupMessageReceived -= Socket_GroupMessage;
        App.Socket.GroupMembersUpdated -= Socket_GroupMembersUpdated;
        MemberPanel.Visibility = Visibility.Collapsed;
    }

    private void Socket_PrivateMessage(int fromId, string content, long fileId)
    {
        if (_nav?.Mode != "private" || fromId != _nav.Id) return;
        DispatcherQueue.TryEnqueue(() => AddMessage(new ChatItem
        {
            IsMine = false,
            FromName = _nav.Name,
            Content = content,
            FileId = fileId,
            FileLabel = fileId > 0 ? "收到一个文件，点击下载" : "",
            TimeText = DateTime.Now.ToString("HH:mm")
        }));
    }

    private void Socket_GroupMessage(int groupId, int fromId, string content, long fileId)
    {
        if (_nav?.Mode != "group" || groupId != _nav.Id) return;
        var me = SettingsService.Instance.Me;
        if (me != null && fromId == me.Id) return; // 自己发送的由发送方本地添加
        DispatcherQueue.TryEnqueue(() => AddMessage(new ChatItem
        {
            IsMine = false,
            ShowSender = true,
            FromName = $"用户{fromId}",
            Content = content,
            FileId = fileId,
            FileLabel = fileId > 0 ? "收到一个文件，点击下载" : "",
            TimeText = DateTime.Now.ToString("HH:mm")
        }));
    }

    // 群成员变化（进群/退群/上下线）实时刷新面板
    private void Socket_GroupMembersUpdated(int groupId)
    {
        if (_nav?.Mode != "group" || groupId != _nav.Id) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (MemberPanel.Visibility == Visibility.Visible) _ = LoadMembersAsync();
        });
    }

    private void AddMessage(ChatItem item)
    {
        Messages.Add(item);
        if (Messages.Count > 0) MsgList.ScrollIntoView(Messages[Messages.Count - 1]);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Current?.MainFrame.CanGoBack == true)
            MainWindow.Current.MainFrame.GoBack();
        else
            MainWindow.Current?.Navigate(typeof(FriendsPage));
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) SendBtn_Click(sender, e);
    }

    private async void SendBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || _nav == null) return;

        InputBox.Text = "";
        AddMessage(new ChatItem
        {
            IsMine = true,
            Content = text,
            TimeText = DateTime.Now.ToString("HH:mm")
        });

        try
        {
            if (_nav.Mode == "group") App.Socket.SendGroup(_nav.Id, text);
            else App.Socket.SendPrivate(_nav.Id, text);
        }
        catch { }
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_nav == null) return;

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            SendBtn.IsEnabled = false;
            var progress = new Progress<(long done, long total)>(p =>
            {
                SendBtn.Content = $"上传中 {p.done}/{p.total}";
            });
            var fileId = await _files.UploadAsync(file.Path, progress);

            if (_nav.Mode == "group") App.Socket.SendGroup(_nav.Id, "", fileId);
            else App.Socket.SendPrivate(_nav.Id, "", fileId);

            AddMessage(new ChatItem
            {
                IsMine = true,
                FileId = fileId,
                FileLabel = "📎 " + file.Name,
                TimeText = DateTime.Now.ToString("HH:mm")
            });
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("上传失败", ex.Message);
        }
        finally
        {
            SendBtn.IsEnabled = true;
            SendBtn.Content = "发送";
        }
    }

    private async void FileCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ChatItem item) return;
        try
        {
            var info = await _api.GetAsync($"/files/{item.FileId}/info");
            var f = info.GetProperty("file");
            var filename = f.GetProperty("filename").GetString() ?? "file";
            var size = f.GetProperty("size").GetInt64();

            var result = await new ContentDialog
            {
                Title = "下载文件",
                Content = $"{filename}\n大小：{FormatSize(size)}",
                PrimaryButtonText = "下载",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            }.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var (path, _) = await _files.DownloadAsync(item.FileId, filename);
                await ShowDialogAsync("下载完成", $"已保存到：\n{path}");
            }
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("下载失败", ex.Message);
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (_nav == null) return;

        // 群聊：三点菜单直接切换右侧群友面板显示/收起
        if (_nav.Mode == "group")
        {
            ToggleMemberPanel();
            return;
        }

        var flyout = new MenuFlyout();

        var redpacket = new MenuFlyoutItem { Text = "发红包（积分）" };
        redpacket.Click += async (_, _) => await SendRedPacketAsync();
        var delete = new MenuFlyoutItem { Text = "删除好友" };
        delete.Click += async (_, _) => await DeleteFriendAsync(false);
        var block = new MenuFlyoutItem { Text = "拉黑" };
        block.Click += async (_, _) => await DeleteFriendAsync(true);
        flyout.Items.Add(redpacket);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(delete);
        flyout.Items.Add(block);

        flyout.ShowAt(sender as FrameworkElement);
    }

    // 群友面板：显示/收起切换
    private void ToggleMemberPanel()
    {
        if (MemberPanel.Visibility == Visibility.Visible)
        {
            MemberPanel.Visibility = Visibility.Collapsed;
            return;
        }
        MemberPanel.Visibility = Visibility.Visible;
        _ = LoadMembersAsync();
    }

    private void CollapseMemberPanel_Click(object sender, RoutedEventArgs e)
    {
        MemberPanel.Visibility = Visibility.Collapsed;
    }

    private async Task LoadMembersAsync()
    {
        if (_nav == null || _nav.Mode != "group") return;
        try
        {
            var res = await _api.GetAsync($"/groups/{_nav.Id}/members");
            var meId = SettingsService.Instance.Me?.Id ?? 0;
            Members.Clear();
            foreach (var m in res.GetProperty("members").EnumerateArray())
            {
                var item = new GroupMemberItem
                {
                    Id = m.GetProperty("id").GetInt32(),
                    Username = m.GetProperty("username").GetString() ?? "",
                    Avatar = m.GetProperty("avatar").GetString() ?? "",
                    Role = m.GetProperty("role").GetString() ?? "member",
                    Online = m.TryGetProperty("online", out var o) && o.GetBoolean()
                };
                item.SelfTag = item.Id == meId ? "我" : "";
                Members.Add(item);
            }
            MemberCountText.Text = $"({Members.Count})";
        }
        catch { /* 加载失败保持面板原状 */ }
    }

    // 点击成员头像发起好友申请（Tapped 带 Handled，防止冒泡触发面板外关闭逻辑）
    private async void MemberAvatar_Click(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not GroupMemberItem m) return;
        e.Handled = true;

        var me = SettingsService.Instance.Me;
        if (me != null && me.Id == m.Id)
        {
            await ShowDialogAsync("提示", "这是你自己，无需添加好友");
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "添加好友",
            Content = $"确定向「{m.Username}」发起好友申请吗？",
            PrimaryButtonText = "申请",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _api.PostAsync("/friends/request", new { friendId = m.Id });
            App.Socket.NotifyFriendRequest(m.Id);
            await ShowDialogAsync("已发送", $"已向 {m.Username} 发送好友申请");
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("申请失败", ex.Message);
        }
    }

    private async Task SendRedPacketAsync()
    {
        if (_nav == null) return;
        var box = new NumberBox
        {
            Header = "红包金额（积分）",
            Minimum = 1,
            Maximum = 999999,
            Value = 10,
            Width = 220
        };
        var dialog = new ContentDialog
        {
            Title = $"发红包给 {_nav.Name}",
            Content = box,
            PrimaryButtonText = "发送",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var res = await _api.PostAsync("/points/redpacket", new { friendId = _nav.Id, amount = (int)box.Value });
            var amount = res.GetProperty("amount").GetInt32();
            AddMessage(new ChatItem
            {
                IsMine = true,
                Content = $"🧧 你向 {_nav.Name} 发送了一个 {amount} 积分红包",
                TimeText = DateTime.Now.ToString("HH:mm")
            });
            UpdateMyPoints();
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("发送失败", ex.Message);
        }
    }

    private async Task DeleteFriendAsync(bool block)
    {
        if (_nav == null) return;
        var action = block ? "拉黑" : "删除";
        var dialog = new ContentDialog
        {
            Title = $"{action}好友",
            Content = $"确定要{action}「{_nav.Name}」吗？{(!block ? "删除后需重新申请添加。" : "拉黑后将无法再收到对方消息。")}",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            await _api.PostAsync(block ? "/friends/block" : "/friends/delete", new { friendId = _nav.Id });
            await ShowDialogAsync("操作成功", $"已{action}「{_nav.Name}」");
            MainWindow.Current?.Navigate(typeof(FriendsPage));
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("操作失败", ex.Message);
        }
    }

    private void UpdateMyPoints()
    {
        var me = SettingsService.Instance.Me;
        if (me == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var res = await _api.GetAsync("/points/sign-status");
                var _ = res.GetProperty("signed").GetBoolean();
            }
            catch { }
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.##} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.##} KB";
        return $"{bytes} B";
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
