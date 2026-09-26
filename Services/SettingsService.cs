using System;
using System.IO;
using System.Text.Json;
using ChatRoomReset.Models;

namespace ChatRoomReset.Services;

/// <summary>本地配置：服务器地址、主题、登录态、用户信息、聊天背景。</summary>
public class SettingsService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatRoomReset");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    public string ServerBase { get; set; } = "http://localhost:3000";
    public string Theme { get; set; } = "System";  // Light / Dark / System（跟随系统）
    public bool AutoUpdate { get; set; } = true;   // 启动时静默检查新版本
    public string? Token { get; set; }
    public UserInfo? Me { get; set; }

    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= Load();

    public string ApiBase => ServerBase.TrimEnd('/') + "/api";

    public static SettingsService Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var s = JsonSerializer.Deserialize<SettingsService>(json);
                if (s != null) return s;
            }
        }
        catch { /* 配置损坏则回退默认 */ }
        return new SettingsService();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 忽略写入失败 */ }
    }

    public void ClearSession()
    {
        Token = null;
        Me = null;
        Save();
    }
}
