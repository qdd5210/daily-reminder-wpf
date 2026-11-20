# 每日待办

Windows 10/11 x64 玻璃面板待办提醒工具。

## 项目目录

此目录是完整的 1.4 最终版项目，上传 GitHub 时使用此目录。

```text
daily-reminder-wpf/
├─ assets/                     d 图标：SVG、PNG、ICO
├─ packaging/                  安装与卸载程序源码、打包脚本
├─ tests/StorageVerification/  数据目录与旧记录迁移验证
├─ release-1.4/                最终安装包及独立运行程序
├─ build.ps1                   一键验证、编译和打包
├─ DailyReminder.csproj        WPF 主项目
└─ *.cs / *.xaml               应用源码
```

`.gitignore` 已排除编译缓存、成品 exe、测试数据和个人待办。GitHub 仓库保存源码，安装包可单独上传到 GitHub Releases。

## 安装

- 双击 `DailyReminderSetup.exe`，通过“浏览”选择程序安装目录。
- 默认安装到 `%LOCALAPPDATA%\Programs\DailyReminder`，也可以选择其他有写权限的目录。
- 默认创建桌面和开始菜单快捷方式，安装界面可取消桌面快捷方式。
- 安装包包含运行环境，无需另外安装 .NET 8。
- 程序目录包含 `DailyReminder.exe`、`Uninstall.exe` 和安装标记；卸载时保留待办数据。

## 使用

- 双击 `DailyReminder.exe` 启动。
- 窗口底部直接显示细横线输入行，输入后按 `Enter` 或点行末 `＋` 添加。
- 输入内容后按 `Enter` 保存，按 `Esc` 取消；提醒时间可以完全不设置。
- 每一条待办右侧都有独立闹钟按钮，可设置提醒时间、每天/仅一次和日期，也可点“不提醒”清除。
- 每一条待办直接行内编辑；左侧圆圈标记完成，右侧 `×` 删除。
- 拖动窗口任意边或右下角可自由缩放，窗口大小会自动记住。
- 列表滚动条已隐藏，内容较多时直接用鼠标滚轮滚动。

## 设置

点击右上角齿轮，或在面板空白处右键：

- 窗口始终置顶
- 开机自启
- 夜蓝、青雾、暖铜三种玻璃样式
- 玻璃透明度
- 自定义背景图片及图片强度；图片会复制到用户数据目录保存。
- 弹跳、果冻、惊叹、呼吸四种提醒动画，点“预览提醒”可立即查看。
- 在面板中按 `F6` 可直接预览当前提醒样式。

右键菜单可直接切换置顶和开机自启；完整样式设置在“设置与样式”中。

## 数据与提醒

- 数据自动保存在 `%LOCALAPPDATA%\DailyReminder\tasks.json`，背景图片也存放于该目录。
- 程序可以放在任意位置；移动 exe 或桌面快捷方式不会改变数据位置，也不会在桌面生成 JSON 或背景文件。
- 第一次运行新版时，会从原 exe 同目录或“文档\DailyReminder”自动迁移旧记录；原始文件保留，已有新版数据不会被覆盖。
- 到点后，文字从对应待办行飞出，在桌面的透明浮层上缩小、放大和弹跳，动画不受面板边界限制。
- 点击桌面提醒文字、对应待办行，或等待约 20 秒即可关闭提醒。
- 设置为当前分钟会立即提醒；空时间不会开启提醒。
- 窗口标题中的 `1.4` 表示独立数据目录和安装包版本；重复启动会唤回已有窗口。
- exe 和任务栏内嵌小写 `d` 闹钟图标，原始矢量图在 `assets/app-icon.svg`。
- 程序启动时，12 小时内错过的提醒仍会补发一次。
- 移动 exe 后，如果之前开过开机自启，需要在新位置重新勾选一次。

## 构建

在 Windows 上安装 .NET 8 SDK 或更高版本，然后在项目目录运行：

```powershell
.\build.ps1
```

脚本先执行 9 项数据验证，再发布包含运行环境的程序并生成安装包：

- `release-1.4/DailyReminderSetup.exe`：发给其他电脑使用的安装包。
- `release-1.4/app/DailyReminder.exe`：无需单独安装 .NET 的独立运行程序。
- `release-1.4/Uninstall.exe`：安装包内使用的卸载程序。

只验证数据逻辑时运行：

```powershell
dotnet run --project tests/StorageVerification/StorageVerification.csproj
```
