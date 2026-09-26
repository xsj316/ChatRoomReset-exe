using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ChatRoomReset.Models;

namespace ChatRoomReset.Services;

/// <summary>
/// 自动更新：启动时请求 GitHub API 检查 ChatRoomReset-exe 仓库最新 Release。
/// 仅当远端版本高于本地版本时返回 UpdateInfo；发现新版本时由调用方弹窗提示并打开下载页。
/// 任何失败（网络/解析/版本相同或更低）静默返回 null，不阻塞启动。
/// </summary>
public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/xsj316/ChatRoomReset-exe/releases/latest";
    private const string FallbackVersion = "1.0.1";
    private static readonly HttpClient Http = new();

    static UpdateService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ChatRoomReset-Client");
        Http.Timeout = TimeSpan.FromSeconds(5);
    }

    /// <summary>本地版本号（优先读程序集版本，失败回退常量），格式为 X.Y.Z。</summary>
    public static string LocalVersion
    {
        get
        {
            try
            {
                var asm = typeof(UpdateService).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    var plus = info.IndexOf('+');
                    if (plus >= 0) info = info[..plus];
                    if (TryParseVersion(info, out _)) return info.Trim().TrimStart('v', 'V');
                }
                var ver = asm.GetName().Version;
                if (ver != null && ver.Major >= 0 && TryParseVersion(ver.ToString(3), out _)) return ver.ToString(3);
            }
            catch
            {
                // 读取失败回退常量
            }
            return FallbackVersion;
        }
    }

    /// <summary>检查最新 Release；仅当远端版本高于本地版本时返回有效 UpdateInfo，否则返回 null（静默）。</summary>
    public static async Task<UpdateInfo?> CheckLatestAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ApiUrl);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            if (!TryParseVersion(tag, out var remoteVersion)) return null;

            // 仅当远端版本严格高于本地版本时才提示更新
            if (!TryParseVersion(LocalVersion, out var localVersion)) return null;
            if (remoteVersion <= localVersion) return null;

            return new UpdateInfo
            {
                TagName = tag,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                HtmlUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "",
                PublishedAt = root.TryGetProperty("published_at", out var pub) ? pub.GetString() ?? "" : ""
            };
        }
        catch
        {
            return null; // 静默失败，不阻塞启动
        }
    }

    /// <summary>解析版本号：容忍 v/V/b/B 前缀、-beta.x 后缀、+metadata 后缀。</summary>
    private static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim().TrimStart('v', 'V', 'b', 'B');
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        if (!Version.TryParse(s, out var parsed)) return false;
        version = parsed;
        return true;
    }
}
