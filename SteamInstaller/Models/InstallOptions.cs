namespace SteamInstaller.Models;

/// <summary>
/// 安装配置选项，由用户界面传入，控制安装行为的各参数。
/// </summary>
public class InstallOptions
{
    /// <summary>目标安装目录路径（例如 D:\Steam）。</summary>
    public string TargetInstallPath { get; set; } = string.Empty;

    /// <summary>安装完成后是否保留 hosts 加速条目，false 则恢复原始 hosts。</summary>
    public bool KeepAcceleration { get; set; }

    /// <summary>是否静默安装（/S 参数），默认 true。</summary>
    public bool SilentMode { get; set; } = true;

    /// <summary>日志文件输出路径。</summary>
    public string LogFilePath { get; set; } = string.Empty;
}
