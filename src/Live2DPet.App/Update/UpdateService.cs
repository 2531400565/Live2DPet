using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Live2DPet.Core.Update;

namespace Live2DPet.App.Update;

/// <summary>
/// 自动更新编排层（App 层，依赖 Windows 文件系统/进程）：
/// 检查 → 下载 → SHA256 校验 → 解压 → 写升级脚本 → 启动脚本并请求宿主退出。
///
/// 为什么用"外部脚本重启"而不是进程内替换：运行中的 exe 不能被自身覆盖。
/// 做法——把新文件解压到临时目录，写一个 robocopy 覆盖安装目录的 .bat，
/// 由 .bat 等待本进程退出后再复制并重启，从而在不引入额外 updater 程序的情况下完成热替换。
/// </summary>
public sealed class UpdateService
{
    private readonly GitHubUpdateClient _client = new();

    /// <summary>当前程序集版本（取自 exe 的 AssemblyVersion）。</summary>
    public Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>下载校验完成、即将重启前触发，由宿主负责退出进程（升级脚本会等进程退出后再覆盖）。</summary>
    public event Action? ShutdownRequested;

    /// <summary>查询最新 Release（无网络时返回 null）。</summary>
    public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) => _client.GetLatestAsync(ct);

    /// <summary>该版本是否需要更新（预发布不参与自动更新）。</summary>
    public bool NeedsUpdate(UpdateInfo info) => !info.Prerelease && GitHubUpdateClient.IsNewer(CurrentVersion, info.Version);

    /// <summary>
    /// 下载并应用更新：下载 → 校验 SHA256 → 解压 → 写升级脚本 → 启动脚本 → 请求退出。
    /// 任何一步失败都会抛出异常（调用方负责提示用户），并清理临时目录。
    /// </summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<(long Received, long Total)>? progress, CancellationToken ct)
    {
        var installDir = Path.GetDirectoryName(Environment.ProcessPath)
                         ?? Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                         ?? AppContext.BaseDirectory;
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? Path.Combine(installDir, "Live2DPet.App.exe");

        var tmpRoot = Path.Combine(Path.GetTempPath(), $"l2p_update_{info.Tag}");
        if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true);
        Directory.CreateDirectory(tmpRoot);
        var zip = Path.Combine(tmpRoot, "update.zip");
        var extract = Path.Combine(tmpRoot, "files");

        try
        {
            await _client.DownloadAsync(info.DownloadUrl, zip, progress, ct);

            // 校验：Release 资产带 SHA256 时必须一致，否则拒绝应用（防损坏/篡改）
            var actual = GitHubUpdateClient.ComputeSha256(zip);
            if (actual == null)
                throw new InvalidOperationException("无法计算下载文件校验和。");
            if (!string.IsNullOrEmpty(info.Sha256) && !GitHubUpdateClient.ShaMatches(info.Sha256, actual))
                throw new InvalidOperationException($"校验失败：下载文件 SHA256 与 Release 不符。");

            ZipFile.ExtractToDirectory(zip, extract);

            // 写升级脚本（robocopy 覆盖安装目录后重启 exe），路径可能含空格，整体加引号
            var bat = Path.Combine(installDir, "_l2p_update.bat");
            File.WriteAllText(bat, BuildUpdaterBat(exePath, extract, installDir, tmpRoot),
                new System.Text.UTF8Encoding(false));

            // 启动脚本（隐藏窗口）并请求宿主退出
            Process.Start(new ProcessStartInfo("cmd", $"/c \"{bat}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            ShutdownRequested?.Invoke();
        }
        catch
        {
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
            throw;
        }
    }

    private static string BuildUpdaterBat(string exePath, string extract, string installDir, string tmpRoot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("echo Live2DPet 正在更新...");
        sb.AppendLine(":wait");
        sb.AppendLine("tasklist | find /i \"Live2DPet.App.exe\" >nul 2>&1");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine($"robocopy \"{extract}\" \"{installDir}\" /E /R:2 /W:2 >nul");
        sb.AppendLine($"rmdir /s /q \"{tmpRoot}\"");
        sb.AppendLine($"start \"\" \"{exePath}\"");
        sb.AppendLine("del \"%~f0\"");
        return sb.ToString();
    }
}
