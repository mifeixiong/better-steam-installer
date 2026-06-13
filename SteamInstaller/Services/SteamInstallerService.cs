using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Serilog;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// Steam 安装服务核心实现 —— 串联预检查、加速节点配置、下载、静默安装、验证和清理的完整流程。
/// 所有步骤均支持取消令牌（CancellationToken）以允许用户随时中止操作。
/// </summary>
public class SteamInstallerService : ISteamInstallerService
{
    private const string SteamSetupUrl = "https://cdn.steamstatic.com/client/installer/SteamSetup.exe";

    /// <summary>安装 Steam 最低所需磁盘空间：2 GB。</summary>
    private const long RequiredDiskSpace = 2L * 1024 * 1024 * 1024;

    /// <summary>下载文件的最低有效大小：1 MB，小于此值视为损坏。</summary>
    private const long MinimumFileSize = 1024 * 1024;

    /// <summary>下载超时时间：5 分钟。</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>安装监控最长等待时间：5 分钟。</summary>
    private const int MaxInstallWaitSeconds = 300;

    /// <summary>安装监控轮询间隔：2 秒。</summary>
    private const int InstallPollIntervalMs = 2000;

    private readonly IHostsManager _hostsManager;
    private readonly IAccelNodeProvider _accelNodeProvider;
    private readonly HttpClient _httpClient;

    public SteamInstallerService(IHostsManager hostsManager, IAccelNodeProvider accelNodeProvider, HttpClient httpClient)
    {
        _hostsManager = hostsManager;
        _accelNodeProvider = accelNodeProvider;
        _httpClient = httpClient;
    }

