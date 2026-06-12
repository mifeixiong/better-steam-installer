using System;
using System.IO;
using System.Security.Principal;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SteamInstaller.Services;
using SteamInstaller.ViewModels;

namespace SteamInstaller;

/// <summary>
/// WPF 应用程序入口类。
/// 负责：日志初始化、依赖注入容器配置、管理员权限检查与 UAC 提权。
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        ConfigureLogging();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// 配置 Serilog 结构化日志：同时输出到 Debug 窗口和滚动文件。
    /// 日志文件存储在应用同目录的 logs/ 子目录下。
    /// </summary>
    private static void ConfigureLogging()
    {
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "steam-installer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("==========================================");
        Log.Information("Steam Installer starting up");
        Log.Information("==========================================");
    }

    /// <summary>
    /// 注册依赖注入服务：Hosts 管理器（单例）、HTTP 客户端服务、ViewModel 和 MainWindow。
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IHostsManager, HostsFileManager>();
        services.AddHttpClient<IAccelNodeProvider, AccelNodeProvider>();
        services.AddHttpClient<ISteamInstallerService, SteamInstallerService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// 应用启动时检查管理员权限。如果非管理员运行则弹出 UAC 提权对话框，
    /// 以管理员身份重新启动自身进程。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!IsAdministrator())
        {
            var result = MessageBox.Show(
                "此程序需要管理员权限才能修改 Hosts 文件并安装 Steam。\n\n点击「是」以管理员身份重新启动。",
                "需要管理员权限",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                string exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath))
                {
                    MessageBox.Show(
                        "无法获取当前程序路径，请右键以管理员身份手动运行此程序。",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try
                {
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restart with admin privileges");
                    MessageBox.Show(
                        $"以管理员身份启动失败: {ex.Message}\n\n请右键程序图标 → 以管理员身份运行。",
                        "提权失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                Shutdown();
                return;
            }

            Shutdown();
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// 应用退出时记录日志、刷新并释放日志系统和 DI 容器。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Steam Installer shutting down");
        Log.CloseAndFlush();
        _serviceProvider.Dispose();
        base.OnExit(e);
    }

    /// <summary>检查当前进程是否以管理员身份运行。</summary>
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 获取当前可执行文件的完整路径。
    /// 优先使用 .NET 6+ 的 Environment.ProcessPath，失败时回退到 Process.MainModule。
    /// </summary>
    private static string GetExecutablePath()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                return Environment.ProcessPath;

            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var fileName = currentProcess.MainModule?.FileName;
            if (!string.IsNullOrEmpty(fileName))
                return fileName;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get executable path");
        }

        return string.Empty;
    }
}
