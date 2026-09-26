using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ChatRoomReset.Models;
using SocketIOClient;

namespace ChatRoomReset.Services;

/// <summary>Socket.IO 实时通信：私聊/群聊消息、好友申请、在线状态。</summary>
public class SocketService : IDisposable
{
    private SocketIOClient.SocketIO? _client;
    public bool Connected => _client?.Connected ?? false;

    public event Action<int, string, long, long, int>? PrivateMessageReceived;   // fromId, content, fileId, msgId, replyTo
    public event Action<int, int, string, long, long, int, string>? GroupMessageReceived; // groupId, fromId, content, fileId, msgId, replyTo, fromName
    public event Action<int, string, string>? FriendRequestReceived;   // fromId, username, avatar
    public event Action<int, bool>? FriendOnlineChanged;               // userId, online
    public event Action<int>? GroupMembersUpdated;                     // groupId（进群/退群/在线变化时触发，前端重拉成员列表）
    public event Action<string, long>? MessageAcked;                   // clientId, msgId（自己发送的消息已落库，回传服务端 id）
    // —— 全量升级新增事件 ——
    public event Action<int, int, int>? MessageRecalled;               // type(0私聊/1群聊), peerId, msgId
    public event Action<int, int, int>? MessageDeleted;                // type(0私聊/1群聊), peerId, msgId
    public event Action<int, int>? PrivateRead;                        // fromId(读方), toId（我的消息被读）
    public event Action<int>? PrivateTyping;                           // fromId（对方正在输入）
    public event Action<int, int, string, string>? GroupMention;       // groupId, fromId, fromName, content
    public event Action<int>? GroupAnnouncementUpdated;                // groupId
    public event Action<int>? GroupFileUpdated;                        // groupId
    public event Action<int, string>? GroupKicked;                     // groupId, name
    public event Action<string>? SocketError;                          // 错误消息（禁言/未登录等）
    public event Action? Reconnected;                                  // 断线自动重连成功（前端补拉漏掉消息）

