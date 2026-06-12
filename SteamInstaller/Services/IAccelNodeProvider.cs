using System.Collections.Generic;
using System.Threading.Tasks;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// 加速节点提供者接口 —— 获取 Steam CDN 域名的加速 IP 映射列表。
/// </summary>
public interface IAccelNodeProvider
{
    /// <summary>
    /// 从远程源获取加速节点列表，失败时回退到内置默认节点。
    /// </summary>
    Task<IList<HostEntry>> FetchAsync();
}
