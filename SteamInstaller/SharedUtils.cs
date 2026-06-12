using System;
using System.IO;
using System.Linq;

namespace SteamInstaller;

/// <summary>
/// 跨模块共享工具方法。
/// </summary>
public static class SharedUtils
{
    /// <summary>
    /// 在指定目录中按大小写不敏感方式查找 steam.exe 文件。
    /// </summary>
    /// <param name="directoryPath">要搜索的目录路径。</param>
    /// <returns>找到的 steam.exe 完整路径，目录不存在或无匹配时返回 null。</returns>
    public static string? FindSteamExeCaseInsensitive(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath)) return null;
            var files = Directory.GetFiles(directoryPath, "*.exe", SearchOption.TopDirectoryOnly);
            return files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("steam.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
