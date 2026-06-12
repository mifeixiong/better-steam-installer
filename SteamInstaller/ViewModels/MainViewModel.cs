using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Serilog;
using SteamInstaller.Models;
using SteamInstaller.Services;

namespace SteamInstaller.ViewModels;

/// <summary>
/// 主界面的 ViewModel —— 管理 UI 绑定状态和安装流程命令。
/// 包含：开始安装、取消、浏览目录、启动 Steam、打开日志 五个主要命令。
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly ISteamInstallerService _installerService;

    /// <summary>安装流程取消令牌源，用于允许用户中途取消。</summary>
    private CancellationTokenSource? _cts;

    public MainViewModel(ISteamInstallerService installerService)
    {
        _installerService = installerService;

        StartCommand = new RelayCommand(async _ => await StartInstallation(), _ => !IsRunning);
        CancelCommand = new RelayCommand(_ => CancelInstallation(), _ => IsRunning);
        BrowseCommand = new RelayCommand(_ => BrowseInstallPath());
        LaunchSteamCommand = new RelayCommand(_ => LaunchSteam(), _ => InstalledSuccessfully);
        OpenLogCommand = new RelayCommand(_ => OpenLogFile());
    }

    // ==================== 绑定属性 ====================

    private bool _isRunning;
    /// <summary>安装是否正在进行中。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsNotRunning));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>安装未在运行（用于绑定 UI 启用/禁用）。</summary>
    public bool IsNotRunning => !IsRunning;

    private bool _installedSuccessfully;
    /// <summary>安装是否已成功完成。</summary>
    public bool InstalledSuccessfully
    {
        get => _installedSuccessfully;
        set
        {
            if (SetField(ref _installedSuccessfully, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private double _progressValue;
    /// <summary>安装进度百分比 (0-100)。</summary>
    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    private string _statusText = "就绪";
    /// <summary>状态栏文本。</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private string _installPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) ?? @"C:\Program Files (x86)",
        "Steam");
    /// <summary>用户选择的安装目标路径。</summary>
    public string InstallPath
    {
        get => _installPath;
        set => SetField(ref _installPath, value);
    }

    private bool _keepAcceleration;
    /// <summary>安装后是否保留 hosts 加速条目。</summary>
    public bool KeepAcceleration
    {
        get => _keepAcceleration;
        set => SetField(ref _keepAcceleration, value);
    }

    private string _installedPath = string.Empty;
    /// <summary>安装成功后的 Steam 实际路径。</summary>
    public string InstalledPath
    {
        get => _installedPath;
        set => SetField(ref _installedPath, value);
    }

    private ObservableCollection<string> _logEntries = new();
    /// <summary>UI 日志条目集合。</summary>
    public ObservableCollection<string> LogEntries
    {
        get => _logEntries;
        set => SetField(ref _logEntries, value);
    }

    // ==================== 命令 ====================

    /// <summary>开始加速安装命令。</summary>
    public ICommand StartCommand { get; }

    /// <summary>取消安装命令。</summary>
    public ICommand CancelCommand { get; }

    /// <summary>浏览安装目录命令。</summary>
    public ICommand BrowseCommand { get; }

    /// <summary>启动已安装的 Steam 客户端命令。</summary>
    public ICommand LaunchSteamCommand { get; }

    /// <summary>打开日志文件夹命令。</summary>
    public ICommand OpenLogCommand { get; }

    /// <summary>当日志有新条目添加时触发，通知 MainWindow 自动滚动。</summary>
    public event Action<string>? StatusLogAppended;

    // ==================== 安装流程 ====================

    /// <summary>
    /// 启动完整安装流程：
    /// 1. 校验并规范化安装路径（自动追加 Steam 子目录）
    /// 2. C 盘安装警告提示
    /// 3. 调用安装服务执行预检→加速→下载→安装→验证
    /// </summary>
    private async Task StartInstallation()
    {
        string targetPath = InstallPath.Trim();

        if (!Path.GetFileName(targetPath).Equals("Steam", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = Path.Combine(targetPath, "Steam");
            InstallPath = targetPath;
            Log.Information("安装目录名不为Steam，自动追加 Steam 子目录: {Path}", targetPath);
            AppendLog("Info", $"安装目录名不为Steam，已自动创建Steam子文件夹: {targetPath}");
        }

        string root = Path.GetPathRoot(targetPath) ?? "C:\\";
        root = root.TrimEnd('\\');
        bool isCdrive = root.Equals("C:", StringComparison.OrdinalIgnoreCase) || root.Equals("C:\\", StringComparison.OrdinalIgnoreCase);

        if (isCdrive)
        {
            try
            {
                var otherDrives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => d.Name.TrimEnd('\\'))
                    .Where(n => !n.Equals("C:", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (otherDrives.Count > 0)
                {
                    var result = MessageBox.Show(
                        "您选择的安装位置在 C 盘。C 盘通常为系统盘，安装 Steam 可能会侵占大量磁盘空间。\n\n" +
                        $"检测到您还有以下磁盘可用: {string.Join(", ", otherDrives)}\n\n" +
                        "是否仍然安装在 C 盘？",
                        "安装位置提醒",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                        return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enumerate drives for C-drive warning");
            }
        }

        _cts = new CancellationTokenSource();

        if (_cts.Token.IsCancellationRequested) return;

        IsRunning = true;
        InstalledSuccessfully = false;
        ProgressValue = 0;
        LogEntries.Clear();

        AppendLog("Info", $"用户选择的安装路径: {targetPath}");
        AppendLog("Info", $"保留加速: {(KeepAcceleration ? "是" : "否")}");
        Log.Information("开始安装 - 路径: {Path}, 保留加速: {KeepAccel}", targetPath, KeepAcceleration);

        var options = new InstallOptions
        {
            TargetInstallPath = targetPath,
            KeepAcceleration = KeepAcceleration,
            SilentMode = true
        };

        var progress = new Progress<InstallProgress>(p =>
        {
            if (Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressValue = p.OverallProgress;
                StatusText = p.DetailMessage;

                string entry = $"[{DateTime.Now:HH:mm:ss}] [{p.StepName}] {p.DetailMessage}";
                LogEntries.Add(entry);
                StatusLogAppended?.Invoke(entry);
            });
        });

        try
        {
            if (_cts.Token.IsCancellationRequested) return;

            var result = await Task.Run(() => _installerService.InstallAsync(options, progress, _cts.Token), _cts.Token);

            if (result.Success)
            {
                InstalledPath = result.InstallPath;
                InstalledSuccessfully = true;
                StatusText = "安装成功！点击「启动Steam」打开客户端。";

                string doneMsg = "下载完成，steam图标在桌面可见，请打开steam以继续";
                AppendLog("Success", doneMsg);

                try
                {
                    var procs = Process.GetProcessesByName("steam");
                    if (procs.Length > 0)
                        AppendLog("Info", "检测到 Steam.exe 正在运行中");
                    else
                        AppendLog("Info", "Steam.exe 当前未运行，可点击「启动Steam」打开");
                }
                catch { }

                await Task.Delay(500);

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(doneMsg, "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
            }
            else
            {
                StatusText = "安装失败: " + result.ErrorMessage;
                AppendLog("Error", result.ErrorMessage);
                ShowErrorDialog(result.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "安装已取消。";
            AppendLog("Info", "用户取消了安装");
        }
        catch (Exception ex)
        {
            StatusText = "发生错误: " + ex.Message;
            AppendLog("Error", ex.Message);
            Log.Error(ex, "Installation failed with exception");
            ShowErrorDialog(ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>取消当前安装流程。</summary>
    private void CancelInstallation()
    {
        _cts?.Cancel();
        StatusText = "正在取消...";
        AppendLog("Info", "User requested cancellation");
    }

    /// <summary>弹出错误对话框，引导用户查看日志文件排查问题。</summary>
    private void ShowErrorDialog(string errorMessage)
    {
        if (Application.Current == null) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                $"安装过程中发生错误：\n\n{errorMessage}\n\n请点击「查看日志」按钮，将日志文件发送给开发者以便排查问题。",
                "安装失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    /// <summary>使用 Windows 文件夹浏览对话框选择安装目录。</summary>
    private void BrowseInstallPath()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择 Steam 安装目录",
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrEmpty(InstallPath) && Directory.Exists(InstallPath))
        {
            dialog.SelectedPath = InstallPath;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            InstallPath = dialog.SelectedPath;
        }
    }

    /// <summary>启动已安装的 Steam 客户端进程。</summary>
    private void LaunchSteam()
    {
        if (!string.IsNullOrEmpty(InstalledPath))
        {
            string steamExe = SharedUtils.FindSteamExeCaseInsensitive(InstalledPath)
                ?? Path.Combine(InstalledPath, "steam.exe");
            if (File.Exists(steamExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true
                });
            }
            else
            {
                StatusText = "未找到 steam.exe，请确认 Steam 安装是否正确。";
                AppendLog("Error", $"未找到 {steamExe}");
            }
        }
    }

    /// <summary>在资源管理器中打开日志目录。</summary>
    private void OpenLogFile()
    {
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (Directory.Exists(logDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = logDir,
                UseShellExecute = true
            });
        }
    }

    /// <summary>向 UI 日志面板追加一条日志条目。</summary>
    private void AppendLog(string level, string message)
    {
        if (Application.Current != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string entry = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
                LogEntries.Add(entry);
                StatusLogAppended?.Invoke(entry);
            });
        }
    }
}
