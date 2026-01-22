using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ProsekaToolsApp.Services;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IOPath = System.IO.Path;

namespace ProsekaToolsApp.Pages;

public sealed partial class DeckRecommendPage : Page
{
    private const int MaxStatusLines = 200;
    private const string MusicMetasDownloadUrl = "https://storage.sekai.best/sekai-best-assets/music_metas.json";
    private const string EmbeddedPythonFolderName = "python-3.12.10-embed-amd64";
    private const string EventsUrlJp = "https://raw.githubusercontent.com/Sekai-World/sekai-master-db-diff/main/events.json";
    private const string EventsUrlTw = "https://raw.githubusercontent.com/Sekai-World/sekai-master-db-tc-diff/main/events.json";
    private const string EventsUrlEn = "https://raw.githubusercontent.com/Sekai-World/sekai-master-db-en-diff/main/events.json";
    private const string EventsUrlKr = "https://raw.githubusercontent.com/Sekai-World/sekai-master-db-kr-diff/main/events.json";
    private const string EventsUrlCn = "https://raw.githubusercontent.com/Sekai-World/sekai-master-db-cn-diff/main/events.json";
    private static readonly string DefaultMasterDataDir = IOPath.Combine(AppContext.BaseDirectory, "Assets", "master");
    private static readonly string MusicMetasFallbackPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "music_metas.json");
    private static readonly string MusicMetasDownloadPath = IOPath.Combine(AppPaths.AppDataRoot, "cache", "music_metas.json");
    private static readonly string EmbeddedPythonExePath = IOPath.Combine(AppContext.BaseDirectory, "python", EmbeddedPythonFolderName, "python.exe");
    private static readonly string EventsFallbackPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "master", "events.json");
    private static readonly string EventsCacheDir = IOPath.Combine(AppPaths.AppDataRoot, "cache", "events");
    private readonly ObservableCollection<DeckRecommendEntry> _results = new();
    private readonly CardImageCacheService _cacheService = new();
    private readonly SemaphoreSlim _cardDatabaseGate = new(1, 1);
    private List<DeckRecommendSekaiCard>? _cardDatabase;
    private readonly SemaphoreSlim _musicNameGate = new(1, 1);
    private Dictionary<int, string>? _musicNameMap;
    private readonly HttpClient _http;
    private string? _suiteJsonPath;
    private string? _masterDataDir;
    private string? _musicMetasPath;
    private string? _pythonExePath;
    private string? _lastResultPath;
    private bool _defaultsReady;

    public DeckRecommendPage()
    {
        InitializeComponent();
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");
        _http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        _http.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
        if (ResultsList != null)
        {
            ResultsList.ItemsSource = _results;
        }
        Loaded += DeckRecommendPage_Loaded;
    }

    private async void DeckRecommendPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= DeckRecommendPage_Loaded;
        await InitializeDefaultsAsync();
    }

    private async Task InitializeDefaultsAsync()
    {
        if (Directory.Exists(DefaultMasterDataDir))
        {
            _masterDataDir = DefaultMasterDataDir;
            if (MasterDataTextBox != null) MasterDataTextBox.Text = _masterDataDir;
        }

        var musicMetasPath = await ResolveMusicMetasDefaultAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(musicMetasPath))
        {
            _musicMetasPath = musicMetasPath;
            if (MusicMetasTextBox != null) MusicMetasTextBox.Text = _musicMetasPath;
        }

        if (File.Exists(EmbeddedPythonExePath))
        {
            _pythonExePath = EmbeddedPythonExePath;
            if (PythonExeTextBox != null) PythonExeTextBox.Text = _pythonExePath;
        }

        await UpdateDefaultEventIdAsync().ConfigureAwait(true);
        _defaultsReady = true;
    }

    private async void RegionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_defaultsReady) return;
        await UpdateDefaultEventIdAsync().ConfigureAwait(true);
    }

    private async Task UpdateDefaultEventIdAsync()
    {
        var region = (RegionCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "cn";
        var eventId = await GetCurrentEventIdAsync(region).ConfigureAwait(true);
        if (EventIdTextBox == null) return;
        EventIdTextBox.Text = eventId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<int?> GetCurrentEventIdAsync(string region)
    {
        var bytes = await TryLoadEventsJsonAsync(region).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int? currentId = null;
            long currentStart = long.MinValue;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!TryGetLong(el, out var startAt, "startAt")) continue;
                if (!TryGetLong(el, out var aggregateAt, "aggregateAt")) continue;
                if (startAt <= now && now < aggregateAt)
                {
                    if (TryGetInt(el, out var id, "id") && startAt >= currentStart)
                    {
                        currentStart = startAt;
                        currentId = id;
                    }
                }
            }

            return currentId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] events.json parse failed: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> TryLoadEventsJsonAsync(string region)
    {
        try
        {
            var url = GetEventsUrl(region);
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length > 0)
            {
                var cachePath = GetEventsCachePath(region);
                var dir = IOPath.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    AppPaths.EnsureDir(dir);
                }
                await File.WriteAllBytesAsync(cachePath, bytes).ConfigureAwait(false);
                return bytes;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] events.json download failed: {ex.Message}");
        }

        try
        {
            if (File.Exists(EventsFallbackPath))
            {
                return await File.ReadAllBytesAsync(EventsFallbackPath).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] events.json fallback failed: {ex.Message}");
        }

        return null;
    }

    private static string GetEventsUrl(string region)
    {
        return region switch
        {
            "jp" => EventsUrlJp,
            "tw" => EventsUrlTw,
            "en" => EventsUrlEn,
            "kr" => EventsUrlKr,
            "cn" => EventsUrlCn,
            _ => EventsUrlJp
        };
    }

    private static string GetEventsCachePath(string region)
    {
        return IOPath.Combine(EventsCacheDir, $"events_{region}.json");
    }

    private static bool TryGetLong(JsonElement el, out long value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!el.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out value))
            {
                return true;
            }
            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out value))
            {
                return true;
            }
        }
        value = 0;
        return false;
    }

    private async Task<string?> ResolveMusicMetasDefaultAsync()
    {
        if (await TryEnsureMusicMetasDownloadedAsync().ConfigureAwait(true))
        {
            if (File.Exists(MusicMetasDownloadPath))
            {
                return MusicMetasDownloadPath;
            }
        }

        if (File.Exists(MusicMetasFallbackPath))
        {
            return MusicMetasFallbackPath;
        }

        return null;
    }

    private async Task<bool> TryEnsureMusicMetasDownloadedAsync()
    {
        try
        {
            if (File.Exists(MusicMetasDownloadPath))
            {
                return true;
            }

            var bytes = await _http.GetByteArrayAsync(MusicMetasDownloadUrl).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var dir = IOPath.GetDirectoryName(MusicMetasDownloadPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                AppPaths.EnsureDir(dir);
            }
            await File.WriteAllBytesAsync(MusicMetasDownloadPath, bytes).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] Music metas download failed: {ex.Message}");
            return false;
        }
    }

    private async void ChooseSuiteJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseLatestSuiteJsonCheckBox?.IsChecked == true)
        {
            var latest = TryGetLatestSuiteJson();
            if (latest == null)
            {
                SetStatus("未在 output/owned_cards 找到 JSON。", isError: true);
                return;
            }
            _suiteJsonPath = latest;
            if (SuiteJsonTextBox != null) SuiteJsonTextBox.Text = _suiteJsonPath;
            SetStatus($"已选择最新: {IOPath.GetFileName(_suiteJsonPath)}");
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _suiteJsonPath = file.Path;
            if (SuiteJsonTextBox != null) SuiteJsonTextBox.Text = _suiteJsonPath;
            SetStatus($"已选择: {file.Name}");
        }
    }

    private async void ChooseMasterDataButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _masterDataDir = folder.Path;
            if (MasterDataTextBox != null) MasterDataTextBox.Text = _masterDataDir;
            SetStatus($"已选择: {folder.Name}");
        }
    }

    private async void ChooseMusicMetasButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _musicMetasPath = file.Path;
            if (MusicMetasTextBox != null) MusicMetasTextBox.Text = _musicMetasPath;
            SetStatus($"已选择: {file.Name}");
        }
    }

    private async void ChoosePythonButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _pythonExePath = file.Path;
            if (PythonExeTextBox != null) PythonExeTextBox.Text = _pythonExePath;
            SetStatus($"已选择: {file.Name}");
        }
    }

    private void ClearResultsButton_Click(object sender, RoutedEventArgs e)
    {
        _results.Clear();
        if (ResultFileText != null) ResultFileText.Text = string.Empty;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WorkingRing != null) WorkingRing.IsActive = true;
            await RunDeckRecommendCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            if (WorkingRing != null) WorkingRing.IsActive = false;
        }
    }

    private async void DecryptAndRunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WorkingRing != null) WorkingRing.IsActive = true;
            if (StatusText != null) StatusText.Text = string.Empty;

            var inputFile = TryGetLatestSuiteCapture();
            if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
            {
                SetStatus("未在 captures/suite 找到文件。", isError: true);
                return;
            }

            var region = (RegionCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "jp";
            var outputDir = AppPaths.OutputOwnedCardsDir;
            Directory.CreateDirectory(outputDir);
            var outName = IOPath.GetFileNameWithoutExtension(inputFile) + ".json";
            var outputPath = IOPath.Combine(outputDir, outName);

            var exePath = IOPath.Combine(AppContext.BaseDirectory, "Services", "sssekai.exe");
            if (!File.Exists(exePath))
            {
                SetStatus($"找不到 sssekai.exe: {exePath}", isError: true);
                return;
            }

            var args = $"apidecrypt \"{inputFile}\" \"{outputPath}\" --region {region}";

            bool launchedViaFullTrust = await TryLaunchFullTrustAsync().ConfigureAwait(true);
            if (launchedViaFullTrust)
            {
                var okOut = await WaitForFileAsync(outputPath, TimeSpan.FromSeconds(40)).ConfigureAwait(true);
                if (okOut && File.Exists(outputPath))
                {
                    SetStatus($"解密完成: {outputPath}");
                    _suiteJsonPath = outputPath;
                    if (SuiteJsonTextBox != null) SuiteJsonTextBox.Text = _suiteJsonPath;
                    await RunDeckRecommendCoreAsync(outputPath).ConfigureAwait(true);
                }
                else
                {
                    SetStatus("已通过 FullTrust 启动辅助进程。若未生成文件，请确认辅助程序支持在无命令行参数时的工作方式。", isError: true);
                }
                return;
            }

#if DISABLE_EXTERNAL_PROCESS
            SetStatus("当前构建禁用了外部进程启动。", isError: true);
            return;
#else
            var ok = await RunProcessAsync(exePath, args).ConfigureAwait(true);
            if (ok && File.Exists(outputPath))
            {
                SetStatus($"解密完成: {outputPath}");
                _suiteJsonPath = outputPath;
                if (SuiteJsonTextBox != null) SuiteJsonTextBox.Text = _suiteJsonPath;
                await RunDeckRecommendCoreAsync(outputPath).ConfigureAwait(true);
            }
            else
            {
                SetStatus("解密失败，请检查输入文件和 region。", isError: true);
            }
#endif
        }
        finally
        {
            if (WorkingRing != null) WorkingRing.IsActive = false;
        }
    }

    private async Task RunDeckRecommendCoreAsync(string? suitePathOverride = null)
    {
        string? suitePath = suitePathOverride;
        if (string.IsNullOrWhiteSpace(suitePath))
        {
            suitePath = _suiteJsonPath;
            if (UseLatestSuiteJsonCheckBox?.IsChecked == true || string.IsNullOrWhiteSpace(suitePath))
            {
                suitePath = TryGetLatestSuiteJson();
            }
        }
        if (string.IsNullOrWhiteSpace(suitePath) || !File.Exists(suitePath))
        {
            SetStatus("未选择有效的 suite JSON。", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_masterDataDir) || !Directory.Exists(_masterDataDir))
        {
            SetStatus("请选择有效的 masterdata 目录。", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_musicMetasPath) || !File.Exists(_musicMetasPath))
        {
            SetStatus("请选择有效的 music metas JSON。", isError: true);
            return;
        }

        var region = (RegionCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "jp";
        var liveType = (LiveTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "multi";
        var target = (TargetCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "score";
        var algorithm = (AlgorithmCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ga";

        if (!TryParseInt(MusicIdTextBox?.Text, out var musicId))
        {
            SetStatus("Music ID 无效。", isError: true);
            return;
        }
        var musicDiff = (MusicDiffCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "expert";

        int? eventId = null;
        if (!string.IsNullOrWhiteSpace(EventIdTextBox?.Text))
        {
            if (!TryParseInt(EventIdTextBox.Text, out var parsedEventId))
            {
                SetStatus("Event ID 无效。", isError: true);
                return;
            }
            eventId = parsedEventId;
        }

        int limit = 10;
        if (!string.IsNullOrWhiteSpace(LimitTextBox?.Text))
        {
            if (!TryParseInt(LimitTextBox.Text, out limit))
            {
                SetStatus("Limit 无效。", isError: true);
                return;
            }
        }

        var runnerPath = IOPath.Combine(AppContext.BaseDirectory, "Services", "Calc", "deck_recommend_runner.py");
        if (!File.Exists(runnerPath))
        {
            SetStatus($"找不到 runner 脚本: {runnerPath}", isError: true);
            return;
        }

        AppPaths.EnsureDir(AppPaths.OutputDeckRecommendDir);
        var outputName = $"deck_recommend_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var outputPath = IOPath.Combine(AppPaths.OutputDeckRecommendDir, outputName);

        var pythonExe = string.IsNullOrWhiteSpace(_pythonExePath)
            ? (PythonExeTextBox?.Text?.Trim().Length > 0 ? PythonExeTextBox.Text.Trim() : "python")
            : _pythonExePath;

        var args = new List<string>
        {
            Quote(runnerPath),
            "--suite-json", Quote(suitePath),
            "--master-dir", Quote(_masterDataDir),
            "--music-metas", Quote(_musicMetasPath),
            "--region", region,
            "--live-type", liveType,
            "--music-id", musicId.ToString(CultureInfo.InvariantCulture),
            "--music-diff", musicDiff,
            "--target", target,
            "--algorithm", algorithm,
            "--limit", limit.ToString(CultureInfo.InvariantCulture),
            "--output", Quote(outputPath)
        };
        if (eventId.HasValue)
        {
            args.Add("--event-id");
            args.Add(eventId.Value.ToString(CultureInfo.InvariantCulture));
        }

        var argLine = string.Join(" ", args);

#if DISABLE_EXTERNAL_PROCESS
        SetStatus("当前构建禁用了外部进程启动。", isError: true);
        return;
#else
        var ok = await RunProcessAsync(pythonExe, argLine).ConfigureAwait(true);
        if (!ok)
        {
            SetStatus("组卡执行失败，请检查 runner 日志。", isError: true);
            return;
        }
        if (!File.Exists(outputPath))
        {
            SetStatus($"未生成结果文件: {outputPath}", isError: true);
            return;
        }
        _lastResultPath = outputPath;
        LoadResultFile(outputPath);
        var scoreSongs = GetScoreSongs(ScoreSongsTextBox?.Text, musicDiff);
        await LoadDeckPreviewsAsync().ConfigureAwait(true);
        await ComputeMultiSongScoresAsync(scoreSongs, suitePath, _masterDataDir, _musicMetasPath, region, liveType, eventId).ConfigureAwait(true);
        SetStatus($"完成: {outputName}");
#endif
    }

#if !DISABLE_EXTERNAL_PROCESS
    private async Task<bool> RunProcessAsync(string exePath, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = IOPath.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>();
            p.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _ = AppendStatusUIAsync(e.Data);
                }
            };
            p.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _ = AppendStatusUIAsync(e.Data, isError: true);
                }
            };
            p.Exited += (s, e) => tcs.TrySetResult(p.ExitCode);

            if (!p.Start()) return false;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            var exit = await tcs.Task;
            return exit == 0;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            return false;
        }
    }
