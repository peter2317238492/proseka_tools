# Proseka Tools

WinUI 3 工具应用，包含抓取数据包、解密 JSON 与地图渲染等功能。

## 功能概览

- 抓取数据包（内置 HTTP 监听服务，手机可上传响应体）
  - 监听地址：`http://<本机IP>:8000/`
  - 上传脚本：`/upload.js`（用于代理工具调用，将响应体转发到本机）
  - 保存目录：`%APPDATA%/ProsekaTools/captures/<category>`（如：`mysekai`、`suite`）
- JSON 解密与地图渲染（Tab3）
  - 支持 `apidecrypt`（依赖 `Services/sssekai.exe`）
  - 解密输入：选择文件或使用最新抓包
  - 解密输出：`%APPDATA%/ProsekaTools/output/mysekai`
  - 渲染背景与图标资源位于 `assets/sekai_xray`

## 存储路径

- 抓包：`%APPDATA%/ProsekaTools/captures/mysekai`
- 解密输出：`%APPDATA%/ProsekaTools/output/mysekai`

## 依赖要求

- Windows 10 2004 (build 19041) 或更高版本
- .NET 8.0 SDK
- Visual Studio 2022（建议安装以下组件）
  - .NET Desktop Development
  - Windows 10 SDK (10.0.19041.0 及以上)

## 构建与运行

1. 在 Visual Studio 2022 打开解决方案 `ProsekaTools.sln`
2. 选择目标平台（x64/x86/ARM64）
3. F5 运行或 Ctrl+Shift+B 构建

> 注意：`Tab3` 依赖外部可执行文件 `Services/sssekai.exe`。请将该文件放入 `ProsekaToolsApp/Services/` 目录，否则无法执行解密。

## 使用说明

- 抓取（GrabData）：
  1) 打开“抓取数据包”页面并开启服务
  2) 在手机/代理工具访问 `http://<本机IP>:8000/`，确认可达
  3) 获取 `http://<本机IP>:8000/upload.js` 内容，按代理工具要求注入以转发响应体
  4) 抓到的二进制数据会保存到 `%APPDATA%/ProsekaTools/captures/mysekai`

- 解密与渲染（Tab3）：
  1) 选择地区（region），点击“开始解密”或“载入最新JSON”
  2) 解密产物保存到 `%APPDATA%/ProsekaTools/output/mysekai` 并自动渲染

## 故障排查

- HTTP 监听启动失败（URLACL 权限）
  - 以管理员运行命令：
    `netsh http add urlacl url=http://+:8000/ user=%USERNAME%`
- 未找到 `sssekai.exe`：
  - 确认文件位于 `ProsekaToolsApp/Services/sssekai.exe`
- XAML 构建错误：
  - 请使用 Windows 环境并安装 Windows 10 SDK 10.0.19041.0+

## 目录结构（节选）

```
ProsekaToolsApp/
├── Assets/sekai_xray/          # 地图与图标资源
├── Pages/
│   ├── GrabDataPage.xaml(.cs)  # 抓包服务
│   └── Tab3Page.xaml(.cs)      # 解密与地图渲染
├── Services/
│   ├── AppPaths.cs             # %APPDATA% 路径统一管理
│   └── sssekai.exe             # 解密器（需自行放置）
└── ProsekaToolsApp.csproj