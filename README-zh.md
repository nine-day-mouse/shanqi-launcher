# 闪启 Launcher · ShanQi Launcher

一个为 Windows 打造的键盘优先启动器，基于 [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher)
做品牌重制与本地化裁剪：按一下热键、输入、回车，即可启动程序、搜索文件、做算式、执行命令、网页搜索。

**English: [README.md](README.md)**

---

## 相比上游改了什么

Flow Launcher 本身非常成熟，但它默认假设你能顺畅访问 GitHub。本项目保留全部启动器内核，只改掉在
弱网 / 受限网络下会拖慢启动的部分，并完成整体品牌替换：

| # | 改动 | 位置 |
|---|------|------|
| 1 | 启动时不再下载插件市场清单（原版会连 GitHub，弱网下阻塞启动） | `Flow.Launcher/App.xaml.cs` |
| 2 | 关闭版本更新检查与插件自动更新 | `Flow.Launcher/App.xaml.cs` |
| 3 | 单实例锁改用独立通道名，可与官方 Flow Launcher 同时运行、互不顶掉 | `Flow.Launcher/Helper/SingleInstance.cs` |
| 4 | 开机自启注册表项使用独立名称，不会覆盖官方项 | `Flow.Launcher/Helper/AutoStartup.cs` |
| 5 | 用户数据目录改为 `%APPDATA%\ShanQi`，与官方配置隔离 | `Infrastructure/UserSettings/DataLocation.cs` |
| 6 | 品牌重制：窗口标题、程序图标、安装包与快捷方式名称 | `MainWindow.xaml`、`Resources/app.ico`、`installer/` |
| 7 | 构建容错：无运行实例时 `taskkill` 返回非 0 不再判定编译失败 | `Flow.Launcher/Flow.Launcher.csproj` |
| 8 | `Shift+Enter` 交给插件处理（如在资源管理器中打开、打开任务管理器） | `MainWindow.xaml` |

其余部分（查询引擎、插件体系、主题、快捷键）均为上游原有行为。

## 功能

- 全局热键（默认 `Alt+Space`），应用 / 文件 / 目录模糊秒搜
- 内置算式、Shell / CMD 执行、URL 打开、网页搜索
- 随包插件：
  `Program`、`Explorer`、`FileManager`、`Calculator`、`Shell`、`Sys`、`Url`、`WebSearch`、
  `BrowserBookmark`、`WindowsSettings`、`KillProcess`、`ProcessKiller`、`PluginsManager`、
  `PluginIndicator`、`Help`
- WPF 界面支持主题切换（Win11 亮色 / 暗色 / 跟随系统），全键盘操作

## 环境要求

| | |
|---|---|
| 系统 | Windows 10 2004（19041）及以上、Windows 11 |
| 运行时 | 装安装包无需额外运行时（内含 .NET 9 桌面运行时，自包含版） |
| 构建 | .NET SDK 9.0、Windows PowerShell 5.1 |

## 安装

到 **Releases** 页面下载：

| 文件 | 用途 |
|------|------|
| `ShanQiLauncher-Setup-<版本>.exe` | 推荐。双击即装，无需管理员权限，装完自动启动 |
| `ShanQiLauncher-Setup-<版本>.msi` | 批量 / 脚本静默部署，内容完全相同 |

安装程序行为：

- 安装到 `%LOCALAPPDATA%\ShanQiLauncher`（当前用户级，不弹 UAC）
- 创建桌面快捷方式 `闪启 Launcher.lnk` 与开始菜单文件夹 `闪启 Launcher`
- 安装完成后自动启动程序
- 卸载会删除程序目录与两处快捷方式；用户配置保留在 `%APPDATA%\ShanQi`

静默安装 / 卸载：

```powershell
msiexec /i ShanQiLauncher-Setup-1.0.0.msi /qn /norestart
msiexec /x ShanQiLauncher-Setup-1.0.0.msi /qn /norestart
```

## 从源码构建

```powershell
# 框架依赖版，输出到 .\release\
.\build.ps1 -Configuration Release

# 额外发布自包含版到 .\release-standalone\
.\build.ps1 -Configuration Release -Standalone

# 完整链路：编译 + 自包含发布 + MSI + 单文件 EXE 安装包
.\build.ps1 -Configuration Release -Standalone -Installer -Version 1.0.0
```

打包需要 WiX Toolset 5（作为全局 dotnet 工具安装一次）：

```powershell
dotnet tool install --global wix --version 5.0.2
```

产物生成在仓库根目录：

- `ShanQiLauncher-Setup-1.0.0.msi` —— 对 `release-standalone\` 打的 WiX 安装包
- `ShanQiLauncher-Setup-1.0.0.exe` —— 用 Windows 自带 .NET Framework 的 `csc.exe` 编译的 WinForms
  引导器，MSI 以托管资源形式内嵌，运行时释放到 `%TEMP%`，调 `msiexec /qn` 安装后自动清理。
  目标机无需任何额外运行时。
- `ShanQiLauncher-Setup-1.0.0.wixpdb` —— 调试符号，分发时不需要

## 目录结构

```
.
├── app/                     # 启动器源码（Flow Launcher 解决方案，已品牌重制）
│   ├── Flow.Launcher/            # WPF 主程序
│   ├── Flow.Launcher.Core/       # 查询与插件调度
│   ├── Flow.Launcher.Infrastructure/
│   ├── Flow.Launcher.Plugin/     # 插件接口
│   ├── Plugins/                  # 随包插件
│   └── LICENSE                   # MIT，上游版权声明
├── installer/
│   ├── installer.wxs        # WiX v4/v5 安装包定义（当前用户级、快捷方式、升级码）
│   └── Bootstrapper.cs      # 内嵌 MSI 的单文件 EXE 安装器
├── build.ps1                # 编译 / 发布 / 打包流水线
└── README.md / README-zh.md
```

## 数据与卸载

- 配置、插件、缓存：`%APPDATA%\ShanQi`
- 程序文件：`%LOCALAPPDATA%\ShanQiLauncher`
- 卸载：设置 → 应用 → 已安装的应用 → **闪启 Launcher**，或使用上面的 `msiexec /x`

## 许可

采用 MIT 许可，继承自 [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher)（版权归
Flow-Launcher、Wox）—— 详见 [`app/LICENSE`](app/LICENSE) 与 [`app/attribution.md`](app/attribution.md)。
上游署名全部保留，本仓库只包含上述品牌重制与弱网适配改动。
