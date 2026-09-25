using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatRoomReset.Services;

public class ApiException : Exception
{
    public int Status { get; }
    public ApiException(int status, string message) : base(message) => Status = status;
}

/// <summary>后端 REST API 封装（自动带 Bearer token）。</summary>
public class ApiService
{
    private readonly HttpClient _http = new();

    public ApiService()
    {
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    private async Task<JsonElement> SendAsync(Func<HttpRequestMessage> factory)
    {
        using var req = factory();
        var token = SettingsService.Instance.Token;
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        JsonElement json;
        try { json = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement; }
        catch { json = JsonDocument.Parse("{}").RootElement; }

        if (!resp.IsSuccessStatusCode)
        {
            var msg = json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : resp.ReasonPhrase ?? "请求失败";
            throw new ApiException((int)resp.StatusCode, msg);
        }
        return json;
    }

    public Task<JsonElement> GetAsync(string path) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, SettingsService.Instance.ApiBase + path));

    public Task<JsonElement> PostAsync(string path, object? body = null) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, SettingsService.Instance.ApiBase + path)
        {
            Content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        });

    public Task<JsonElement> DeleteAsync(string path) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, SettingsService.Instance.ApiBase + path));

    /// <summary>原始字节下载（文件）。</summary>
    public async Task<byte[]> DownloadBytesAsync(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, SettingsService.Instance.ApiBase + path);
        var token = SettingsService.Instance.Token;
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            throw new ApiException((int)resp.StatusCode, text);
        }
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>测试服务器连通性（初始化页用，不带 token）。</summary>
    public async Task<(bool ok, string message)> TestConnectionAsync(string baseUrl)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/health");
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                return (true, json);
            }
            return (false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
