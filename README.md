# Proseka Tools

Project Sekai（プロセカ）相关的 WinUI 3 桌面工具箱。主功能与实现细节请参考：`ProsekaToolsApp/README.md`。

## 功能概览

- 数据抓取（内置 HTTP 监听服务，提供 /upload.js 便于代理工具转发）
- suite 工具（上传 suite 捕获到远端服务）
- Mysekai 解密与地图渲染（掉落点、稀有标记、唱片封面）
- 已拥有的卡片（解析 suite JSON，在线拉取卡面/装饰并缓存）
- 组卡器（Python + C++/pybind 推荐引擎）

## 项目结构

- `ProsekaToolsApp/` 主应用（WinUI 3）
  - 导航页：数据抓取、suite 工具、Mysekai 工具、已拥有的卡片、组卡器
  - 解密依赖：`ProsekaToolsApp/Services/sssekai.exe`（需自行放置）
  - 资源与缓存：`ProsekaToolsApp/Assets/`、`%APPDATA%\ProsekaTools\...`
- 其他目录为构建产物或依赖资源

## 依赖要求

- Windows 10 2004 (build 19041) 或更高版本
- .NET 8.0 SDK
- Visual Studio 2022（建议组件：.NET Desktop Development，Windows 10 SDK 10.0.19041.0+）

## 构建与运行

1. 使用 Visual Studio 2022 打开 `ProsekaTools.sln`
2. 选择目标平台（x64/x86/ARM64）
3. F5 运行或 Ctrl+Shift+B 构建

## 详细文档

- 详尽功能与实现方式：`ProsekaToolsApp/README.md`
- 常见问题与路径说明也在上述文档中
