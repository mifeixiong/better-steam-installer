namespace SteamInstaller.Models;

/// <summary>
/// 表示一条 Hosts 文件的 IP-域名映射记录。
/// </summary>
public class HostEntry
{
    /// <summary>加速节点的目标 IP 地址。</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>需要加速的域名（如 cdn.steamstatic.com）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>输出符合 hosts 文件格式的字符串：IP 域名。</summary>
    public override string ToString() => $"{Ip} {Host}";
}
