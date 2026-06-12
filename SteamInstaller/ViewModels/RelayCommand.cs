using System;
using System.Windows.Input;

namespace SteamInstaller.ViewModels;

/// <summary>
/// 通用 RelayCommand —— 将 WPF 命令绑定委托给 Action/Func，
/// 支持 CanExecute 条件判断和 CommandManager.RequerySuggested 自动刷新。
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// 创建 RelayCommand。
    /// </summary>
    /// <param name="execute">命令执行逻辑。</param>
    /// <param name="canExecute">可选的条件检查，为 null 时始终可执行。</param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>判断命令当前是否可执行。</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>执行命令。</summary>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// CanExecuteChanged 事件 —— 当命令的可执行状态改变时触发。
    /// 通过绑定到 CommandManager.RequerySuggested 实现自动刷新。
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
