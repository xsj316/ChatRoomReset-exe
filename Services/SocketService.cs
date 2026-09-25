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

    public event Action<int, string, long>? PrivateMessageReceived;   // fromId, content, fileId
    public event Action<int, int, string, long>? GroupMessageReceived; // groupId, fromId, content, fileId
    public event Action<int, string, string>? FriendRequestReceived;   // fromId, username, avatar
    public event Action<int, bool>? FriendOnlineChanged;               // userId, online
    public event Action<int>? GroupMembersUpdated;                     // groupId（进群/退群/在线变化时触发，前端重拉成员列表）

    public async Task ConnectAsync(string serverBase, string token)
    {
        Disconnect();

        _client = new SocketIOClient.SocketIO(serverBase.TrimEnd('/'), new SocketIOOptions
        {
            Auth = new Dictionary<string, string> { ["token"] = token },
            Reconnection = true,
            EIO = SocketIO.Core.EngineIO.V4
        });

        _client.On("private:message", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("from", out var f) && el.TryGetProperty("content", out var c))
            {
                var fileId = el.TryGetProperty("fileId", out var fid) ? fid.GetInt64() : 0;
                PrivateMessageReceived?.Invoke(f.GetInt32(), c.GetString() ?? "", fileId);
            }
        });

        _client.On("group:message", (resp) =>
        {
            var el = resp.GetValue<JsonElement>();
            if (el.TryGetProperty("groupId", out var g) && el.TryGetProperty("from", out var f) && el.TryGetProperty("content", out var c))
            {
                var fileId = el.TryGetProperty("fileId", out var fid) ? fid.GetInt64() : 0;
                GroupMessageReceived?.Invoke(g.GetInt32(), f.GetInt32(), c.GetString() ?? "", fileId);
            }
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

    public void SendPrivate(int toId, string content, long fileId = 0)
    {
        _client?.EmitAsync("private:message", new { to = toId, content, fileId }).GetAwaiter().GetResult();
    }

    public void SendGroup(int groupId, string content, long fileId = 0)
    {
        _client?.EmitAsync("group:message", new { groupId, content, fileId }).GetAwaiter().GetResult();
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
