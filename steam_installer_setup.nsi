; ============================================================
; NSIS 安装脚本 —— 将 BetterSteamInstaller 打包为 Windows 安装程序
; 使用 Modern UI 2 (MUI2) 界面，支持开始菜单/桌面快捷方式和卸载
; ============================================================

; ---------- 产品元数据 ----------
!define PRODUCT_NAME "Steam 安装加速器"
!define PRODUCT_VERSION "1.0.0"
!define PRODUCT_PUBLISHER "Steam Installer"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\SteamInstaller.exe"

; 使用 LZMA 固实压缩以减小安装包体积
SetCompressor /SOLID lzma
SetCompressorDictSize 64

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "E:\code\better steam installer\SteamInstaller_Setup.exe"
InstallDir "$PROGRAMFILES64\Steam Installer"
RequestExecutionLevel admin
XPStyle on

; ---------- Modern UI 2 ----------
!include "MUI2.nsh"

!define MUI_ABORTWARNING

; 安装向导页面
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; 卸载向导页面
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; 简体中文语言包
!insertmacro MUI_LANGUAGE "SimpChinese"

; ============================================================
; 安装段
; ============================================================
Section "Install"
    SetOutPath "$INSTDIR"

    ; 将发布后的主程序复制到安装目录
    File "E:\code\better steam installer\publish\SteamInstaller.exe"

    ; 创建开始菜单快捷方式
    CreateDirectory "$SMPROGRAMS\Steam 安装加速器"
    CreateShortCut "$SMPROGRAMS\Steam 安装加速器\Steam 安装加速器.lnk" "$INSTDIR\SteamInstaller.exe"

    ; 创建桌面快捷方式
    CreateShortCut "$DESKTOP\Steam 安装加速器.lnk" "$INSTDIR\SteamInstaller.exe"

    ; 注册应用程序路径（用于"打开方式"等系统功能）
    WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\SteamInstaller.exe"

    ; 注册卸载信息
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "UninstallString" "$INSTDIR\uninst.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}" "Publisher" "${PRODUCT_PUBLISHER}"

    ; 生成卸载程序
    WriteUninstaller "$INSTDIR\uninst.exe"
SectionEnd

; ============================================================
; 卸载段
; ============================================================
Section "Uninstall"
    ; 删除主程序和卸载程序
    Delete "$INSTDIR\SteamInstaller.exe"
    Delete "$INSTDIR\uninst.exe"

    ; 删除开始菜单和桌面快捷方式
    Delete "$SMPROGRAMS\Steam 安装加速器\Steam 安装加速器.lnk"
    Delete "$DESKTOP\Steam 安装加速器.lnk"
    RMDir "$SMPROGRAMS\Steam 安装加速器"

    ; 清理日志目录（应用程序运行时产生的日志文件）
    RMDir /r "$INSTDIR\logs"

    ; 清理安装目录本身
    RMDir "$INSTDIR"

    ; 清理注册表项
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
    DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"
SectionEnd
