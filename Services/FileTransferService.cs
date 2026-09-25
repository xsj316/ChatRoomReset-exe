using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatRoomReset.Services;

/// <summary>
/// 无限大附件传输：
/// 上传 = 按 1MB 分片 base64 POST → complete 合并校验 SHA-256；
/// 下载 = 点击消息中的文件 → 从后端拉取保存到本地。
/// </summary>
public class FileTransferService
{
    private const int CHUNK_SIZE = 1024 * 1024; // 1MB
    private readonly ApiService _api = new();

    public async Task<long> UploadAsync(string filePath, IProgress<(long done, long total)>? progress = null)
    {
        var fi = new FileInfo(filePath);
        var totalChunks = (int)Math.Ceiling(fi.Length / (double)CHUNK_SIZE);
        if (totalChunks == 0) totalChunks = 1;

        var fileId = "c-" + Guid.NewGuid().ToString("N");
        var sha = await ComputeSha256Async(filePath);

        using var fs = File.OpenRead(filePath);
        var buf = new byte[CHUNK_SIZE];
        for (int i = 0; i < totalChunks; i++)
        {
            var read = await fs.ReadAsync(buf, 0, CHUNK_SIZE);
            var chunk = read < CHUNK_SIZE ? buf.AsSpan(0, read).ToArray() : buf;
            var body = new
            {
                fileId,
                chunkIndex = i,
                totalChunks,
                data = Convert.ToBase64String(chunk)
            };
            await _api.PostAsync("/files/upload-chunk", body);
            progress?.Report((i + 1, totalChunks));
        }

        var res = await _api.PostAsync("/files/complete", new { fileId, filename = fi.Name, totalChunks, sha256 = sha });
        return res.GetProperty("fileId").GetInt64();
    }

    public async Task<(string path, long size)> DownloadAsync(long fileId, string filename)
    {
        var bytes = await _api.DownloadBytesAsync($"/files/{fileId}/download");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ChatRoomFiles");
        Directory.CreateDirectory(dir);

        var safeName = SanitizeFilename(filename);
        var path = Path.Combine(dir, safeName);
        var n = 1;
        while (File.Exists(path))
        {
            var ext = Path.GetExtension(safeName);
            var nameOnly = Path.GetFileNameWithoutExtension(safeName);
            path = Path.Combine(dir, $"{nameOnly} ({n++}){ext}");
        }
        await File.WriteAllBytesAsync(path, bytes);
        return (path, bytes.Length);
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "file" : s;
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
