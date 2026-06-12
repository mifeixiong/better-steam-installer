using System.Windows;

/// <summary>
/// 指定 WPF 主题资源的查找位置。
/// ThemeInfo 告知 WPF 框架在哪里查找控件默认样式：
/// - None: 不在外部程序集中查找主题特定资源
/// - SourceAssembly: 在当前程序集中查找通用主题资源字典
/// </summary>
[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
