using System;
using System.Threading;
using System.Threading.Tasks;
using SteamInstaller.Models;

namespace SteamInstaller.Services;

/// <summary>
/// Steam 安装服务接口 —— 串联预检、加速配置、下载、安装、验证的完整流程。
/// </summary>
public interface ISteamInstallerService
{
    /// <summary>
    /// 执行完整的 Steam 安装流程。
    /// </summary>
    /// <param name="options">安装配置选项。</param>
    /// <param name="progress">进度报告器，用于向 UI 层报告实时进度。</param>
    /// <param name="ct">取消令牌，允许用户中止安装。</param>
    /// <returns>安装结果（成功/失败、路径、错误信息）。</returns>
    Task<InstallResult> InstallAsync(InstallOptions options, IProgress<InstallProgress> progress, CancellationToken ct);
}
