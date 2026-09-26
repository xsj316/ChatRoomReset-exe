using System;

namespace ChatRoomReset.Models;

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public bool EmailVerified { get; set; }
    public string Avatar { get; set; } = "";
    public string Background { get; set; } = "";
    public int Points { get; set; }
    public string Role { get; set; } = "user";
}

public class FriendInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Background { get; set; } = "";
    public bool Online { get; set; }
}

public class GroupInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
}

public class GroupMember
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Role { get; set; } = "member";   // owner / member
    public bool Online { get; set; }
}

public class AdminInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Avatar { get; set; } = "";
}

public class ChatMessage
{
    public int Id { get; set; }
    public string Type { get; set; } = "private";   // private / group
    public int FromId { get; set; }
    public string FromName { get; set; } = "";
    public int ToId { get; set; }
    public string Content { get; set; } = "";
    public long FileId { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public bool IsMine { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    // —— 全量升级新增字段 ——
    public string Status { get; set; } = "normal";   // normal / recalled / deleted（双方删）
    public string? RecalledAt { get; set; }
    public bool Deleted { get; set; }                // 删除者视角占位
    public string? DeletedAt { get; set; }
    public int ReplyTo { get; set; }                 // 引用回复的消息 id
    public string ReplyContent { get; set; } = "";   // 被引用消息摘要
    public string MentionsJson { get; set; } = "";   // @ 提及成员 id 列表 JSON
    public string? ReadAt { get; set; }              // 私聊已读时间
    public string ClientId { get; set; } = "";
    public string Ts { get; set; } = "";             // 服务端时间
}

/// <summary>未读汇总项（私聊按好友、群聊按群）。</summary>
public class UnreadInfo
{
    public int PeerId { get; set; }
    public string PeerName { get; set; } = "";
    public int Unread { get; set; }
}

/// <summary>群公告。</summary>
public class GroupAnnouncementInfo
{
    public int Id { get; set; }
    public string Content { get; set; } = "";
    public string PublisherName { get; set; } = "";
    public int Pinned { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>群共享文件项。</summary>
public class GroupFileItem
{
    public int GroupFileId { get; set; }
    public int GroupId { get; set; }
    public long FileId { get; set; }
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public string UploaderName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

/// <summary>GitHub Release 信息（自动更新检查）。</summary>
public class UpdateInfo
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string PublishedAt { get; set; } = "";
}

public class FileMeta
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public class TransactionItem
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Amount { get; set; }
    public string Note { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