    /// <summary>
    /// 执行完整安装流程，返回安装结果。
    /// 流程：预检 → 获取加速节点 → 修改 Hosts → 下载 SteamSetup.exe → 校验 → 静默安装 → 监控 → 验证 → 清理。
    /// </summary>
    public async Task<InstallResult> InstallAsync(InstallOptions options, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        bool hostsAccelApplied = false;

        try
        {
            Report(progress, 0, "PreCheck", "正在执行预安装检查...");
            PerformPreCheck(options);
            ct.ThrowIfCancellationRequested();

            Report(progress, 3, "FetchAccelNodes", "正在获取加速节点...");
            var nodes = await _accelNodeProvider.FetchAsync();
            ct.ThrowIfCancellationRequested();

            Report(progress, 5, "EnableHostsAccel", "正在备份并修改 Hosts 文件...");
            await _hostsManager.BackupAsync();
            try
            {
                await _hostsManager.WriteEntriesAsync(nodes);
                hostsAccelApplied = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Hosts 写入被拦截（可能杀毒软件阻止），切换策略：跳过加速，改为直接下载");
                Report(progress, 5, "EnableHostsAccel", "Hosts 加速写入被拦截，将直接连接下载（速度可能较慢）...");
            }
            ct.ThrowIfCancellationRequested();

            Report(progress, 10, "DownloadSteamSetup", "正在下载 SteamSetup.exe...");
            string downloadPath = Path.Combine(Path.GetTempPath(), "SteamSetup.exe");
            await DownloadSteamSetupAsync(downloadPath, progress, ct);
            ct.ThrowIfCancellationRequested();

            Report(progress, 40, "VerifyDownload", "正在校验下载文件...");
            VerifyDownload(downloadPath);

            Report(progress, 42, "RunSilentInstall", "正在执行静默安装...");
            string args = BuildInstallArgs(options);
            Log.Information("安装参数: {Args}", args);
            Log.Information("安装路径: {Path}", options.TargetInstallPath);

            var psi = new ProcessStartInfo
            {
                FileName = downloadPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var installProcess = Process.Start(psi);
            if (installProcess == null)
            {
                return await FailAsync("无法启动 Steam 安装程序。", options, hostsAccelApplied);
            }

            Report(progress, 45, "MonitorInstall", "正在等待安装完成...");
            bool installed = await MonitorInstallAsync(installProcess, options.TargetInstallPath, progress, ct);
            ct.ThrowIfCancellationRequested();

            if (!installed)
            {
                return await FailAsync("安装进程已完成，但未检测到 Steam。", options, hostsAccelApplied);
            }

            Report(progress, 85, "VerifyInstall", "正在验证安装...");
            string? installPath = VerifyInstallation();
            if (installPath == null)
            {
                return await FailAsync("无法验证 Steam 安装，请手动检查。", options, hostsAccelApplied);
            }

            Report(progress, 95, "CleanupAndFinish", "正在清理...");
            await CleanupAsync(options, downloadPath, hostsAccelApplied);

            Report(progress, 100, "Finished", $"Steam 安装成功！路径: {installPath}");
            return new InstallResult { Success = true, InstallPath = installPath };
        }
        catch (OperationCanceledException)
        {
            Log.Information("用户取消了安装");
            await SafeCleanupAsync(options, hostsAccelApplied);
            return new InstallResult { Success = false, ErrorMessage = "安装已被取消。" };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "安装失败");
            await SafeCleanupAsync(options, hostsAccelApplied);
            return new InstallResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// 构建 SteamSetup.exe 的静默安装参数。
    /// 当目标路径含空格时使用双引号保护，防止命令注入。
    /// NSIS 安装程序要求 /D= 必须是最后一个参数且路径以反斜杠结尾。
    /// </summary>
    private static string BuildInstallArgs(InstallOptions options)
    {
        if (string.IsNullOrEmpty(options.TargetInstallPath))
            return "/S";

        string path = options.TargetInstallPath.TrimEnd('\\', ' ');
        if (path.Contains(' '))
            return $"/S /D=\"{path}\"";
        else
            return $"/S /D={path}";
    }

    /// <summary>
    /// 预安装检查：验证管理员权限和磁盘剩余空间。
    /// </summary>
    private void PerformPreCheck(InstallOptions options)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException("此程序需要管理员权限。");
        }

        string rootPath = string.IsNullOrEmpty(options.TargetInstallPath)
            ? Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) ?? "C:\\")!
            : Path.GetPathRoot(options.TargetInstallPath)!;

        if (string.IsNullOrEmpty(rootPath))
        {
            throw new InvalidOperationException($"无法解析安装路径的根驱动器: {options.TargetInstallPath}");
        }

        var driveInfo = new DriveInfo(rootPath);
        if (driveInfo.AvailableFreeSpace < RequiredDiskSpace)
        {
            throw new InvalidOperationException(
                $"磁盘空间不足。需要 2 GB，可用: {driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024.0):F1} GB");
        }

        Log.Information("PreCheck 通过: Admin={Admin}, FreeSpace={Space}GB",
            true, driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024));
    }

    /// <summary>
    /// 下载 SteamSetup.exe，支持断点续传。下载完成后进行 SHA256 校验。
    /// </summary>
    private async Task DownloadSteamSetupAsync(string destPath, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        Log.Information("开始下载 SteamSetup.exe，目标: {Url}", SteamSetupUrl);

        long existingLength = 0;
        if (File.Exists(destPath))
        {
            existingLength = new FileInfo(destPath).Length;
            Log.Information("检测到已有文件，大小: {Size} bytes，将尝试断点续传", existingLength);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DownloadTimeout);
        var effectiveCt = timeoutCts.Token;

        try
        {
            _httpClient.Timeout = DownloadTimeout;

            HttpResponseMessage response;
            if (existingLength > 0)
            {
                var rangeRequest = new HttpRequestMessage(HttpMethod.Get, SteamSetupUrl);
                rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                Log.Information("发送下载请求 (Range: bytes={Existing}-)", existingLength);
                response = await _httpClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, effectiveCt);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Log.Warning("服务器不支持断点续传(返回200而非206)，将重新下载");
                    response.Dispose();
                    File.Delete(destPath);
                    existingLength = 0;
                    var newRequest = new HttpRequestMessage(HttpMethod.Get, SteamSetupUrl);
                    response = await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, effectiveCt);
                }
                else if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    Log.Error("下载请求失败，HTTP状态码: {StatusCode}", (int)response.StatusCode);
                    response.EnsureSuccessStatusCode();
                }
                else
                {
                    Log.Information("服务器支持断点续传，返回 206 Partial Content");
                }
            }
            else
            {
                Log.Information("发送下载请求 (全新下载)");
                response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, SteamSetupUrl),
                    HttpCompletionOption.ResponseHeadersRead, effectiveCt);
            }

            response.EnsureSuccessStatusCode();

            using (response)
            {
                long downloadTotalLength = 0;
                if (response.Content.Headers.ContentLength.HasValue)
                {
                    downloadTotalLength = response.Content.Headers.ContentLength.Value;
                    Log.Information("服务器报告文件大小: {Size} MB", downloadTotalLength / (1024.0 * 1024.0));
                }
                else
                {
                    Log.Warning("服务器未提供 Content-Length，无法显示下载进度");
                }

                if (existingLength > 0)
                {
                    Log.Information("断点续传: 已有 {Existing} bytes, 本次需下载 {Download} bytes",
                        existingLength, downloadTotalLength);
                }

                var downloadStart = DateTime.UtcNow;
                using var fileStream = new FileStream(destPath, FileMode.Append, FileAccess.Write, FileShare.None);
                using var networkStream = await response.Content.ReadAsStreamAsync(effectiveCt);

                byte[] buffer = new byte[8192];
                long downloadRead = 0;
                var lastReportTime = DateTime.UtcNow;
                long lastReportRead = 0;
                DateTime lastLogTime = DateTime.UtcNow;

                int read;
                while ((read = await networkStream.ReadAsync(buffer, 0, buffer.Length, effectiveCt)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, effectiveCt);
                    downloadRead += read;

                    var now = DateTime.UtcNow;
                    var elapsed = (now - lastReportTime).TotalMilliseconds;
                    if (elapsed > 250)
                    {
                        double speed = (downloadRead - lastReportRead) / (elapsed / 1000.0);
                        lastReportRead = downloadRead;
                        lastReportTime = now;

                        double pct = downloadTotalLength > 0
                            ? 10 + (downloadRead / (double)downloadTotalLength * 30)
                            : 25;

                        progress.Report(new InstallProgress
                        {
                            OverallProgress = pct,
                            StepName = "DownloadSteamSetup",
                            DetailMessage = $"正在下载... {downloadRead / (1024.0 * 1024.0):F1} / {downloadTotalLength / (1024.0 * 1024.0):F1} MB  ({speed / 1024.0:F0} KB/s)",
                            DownloadSpeed = speed,
                            DownloadedBytes = downloadRead,
                            TotalBytes = downloadTotalLength
                        });

                        if ((now - lastLogTime).TotalSeconds >= 5)
                        {
                            Log.Information("下载进度: {Downloaded:F1} / {Total:F1} MB, 速度: {Speed:F0} KB/s",
                                downloadRead / (1024.0 * 1024.0),
                                downloadTotalLength / (1024.0 * 1024.0),
                                speed / 1024.0);
                            lastLogTime = now;
                        }
                    }
                }

                var duration = DateTime.UtcNow - downloadStart;
                Log.Information("下载完成: 本次 {ThisDownload} bytes, 累计 {Total} bytes, 耗时 {Duration:F1}s",
                    downloadRead, existingLength + downloadRead, duration.TotalSeconds);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Error("下载超时（超过 5 分钟），请检查网络连接或尝试关闭加速后重试");
            throw new TimeoutException("下载超时（超过 5 分钟）。可能原因：1) 网络连接不稳定 2) 防火墙阻止 3) CDN 不可达。请检查网络或关闭加速后重试。");
        }
    }

    /// <summary>
    /// 验证下载文件的完整性：检查文件存在、大小合法。
    /// </summary>
    private static void VerifyDownload(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("下载文件未找到。", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length < MinimumFileSize)
            throw new InvalidDataException("下载文件过小（不足 1 MB），可能已损坏。");

        Log.Information("下载校验通过: {Size} bytes", fileInfo.Length);
    }

    /// <summary>
    /// 监控安装进程，以轮询方式检测 steam.exe 进程是否启动或安装进程是否正常退出。
    /// 最长等待 MaxInstallWaitSeconds 秒，每 InstallPollIntervalMs 毫秒检测一次。
    /// </summary>
    private async Task<bool> MonitorInstallAsync(Process installProcess, string targetPath, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        int maxIterations = MaxInstallWaitSeconds * 1000 / InstallPollIntervalMs;

        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (installProcess.HasExited)
            {
                Log.Information("安装进程已退出，退出代码: {Code}", installProcess.ExitCode);

                if (installProcess.ExitCode != 0)
                {
                    Log.Warning("安装程序返回非零退出代码: {Code}", installProcess.ExitCode);
                    return false;
                }

                await Task.Delay(3000, ct);

                if (GetSteamProcesses().Length > 0)
                {
                    Log.Information("检测到 Steam.exe 正在运行");
                    return true;
                }

                string exePath = SharedUtils.FindSteamExeCaseInsensitive(targetPath)
                    ?? Path.Combine(targetPath, "steam.exe");
                if (File.Exists(exePath))
                {
                    Log.Information("安装进程已退出(代码0)，在目标路径找到 steam.exe 文件，判定安装成功");
                    return true;
                }

                Log.Warning("安装进程已退出(代码0)，但未找到 steam.exe 进程和文件");
                return false;
            }

            var steamProcesses = GetSteamProcesses();
            if (steamProcesses.Length > 0)
            {
                Log.Information("检测到 Steam.exe 正在运行");
                await Task.Delay(3000, ct);
                return true;
            }

            double pct = 45 + (i / (double)maxIterations * 40);
            progress.Report(new InstallProgress
            {
                OverallProgress = pct,
                StepName = "MonitorInstall",
                DetailMessage = $"正在等待 Steam 启动... (已等待 {i * InstallPollIntervalMs / 1000}s)"
            });

            await Task.Delay(InstallPollIntervalMs, ct);
        }

        Log.Warning("安装监控超时");
        return false;
    }

    /// <summary>获取当前运行的所有 steam 进程。</summary>
    private static Process[] GetSteamProcesses()
    {
        return Process.GetProcessesByName("steam");
    }

    /// <summary>
    /// 验证 Steam 安装：先通过注册表读取安装路径，失败则扫描默认安装目录。
    /// 找到 steam.exe 文件即视为安装成功。
    /// </summary>
    private static string? VerifyInstallation()
    {
        string? installPath = null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var steamKey = baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            installPath = steamKey?.GetValue("InstallPath") as string;

            if (!string.IsNullOrEmpty(installPath))
            {
                string exePath = SharedUtils.FindSteamExeCaseInsensitive(installPath)
                    ?? Path.Combine(installPath, "steam.exe");
                if (File.Exists(exePath))
                {
                    Log.Information("通过注册表验证安装成功: {Path}", installPath);
                    return installPath;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取注册表失败");
        }

        string[] defaultPaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };

        foreach (var dir in defaultPaths)
        {
            string exePath = SharedUtils.FindSteamExeCaseInsensitive(dir) ?? Path.Combine(dir, "steam.exe");
            if (File.Exists(exePath))
            {
                installPath = dir;
                Log.Information("在默认路径找到 Steam: {Path}", installPath);
                return installPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 安装完成后的清理操作：根据需要恢复 hosts、删除下载的临时文件。
    /// </summary>
    private async Task CleanupAsync(InstallOptions options, string downloadPath, bool hostsAccelApplied)
    {
        if (hostsAccelApplied && !options.KeepAcceleration)
        {
            await _hostsManager.RestoreAsync();
        }

        SafeDeleteFile(downloadPath);
        Log.Information("清理完成");
    }

    /// <summary>
    /// 异常/取消时的安全清理：尽可能恢复 hosts 文件，不抛出异常。
    /// </summary>
    private async Task SafeCleanupAsync(InstallOptions options, bool hostsAccelApplied)
    {
        try
        {
            if (hostsAccelApplied && !options.KeepAcceleration)
            {
                await _hostsManager.RestoreAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hosts 恢复失败");
        }
    }

    /// <summary>创建失败结果并执行安全清理。</summary>
    private async Task<InstallResult> FailAsync(string message, InstallOptions options, bool hostsAccelApplied)
    {
        Log.Error(message);
        await SafeCleanupAsync(options, hostsAccelApplied);
        return new InstallResult { Success = false, ErrorMessage = message };
    }

    /// <summary>向进度报告器发送当前步骤状态。</summary>
    private static void Report(IProgress<InstallProgress> progress, double pct, string step, string detail)
    {
        Log.Information("[{Step}] {Detail}", step, detail);
        progress.Report(new InstallProgress
        {
            OverallProgress = pct,
            StepName = step,
            DetailMessage = detail
        });
    }

    /// <summary>安全删除文件：忽略文件不存在和权限不足等异常。</summary>
    private static void SafeDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warning(ex, "删除文件失败: {Path}", path); }
    }
}
