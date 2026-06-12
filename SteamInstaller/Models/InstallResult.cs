namespace SteamInstaller.Models;

/// <summary>
/// 安装操作的结果状态。
/// </summary>
public class InstallResult
{
    /// <summary>安装是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>成功时的 Steam 安装路径。</summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>失败时的错误描述信息。</summary>
    public string ErrorMessage { get; set; } = string.Empty;
}
