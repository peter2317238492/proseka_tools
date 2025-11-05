# Build Instructions

本项目为 WinUI 3（.NET 8）应用，包含抓包、解密与地图渲染功能。

## 先决条件

1. Windows 10/11
2. Visual Studio 2022（17.0+）及组件：
   - .NET Desktop Development
   - Windows 10 SDK (10.0.19041.0 或更高)
3. .NET 8.0 SDK（VS2022 自带或独立安装）

## 从 Visual Studio 构建

1. 打开解决方案 `ProsekaTools.sln`
2. 选择目标平台：x64（推荐）/x86/ARM64
3. 右键解决方案 -> Restore NuGet Packages
4. 构建：Ctrl+Shift+B 或 Build > Build Solution
5. 运行：F5 或 Debug > Start Debugging

## 命令行构建

1. 打开 “Developer Command Prompt for VS 2022”
2. 进入仓库目录：
   ```cmd
   cd path\to\proseka_tools
   ```
3. 还原依赖：
   ```cmd
   dotnet restore
   ```
4. 构建发布：
   ```cmd
   dotnet build ProsekaToolsApp\ProsekaToolsApp.csproj -c Release
   ```

## 运行前准备

- 解密依赖：将 `sssekai.exe` 放置到 `ProsekaToolsApp/Services/` 目录
- 存储路径：程序会把抓包与解密 JSON 写入 Roaming AppData
  - 抓包：`%APPDATA%/ProsekaTools/captures/<category>`（如 `mysekai`）
  - 解密输出：`%APPDATA%/ProsekaTools/output/mysekai`

## 常见问题排查（FAQ）

- Windows SDK not found
  - 通过 VS Installer 安装 Windows 10 SDK 10.0.19041.0 或更高
- Microsoft.WindowsAppSDK not found
  - 执行 `dotnet restore`
- XAML 相关编译错误
  - 需在 Windows 上构建并安装对应 SDK，尝试 Clean + Rebuild
- 抓包服务无法启动（HTTP 监听失败）
  - 以管理员打开命令行执行：
    ```cmd
    netsh http add urlacl url=http://+:8000/ user=%USERNAME%
    ```

## 目录结构（简要）

```
ProsekaToolsApp/
├── Assets/sekai_xray/
├── Pages/
│   ├── GrabDataPage.xaml(.cs)
│   └── Tab3Page.xaml(.cs)
├── Services/
│   ├── AppPaths.cs
│   └── sssekai.exe   # 外部依赖，需自行放置
└── ProsekaToolsApp.csproj