#endif

    private static bool TryParseInt(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private string? TryGetLatestSuiteJson()
    {
        try
        {
            var dir = AppPaths.OutputOwnedCardsDir;
            if (!Directory.Exists(dir)) return null;
            var latest = Directory.GetFiles(dir, "*.json")
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            return latest;
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetLatestSuiteCapture()
    {
        try
        {
            var dir = AppPaths.CapturesSuiteDir;
            if (!Directory.Exists(dir)) return null;
            var latest = Directory.GetFiles(dir, "*.bin")
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            return latest;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryLaunchFullTrustAsync()
    {
        try
        {
            if (ApiInformation.IsMethodPresent("Windows.ApplicationModel.FullTrustProcessLauncher", nameof(FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync)))
            {
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            return false;
        }
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                if (File.Exists(path)) return true;
            }
            catch { }
            await Task.Delay(300);
        }
        return false;
    }

    private void LoadResultFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("decks", out var decks) || decks.ValueKind != JsonValueKind.Array)
            {
                SetStatus("结果缺少 decks。", isError: true);
                return;
            }

            _results.Clear();
            int index = 1;
            foreach (var deck in decks.EnumerateArray())
            {
                int score = GetInt(deck, "score");
                int liveScore = GetInt(deck, "live_score");
                int totalPower = GetInt(deck, "total_power");
                double eventBonus = GetDouble(deck, "event_bonus_rate");
                var cardPreviews = BuildCardPreviews(deck, out var effectiveSkill);

                _results.Add(new DeckRecommendEntry
                {
                    Title = $"#{index} score={score}",
                    TotalPower = totalPower,
                    EventBonusRate = eventBonus,
                    SummaryLine = $"effective={effectiveSkill:0.##} power={totalPower} event_bonus={eventBonus:0.##}%",
                    ScoreLine = $"live_score={liveScore} score={score}",
                    CardPreviews = new ObservableCollection<DeckCardPreview>(cardPreviews)
                });
                index++;
            }

            if (ResultFileText != null) ResultFileText.Text = IOPath.GetFileName(path);
        }
        catch (Exception ex)
        {
            SetStatus($"解析失败: {ex.Message}", isError: true);
        }
    }

    private static List<DeckCardPreview> BuildCardPreviews(JsonElement deck, out double effectiveSkill)
    {
        effectiveSkill = 0;
        var previews = new List<DeckCardPreview>();
        if (!deck.TryGetProperty("cards", out var cardsEl) || cardsEl.ValueKind != JsonValueKind.Array)
        {
            return previews;
        }
        var cards = new List<(DeckCardPreview preview, int skill)>();
        foreach (var card in cardsEl.EnumerateArray())
        {
            if (card.TryGetProperty("card_id", out var idEl) && idEl.TryGetInt32(out var id))
            {
                bool afterTraining = card.TryGetProperty("after_training", out var atEl) && atEl.ValueKind == JsonValueKind.True;
                string defaultImage = card.TryGetProperty("default_image", out var diEl) && diEl.ValueKind == JsonValueKind.String
                    ? diEl.GetString() ?? string.Empty
                    : string.Empty;
                int skillScoreUp = 0;
                if (card.TryGetProperty("skill_score_up", out var skillEl))
                {
                    if (skillEl.ValueKind == JsonValueKind.Number && skillEl.TryGetInt32(out var v)) skillScoreUp = v;
                    else if (skillEl.ValueKind == JsonValueKind.String && int.TryParse(skillEl.GetString(), out v)) skillScoreUp = v;
                }
                var preview = new DeckCardPreview
                {
                    CardId = id,
                    AfterTraining = afterTraining,
                    DefaultImageType = defaultImage,
                    SkillScoreUp = skillScoreUp,
                    LoadStatus = "Pending"
                };
                cards.Add((preview, skillScoreUp));
            }
        }
        if (cards.Count == 0) return previews;

        int maxIndex = 0;
        int maxSkill = cards[0].skill;
        int sumSkill = cards[0].skill;
        for (int i = 1; i < cards.Count; i++)
        {
            int skill = cards[i].skill;
            sumSkill += skill;
            if (skill > maxSkill)
            {
                maxSkill = skill;
                maxIndex = i;
            }
        }
        if (cards.Count > 1)
        {
            effectiveSkill = maxSkill + (sumSkill - maxSkill) / 5.0;
        }
        else
        {
            effectiveSkill = maxSkill;
        }

        previews.Add(cards[maxIndex].preview);
        for (int i = 0; i < cards.Count; i++)
        {
            if (i == maxIndex) continue;
            previews.Add(cards[i].preview);
        }
        return previews;
    }

    private static int GetInt(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v)) return v;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out v)) return v;
        }
        return 0;
    }

    private static double GetDouble(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var v)) return v;
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
        }
        return 0;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private Task SetStatusUIAsync(string message, bool isError = false)
    {
        return EnqueueOnUIAsync(() => SetStatus(message, isError));
    }

    private Task AppendStatusUIAsync(string message, bool isError = false)
    {
        return EnqueueOnUIAsync(() => AppendStatus(message, isError));
    }

    private static List<ScoreSong> GetScoreSongs(string? input, string fallbackDiff)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new List<ScoreSong>
            {
                new(226, "hard"),
                new(74, "expert"),
                new(448, "expert")
            };
        }

        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return new List<ScoreSong> { new(id, fallbackDiff) };
            }
        }

        return new List<ScoreSong>
        {
            new(226, "hard"),
            new(74, "expert"),
            new(448, "expert")
        };
    }

    private async Task ComputeMultiSongScoresAsync(
        List<ScoreSong> songs,
        string suitePath,
        string masterDir,
        string musicMetas,
        string region,
        string liveType,
        int? eventId)
    {
        if (songs.Count == 0 || _results.Count == 0) return;

        var runnerPath = IOPath.Combine(AppContext.BaseDirectory, "Services", "Calc", "deck_recommend_runner.py");
        if (!File.Exists(runnerPath))
        {
            await SetStatusUIAsync($"找不到 runner 脚本: {runnerPath}", isError: true);
            return;
        }

        var pythonExe = string.IsNullOrWhiteSpace(_pythonExePath)
            ? (PythonExeTextBox?.Text?.Trim().Length > 0 ? PythonExeTextBox.Text.Trim() : "python")
            : _pythonExePath;

        int deckIndex = 1;
        foreach (var entry in _results)
        {
            var cardIds = entry.CardPreviews.Select(c => c.CardId).ToList();
            if (cardIds.Count == 0)
            {
                deckIndex++;
                continue;
            }

            var parts = new List<string>();
            foreach (var song in songs)
            {
                await SetStatusUIAsync($"计算 #{deckIndex} song={song.MusicId} ...");
                var outputName = $"deck_score_{deckIndex}_{song.MusicId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = IOPath.Combine(AppPaths.OutputDeckRecommendDir, outputName);

                var args = new List<string>
                {
                    Quote(runnerPath),
                    "--suite-json", Quote(suitePath),
                    "--master-dir", Quote(masterDir),
                    "--music-metas", Quote(musicMetas),
                    "--region", region,
                    "--live-type", liveType,
                    "--music-id", song.MusicId.ToString(CultureInfo.InvariantCulture),
                    "--music-diff", song.Difficulty,
                    "--target", "score",
                    "--algorithm", "dfs",
                    "--limit", "1",
                    "--fixed-cards", string.Join(",", cardIds),
                    "--output", Quote(outputPath)
                };
                if (eventId.HasValue)
                {
                    args.Add("--event-id");
                    args.Add(eventId.Value.ToString(CultureInfo.InvariantCulture));
                }

                var argLine = string.Join(" ", args);
#if !DISABLE_EXTERNAL_PROCESS
                var ok = await RunProcessAsync(pythonExe, argLine);
                if (ok && File.Exists(outputPath) && TryReadDeckScore(outputPath, out var liveScore, out var score))
                {
                    var title = await GetMusicTitleAsync(song.MusicId).ConfigureAwait(false);
                    parts.Add($"{title}({song.Difficulty}): live={liveScore} score={score}");
                }
                else
                {
                    parts.Add($"{song.MusicId}({song.Difficulty}): N/A");
                }
#else
                parts.Add($"{song.MusicId}({song.Difficulty}): N/A");
#endif
            }

            await EnqueueOnUIAsync(() =>
            {
                entry.ScoreLine = string.Join(" | ", parts);
            });

            deckIndex++;
        }
    }

    private static bool TryReadDeckScore(string path, out int liveScore, out int score)
    {
        liveScore = 0;
        score = 0;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("decks", out var decks) || decks.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var first = decks.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return false;
            liveScore = GetInt(first, "live_score");
            score = GetInt(first, "score");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadDeckPreviewsAsync()
    {
        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue == null) return;

        var sem = new SemaphoreSlim(4);
        var tasks = new List<Task>();
        foreach (var entry in _results)
        {
            foreach (var preview in entry.CardPreviews)
            {
                tasks.Add(LoadCardPreviewAsync(preview, sem, dispatcherQueue));
            }
        }
        await Task.WhenAll(tasks);
    }

    private async Task LoadCardPreviewAsync(DeckCardPreview preview, SemaphoreSlim sem, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        await sem.WaitAsync();
        try
        {
            await UpdatePreviewStatusAsync(dispatcherQueue, preview, "Loading...");
            var database = await GetCardDatabaseAsync().ConfigureAwait(false);
            var card = database.FirstOrDefault(c => c.Id == preview.CardId);
            if (card == null)
            {
                await UpdatePreviewStatusAsync(dispatcherQueue, preview, "Card not found");
                return;
            }

            var cardAfterTraining = ShouldUseAfterTrainingCard(preview);
            var starAfterTraining = ShouldUseAfterTrainingStar(preview, card);
            var urls = GetCardImageUrls(card, afterTraining: cardAfterTraining, starAfterTraining: starAfterTraining);
            var attrKey = NormalizeAttrValue(card.Attr);

            var cachedCardImage = await LoadCachedBitmapImageAsync(_cacheService.GetCachePath(preview.CardId, cardAfterTraining), dispatcherQueue).ConfigureAwait(false);
            var cachedFrameImage = await LoadCachedBitmapImageAsync(_cacheService.GetFrameCachePath(card.Rarity), dispatcherQueue).ConfigureAwait(false);
            var cachedAttributeImage = await LoadCachedBitmapImageAsync(_cacheService.GetAttributeCachePath(attrKey), dispatcherQueue).ConfigureAwait(false);
            var cachedStarImage = await LoadCachedBitmapImageAsync(_cacheService.GetStarCachePath(afterTraining: starAfterTraining), dispatcherQueue).ConfigureAwait(false);

            BitmapImage? cardBitmapImage = cachedCardImage;
            if (cardBitmapImage == null)
            {
                var bitmap = await DownloadBitmapAsync(urls.CharacterImage).ConfigureAwait(false);
                if (bitmap == null)
                {
                    await UpdatePreviewStatusAsync(dispatcherQueue, preview, "Download failed");
                    return;
                }
                await _cacheService.SaveCompositedImageAsync(preview.CardId, cardAfterTraining, bitmap).ConfigureAwait(false);
                var pngBytes = await EncodeToPngBytesAsync(bitmap).ConfigureAwait(false);
                await EnqueueOnUIAsync(dispatcherQueue, async () =>
                {
                    cardBitmapImage = await CreateBitmapImageFromBytesAsync(pngBytes);
                });
            }

            BitmapImage? frameImage = cachedFrameImage;
            if (frameImage == null)
            {
                frameImage = await TryLoadFrameFromBase64Async(card.Rarity, dispatcherQueue).ConfigureAwait(false);
            }
            if (frameImage == null && !string.IsNullOrWhiteSpace(urls.FrameImage))
            {
                frameImage = await DownloadCacheAndCreateBitmapAsync(urls.FrameImage,
                    bitmap => _cacheService.SaveFrameAsync(card.Rarity, bitmap)).ConfigureAwait(false);
            }

            BitmapImage? attributeImage = cachedAttributeImage;
            if (attributeImage == null && !string.IsNullOrWhiteSpace(urls.AttributeImage))
            {
                attributeImage = await DownloadCacheAndCreateBitmapAsync(urls.AttributeImage,
                    bitmap => _cacheService.SaveAttributeAsync(attrKey, bitmap)).ConfigureAwait(false);
            }

            var starImages = new List<BitmapImage>();
            if (cachedStarImage != null)
            {
                for (int i = 0; i < Math.Max(card.Rarity, 0); i++)
                {
                    starImages.Add(cachedStarImage);
                }
            }
            else if (urls.RarityStars.Count > 0)
            {
                var starImage = await DownloadCacheAndCreateBitmapAsync(urls.RarityStars[0],
                    bitmap => _cacheService.SaveStarAsync(afterTraining: starAfterTraining, bitmap)).ConfigureAwait(false);
                if (starImage != null)
                {
                    for (int i = 0; i < Math.Max(card.Rarity, 0); i++)
                    {
                        starImages.Add(starImage);
                    }
                }
            }

            await EnqueueOnUIAsync(dispatcherQueue, () =>
            {
                preview.CardImage = cardBitmapImage;
                preview.FrameImage = frameImage;
                preview.AttributeImage = attributeImage;
                preview.RarityStars.Clear();
                for (int i = starImages.Count - 1; i >= 0; i--)
                {
                    preview.RarityStars.Add(starImages[i]);
                }
                preview.LoadStatus = cachedCardImage != null ? "Cached" : "OK";
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] Load card preview failed: {ex.Message}");
            await UpdatePreviewStatusAsync(dispatcherQueue, preview, "Error");
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task UpdatePreviewStatusAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, DeckCardPreview preview, string status)
    {
        await EnqueueOnUIAsync(dispatcherQueue, () => preview.LoadStatus = status);
    }

    private async Task<BitmapImage?> LoadCachedBitmapImageAsync(string path, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            BitmapImage? image = null;
            await EnqueueOnUIAsync(dispatcherQueue, async () =>
            {
                image = await CreateBitmapImageFromBytesAsync(bytes);
            });
            return image;
        }
        catch
        {
            return null;
        }
    }

    private async Task<BitmapImage?> TryLoadFrameFromBase64Async(int rarity, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        if (rarity < 1 || rarity > 3) return null;

        var base64Path = _cacheService.GetFrameBase64Path(rarity);
        if (!File.Exists(base64Path)) return null;

        try
        {
            var content = await File.ReadAllTextAsync(base64Path).ConfigureAwait(false);
            var base64 = ExtractBase64Payload(content);
            if (string.IsNullOrWhiteSpace(base64)) return null;

            var bytes = Convert.FromBase64String(base64);
            var pngPath = _cacheService.GetFrameCachePath(rarity);
            if (!File.Exists(pngPath))
            {
                await File.WriteAllBytesAsync(pngPath, bytes).ConfigureAwait(false);
            }

            BitmapImage? image = null;
            await EnqueueOnUIAsync(dispatcherQueue, async () =>
            {
                image = await CreateBitmapImageFromBytesAsync(bytes);
            });
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractBase64Payload(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var trimmed = content.Trim();
        var marker = "base64,";
        var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return trimmed[(index + marker.Length)..].Trim();
        }
        return trimmed;
    }

    private async Task<BitmapImage?> DownloadCacheAndCreateBitmapAsync(string url, Func<SoftwareBitmap, Task<bool>> saveToCache)
    {
        try
        {
            var bitmap = await DownloadBitmapAsync(url).ConfigureAwait(false);
            if (bitmap == null) return null;

            await saveToCache(bitmap).ConfigureAwait(false);

            var pngBytes = await EncodeToPngBytesAsync(bitmap).ConfigureAwait(false);
            BitmapImage? result = null;
            await EnqueueOnUIAsync(async () =>
            {
                result = await CreateBitmapImageFromBytesAsync(pngBytes);
            });
            return result;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SoftwareBitmap?> DownloadBitmapAsync(string url)
    {
        try
        {
            var downloadedBytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            return await DecodeToSoftwareBitmapAsync(downloadedBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] Download failed {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task<SoftwareBitmap> DecodeToSoftwareBitmapAsync(byte[] bytes)
    {
        using var memStream = new InMemoryRandomAccessStream();
        using var dataWriter = new DataWriter(memStream);
        dataWriter.WriteBytes(bytes);
        await dataWriter.StoreAsync();
        memStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(memStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
        {
            softwareBitmap = SoftwareBitmap.Convert(
                softwareBitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
        }
        return softwareBitmap;
    }

    private static async Task<byte[]> EncodeToPngBytesAsync(SoftwareBitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        var size = (uint)stream.Size;
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task<BitmapImage> CreateBitmapImageFromBytesAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private async Task<List<DeckRecommendSekaiCard>> GetCardDatabaseAsync()
    {
        if (_cardDatabase != null)
        {
            return _cardDatabase;
        }

        await _cardDatabaseGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cardDatabase != null)
            {
                return _cardDatabase;
            }

            string json;
            var apiUrl = "https://sekai-world.github.io/sekai-master-db-diff/cards.json";
            try
            {
                json = await _http.GetStringAsync(apiUrl).ConfigureAwait(false);
            }
            catch
            {
                var backupPath = Path.Combine(AppContext.BaseDirectory, "Assets", "cards_backup.json");
                json = File.Exists(backupPath) ? await File.ReadAllTextAsync(backupPath).ConfigureAwait(false) : "[]";
            }
            _cardDatabase = ParseCardDatabaseJson(json);
            return _cardDatabase;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeckRecommend] GetCardDatabase failed: {ex.Message}");
            return new List<DeckRecommendSekaiCard>();
        }
        finally
        {
            _cardDatabaseGate.Release();
        }
    }

    private static List<DeckRecommendSekaiCard> ParseCardDatabaseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement cardsArray = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            cardsArray = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("cards", out cardsArray) && cardsArray.ValueKind == JsonValueKind.Array)
            {
            }
            else if (root.TryGetProperty("data", out cardsArray) && cardsArray.ValueKind == JsonValueKind.Array)
            {
            }
        }

        if (cardsArray.ValueKind != JsonValueKind.Array)
        {
        return new List<DeckRecommendSekaiCard>();
        }

        var list = new List<DeckRecommendSekaiCard>();
        foreach (var el in cardsArray.EnumerateArray())
        {
            if (TryParseCard(el, out var card))
            {
                list.Add(card);
            }
        }
        return list;
    }

    private static bool TryParseCard(JsonElement el, out DeckRecommendSekaiCard card)
    {
        card = new DeckRecommendSekaiCard();

        if (!TryGetInt(el, out var id, "id", "cardId", "cardID", "card_id"))
        {
            return false;
        }
        card.Id = id;

        card.AssetbundleName = TryGetString(el, "assetbundleName", "assetBundleName", "assetbundle_name") ?? string.Empty;
        card.Attr = NormalizeAttrValue(TryGetString(el, "attr", "attribute", "cardAttrType", "cardAttributeType", "card_attr", "card_attribute") ?? string.Empty);
        card.SupportUnit = TryGetString(el, "supportUnit", "support_unit") ?? string.Empty;
        card.CardRarityType = TryGetString(el, "cardRarityType", "rarityType") ?? string.Empty;

        if (TryGetInt(el, out var rarity, "rarity"))
        {
            card.Rarity = rarity;
        }
        else if (!string.IsNullOrWhiteSpace(card.CardRarityType))
        {
            card.Rarity = ParseTrailingInt(card.CardRarityType);
        }

        return true;
    }

    private static bool TryGetString(JsonElement el, out string? value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString();
                return true;
            }
        }
        value = null;
        return false;
    }

    private static string? TryGetString(JsonElement el, params string[] keys)
    {
        return TryGetString(el, out var value, keys) ? value : null;
    }

    private static bool TryGetInt(JsonElement el, out int value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!el.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            {
                return true;
            }
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
            {
                return true;
            }
        }
        value = 0;
        return false;
    }

    private static int ParseTrailingInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(value[i]))
            {
                var digits = value[(i + 1)..];
                return int.TryParse(digits, out var result) ? result : 0;
            }
        }
        return int.TryParse(value, out var v) ? v : 0;
    }

    private static string NormalizeAttrValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "cool";
        var v = value.Trim().ToLowerInvariant();
        string[] known = { "cute", "cool", "pure", "happy", "mysterious" };
        foreach (var k in known)
        {
            if (v == k) return k;
            if (v.EndsWith("_" + k, StringComparison.Ordinal)) return k;
        }
        return v;
    }

    private static bool ShouldUseAfterTrainingCard(DeckCardPreview preview)
    {
        if (preview.AfterTraining) return true;
        return IsSpecialTraining(preview.DefaultImageType);
    }

    private static bool ShouldUseAfterTrainingStar(DeckCardPreview preview, DeckRecommendSekaiCard card)
    {
        if (IsBirthdayCard(card)) return true;
        if (preview.AfterTraining) return true;
        return IsSpecialTraining(preview.DefaultImageType);
    }

    private static bool IsSpecialTraining(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Trim().Equals("special_training", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBirthdayCard(DeckRecommendSekaiCard card)
    {
        return !string.IsNullOrWhiteSpace(card.CardRarityType) &&
            card.CardRarityType.Contains("birthday", StringComparison.OrdinalIgnoreCase);
    }

    private static DeckRecommendCardImageUrls GetCardImageUrls(DeckRecommendSekaiCard card, bool afterTraining, bool starAfterTraining)
    {
        var suffix = afterTraining ? "_after_training" : "_normal";
        var baseUrl = "https://storage.sekai.best/sekai-jp-assets";
        var characterUrl = $"{baseUrl}/thumbnail/chara/{card.AssetbundleName}{suffix}.webp";
        var isBirthday = IsBirthdayCard(card);
        var frameUrl = isBirthday
            ? "https://sekai.best/assets/cardFrame_S_bd-CrsQsaNc.png"
            : card.Rarity >= 4
                ? "https://sekai.best/assets/cardFrame_S_4-DkwuVAqt.png"
                : string.Empty;

        var attrMap = new Dictionary<string, string>
        {
            ["cute"] = "icon_attribute_cute-BqKuT21a.png",
            ["cool"] = "icon_attribute_cool-Cm_EFAKA.png",
            ["pure"] = "icon_attribute_pure-DMCNUXNX.png",
            ["happy"] = "icon_attribute_happy-POeZUq3N.png",
            ["mysterious"] = "icon_attribute_mysterious-DRt6JUuH.png"
        };
        var attrKey = NormalizeAttrValue(card.Attr);
        var attrIcon = attrMap.GetValueOrDefault(attrKey, "icon_attribute_cool-Cm_EFAKA.png");
        var attributeUrl = $"https://sekai.best/assets/{attrIcon}";

        var starUrl = starAfterTraining
            ? "https://sekai.best/assets/rarity_star_afterTraining-CUlLhfpl.png"
            : "https://sekai.best/assets/rarity_star_normal-BYSplh9m.png";
        var rarityStars = Enumerable.Repeat(starUrl, Math.Max(card.Rarity, 0)).ToList();

        return new DeckRecommendCardImageUrls(characterUrl, frameUrl, attributeUrl, rarityStars);
    }

    private async Task<string> GetMusicTitleAsync(int musicId)
    {
        var map = await GetMusicNameMapAsync().ConfigureAwait(false);
        if (map != null && map.TryGetValue(musicId, out var title))
        {
            return title;
        }
        return musicId.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<int, string>?> GetMusicNameMapAsync()
    {
        if (_musicNameMap != null) return _musicNameMap;

        await _musicNameGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_musicNameMap != null) return _musicNameMap;
            var path = IOPath.Combine(AppContext.BaseDirectory, "Assets", "musics.json");
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var map = new Dictionary<int, string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) &&
                    el.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                {
                    map[id] = titleEl.GetString() ?? id.ToString(CultureInfo.InvariantCulture);
                }
            }
            _musicNameMap = map;
            return _musicNameMap;
        }
        catch
        {
            return null;
        }
        finally
        {
            _musicNameGate.Release();
        }
    }

    private Task EnqueueOnUIAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
    {
        if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private Task EnqueueOnUIAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Func<Task> action)
    {
        if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<bool>();
        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private Task EnqueueOnUIAsync(Action action)
    {
        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue == null)
        {
            return Task.CompletedTask;
        }
        return EnqueueOnUIAsync(dispatcherQueue, action);
    }

    private Task EnqueueOnUIAsync(Func<Task> action)
    {
        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue == null)
        {
            return Task.CompletedTask;
        }
        return EnqueueOnUIAsync(dispatcherQueue, action);
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (StatusText == null) return;
        StatusText.Text = message;
        if (isError)
        {
            StatusText.Foreground = new SolidColorBrush(Colors.IndianRed);
        }
        else
        {
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private void AppendStatus(string message, bool isError = false)
    {
        if (StatusText == null) return;
        var current = StatusText.Text ?? string.Empty;
        var next = string.IsNullOrWhiteSpace(current) ? message : current + Environment.NewLine + message;
        var lines = next.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length > MaxStatusLines)
        {
            next = string.Join(Environment.NewLine, lines[^MaxStatusLines..]);
        }
        StatusText.Text = next;
        if (isError)
        {
            StatusText.Foreground = new SolidColorBrush(Colors.IndianRed);
        }
        else
        {
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}

public class DeckRecommendEntry : System.ComponentModel.INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _summaryLine = string.Empty;
    private string _scoreLine = string.Empty;

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
            }
        }
    }

    public int TotalPower { get; set; }

    public double EventBonusRate { get; set; }

    public string SummaryLine
    {
        get => _summaryLine;
        set
        {
            if (_summaryLine != value)
            {
                _summaryLine = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SummaryLine)));
            }
        }
    }

    public string ScoreLine
    {
        get => _scoreLine;
        set
        {
            if (_scoreLine != value)
            {
                _scoreLine = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ScoreLine)));
            }
        }
    }

    public ObservableCollection<DeckCardPreview> CardPreviews { get; set; } = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class DeckCardPreview : System.ComponentModel.INotifyPropertyChanged
{
    private BitmapImage? _cardImage;
    private BitmapImage? _frameImage;
    private BitmapImage? _attributeImage;
    private string _loadStatus = "Pending";

    public int CardId { get; set; }

    public bool AfterTraining { get; set; }

    public string DefaultImageType { get; set; } = string.Empty;

    public int SkillScoreUp { get; set; }

    public BitmapImage? CardImage
    {
        get => _cardImage;
        set
        {
            if (_cardImage != value)
            {
                _cardImage = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CardImage)));
            }
        }
    }

    public BitmapImage? FrameImage
    {
        get => _frameImage;
        set
        {
            if (_frameImage != value)
            {
                _frameImage = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(FrameImage)));
            }
        }
    }

    public BitmapImage? AttributeImage
    {
        get => _attributeImage;
        set
        {
            if (_attributeImage != value)
            {
                _attributeImage = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AttributeImage)));
            }
        }
    }

    public ObservableCollection<BitmapImage> RarityStars { get; } = new();

    public string LoadStatus
    {
        get => _loadStatus;
        set
        {
            if (_loadStatus != value)
            {
                _loadStatus = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LoadStatus)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal class DeckRecommendSekaiCard
{
    public int Id { get; set; }
    public string AssetbundleName { get; set; } = string.Empty;
    public int Rarity { get; set; }
    public string Attr { get; set; } = string.Empty;
    public string SupportUnit { get; set; } = string.Empty;
    public string CardRarityType { get; set; } = string.Empty;
}

internal record DeckRecommendCardImageUrls(
    string CharacterImage,
    string FrameImage,
    string AttributeImage,
    List<string> RarityStars);

internal record ScoreSong(int MusicId, string Difficulty);
