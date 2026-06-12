namespace SteamInstaller.Models;

/// <summary>
/// 安装进度信息，由安装服务通过 IProgress&lt;T&gt; 报告给 UI 层。
/// </summary>
public class InstallProgress
{
    /// <summary>整体进度百分比 (0-100)。</summary>
    public double OverallProgress { get; set; }

    /// <summary>当前步骤的名称标识（如 DownloadSteamSetup）。</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>当前步骤的详细描述文本。</summary>
    public string DetailMessage { get; set; } = string.Empty;

    /// <summary>下载速度（字节/秒），仅在下载步骤中有意义。</summary>
    public double DownloadSpeed { get; set; }

    /// <summary>已下载的字节数（本次会话累加）。</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>需要下载的总字节数，0 表示未知。</summary>
    public long TotalBytes { get; set; }
}
