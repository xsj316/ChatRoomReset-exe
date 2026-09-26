using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ChatRoomReset.Services;
using Microsoft.UI.Dispatching;
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

    // 消息 ID 与类型（用于定位撤回/删除/已读）
    public long MsgId { get; set; }
    public string ClientId { get; set; } = ""; // 发送方本地标识，服务端落库后回传真实 MsgId
    public string Type { get; set; } = "private"; // private / group
    public int PeerId { get; set; }

    // 状态：normal / recalled / deleted
    public string Status { get; set; } = "normal";
    public bool ShowPlaceholder => Status != "normal";
    public string PlaceholderText => Status == "recalled" ? "撤回了一条消息" : "该消息已删除";

    // 引用回复
    public long ReplyTo { get; set; }
    public string ReplyContent { get; set; } = "";
    public bool ShowReply => ReplyTo > 0;

    // @ 提醒
    public bool Mentioned { get; set; }
    public bool ShowMentioned => Mentioned;

    // 已读回执（仅我方消息展示）
    public bool Read { get; set; }
    public bool ShowRead => IsMine && Read && Status == "normal";

    // 本地发送时间（撤回 2 分钟窗口判断）
    public DateTime SendAt { get; set; } = DateTime.Now;
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

    // 禁言截止时间（服务端返回的本地时间字符串，空=未禁言）
    public string MutedUntil { get; set; } = "";
    public bool MutedVisible => !string.IsNullOrEmpty(MutedUntil);

    // 当前用户为群主/管理员且目标可被管理时显示管理菜单
    public bool ManageVisible { get; set; }
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

    // —— 全量升级状态 ——
    private bool _loadingOlder;            // 正在加载更早消息
    private bool _hasMore = true;          // 是否还有更早消息
    private long _lastMsgId;               // 本地最新消息 id（重连补拉游标）
    private long _replyTo;                 // 引用回复目标消息 id
    private string _replyHint = "";        // 引用摘要文本
    private DispatcherQueueTimer? _typingTimer; // 输入中 debounce 计时器
    private DispatcherQueueTimer? _typingClearTimer; // "正在输入…" 3s 超时清除
    private ScrollViewer? _scroll;         // 消息列表内部滚动容器（上滑分页）
    private bool _mentionMenuOpen;          // @ 成员选择框是否打开
    private string _currentAnnouncement = ""; // 当前群公告内容

    public ChatPage()
    {
        InitializeComponent();
        MsgList.ItemsSource = Messages;
        MemberList.ItemsSource = Members;
        Loaded += (_, _) => AttachScrollViewer();
    }

    // 消息列表加载完成后挂接内部 ScrollViewer，用于上滑分页
    private void AttachScrollViewer()
    {
        if (_scroll != null) return;
        _scroll = FindScrollViewer(MsgList);
        if (_scroll != null) _scroll.ViewChanged += MsgList_ViewChanged;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindScrollViewer(child);
            if (nested != null) return nested;
        }
        return null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _nav = e.Parameter as ChatNavParams;
        if (_nav == null) return;

        TitleText.Text = _nav.Name;
        Messages.Clear();
        _loadingOlder = false;
        _hasMore = true;
        _lastMsgId = 0;
        _replyTo = 0;
        _replyHint = "";
        _typingTimer?.Stop();
        _typingTimer = null;
        TypingText.Visibility = Visibility.Collapsed;
        AnnouncementBar.Visibility = Visibility.Collapsed;
        ReplyHintBar.Visibility = Visibility.Collapsed;

        // 订阅全部升级事件
        App.Socket.PrivateMessageReceived -= Socket_PrivateMessage;
        App.Socket.PrivateMessageReceived += Socket_PrivateMessage;
        App.Socket.GroupMessageReceived -= Socket_GroupMessage;
        App.Socket.GroupMessageReceived += Socket_GroupMessage;
        App.Socket.GroupMembersUpdated -= Socket_GroupMembersUpdated;
        App.Socket.GroupMembersUpdated += Socket_GroupMembersUpdated;
        App.Socket.MessageRecalled -= Socket_MessageRecalled;
        App.Socket.MessageRecalled += Socket_MessageRecalled;
        App.Socket.MessageDeleted -= Socket_MessageDeleted;
        App.Socket.MessageDeleted += Socket_MessageDeleted;
        App.Socket.PrivateRead -= Socket_PrivateRead;
        App.Socket.PrivateRead += Socket_PrivateRead;
        App.Socket.PrivateTyping -= Socket_PrivateTyping;
        App.Socket.PrivateTyping += Socket_PrivateTyping;
        App.Socket.GroupMention -= Socket_GroupMention;
        App.Socket.GroupMention += Socket_GroupMention;
        App.Socket.GroupAnnouncementUpdated -= Socket_GroupAnnouncementUpdated;
        App.Socket.GroupAnnouncementUpdated += Socket_GroupAnnouncementUpdated;
        App.Socket.GroupFileUpdated -= Socket_GroupFileUpdated;
        App.Socket.GroupFileUpdated += Socket_GroupFileUpdated;
        App.Socket.GroupKicked -= Socket_GroupKicked;
        App.Socket.GroupKicked += Socket_GroupKicked;
        App.Socket.SocketError -= Socket_SocketError;
        App.Socket.SocketError += Socket_SocketError;
        App.Socket.Reconnected -= Socket_Reconnected;
        App.Socket.Reconnected += Socket_Reconnected;
        App.Socket.MessageAcked -= Socket_MessageAcked;
        App.Socket.MessageAcked += Socket_MessageAcked;

        if (_nav.Mode == "group") App.Socket.JoinGroup(_nav.Id);

        // 进入会话：加载最近 50 条 + 清零未读 + 群公告
        _ = LoadHistoryAsync(initial: true);
        _ = ClearUnreadAsync();
        if (_nav.Mode == "group") _ = LoadAnnouncementAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Socket.PrivateMessageReceived -= Socket_PrivateMessage;
        App.Socket.GroupMessageReceived -= Socket_GroupMessage;
        App.Socket.GroupMembersUpdated -= Socket_GroupMembersUpdated;
        App.Socket.MessageRecalled -= Socket_MessageRecalled;
        App.Socket.MessageDeleted -= Socket_MessageDeleted;
        App.Socket.PrivateRead -= Socket_PrivateRead;
        App.Socket.PrivateTyping -= Socket_PrivateTyping;
        App.Socket.GroupMention -= Socket_GroupMention;
        App.Socket.GroupAnnouncementUpdated -= Socket_GroupAnnouncementUpdated;
        App.Socket.GroupFileUpdated -= Socket_GroupFileUpdated;
        App.Socket.GroupKicked -= Socket_GroupKicked;
        App.Socket.SocketError -= Socket_SocketError;
        App.Socket.Reconnected -= Socket_Reconnected;
        App.Socket.MessageAcked -= Socket_MessageAcked;
        _typingTimer?.Stop();
        MemberPanel.Visibility = Visibility.Collapsed;
    }

    private void Socket_PrivateMessage(int fromId, string content, long fileId, long msgId, int replyTo)
    {
        if (_nav?.Mode != "private" || fromId != _nav.Id) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            AddMessage(new ChatItem
            {
                IsMine = false,
                FromName = _nav.Name,
                Content = content,
                FileId = fileId,
                FileLabel = fileId > 0 ? "收到一个文件，点击下载" : "",
                TimeText = DateTime.Now.ToString("HH:mm"),
                MsgId = msgId,
                Type = "private",
                PeerId = fromId,
                ReplyTo = replyTo,
                ReplyContent = replyTo > 0 ? FindReplyContent(replyTo) : ""
            });
            if (msgId > _lastMsgId) _lastMsgId = msgId;
        });
    }

    private void Socket_GroupMessage(int groupId, int fromId, string content, long fileId, long msgId, int replyTo, string fromName)
    {
        if (_nav?.Mode != "group" || groupId != _nav.Id) return;
        var me = SettingsService.Instance.Me;
        if (me != null && fromId == me.Id) return; // 自己发送的由发送方本地添加
        DispatcherQueue.TryEnqueue(() =>
        {
            var mentioned = me != null && content.Contains("@" + me.Username, StringComparison.OrdinalIgnoreCase);
            AddMessage(new ChatItem
            {
                IsMine = false,
                ShowSender = true,
                FromName = string.IsNullOrEmpty(fromName) ? $"用户{fromId}" : fromName,
                Content = content,
                FileId = fileId,
                FileLabel = fileId > 0 ? "收到一个文件，点击下载" : "",
                TimeText = DateTime.Now.ToString("HH:mm"),
                MsgId = msgId,
                Type = "group",
                PeerId = groupId,
                ReplyTo = replyTo,
                ReplyContent = replyTo > 0 ? FindReplyContent(replyTo) : "",
                Mentioned = mentioned
            });
            if (msgId > _lastMsgId) _lastMsgId = msgId;
        });
    }

    // 从本地已加载消息中查找被引用消息内容
    private string FindReplyContent(long msgId)
    {
        var m = Messages.FirstOrDefault(x => x.MsgId == msgId);
        if (m == null) return "";
        return m.Status == "normal" ? (m.Content.Length > 40 ? m.Content.Substring(0, 40) + "…" : m.Content) : m.PlaceholderText;
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

        var replyTo = _replyTo;   // 先保存引用目标，再清空提示条
        var replyHint = _replyHint;
        InputBox.Text = "";
        var me = SettingsService.Instance.Me;
        var clientId = Guid.NewGuid().ToString("N");
        var item = new ChatItem
        {
            IsMine = true,
            FromName = me?.Username ?? "我",
            Content = text,
            TimeText = DateTime.Now.ToString("HH:mm"),
            ClientId = clientId,
            ReplyTo = replyTo,
            ReplyContent = replyHint,
            Type = _nav.Mode,
            PeerId = _nav.Id
        };
        AddMessage(item);
        CancelReply_Click(sender, e); // 清除引用提示条

        try
        {
            if (_nav.Mode == "group") App.Socket.SendGroup(_nav.Id, text, 0, (int)replyTo, clientId);
            else App.Socket.SendPrivate(_nav.Id, text, 0, (int)replyTo, clientId);
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

            var me = SettingsService.Instance.Me;
            var clientId = Guid.NewGuid().ToString("N");
            var replyTo = _replyTo;
            var replyHint = _replyHint;

            if (_nav.Mode == "group") App.Socket.SendGroup(_nav.Id, "", fileId, (int)replyTo, clientId);
            else App.Socket.SendPrivate(_nav.Id, "", fileId, (int)replyTo, clientId);

            AddMessage(new ChatItem
            {
                IsMine = true,
                FileId = fileId,
                FileLabel = "📎 " + file.Name,
                TimeText = DateTime.Now.ToString("HH:mm"),
                ClientId = clientId,
                ReplyTo = replyTo,
                ReplyContent = replyHint,
                Type = _nav.Mode,
                PeerId = _nav.Id
            });
            CancelReply_Click(sender, e);
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
                    Online = m.TryGetProperty("online", out var o) && o.GetBoolean(),
                    MutedUntil = m.TryGetProperty("mutedUntil", out var mu) ? mu.GetString() ?? "" : ""
                };
                item.SelfTag = item.Id == meId ? "我" : "";
                Members.Add(item);
            }
            // 群主（或服务端管理员）视角：非自己、非群主的成员显示管理菜单
            var canManage = Members.Any(x => x.Id == meId && x.Role == "owner");
            foreach (var mm in Members)
            {
                mm.ManageVisible = canManage && mm.Id != meId && mm.Role != "owner";
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

    // ==================== 第二批：历史分页 ====================

    private async Task LoadHistoryAsync(bool initial)
    {
        if (_nav == null || _loadingOlder) return;
        _loadingOlder = true;
        try
        {
            long beforeId = 0;
            string anchorKey = "";
            if (!initial && Messages.Count > 0)
            {
                beforeId = Messages[0].MsgId;
                anchorKey = Messages[0].ClientId + "|" + Messages[0].MsgId;
            }

            var url = $"/messages/history?type={_nav.Mode}&peerId={_nav.Id}&limit=50";
            if (beforeId > 0) url += $"&beforeId={beforeId}";
            var res = await _api.GetAsync(url);
            var parsed = res.GetProperty("messages").EnumerateArray()
                .Select(x => ParseHistoryItem(x)).ToList();

            if (initial)
            {
                Messages.Clear();
                foreach (var m in parsed) Messages.Add(m);
                if (Messages.Count > 0) MsgList.ScrollIntoView(Messages[Messages.Count - 1]);
            }
            else
            {
                // 插入头部（保持时间升序），并定位到加载前的锚点消息
                for (int i = parsed.Count - 1; i >= 0; i--) Messages.Insert(0, parsed[i]);
                var anchor = Messages.FirstOrDefault(x => (x.ClientId + "|" + x.MsgId) == anchorKey);
                if (anchor != null) MsgList.ScrollIntoView(anchor);
            }

            if (parsed.Count < 50) _hasMore = false;
            _lastMsgId = Messages.Count > 0 ? Messages.Max(x => x.MsgId) : _lastMsgId;
        }
        catch
        {
            // 加载失败：保留现有消息，允许用户再次上滑重试
        }
        finally
        {
            _loadingOlder = false;
        }
    }

    private ChatItem ParseHistoryItem(JsonElement m)
    {
        var me = SettingsService.Instance.Me;
        var fromId = m.TryGetProperty("fromId", out var f) ? f.GetInt32() : 0;
        var isMine = me != null && fromId == me.Id;
        var msgId = m.TryGetProperty("id", out var i) ? i.GetInt64() : 0;
        var status = m.TryGetProperty("status", out var s) ? s.GetString() ?? "normal" : "normal";
        var isDeleted = m.TryGetProperty("deleted", out var d) && d.GetInt32() == 1;
        var content = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var fileId = m.TryGetProperty("fileId", out var fid) ? fid.GetInt64() : 0;
        var replyTo = m.TryGetProperty("replyTo", out var rt) ? rt.GetInt32() : 0;
        var replyContent = m.TryGetProperty("replyContent", out var rc) ? rc.GetString() ?? "" : "";
        var fromName = m.TryGetProperty("fromName", out var fn) ? fn.GetString() ?? "" : "";
        var ts = m.TryGetProperty("ts", out var t) ? t.GetString() ?? "" : "";
        var readAt = m.TryGetProperty("readAt", out var ra) ? ra.GetString() ?? "" : "";
        var clientId = m.TryGetProperty("clientId", out var cid) ? cid.GetString() ?? "" : "";

        var sendAt = DateTime.Now;
        if (DateTime.TryParse(ts, out var dt)) sendAt = dt;

        return new ChatItem
        {
            MsgId = msgId,
            ClientId = clientId,
            Type = _nav?.Mode ?? "private",
            PeerId = _nav?.Id ?? 0,
            IsMine = isMine,
            FromName = string.IsNullOrEmpty(fromName)
                ? (isMine ? (me?.Username ?? "我") : $"用户{fromId}")
                : fromName,
            ShowSender = !isMine && _nav?.Mode == "group",
            Content = content,
            FileId = fileId,
            FileLabel = fileId > 0 ? "📎 文件，点击下载" : "",
            TimeText = sendAt.ToString("HH:mm"),
            SendAt = sendAt,
            ReplyTo = replyTo,
            ReplyContent = replyContent,
            Read = isMine && !string.IsNullOrEmpty(readAt),
            Status = isDeleted ? "deleted" : (status == "recalled" ? "recalled" : "normal")
        };
    }

    // 消息列表上滑到顶：加载更早一页（游标分页）
    private void MsgList_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroll == null || _nav == null) return;
        if (_scroll.VerticalOffset <= 2 && !_loadingOlder && _hasMore)
            _ = LoadHistoryAsync(initial: false);
    }

    // ==================== 已读 / 未读 ====================

    private async Task ClearUnreadAsync()
    {
        if (_nav == null) return;
        try
        {
            if (_nav.Mode == "private") App.Socket.SendRead(_nav.Id);
            await _api.PostAsync("/messages/read", new { type = _nav.Mode, peerId = _nav.Id });
        }
        catch { }
    }

    private void Socket_PrivateRead(int fromId, int toId)
    {
        if (_nav?.Mode != "private" || fromId != _nav.Id) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var m in Messages)
            {
                if (m.IsMine && m.Status == "normal") m.Read = true;
            }
        });
    }

    // ==================== 撤回 / 删除 ====================

    private void Socket_MessageRecalled(int type, int peerId, int msgId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var m = Messages.FirstOrDefault(x => x.MsgId == msgId);
            if (m != null) m.Status = "recalled";
        });
    }

    private void Socket_MessageDeleted(int type, int peerId, int msgId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var m = Messages.FirstOrDefault(x => x.MsgId == msgId);
            if (m != null) m.Status = "deleted";
        });
    }

    private void MsgMenu_Reply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem mi && mi.Tag is ChatItem item && item.Status == "normal")
        {
            _replyTo = item.MsgId;
            _replyHint = item.Content.Length > 40 ? item.Content.Substring(0, 40) + "…" : item.Content;
            if (string.IsNullOrEmpty(_replyHint)) _replyHint = item.FileLabel;
            ReplyHintText.Text = $"回复：{_replyHint}";
            ReplyHintBar.Visibility = Visibility.Visible;
            InputBox.Focus(FocusState.Programmatic);
        }
    }

    private async void MsgMenu_Recall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem mi || mi.Tag is not ChatItem item) return;
        if (item.MsgId <= 0)
        {
            await ShowDialogAsync("撤回失败", "消息尚未落库，请稍后重试");
            return;
        }
        if (DateTime.Now - item.SendAt > TimeSpan.FromMinutes(2))
        {
            await ShowDialogAsync("撤回失败", "超过 2 分钟，无法撤回");
            return;
        }
        try
        {
            await _api.PostAsync("/messages/recall", new { id = item.MsgId });
            item.Status = "recalled";
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("撤回失败", ex.Message);
        }
    }

    private async void MsgMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem mi || mi.Tag is not ChatItem item) return;
        if (item.MsgId <= 0)
        {
            await ShowDialogAsync("删除失败", "消息尚未落库，请稍后重试");
            return;
        }
        try
        {
            await _api.PostAsync("/messages/delete", new { id = item.MsgId });
            item.Status = "deleted";
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("删除失败", ex.Message);
        }
    }

    private void CancelReply_Click(object sender, RoutedEventArgs e)
    {
        _replyTo = 0;
        _replyHint = "";
        ReplyHintBar.Visibility = Visibility.Collapsed;
    }

    // ==================== 表情 / 贴纸 ====================

    private void EmojiItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem mi && mi.Tag is string emoji)
        {
            var pos = InputBox.SelectionStart;
            var text = InputBox.Text;
            InputBox.Text = text.Substring(0, pos) + emoji + text.Substring(pos);
            InputBox.SelectionStart = pos + emoji.Length;
            InputBox.Focus(FocusState.Programmatic);
            EmojiFlyout.Hide();
        }
    }

    // ==================== 输入中状态（debounce 800ms） ====================

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text;
        if (_nav == null) return;

        // 群聊输入 @ 弹出成员选择
        if (_nav.Mode == "group" && text.EndsWith("@") && !_mentionMenuOpen)
        {
            _ = ShowMentionPickerAsync();
        }

        if (_nav.Mode != "private") return;
        if (string.IsNullOrEmpty(text.Trim()))
        {
            _typingTimer?.Stop();
            return;
        }
        if (_typingTimer == null)
        {
            _typingTimer = DispatcherQueue.CreateTimer();
            _typingTimer.Interval = TimeSpan.FromMilliseconds(800);
            _typingTimer.IsRepeating = false;
            _typingTimer.Tick += (_, _) =>
            {
                try { App.Socket.SendTyping(_nav!.Id); } catch { }
            };
        }
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private async Task ShowMentionPickerAsync()
    {
        _mentionMenuOpen = true;
        try
        {
            if (_nav?.Mode != "group") return;
            if (Members.Count == 0) await LoadMembersAsync();
            var me = SettingsService.Instance.Me;
            var list = new ListView
            {
                MaxHeight = 320,
                SelectionMode = ListViewSelectionMode.Single,
                ItemsSource = Members.Where(x => me == null || x.Id != me.Id).ToList()
            };
            list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<TextBlock Text=\"{Binding Username}\" FontSize=\"15\" Padding=\"0,6\"/></DataTemplate>");
            var dialog = new ContentDialog
            {
                Title = "选择要 @ 的成员",
                Content = list,
                PrimaryButtonText = "插入",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && list.SelectedItem is GroupMemberItem m)
            {
                var text = InputBox.Text;
                var idx = text.LastIndexOf('@');
                if (idx >= 0)
                {
                    InputBox.Text = text.Substring(0, idx) + "@" + m.Username + " ";
                    InputBox.SelectionStart = InputBox.Text.Length;
                    InputBox.Focus(FocusState.Programmatic);
                }
            }
        }
        finally
        {
            _mentionMenuOpen = false;
        }
    }

    private void Socket_PrivateTyping(int fromId)
    {
        if (_nav?.Mode != "private" || fromId != _nav.Id) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            TypingText.Visibility = Visibility.Visible;
            if (_typingClearTimer == null)
            {
                _typingClearTimer = DispatcherQueue.CreateTimer();
                _typingClearTimer.Interval = TimeSpan.FromSeconds(3);
                _typingClearTimer.IsRepeating = false;
                _typingClearTimer.Tick += (_, _) => TypingText.Visibility = Visibility.Collapsed;
            }
            _typingClearTimer.Stop();
            _typingClearTimer.Start();
        });
    }

    // ==================== @ 提醒 ====================

    private void Socket_GroupMention(int groupId, int fromId, string fromName, string content)
    {
        // 当前正在该群会话：气泡已带 @我 徽标，不打扰
        if (_nav != null && _nav.Mode == "group" && _nav.Id == groupId) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            var preview = content.Length > 50 ? content.Substring(0, 50) + "…" : content;
            var dialog = new ContentDialog
            {
                Title = "有人 @ 你",
                Content = $"{fromName} 在群聊中提到了你：\n{preview}",
                PrimaryButtonText = "去看看",
                CloseButtonText = "稍后",
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                MainWindow.Current?.Navigate(typeof(ChatPage),
                    new ChatNavParams { Mode = "group", Id = groupId, Name = $"群聊 {groupId}" });
            }
        });
    }

    // ==================== 群公告 ====================

    private async Task LoadAnnouncementAsync()
    {
        if (_nav == null || _nav.Mode != "group") return;
        try
        {
            var res = await _api.GetAsync($"/groups/{_nav.Id}/announcement");
            var has = res.TryGetProperty("announcement", out var a)
                && a.ValueKind == System.Text.Json.JsonValueKind.Object;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (has)
                {
                    var content = a.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    AnnouncementText.Text = "📢 " + content;
                    AnnouncementBar.Visibility = Visibility.Visible;
                    _currentAnnouncement = content;
                }
                else
                {
                    AnnouncementBar.Visibility = Visibility.Collapsed;
                    _currentAnnouncement = "";
                }
            });
        }
        catch { }
    }

    private void Socket_GroupAnnouncementUpdated(int groupId)
    {
        if (_nav?.Mode == "group" && groupId == _nav.Id)
            DispatcherQueue.TryEnqueue(() => _ = LoadAnnouncementAsync());
    }

    private async void AnnouncementManage_Click(object sender, RoutedEventArgs e)
    {
        if (_nav?.Mode != "group") return;
        var me = SettingsService.Instance.Me;
        var isOwner = me != null && Members.Any(x => x.Id == me.Id && x.Role == "owner");
        var box = new TextBox
        {
            Text = _currentAnnouncement,
            PlaceholderText = "输入公告内容（1-2000 字）",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            MaxLength = 2000
        };
        if (!isOwner)
        {
            var view = new ContentDialog
            {
                Title = "群公告",
                Content = box,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            await view.ShowAsync();
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "编辑群公告",
            Content = box,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var content = box.Text.Trim();
        if (string.IsNullOrEmpty(content)) return;
        try
        {
            await _api.PostAsync($"/groups/{_nav.Id}/announcement", new { content });
            await LoadAnnouncementAsync();
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("发布失败", ex.Message);
        }
    }

    // ==================== 禁言 / 踢人 ====================

    private async void MemberMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem mi || mi.Tag is not GroupMemberItem m || _nav?.Mode != "group") return;
        var box = new NumberBox
        {
            Header = "禁言时长（分钟，1-1440）",
            Minimum = 1,
            Maximum = 1440,
            Value = 10,
            Width = 220
        };
        var dialog = new ContentDialog
        {
            Title = $"禁言 {m.Username}",
            Content = box,
            PrimaryButtonText = "禁言",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await _api.PostAsync($"/groups/{_nav.Id}/mute", new { userId = m.Id, durationMinutes = (int)box.Value });
            await LoadMembersAsync();
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("操作失败", ex.Message);
        }
    }

    private async void MemberUnmute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem mi || mi.Tag is not GroupMemberItem m || _nav?.Mode != "group") return;
        try
        {
            await _api.PostAsync($"/groups/{_nav.Id}/unmute", new { userId = m.Id });
            await LoadMembersAsync();
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("操作失败", ex.Message);
        }
    }

    private async void MemberKick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem mi || mi.Tag is not GroupMemberItem m || _nav?.Mode != "group") return;
        var confirm = new ContentDialog
        {
            Title = "移出群聊",
            Content = $"确定将「{m.Username}」移出本群吗？",
            PrimaryButtonText = "移出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await _api.PostAsync($"/groups/{_nav.Id}/kick", new { userId = m.Id });
            await LoadMembersAsync();
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("操作失败", ex.Message);
        }
    }

    private void GroupManage_Click(object sender, RoutedEventArgs e)
    {
        if (_nav?.Mode != "group" || sender is not FrameworkElement fe) return;
        var gid = _nav.Id;
        var flyout = new MenuFlyout();
        var muteAll = new MenuFlyoutItem { Text = "全体禁言…" };
        muteAll.Click += async (_, _) => await MuteAllAsync(gid);
        var unmuteAll = new MenuFlyoutItem { Text = "解除全体禁言" };
        unmuteAll.Click += async (_, _) =>
        {
            try { await _api.PostAsync($"/groups/{gid}/unmute-all"); }
            catch (ApiException ex) { await ShowDialogAsync("操作失败", ex.Message); }
        };
        flyout.Items.Add(muteAll);
        flyout.Items.Add(unmuteAll);
        flyout.ShowAt(fe);
    }

    private async Task MuteAllAsync(int gid)
    {
        var box = new NumberBox
        {
            Header = "时长（分钟，留 0 表示持续到手动解除）",
            Minimum = 0,
            Maximum = 1440,
            Value = 10,
            Width = 240
        };
        var dialog = new ContentDialog
        {
            Title = "全体禁言",
            Content = box,
            PrimaryButtonText = "禁言",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await _api.PostAsync($"/groups/{gid}/mute-all", new { durationMinutes = (int)box.Value });
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("操作失败", ex.Message);
        }
    }

    // ==================== 群文件 ====================

    private async void GroupFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_nav?.Mode != "group") return;
        var gid = _nav.Id;
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 360
        };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<StackPanel Padding=\"0,6\" Spacing=\"2\">" +
            "<StackPanel Orientation=\"Horizontal\" Spacing=\"10\">" +
            "<TextBlock Text=\"{Binding Filename}\" FontSize=\"14\" MaxWidth=\"200\" TextTrimming=\"CharacterEllipsis\"/>" +
            "<TextBlock Text=\"{Binding SizeText}\" FontSize=\"12\" Foreground=\"Gray\" VerticalAlignment=\"Center\"/>" +
            "</StackPanel>" +
            "<TextBlock FontSize=\"11\" Foreground=\"Gray\"><Run Text=\"{Binding UploaderName}\"/><Run Text=\" · \"/><Run Text=\"{Binding CreatedAt}\"/></TextBlock>" +
            "</StackPanel></DataTemplate>");
        list.ItemClick += async (s, args) =>
        {
            if (args.ClickedItem is GroupFileRow f) await DownloadGroupFileAsync(f);
        };

        var uploadBtn = new Button
        {
            Content = "上传文件",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8)
        };
        uploadBtn.Click += async (_, _) => await UploadGroupFileAsync(gid, list);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(uploadBtn);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = $"群文件（{_nav.Name}）",
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot
        };
        await RefreshGroupFilesAsync(gid, list);
        await dialog.ShowAsync();
    }

    private async Task RefreshGroupFilesAsync(int gid, ListView list)
    {
        try
        {
            var res = await _api.GetAsync($"/groups/{gid}/files");
            list.ItemsSource = res.GetProperty("files").EnumerateArray().Select(f => new GroupFileRow
            {
                GroupFileId = f.GetProperty("group_file_id").GetInt32(),
                FileId = f.GetProperty("file_id").GetInt64(),
                Filename = f.GetProperty("filename").GetString() ?? "",
                SizeText = FormatSize(f.GetProperty("size").GetInt64()),
                UploaderName = f.GetProperty("uploader_name").GetString() ?? "",
                CreatedAt = f.GetProperty("created_at").GetString() ?? ""
            }).ToList();
        }
        catch { }
    }

    private async Task UploadGroupFileAsync(int gid, ListView list)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try
        {
            var fileId = await _files.UploadAsync(file.Path, new Progress<(long, long)>());
            await _api.PostAsync($"/groups/{gid}/files", new { fileId });
            await RefreshGroupFilesAsync(gid, list);
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("上传失败", ex.Message);
        }
    }

    private async Task DownloadGroupFileAsync(GroupFileRow f)
    {
        try
        {
            var info = await _api.GetAsync($"/files/{f.FileId}/info");
            var file = info.GetProperty("file");
            var filename = file.TryGetProperty("filename", out var fn) ? fn.GetString() ?? f.Filename : f.Filename;
            var (path, _) = await _files.DownloadAsync(f.FileId, filename);
            await ShowDialogAsync("下载完成", $"已保存到：\n{path}");
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("下载失败", ex.Message);
        }
    }

    private void Socket_GroupFileUpdated(int groupId)
    {
        // 群文件列表为打开时实时拉取，此处静默；如需提示可在此弹窗
    }

    // ==================== 被移出群聊 / Socket 错误 ====================

    private void Socket_GroupKicked(int groupId, string name)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = "已被移出群聊",
                Content = $"你已被移出群聊「{name}」",
                CloseButtonText = "好的",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            if (_nav != null && _nav.Mode == "group" && _nav.Id == groupId)
                MainWindow.Current?.Navigate(typeof(GroupsPage));
        });
    }

    private void Socket_SocketError(string message)
    {
        DispatcherQueue.TryEnqueue(async () => await ShowDialogAsync("提示", message));
    }

    // ==================== 断线重连补拉 ====================

    private void Socket_Reconnected()
    {
        DispatcherQueue.TryEnqueue(() => _ = LoadSinceAsync());
    }

    private async Task LoadSinceAsync()
    {
        if (_nav == null) return;
        try
        {
            var url = $"/messages/since?type={_nav.Mode}&peerId={_nav.Id}&afterId={_lastMsgId}";
            var res = await _api.GetAsync(url);
            var arr = res.GetProperty("messages").EnumerateArray().ToList();
            if (arr.Count == 0) return;
            var parsed = arr.Select(x => ParseHistoryItem(x))
                .Where(x => x.MsgId > 0 && !Messages.Any(m => m.MsgId == x.MsgId)).ToList();
            foreach (var m in parsed)
            {
                Messages.Add(m);
                if (m.MsgId > _lastMsgId) _lastMsgId = m.MsgId;
            }
            if (parsed.Count > 0) MsgList.ScrollIntoView(Messages[Messages.Count - 1]);
            await ClearUnreadAsync();
        }
        catch { }
    }

    // ==================== 落库回执 ack ====================

    private void Socket_MessageAcked(string clientId, long msgId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var m = Messages.FirstOrDefault(x => x.ClientId == clientId);
            if (m != null) m.MsgId = msgId;
            if (msgId > _lastMsgId) _lastMsgId = msgId;
        });
    }

    // ==================== 消息搜索 ====================

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_nav == null) return;
        var box = new TextBox { PlaceholderText = "输入关键词，搜索本会话聊天记录", MinWidth = 260 };
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 300,
            Visibility = Visibility.Collapsed
        };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<StackPanel Padding=\"0,4\" Spacing=\"2\">" +
            "<TextBlock Text=\"{Binding FromName}\" FontSize=\"12\" Foreground=\"Gray\"/>" +
            "<TextBlock Text=\"{Binding Content}\" FontSize=\"14\" TextTrimming=\"CharacterEllipsis\"/>" +
            "</StackPanel></DataTemplate>");
        ContentDialog dialog = null!;
        list.ItemClick += async (s, args) =>
        {
            if (args.ClickedItem is ChatItem hit)
            {
                dialog?.Hide();
                await LocateMessageAsync(hit.MsgId);
            }
        };
        box.KeyDown += async (_, k) =>
        {
            if (k.Key == Windows.System.VirtualKey.Enter) await RunSearchAsync(box, list);
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(list);
        dialog = new ContentDialog
        {
            Title = "搜索聊天记录",
            Content = panel,
            PrimaryButtonText = "搜索",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };
        dialog.PrimaryButtonClick += async (_, _) => await RunSearchAsync(box, list);
        await dialog.ShowAsync();
    }

    private async Task RunSearchAsync(TextBox box, ListView list)
    {
        if (_nav == null) return;
        var q = box.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;
        try
        {
            var url = $"/messages/search?q={Uri.EscapeDataString(q)}&type={_nav.Mode}&peerId={_nav.Id}";
            var res = await _api.GetAsync(url);
            var items = res.GetProperty("messages").EnumerateArray()
                .Select(x => ParseHistoryItem(x))
                .Where(x => x.Status == "normal")
                .ToList();
            list.ItemsSource = items;
            list.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) await ShowDialogAsync("搜索结果", "未找到包含该关键词的消息");
        }
        catch (ApiException ex)
        {
            await ShowDialogAsync("搜索失败", ex.Message);
        }
    }

    // 跳转定位：已加载直接滚动；更早的历史向前翻页加载直到命中
    private async Task LocateMessageAsync(long msgId)
    {
        var existing = Messages.FirstOrDefault(x => x.MsgId == msgId);
        if (existing != null)
        {
            MsgList.ScrollIntoView(existing);
            return;
        }
        int guard = 0;
        while (_hasMore && guard < 20)
        {
            guard++;
            var beforeId = Messages.Count > 0 ? Messages[0].MsgId : 0;
            var url = $"/messages/history?type={_nav!.Mode}&peerId={_nav.Id}&limit=50";
            if (beforeId > 0) url += $"&beforeId={beforeId}";
            try
            {
                var res = await _api.GetAsync(url);
                var parsed = res.GetProperty("messages").EnumerateArray()
                    .Select(x => ParseHistoryItem(x)).ToList();
                for (int i = parsed.Count - 1; i >= 0; i--) Messages.Insert(0, parsed[i]);
                if (parsed.Count == 0) { _hasMore = false; break; }
                if (parsed.Count < 50) _hasMore = false;
                var hit = Messages.FirstOrDefault(x => x.MsgId == msgId);
                if (hit != null)
                {
                    MsgList.ScrollIntoView(hit);
                    return;
                }
            }
            catch { break; }
        }
        await ShowDialogAsync("定位失败", "未能在历史消息中找到该消息");
    }

    private sealed class GroupFileRow
    {
        public int GroupFileId { get; set; }
        public long FileId { get; set; }
        public string Filename { get; set; } = "";
        public string SizeText { get; set; } = "";
        public string UploaderName { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }
}
