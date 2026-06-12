using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using Serilog;
using SteamInstaller.ViewModels;

namespace SteamInstaller;

/// <summary>
/// 主窗口代码后置类 —— 处理窗口生命周期事件和图标加载。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Steam 官网 favicon 图标地址，用于窗口标题栏图标展示。</summary>
    private const string FaviconUrl = "https://store.steampowered.com/favicon.ico";

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.StatusLogAppended += OnStatusLogAppended;
    }

    /// <summary>当日志新条目被添加时，自动滚动日志面板到最底端。</summary>
    private void OnStatusLogAppended(string entry)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try { LogScrollViewer.ScrollToEnd(); }
            catch { }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>窗口加载完成后记录日志并异步加载 Steam favicon。</summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Log.Information("Steam Installer window loaded");
        await LoadFaviconAsync();
    }

    /// <summary>
    /// 从 Steam 官网下载 favicon.ico 并显示在窗口标题栏。
    /// 下载失败时回退到内置的占位图标（S 字母方块）。
    /// </summary>
    private async System.Threading.Tasks.Task LoadFaviconAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var data = await client.GetByteArrayAsync(FaviconUrl);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new System.IO.MemoryStream(data);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            SteamIcon.Source = bitmap;
            SteamIcon.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
            Log.Information("Steam favicon loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load Steam favicon, using fallback icon");
        }
    }

    /// <summary>
    /// 窗口关闭前检查：如果安装正在进行中，提示用户确认退出。
    /// </summary>
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsRunning)
        {
            var result = MessageBox.Show("安装正在进行中，确定要退出吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
        }
    }

    /// <summary>最小化窗口到任务栏。</summary>
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>关闭应用程序窗口。</summary>
    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
