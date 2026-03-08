<div align="center">
    <img src="assets/appicon-full-01.png" width="150">
    <h1>WebMusicPlayer</h1>
</div>

一个基于 .NET MAUI 的跨平台网络音频播放器，用于管理、筛选、收藏、播放网络媒体流，并支持通过订阅地址批量导入电台/流媒体列表。

## 功能概览

- 播放 HTTP/HTTPS 网络媒体流
- 手动添加媒体流
  - 支持填写名称、流地址、可选封面地址 `ArtworkUrl`
- 导入本地播放列表/资源
  - `XSPF`
  - `M3U` / `M3U8`
  - `ZIP`（可包含播放列表或文本地址）
  - 纯文本 URL 列表
- 订阅管理
  - 通过订阅 URL 抓取并更新媒体流
  - 可限制单个订阅导入的最大媒体流数量
- 收藏管理
- 按来源与关键字筛选媒体流
- 播放元数据同步
  - 播放时优先使用媒体流自己的 `ArtworkUrl`
  - 若未提供封面，则回退到应用内默认封面
- 中英文本地化

## 截图

<div>
<img src="assets/snapshot-favs.jpg" width="200">
<img src="assets/snapshot-streams.jpg" width="200">
<img src="assets/snapshot-subs.jpg" width="200">
</div>

## 当前订阅导入规则

项目当前对 `M3U` 的处理规则如下：

- 按普通 `M3U` 条目解析，不进行递归解析
- 通过 `#EXTINF` 读取条目标题
- 通过 `tvg-logo` 提取封面地址并写入 `ArtworkUrl`
- 通过 `#PLAYLIST:` 读取播放列表名称作为回退名称
- 达到“最大媒体流数量”后停止导入

这适合常见的广播/电台类 `M3U` 订阅源。

## 技术栈

- .NET 10 / C# 预览特性
- .NET MAUI
- CommunityToolkit.Maui
- CommunityToolkit.Maui.MediaElement
- CommunityToolkit.Mvvm
- XAML Source Generator

## 支持平台

项目配置的目标平台：

- Android
- iOS
- MacCatalyst
- Windows 10+

相关配置见 [WebMusicPlayer/WebMusicPlayer.csproj](WebMusicPlayer/WebMusicPlayer.csproj)。

## 项目结构

```text
WebMusicPlayer.slnx
WebMusicPlayer/
├─ Models/          数据模型
├─ Services/        状态持久化、播放封面、播放列表导入
├─ ViewModels/      主界面业务逻辑
├─ Views/           页面与弹窗
├─ Localization/    本地化扩展
├─ Resources/       图标、字体、样式、多语言资源
├─ Platforms/       各平台启动与清单配置
└─ MauiProgram.cs   DI 与应用启动配置
```

## 核心模块

- [WebMusicPlayer/Services/StreamImportService.cs](WebMusicPlayer/Services/StreamImportService.cs)
  - 处理 `XSPF`、`M3U/M3U8`、`ZIP`、纯文本 URL 导入
  - 负责订阅更新解析与名称修正
- [WebMusicPlayer/ViewModels/MainViewModel.cs](WebMusicPlayer/ViewModels/MainViewModel.cs)
  - 管理媒体流、收藏、订阅、筛选、播放状态
- [WebMusicPlayer/Views/MainPage.xaml](WebMusicPlayer/Views/MainPage.xaml)
  - 主界面，包含标签切换、底部播放区与隐藏 `MediaElement`
- [WebMusicPlayer/Services/AppStateStore.cs](WebMusicPlayer/Services/AppStateStore.cs)
  - 持久化应用状态
- [WebMusicPlayer/Services/MediaArtworkService.cs](WebMusicPlayer/Services/MediaArtworkService.cs)
  - 提供默认播放封面地址

## 本地运行

### 环境要求

建议安装：

- .NET 10 SDK
- .NET MAUI Workload
- Visual Studio 2022 / 2026 Preview（含 MAUI 工作负载）或等效命令行环境
- 对应平台 SDK：Android / iOS / Windows

### 还原与构建

在仓库根目录执行：

```powershell
dotnet build WebMusicPlayer.slnx -v minimal
```

### 按平台运行

示例：

```powershell
dotnet build WebMusicPlayer/WebMusicPlayer.csproj -f net10.0-windows10.0.19041.0
```

也可以直接用 Visual Studio 打开 [WebMusicPlayer.slnx](WebMusicPlayer.slnx) 进行调试。

## 使用说明

### 手动添加媒体流

1. 进入“媒体流”页
2. 打开右上角菜单
3. 选择“手动添加”
4. 输入：
   - 名称
   - 媒体流地址
   - 封面地址（可留空）

### 导入本地播放列表

在“媒体流”页右上角菜单中可导入：

- XSPF
- ZIP

### 添加订阅

1. 进入“订阅”页
2. 点击右上角菜单添加订阅
3. 填写：
   - 订阅名称
   - 订阅地址
   - 最大媒体流数量
4. 保存后应用会自动抓取并导入媒体流

### 更新订阅

在“订阅”页点击刷新按钮，即可更新全部订阅。

## 数据与状态

应用会保存以下内容：

- 已添加媒体流
- 收藏状态
- 已添加订阅
- 最后更新时间
- 当前筛选条件

状态由 [WebMusicPlayer/Services/AppStateStore.cs](WebMusicPlayer/Services/AppStateStore.cs) 负责持久化，保存位置使用 MAUI 的应用数据目录。

## 本地化

项目内置多语言资源，目前可见资源包括：

- 英文： [WebMusicPlayer/Resources/Localization/AppResources.resx](WebMusicPlayer/Resources/Localization/AppResources.resx)
- 中文： [WebMusicPlayer/Resources/Localization/AppResources.zh.resx](WebMusicPlayer/Resources/Localization/AppResources.zh.resx)

## 依赖包

主要 NuGet 依赖见 [WebMusicPlayer/WebMusicPlayer.csproj](WebMusicPlayer/WebMusicPlayer.csproj)：

- `CommunityToolkit.Maui`
- `CommunityToolkit.Maui.MediaElement`
- `CommunityToolkit.Mvvm`
- `Microsoft.Maui.Controls`

## 说明

这是一个偏轻量的网络媒体流播放器项目，当前实现更侧重：

- 简单直接的媒体流管理
- 订阅式导入与批量更新
- 跨平台播放体验
- 面向广播/电台类播放列表的 M3U 解析
