using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Live2DPet.Core.Update;

/// <summary>
/// 检查 GitHub Release 并下载更新包。纯逻辑（不依赖 WinForms），
/// 仅用 BCL（System.Net.Http / System.Text.Json / System.Security.Cryptography），无需额外 NuGet 包。
///
/// 仓库地址与接收的 tag 通过常量固定（本项目单仓库单 Release）。
/// </summary>
public sealed class GitHubUpdateClient
{
    private const string ApiUrl = "https://api.github.com/repos/2531400565/Live2DPet/releases/latest";
    private const string UserAgent = "Live2DPet-Updater";
    private readonly HttpClient _http;

    public GitHubUpdateClient(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>获取最新 Release 信息；网络/解析失败返回 null（调用方应把它当作"暂无更新"处理，不影响主功能）。</summary>
    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseRelease(json);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// 把 GitHub Release JSON 解析为 UpdateInfo（取第一个 .zip 资产）。纯函数，便于单测。
    /// 解析失败（字段缺失/非法）返回 null。
    /// </summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.ValueKind != JsonValueKind.String) return null;
            var tag = tagEl.GetString()!;
            var ver = TryParseVersion(tag);
            if (ver == null) return null;

            var pre = root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : tag;
            var body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "";

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var a in assets.EnumerateArray())
            {
                var an = a.TryGetProperty("name", out var anEl) && anEl.ValueKind == JsonValueKind.String ? anEl.GetString()! : "";
                if (!an.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                var url = a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";
                var size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                return new UpdateInfo
                {
                    Tag = tag,
                    Version = ver,
                    Name = name,
                    Body = body,
                    DownloadUrl = url,
                    Sha256 = digest.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).Trim(),
                    Size = size,
                    Prerelease = pre
                };
            }
            return null;
        }
        catch
        {
            // 非法 JSON / 字段缺失 → 当作"无更新"处理
            return null;
        }
    }

    /// <summary>latest 是否比 current 更新（按语义版本比较）。</summary>
    public static bool IsNewer(Version current, Version latest) => latest.CompareTo(current) > 0;

    /// <summary>
    /// 下载到 destPath，并通过 progress 回报 (已接收字节, 总字节)。
    /// 使用 ResponseHeadersRead 流式写入，避免把整个 zip 读进内存。
    /// </summary>
    public async Task DownloadAsync(string url, string destPath, IProgress<(long Received, long Total)>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var outFile = File.Create(destPath);
        var buf = new byte[81_920];
        long received = 0;
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await outFile.WriteAsync(buf.AsMemory(0, read), ct);
            received += read;
            progress?.Report((received, total));
        }
    }

    // ---- SHA256 校验（下载后比对 Release 资产 digest，防止被篡改/损坏）----

    /// <summary>计算文件 SHA256，返回小写十六进制；失败返回 null。</summary>
    public static string? ComputeSha256(string filePath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch { return null; }
    }

    /// <summary>比较两个 SHA256 是否一致（忽略大小写与 "sha256:" 前缀）。</summary>
    public static bool ShaMatches(string expected, string actual)
    {
        expected = expected.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static Version? TryParseVersion(string tag)
    {
        var t = tag.Trim();
        if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t[1..];
        var sep = t.IndexOfAny(new[] { '-', '+' });
        if (sep >= 0) t = t[..sep];
        return Version.TryParse(t, out var v) ? v : null;
    }
}