    public async Task ConnectAsync(string serverBase, string token)
    {
        Disconnect();

        _client = new SocketIOClient.SocketIO(serverBase.TrimEnd('/'), new SocketIOOptions
        {
            Auth = new Dictionary<string, string> { ["token"] = token },
            Reconnection = true,
            ReconnectionDelay = 1000,          // 指数退避起始 1s
            ReconnectionDelayMax = 15000,      // 上限 15s
            ReconnectionAttempts = int.MaxValue, // 无限重试
            RandomizationFactor = 0.5,
            EIO = SocketIO.Core.EngineIO.V4
        });

        // 重连成功后通知前端补拉断线期间漏掉的消息
        _client.OnReconnected += (_, _) => Reconnected?.Invoke();

        _client.On("private:message", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("from", out var f) && el.TryGetProperty("content", out var c))
            {
                var fileId = el.TryGetProperty("fileId", out var fid) ? fid.GetInt64() : 0;
                var msgId = el.TryGetProperty("id", out var mid) ? mid.GetInt64() : 0;
                var replyTo = el.TryGetProperty("replyTo", out var rt) ? rt.GetInt32() : 0;
                PrivateMessageReceived?.Invoke(f.GetInt32(), c.GetString() ?? "", fileId, msgId, replyTo);
            }
        });

        _client.On("group:message", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g) && el.TryGetProperty("from", out var f) && el.TryGetProperty("content", out var c))
            {
                var fileId = el.TryGetProperty("fileId", out var fid) ? fid.GetInt64() : 0;
                var msgId = el.TryGetProperty("id", out var mid) ? mid.GetInt64() : 0;
                var replyTo = el.TryGetProperty("replyTo", out var rt) ? rt.GetInt32() : 0;
                var fromName = el.TryGetProperty("fromName", out var n) ? n.GetString() ?? "" : "";
                GroupMessageReceived?.Invoke(g.GetInt32(), f.GetInt32(), c.GetString() ?? "", fileId, msgId, replyTo, fromName);
            }
        });

        // 自己发送的消息落库回执：clientId -> 服务端 msgId（用于撤回/删除/已读定位）
        _client.On("private:message:ack", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("clientId", out var cid) && el.TryGetProperty("id", out var mid))
                MessageAcked?.Invoke(cid.GetString() ?? "", mid.GetInt64());
        });
        _client.On("group:message:ack", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("clientId", out var cid) && el.TryGetProperty("id", out var mid))
                MessageAcked?.Invoke(cid.GetString() ?? "", mid.GetInt64());
        });

        // —— 全量升级新增事件处理 ——

        // 撤回：双方显示"撤回了一条消息"
        _client.On("message:recalled", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("id", out var id))
            {
                var isGroup = el.TryGetProperty("type", out var tp) && tp.GetString() == "group";
                var peerId = isGroup
                    ? (el.TryGetProperty("groupId", out var g) ? g.GetInt32() : 0)
                    : 0;
                if (isGroup) MessageRecalled?.Invoke(1, peerId, id.GetInt32());
                else
                {
                    // 私聊：peerId 从本地会话上下文判断，页面通过 msgId 定位
                    MessageRecalled?.Invoke(0, 0, id.GetInt32());
                }
            }
        });

        // 删除：服务端标记后广播（双方可见占位/消失由服务端历史接口控制）
        _client.On("message:deleted", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("id", out var id))
            {
                var isGroup = el.TryGetProperty("type", out var tp) && tp.GetString() == "group";
                var peerId = isGroup
                    ? (el.TryGetProperty("groupId", out var g) ? g.GetInt32() : 0)
                    : 0;
                if (isGroup) MessageDeleted?.Invoke(1, peerId, id.GetInt32());
                else MessageDeleted?.Invoke(0, 0, id.GetInt32());
            }
        });

        // 私聊已读回执
        _client.On("private:read", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("from", out var f))
                PrivateRead?.Invoke(f.GetInt32(), el.TryGetProperty("to", out var t) ? t.GetInt32() : 0);
        });

        // 私聊输入中状态
        _client.On("private:typing", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("from", out var f))
                PrivateTyping?.Invoke(f.GetInt32());
        });

        // 群聊 @ 提醒
        _client.On("group:mention", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g) && el.TryGetProperty("from", out var f))
            {
                var name = el.TryGetProperty("fromName", out var n) ? n.GetString() ?? "" : "";
                var content = el.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                GroupMention?.Invoke(g.GetInt32(), f.GetInt32(), name, content);
            }
        });

        // 群公告更新
        _client.On("group:announcement-updated", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g))
                GroupAnnouncementUpdated?.Invoke(g.GetInt32());
        });

        // 群文件更新
        _client.On("group:file-updated", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g))
                GroupFileUpdated?.Invoke(g.GetInt32());
        });

        // 被移出群聊
        _client.On("group:kicked", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g))
            {
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                GroupKicked?.Invoke(g.GetInt32(), name);
            }
        });

        // 错误（禁言拒绝 / 好友校验失败等）
        _client.On("group:error", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("message", out var m))
                SocketError?.Invoke(m.GetString() ?? "");
        });
        _client.On("private:error", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("message", out var m))
                SocketError?.Invoke(m.GetString() ?? "");
        });

        _client.On("friend:request", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("from", out var f))
            {
                var name = el.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                var avatar = el.TryGetProperty("avatar", out var a) ? a.GetString() ?? "" : "";
                FriendRequestReceived?.Invoke(f.GetInt32(), name, avatar);
            }
        });

        _client.On("friend:online", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("userId", out var uid))
                FriendOnlineChanged?.Invoke(uid.GetInt32(), el.TryGetProperty("online", out var o) && o.GetBoolean());
        });

        _client.On("group:members-updated", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g))
                GroupMembersUpdated?.Invoke(g.GetInt32());
        });

        await _client.ConnectAsync();
    }

    public void SendPrivate(int toId, string content, long fileId = 0, int replyTo = 0, string clientId = "")
    {
        _client?.EmitAsync("private:message", new { to = toId, content, fileId, replyTo, clientId }).GetAwaiter().GetResult();
    }

    public void SendGroup(int groupId, string content, long fileId = 0, int replyTo = 0, string clientId = "")
    {
        _client?.EmitAsync("group:message", new { groupId, content, fileId, replyTo, clientId }).GetAwaiter().GetResult();
    }

    /// <summary>发送私聊已读回执（打开会话时调用）。</summary>
    public void SendRead(int toId)
    {
        _client?.EmitAsync("private:read", new { to = toId }).GetAwaiter().GetResult();
    }

    /// <summary>发送私聊输入中状态（debounce 由调用方控制）。</summary>
    public void SendTyping(int toId)
    {
        _client?.EmitAsync("private:typing", new { to = toId }).GetAwaiter().GetResult();
    }

    public void JoinGroup(int groupId)
    {
        _client?.EmitAsync("group:join", new { groupId }).GetAwaiter().GetResult();
    }

    public void NotifyFriendRequest(int toId)
    {
        _client?.EmitAsync("friend:request", new { to = toId }).GetAwaiter().GetResult();
    }

    public void Disconnect()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    public void Dispose() => Disconnect();
}
