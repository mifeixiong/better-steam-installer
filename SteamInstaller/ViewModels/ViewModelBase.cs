using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SteamInstaller.ViewModels;

/// <summary>
/// MVVM ViewModel 基类，实现 INotifyPropertyChanged 接口。
/// 提供属性变更通知和字段设置辅助方法。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>属性值发生变更时触发的事件。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发 PropertyChanged 事件通知 UI 绑定更新。</summary>
    /// <param name="propertyName">由 CallerMemberName 自动填充的属性名。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 设置属性字段值，只在值发生变化时触发通知。
    /// </summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="field">字段引用。</param>
    /// <param name="value">新值。</param>
    /// <param name="propertyName">由 CallerMemberName 自动填充的属性名。</param>
    /// <returns>值是否发生了变化。</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
