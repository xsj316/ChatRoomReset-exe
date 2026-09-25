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
