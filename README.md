# Steam 安装加速器

一个 Windows GUI 工具，用于自动下载、静默安装 Steam 客户端，并通过 Hosts 优化提升下载速度。

## 功能

- 自动从官方 CDN 下载 `SteamSetup.exe`，支持断点续传
- 静默安装 Steam 客户端（无人值守）
- 通过修改系统 Hosts 文件加速 Steam 下载
- 安装完成后可选择性保留加速配置
- NSIS 安装程序打包，支持开始菜单/桌面快捷方式和卸载

## 技术栈

- **语言**: C#
- **框架**: .NET 8 / WPF
- **打包**: NSIS (Nullsoft Scriptable Install System)
- **日志**: Serilog

## 项目结构

```
better-steam-installer/
├── .gitignore                         # Git 忽略规则
├── README.md                          # 项目说明
├── steam_installer_setup.nsi           # NSIS 安装脚本
└── SteamInstaller/                    # 主程序项目
    ├── SteamInstaller.csproj           # 项目文件
    ├── App.xaml                        # WPF 应用入口
    ├── App.xaml.cs                     # 应用逻辑代码
    ├── AssemblyInfo.cs                 # 程序集信息
    ├── MainWindow.xaml                 # 主窗口界面
    ├── MainWindow.xaml.cs              # 主窗口逻辑
    ├── SharedUtils.cs                  # 通用工具类
    ├── Models/                         # 数据模型
    │   ├── HostEntry.cs                # Hosts 条目模型
    │   ├── InstallOptions.cs           # 安装选项模型
    │   ├── InstallProgress.cs          # 安装进度模型
    │   └── InstallResult.cs            # 安装结果模型
    ├── Services/                       # 业务服务层
    │   ├── ISteamInstallerService.cs   # 安装服务接口
    │   ├── SteamInstallerService.cs    # 安装服务实现
    │   ├── IHostsManager.cs            # Hosts 管理接口
    │   ├── HostsFileManager.cs         # Hosts 文件管理实现
    │   ├── IAccelNodeProvider.cs       # 加速节点接口
    │   └── AccelNodeProvider.cs        # 加速节点提供者
    └── ViewModels/                     # MVVM 视图模型
        ├── MainViewModel.cs            # 主视图模型
        ├── RelayCommand.cs             # 命令绑定辅助
        └── ViewModelBase.cs            # ViewModel 基类
```

## 安装流程

| 阶段 | 动作 | 说明 |
|------|------|------|
| **准备** | 权限检查、磁盘空间验证、网络检测 | 确保环境可执行 |
| **加速 + 下载** | 修改 Hosts → 下载 SteamSetup.exe | 加速是核心亮点 |
| **安装 + 清理** | 静默安装 → 监控进程 → 验证结果 → 恢复 Hosts | 无人值守完成 |

## 构建

### 开发环境要求

- .NET 8 SDK
- NSIS 3.x（用于构建安装程序）

### 编译主程序

```bash
dotnet build SteamInstaller/SteamInstaller.csproj -c Release
```

### 发布为单文件

```bash
dotnet publish SteamInstaller/SteamInstaller.csproj -c Release -r win-x64
```

### 打包安装程序

使用 NSIS 编译安装脚本：

```bash
makensis steam_installer_setup.nsi
```

## 许可证

MIT
