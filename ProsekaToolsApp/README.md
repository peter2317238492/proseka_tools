# Proseka Tools

WinUI 3 桌面工具箱，用于抓取/解密 Project Sekai（プロセカ）相关数据，并提供 Mysekai 地图可视化、已拥有卡片展示、组卡推荐等功能。

## 功能总览

- 数据抓取（内置 HTTP 监听服务）
- suite 工具（上传 suite 捕获到远端服务）
- Mysekai 解密与地图渲染（含掉落与音乐唱片展示）
- 已拥有的卡片（解析 suite JSON，在线拉取卡面/装饰并缓存）
- 组卡器（调用 Python + C++ 模块进行组卡推荐）

## 功能与实现方式

### 1) 数据抓取（GrabData / Tab1）

- **实现**：`HttpListener` 监听 `http://+:{8000}/`，对外可访问（需要 URLACL 权限）。
- **路由**：
  - `/`：简单 HTML 测试页（含 `/status` ping）。
  - `/status`：返回 JSON（ip/port/time）。
  - `/upload`：接收 POST 二进制 body 并落盘。`X-Original-Url` 用于分类与命名。
  - `/upload.js`：生成代理工具可用的上传脚本（自动把响应体转发到本机）。
- **分类逻辑**：根据 `X-Original-Url` 判断 `mysekai` / `suite`，否则归类为 `unknown`。
- **命名**：`{apiType}_user{uid?}_{yyyyMMdd_HHmmss}_{pid}.bin`，UID 从 `.../user/<id>` 提取。
- **保存目录**：`%APPDATA%\ProsekaTools\captures\<category>`。

相关代码：`Pages\GrabDataPage.xaml(.cs)`。

### 2) suite 工具（Tab2）

- **用途**：把抓到的 `suite` 二进制捕获上传到指定服务。
- **实现**：`HttpClient` 以 `multipart/form-data` 上传，字段名为 `files`，并携带 `uploadtime`。
- **网络行为**：先请求 `http://go.mikuware.top/` 作为 warm-up，然后向 `http://101.34.19.31:5225/uploadTwSuite` 上传。
- **输入来源**：手动选择 `.bin` 或使用最新 `captures\suite` 文件，也支持拖拽。

相关代码：`Pages\Tab2Page.xaml(.cs)`。

### 3) Mysekai 工具（Tab3）

- **解密**：调用 `Services\sssekai.exe apidecrypt` 输出 JSON。
  - 支持 `FullTrustProcessLauncher`（打包/商店环境）与开发模式直接启动。
- **解析**：读取 `updatedResources.userMysekaiHarvestMaps`，收集可采集点与掉落。
- **地图渲染**：
  - 背景图来自 `Assets\sekai_xray\img\*.png`。
  - `MapScene` 定义各地图的比例、偏移、坐标方向与翻转规则。
  - `Canvas` 覆盖点位，稀有/超稀有掉落用不同描边颜色标记。
- **图标与唱片**：
  - 物品图标来自 `Assets\sekai_xray\icon\Texture2D`。
  - 音乐唱片 ID 通过 `mysekaiMusicRecords.json` 映射到歌曲 viewId（远程 GitHub，失败则使用本地 `Assets\mysekaiMusicRecords.json`）。
  - 歌曲元数据来自 `sekai-world` 的 `musics.json`（失败则使用本地 `Assets\musics.json`）。
  - 封面图从 `storage.sekai.best` 拉取 WebP，使用 `BitmapDecoder` 解码后显示。

相关代码：`Pages\Tab3Page.xaml(.cs)`。

### 4) 已拥有的卡片（Tab4）

- **解密**：同上，输入为 `suite` 捕获，输出到 `output\owned_cards`。
- **解析**：读取 `userCards`（或 `updatedResources.userCards`），收集卡片 ID 与训练状态。
- **卡面与装饰**：
  - 卡片主图：`https://storage.sekai.best/sekai-jp-assets/thumbnail/chara/{assetbundle}_{normal/after}.webp`。
  - 边框与星星：`https://sekai.best/assets/`。
  - 属性图标：优先固定路径，失败时从 `https://sekai.best/card/<id>` 页面解析兜底。
