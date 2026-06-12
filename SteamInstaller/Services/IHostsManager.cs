using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// Hosts 文件管理器接口 —— 负责备份、写入加速条目、恢复原始 hosts 文件。
/// </summary>
public interface IHostsManager
{
    /// <summary>备份当前 hosts 文件内容到临时文件。</summary>
    Task BackupAsync();

    /// <summary>将加速条目写入 hosts 文件（去重已有条目）。</summary>
    Task WriteEntriesAsync(IEnumerable<HostEntry> entries);

    /// <summary>从备份恢复 hosts 文件原始内容并刷新 DNS 缓存。</summary>
    Task RestoreAsync();
}
