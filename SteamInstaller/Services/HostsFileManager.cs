using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// Hosts 文件管理器 —— 负责 Windows hosts 文件的备份、加速条目写入、恢复操作。
/// 使用原子写入策略（先写临时文件，再 Move 到目标位置）防止写入中断导致 hosts 损坏。
/// </summary>
public class HostsFileManager : IHostsManager
{
    private readonly string _hostsPath;
    private string? _backupPath;
    private string[]? _originalLines;

    public HostsFileManager()
    {
        _hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers", "etc", "hosts");
    }

    /// <summary>
    /// 备份当前 hosts 文件：将原始内容快照存入内存并写入临时备份文件。
    /// </summary>
    public Task BackupAsync()
    {
        if (!File.Exists(_hostsPath))
        {
            Log.Warning("Hosts file not found at {Path}", _hostsPath);
            _originalLines = Array.Empty<string>();
            return Task.CompletedTask;
        }

        _originalLines = File.ReadAllLines(_hostsPath);
        _backupPath = Path.GetTempFileName();
        File.WriteAllLines(_backupPath, _originalLines);
        Log.Information("Hosts file backed up to {BackupPath}", _backupPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将加速条目写入 hosts 文件。
    /// 采用"读取→合并→写入临时文件→原子替换"策略，避免并发写入损坏 hosts。
    /// 对已有相同域名的条目进行更新而非重复添加。
    /// </summary>
    public async Task WriteEntriesAsync(IEnumerable<HostEntry> entries)
    {
        if (!File.Exists(_hostsPath))
        {
            Log.Warning("Hosts file not found, creating new one at {Path}", _hostsPath);
            var dir = Path.GetDirectoryName(_hostsPath);
            if (dir != null)
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_hostsPath, "# Created by Steam Installer\n", Encoding.UTF8);
        }

        string tempPath = Path.GetTempFileName();
        bool modified = false;

        var lines = (await File.ReadAllLinesAsync(_hostsPath)).ToList();

        foreach (var entry in entries)
        {
            string pattern = $"{entry.Ip} {entry.Host}";

            int index = lines.FindIndex(l =>
                l.TrimEnd().EndsWith(entry.Host, StringComparison.OrdinalIgnoreCase)
                && !l.TrimStart().StartsWith("#"));

            if (index >= 0)
            {
                if (lines[index] != pattern)
                {
                    lines[index] = pattern;
                    modified = true;
                    Log.Information("Updated hosts entry: {Entry}", pattern);
                }
            }
            else
            {
                lines.Add(pattern);
                modified = true;
                Log.Information("Added hosts entry: {Entry}", pattern);
            }
        }

        if (modified)
        {
            Log.Information("Hosts 文件已修改，共写入 {Count} 条加速记录", entries.Count());

            await File.WriteAllLinesAsync(tempPath, lines, new UTF8Encoding(false));

            try
            {
                // 使用 File.Move 原子替换（同卷），比 File.Copy+Delete 更安全：
                // 如果移动失败，原始 hosts 不会被部分覆盖。
                File.Move(tempPath, _hostsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to replace hosts file atomically");
                try { File.Delete(tempPath); } catch { }
                throw new InvalidOperationException("无法写入 Hosts 文件，请检查权限或临时关闭杀毒软件，本软件并非病毒软件，只是在申请必要的DNS写入权限。", ex);
            }

            Log.Information("Hosts 写入完成，正在刷新 DNS 缓存以使新记录生效...");
            FlushDns();
        }
        else
        {
            // 无任何修改，清理临时文件
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// 恢复原始 hosts 文件：优先从备份文件还原，其次从内存快照还原。
    /// </summary>
    public Task RestoreAsync()
    {
        try
        {
            if (_backupPath != null && File.Exists(_backupPath))
            {
                File.Copy(_backupPath, _hostsPath, overwrite: true);
                try { File.Delete(_backupPath); } catch (Exception ex) { Log.Warning(ex, "Failed to delete backup file"); }
                Log.Information("Hosts file restored from backup");
                _backupPath = null;
                FlushDns();
            }
            else if (_originalLines != null)
            {
                File.WriteAllLines(_hostsPath, _originalLines);
                Log.Information("Hosts file restored from original snapshot");
                FlushDns();
            }
            else
            {
                Log.Warning("No backup or original snapshot available to restore hosts file");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore hosts file");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 通过 ipconfig /flushdns 刷新系统 DNS 缓存，使 hosts 修改立即生效。
    /// </summary>
    private static void FlushDns()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            bool exited = process?.WaitForExit(3000) ?? false;
            if (exited)
                Log.Information("DNS cache flushed");
            else
                Log.Warning("DNS flush did not complete within timeout");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to flush DNS cache");
        }
    }
}