- **缓存**：`CardImageCacheService` 将卡面、边框、属性、星星保存到
  `output\owned_cards\ImageCache/FramesCache/AttributesCache/StarsCache`。
- **并发与 UI**：使用 `SemaphoreSlim` 控制并发下载，所有 `BitmapImage` 在 UI 线程创建。

相关代码：`Pages\OwnedCardsPage.xaml(.cs)`、`Services\CardImageCacheService.cs`。

### 5) 组卡器（Tab5）

- **核心逻辑**：调用 `Services\Calc\deck_recommend_runner.py`，内部使用 C++/pybind 模块 `sekai_deck_recommend_cpp` 进行推荐。
- **数据输入**：
  - `suite JSON`（玩家卡池）
  - `masterdata` 目录（默认 `Assets\master`）
  - `music_metas.json`（默认从网络下载至 `AppData\cache`，失败则用 `Assets\music_metas.json`）
  - 区服、活动 ID、歌曲 ID/难度、目标/算法/数量等参数
- **活动 ID 自动填充**：从 `Sekai-World` 各区服 `events.json` 拉取并缓存到 `AppData\cache\events`，根据当前时间判断进行中的活动。
- **输出**：结果写入 `output\deck_recommend\deck_recommend_*.json`，UI 解析后展示总分/加成/卡面。
- **Python**：默认使用内置嵌入式 Python（`python\python-3.12.10-embed-amd64`），也可手动选择外部 Python。

相关代码：`Pages\DeckRecommendPage.xaml(.cs)`、`Services\Calc\deck_recommend_runner.py`。

## 数据与目录

- AppData 根目录：`%APPDATA%\ProsekaTools`
- 抓包：`%APPDATA%\ProsekaTools\captures\<category>`
- 解密输出：
  - Mysekai：`%APPDATA%\ProsekaTools\output\mysekai`
  - 已拥有卡片：`%APPDATA%\ProsekaTools\output\owned_cards`
  - 组卡结果：`%APPDATA%\ProsekaTools\output\deck_recommend`
- 组卡缓存：`%APPDATA%\ProsekaTools\cache\music_metas.json`、`%APPDATA%\ProsekaTools\cache\events\events_<region>.json`

## 依赖要求

- Windows 10 2004 (build 19041) 或更高版本
- .NET 8.0 SDK
- Visual Studio 2022（建议安装以下组件）
  - .NET Desktop Development
  - Windows 10 SDK (10.0.19041.0 及以上)

## 构建与运行

1. 在 Visual Studio 2022 打开 `ProsekaTools.sln`
2. 选择目标平台（x64/x86/ARM64）
3. F5 运行或 Ctrl+Shift+B 构建

> 注意：`Tab3/Tab4/Tab5` 依赖外部可执行文件 `Services/sssekai.exe`。请将该文件放入 `ProsekaToolsApp/Services/` 目录，否则无法执行解密。

## 故障排查

- HTTP 监听启动失败（URLACL 权限）
  - 以管理员运行命令：
    `netsh http add urlacl url=http://+:8000/ user=%USERNAME%`
- 未找到 `sssekai.exe`
  - 确认文件位于 `ProsekaToolsApp/Services/sssekai.exe`
- XAML 构建错误
  - 请使用 Windows 环境并安装 Windows 10 SDK 10.0.19041.0+

## 目录结构（节选）

```
ProsekaToolsApp/
├── Assets/sekai_xray/          # 地图与图标资源
├── Assets/master/              # 组卡计算用 masterdata
├── Pages/
│   ├── GrabDataPage.xaml(.cs)  # 抓包服务
│   ├── Tab2Page.xaml(.cs)      # suite 上传
│   ├── Tab3Page.xaml(.cs)      # Mysekai 解密与地图渲染
│   ├── OwnedCardsPage.xaml(.cs)# 已拥有卡片
│   └── DeckRecommendPage.xaml(.cs) # 组卡器
├── Services/
│   ├── AppPaths.cs             # AppData 路径统一管理
│   ├── CardImageCacheService.cs# 卡图缓存
│   ├── Calc/deck_recommend_runner.py
│   └── sssekai.exe             # 解密器（需自行放置）
└── python/                     # 嵌入式 Python 运行时
```
