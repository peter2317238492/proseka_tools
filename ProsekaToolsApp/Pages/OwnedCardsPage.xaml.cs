using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ProsekaToolsApp.Services;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using IOPath = System.IO.Path;

namespace ProsekaToolsApp.Pages;

public sealed partial class OwnedCardsPage : Page
{
	private const int MaxStatusLines = 200;
	private string? _selectedFile;
	private string? _lastLoadedJson;
	private int _cardLoadVersion;
	private volatile bool _isUnloading;
	private readonly ObservableCollection<OwnedCardEntry> _cards = new();
	private readonly CardImageCacheService _cacheService = new();
	private readonly SemaphoreSlim _cardDatabaseGate = new(1, 1);
	private List<SekaiCard>? _cardDatabase;
	private readonly SemaphoreSlim _attributeUrlGate = new(1, 1);
	private readonly Dictionary<string, string> _attributeUrlCache = new();

	private readonly HttpClient _http;



	public OwnedCardsPage()
	{
		InitializeComponent();
		Unloaded += (_, __) =>
		{
			_isUnloading = true;
			Interlocked.Increment(ref _cardLoadVersion);
		};
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

		if (CardsGrid != null)
		{
			CardsGrid.ItemsSource = _cards;
		}
	}

	private CheckBox? GetUseLatestCheckBox() => FindName("UseLatestCheckBox") as CheckBox;
	private TextBox? GetSelectedFileTextBox() => FindName("SelectedFileTextBox") as TextBox;
	private ProgressRing? GetWorkingRing() => FindName("WorkingRing") as ProgressRing;
	private ProgressRing? GetCardWorkingRing() => FindName("CardWorkingRing") as ProgressRing;
	private TextBlock? GetStatusTextBlock() => FindName("StatusText") as TextBlock;
	private ComboBox? GetRegionCombo() => FindName("RegionCombo") as ComboBox;
	private GridView? GetCardsGrid() => FindName("CardsGrid") as GridView;

	private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
	{
		if (GetUseLatestCheckBox()?.IsChecked == true)
		{
			var latest = TryGetLatestCapture();
			if (latest is null)
			{
				SetStatus("未在 captures/suite 找到文件。", isError: true);
				return;
			}
			_selectedFile = latest;
			var tb = GetSelectedFileTextBox();
			if (tb != null) tb.Text = _selectedFile;
			SetStatus($"已选择最新: {IOPath.GetFileName(_selectedFile)}");
			return;
		}

		var picker = new FileOpenPicker();
		picker.FileTypeFilter.Add(".bin");
		picker.FileTypeFilter.Add(".json");
		picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
		var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
		InitializeWithWindow.Initialize(picker, hwnd);
		var file = await picker.PickSingleFileAsync();
		if (file != null)
		{
			_selectedFile = file.Path;
			var tb = GetSelectedFileTextBox();
			if (tb != null) tb.Text = _selectedFile;
			SetStatus($"已选择: {file.Name}");
		}
	}

	private string? TryGetLatestCapture()
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
		catch { return null; }
	}

	private async void StartDecryptButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var ring = GetWorkingRing();
			if (ring != null) ring.IsActive = true;
			var st = GetStatusTextBlock();
			if (st != null) st.Text = string.Empty;

			string? inputFile = _selectedFile;
			if (GetUseLatestCheckBox()?.IsChecked == true || string.IsNullOrWhiteSpace(inputFile))
			{
				inputFile = TryGetLatestCapture();
			}
			if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
			{
				SetStatus("未选择有效的输入文件。", isError: true);
				return;
			}

			var regionCb = GetRegionCombo();
			var region = (regionCb?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "jp";

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

			bool launchedViaFullTrust = await TryLaunchFullTrustAsync();
			if (launchedViaFullTrust)
			{
				var okOut = await WaitForFileAsync(outputPath, TimeSpan.FromSeconds(40));
				if (okOut && File.Exists(outputPath))
				{
					SetStatus($"解密完成: {outputPath}");
					await LoadJsonAndPopulateCardsAsync(outputPath);
				}
				else
				{
					SetStatus("已通过 FullTrust 启动辅助进程。若未生成文件，请确认辅助程序支持在无命令行参数时的工作方式。", isError: true);
				}
				return;
			}

#if DISABLE_EXTERNAL_PROCESS
			SetStatus("当前构建禁用了外部进程启动。请手动将 JSON 放入输出目录后使用‘载入最新JSON’。", isError: true);
			return;
#else
			var ok = await RunProcessAsync(exePath, args);
			if (ok && File.Exists(outputPath))
			{
				SetStatus($"解密完成: {outputPath}");
				await LoadJsonAndPopulateCardsAsync(outputPath);
			}
			else
			{
				SetStatus("解密失败，请检查输入文件和 region。", isError: true);
			}
#endif
		}
		finally
		{
			var ring = GetWorkingRing();
			if (ring != null) ring.IsActive = false;
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
				WorkingDirectory = IOPath.GetDirectoryName(exePath)!,
			};
			var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
			var tcs = new TaskCompletionSource<int>();
			p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) SetStatus(e.Data); };
			p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) SetStatus(e.Data, isError: true); };
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
#else
	private Task<bool> RunProcessAsync(string exePath, string arguments)
	{
		return Task.FromResult(false);
	}
#endif

	private async void LoadLatestJsonButton_Click(object sender, RoutedEventArgs e)
	{
		var latest = TryGetLatestJson();
		if (latest == null)
		{
			SetStatus("未在 output/owned_cards 找到 JSON。", isError: true);
			return;
		}
		await LoadJsonAndPopulateCardsAsync(latest);
	}

	private async void ReloadCardsButton_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_lastLoadedJson))
		{
			SetStatus("尚未加载 JSON。", isError: true);
			return;
		}
		await LoadJsonAndPopulateCardsAsync(_lastLoadedJson);
	}

	private string? TryGetLatestJson()
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
		catch { return null; }
	}

	private async Task LoadJsonAndPopulateCardsAsync(string path)
	{
		try
		{
			_lastLoadedJson = path;
			var json = await File.ReadAllTextAsync(path);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!TryGetUserCards(root, out var cardsArray))
			{
				SetStatus("JSON 缺少 userCards。", isError: true);
				return;
			}

			var ids = new HashSet<int>();
			foreach (var el in cardsArray.EnumerateArray())
			{
				if (TryGetCardId(el, out var id)) ids.Add(id);
			}

			_cards.Clear();
			foreach (var id in ids.OrderBy(i => i))
			{
				_cards.Add(new OwnedCardEntry { CardId = id, LoadStatus = "Pending" });
			}

			var grid = GetCardsGrid();
			if (grid != null) grid.ItemsSource = _cards;

			SetStatus($"已加载 JSON: {IOPath.GetFileName(path)} (cards: {ids.Count})");
			await LoadCardImagesAsync(_cards.ToList());
		}
		catch (Exception ex)
		{
			SetStatus(ex.Message, isError: true);
		}
	}

	private static bool TryGetUserCards(JsonElement root, out JsonElement cardsArray)
	{
		if (root.TryGetProperty("userCards", out cardsArray) && cardsArray.ValueKind == JsonValueKind.Array) return true;
		if (root.TryGetProperty("updatedResources", out var updated) && updated.TryGetProperty("userCards", out cardsArray) && cardsArray.ValueKind == JsonValueKind.Array) return true;
		cardsArray = default;
		return false;
	}

	private static bool TryGetCardId(JsonElement el, out int cardId)
	{
		string[] keys = { "cardId", "cardID", "CardId", "CardID" };
		foreach (var key in keys)
		{
			if (el.TryGetProperty(key, out var idEl) && idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out cardId))
			{
				return true;
			}
		}
		cardId = 0;
		return false;
	}

	private async Task LoadCardImagesAsync(List<OwnedCardEntry> cards)
	{
		var ring = GetCardWorkingRing();
		if (ring != null) ring.IsActive = true;

		int version = Interlocked.Increment(ref _cardLoadVersion);
		var sem = new SemaphoreSlim(4);
		var tasks = cards.Select(c => LoadCardImageAsync(c, sem, version)).ToArray();
		await Task.WhenAll(tasks);

		if (version == _cardLoadVersion)
		{
			if (ring != null) ring.IsActive = false;
		}
	}

	private async Task LoadCardImageAsync(OwnedCardEntry entry, SemaphoreSlim sem, int version)
	{
		// 在方法开始时捕获 DispatcherQueue 引用,以便在后台线程上也能使用
		var dispatcherQueue = DispatcherQueue;
		await sem.WaitAsync();
		try
		{
			if (version != _cardLoadVersion) return;
			
			await UpdateEntryStatusAsync(dispatcherQueue, entry, "Loading...");

			var database = await GetCardDatabaseAsync().ConfigureAwait(false);
			var card = database.FirstOrDefault(c => c.Id == entry.CardId);
			if (card == null)
			{
				await UpdateEntryStatusAsync(dispatcherQueue, entry, "Card not found");
				return;
			}
			var attrKey = NormalizeAttrValue(card.Attr);
			AppendStatus($"[OwnedCards] Match id={card.Id} attr='{card.Attr}' normalized='{attrKey}'");

			var urls = GetCardImageUrls(card, afterTraining: false);
			
			// Try to load from cache first (BitmapImage must be created on UI thread)
			var cachedCardImage = await LoadCachedBitmapImageAsync(_cacheService.GetCachePath(entry.CardId), dispatcherQueue).ConfigureAwait(false);
			var cachedFrameImage = await LoadCachedBitmapImageAsync(_cacheService.GetFrameCachePath(card.Rarity), dispatcherQueue).ConfigureAwait(false);
			var cachedAttributeImage = await LoadCachedBitmapImageAsync(_cacheService.GetAttributeCachePath(attrKey), dispatcherQueue).ConfigureAwait(false);
			var cachedStarImage = await LoadCachedBitmapImageAsync(_cacheService.GetStarCachePath(afterTraining: false), dispatcherQueue).ConfigureAwait(false);
			
			// Download card image if not cached
			BitmapImage? cardBitmapImage = cachedCardImage;
			if (cardBitmapImage == null)
			{
				var bitmap = await DownloadBitmapAsync(urls.CharacterImage).ConfigureAwait(false);
				if (bitmap == null)
				{
					await UpdateEntryStatusAsync(dispatcherQueue, entry, "Download failed");
					return;
				}

				await _cacheService.SaveCompositedImageAsync(entry.CardId, bitmap).ConfigureAwait(false);
				var pngBytes = await EncodeToPngBytesAsync(bitmap).ConfigureAwait(false);

				await EnqueueOnUIAsync(dispatcherQueue, async () =>
				{
					cardBitmapImage = await CreateBitmapImageFromBytesAsync(pngBytes);
				});
			}
			
			// Download and cache decorative images if not cached
			BitmapImage? frameImage = cachedFrameImage;
			if (frameImage == null)
			{
				frameImage = await DownloadCacheAndCreateBitmapAsync(urls.FrameImage, 
					bitmap => _cacheService.SaveFrameAsync(card.Rarity, bitmap));
			}
			
			BitmapImage? attributeImage = cachedAttributeImage;
			string? resolvedAttributeUrl = null;
			if (attributeImage == null)
			{
				attributeImage = await DownloadCacheAndCreateBitmapAsync(urls.AttributeImage,
					bitmap => _cacheService.SaveAttributeAsync(attrKey, bitmap));
				if (attributeImage == null)
				{
					resolvedAttributeUrl = await GetAttributeImageUrlAsync(card.Id, attrKey).ConfigureAwait(false);
					if (!string.IsNullOrWhiteSpace(resolvedAttributeUrl))
					{
						attributeImage = await DownloadCacheAndCreateBitmapAsync(resolvedAttributeUrl,
							bitmap => _cacheService.SaveAttributeAsync(attrKey, bitmap));
					}
				}
			}
			if (attributeImage == null)
			{
				var msg = $"[OwnedCards] Attr icon failed id={card.Id} attr='{attrKey}' url='{urls.AttributeImage}'";
				if (!string.IsNullOrWhiteSpace(resolvedAttributeUrl))
				{
					msg += $" resolved='{resolvedAttributeUrl}'";
				}
				await EnqueueOnUIAsync(dispatcherQueue, () => AppendStatus(msg, isError: true));
			}
			
			// Download and cache star images
			var starImages = new List<BitmapImage>();
			if (cachedStarImage != null)
			{
				// Use cached star for all stars
				for (int i = 0; i < urls.RarityStars.Count; i++)
				{
					starImages.Add(cachedStarImage);
				}
			}
			else if (urls.RarityStars.Count > 0)
			{
				// Download and cache the star image once
				var starImage = await DownloadCacheAndCreateBitmapAsync(urls.RarityStars[0],
					bitmap => _cacheService.SaveStarAsync(afterTraining: false, bitmap));
				
				if (starImage != null)
				{
					// Use same star image for all stars
					for (int i = 0; i < urls.RarityStars.Count; i++)
					{
						starImages.Add(starImage);
					}
				}
			}
			
			// Set all properties on UI thread
			await EnqueueOnUIAsync(dispatcherQueue, () =>
			{
				entry.CardImage = cardBitmapImage;
				entry.Rarity = card.Rarity;
				entry.Attribute = card.Attr;
				entry.FrameImage = frameImage;
				entry.AttributeImage = attributeImage;
				
				entry.RarityStars.Clear();
				// Add stars in reverse order so they stack from bottom to top
				for (int i = starImages.Count - 1; i >= 0; i--)
				{
					entry.RarityStars.Add(starImages[i]);
				}
				
				entry.LoadStatus = cachedCardImage != null ? "Cached" : "OK";
			});
		}
		catch (Exception ex)
		{
			await UpdateEntryStatusAsync(dispatcherQueue, entry, $"Error: {ex.GetType().Name}");
		}
		finally
		{
			sem.Release();
		}
	}

	private async Task UpdateEntryStatusAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, OwnedCardEntry entry, string status)
	{
		await EnqueueOnUIAsync(dispatcherQueue, () =>
		{
			entry.LoadStatus = status;
		});
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

	private async Task UpdateEntryImageAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, OwnedCardEntry entry, BitmapImage image, string status)
	{
		await EnqueueOnUIAsync(dispatcherQueue, () =>
		{
			entry.CardImage = image;
			entry.LoadStatus = status;
		});
	}

	private async Task EnqueueOnUIAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Action action)
	{
		if (_isUnloading || dispatcherQueue == null) return;
		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcherQueue.TryEnqueue(() =>
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
			}))
		{
			tcs.TrySetResult(false);
		}
		await tcs.Task;
	}

	private async Task EnqueueOnUIAsync(Action action)
	{
		await EnqueueOnUIAsync(DispatcherQueue, action);
	}

	private async Task EnqueueOnUIAsync(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, Func<Task> action)
	{
		if (_isUnloading || dispatcherQueue == null) return;
		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcherQueue.TryEnqueue(async () =>
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
			}))
		{
			tcs.TrySetResult(false);
		}
		await tcs.Task;
	}

	private async Task EnqueueOnUIAsync(Func<Task> action)
	{
		await EnqueueOnUIAsync(DispatcherQueue, action);
	}

	private async Task<SoftwareBitmap?> RenderCompositeAsync(SvgCardData data, List<(SvgLayerData layer, SoftwareBitmap bitmap)> layerBitmaps)
	{
		SoftwareBitmap? composite = null;
		await EnqueueOnUIAsync(async () =>
		{
			var canvas = new Canvas { Width = data.Width, Height = data.Height };
			foreach (var (layer, bitmap) in layerBitmaps)
			{
				var img = new Image
				{
					Width = layer.Width,
					Height = layer.Height,
					Stretch = Stretch.UniformToFill
				};
				var source = new SoftwareBitmapSource();
				await source.SetBitmapAsync(bitmap);
				img.Source = source;
				Canvas.SetLeft(img, layer.X);
				Canvas.SetTop(img, layer.Y);
				canvas.Children.Add(img);
			}

			canvas.Measure(new Size(data.Width, data.Height));
			canvas.Arrange(new Rect(0, 0, data.Width, data.Height));
			canvas.UpdateLayout();

			var renderBitmap = new RenderTargetBitmap();
			await renderBitmap.RenderAsync(canvas);
			var buffer = await renderBitmap.GetPixelsAsync();
			composite = SoftwareBitmap.CreateCopyFromBuffer(
				buffer,
				BitmapPixelFormat.Bgra8,
				renderBitmap.PixelWidth,
				renderBitmap.PixelHeight,
				BitmapAlphaMode.Premultiplied);
		});
		return composite;
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

	private async Task<BitmapImage?> DownloadCacheAndCreateBitmapAsync(string url, Func<SoftwareBitmap, Task<bool>> saveToCache)
	{
		try
		{
			var bitmap = await DownloadBitmapAsync(url).ConfigureAwait(false);
			if (bitmap == null) return null;
			
			// Save to cache before creating BitmapImage to avoid concurrent access to SoftwareBitmap.
			await saveToCache(bitmap).ConfigureAwait(false);
			
			var pngBytes = await EncodeToPngBytesAsync(bitmap).ConfigureAwait(false);

			// Must create BitmapImage on UI thread
			BitmapImage? result = null;
			await EnqueueOnUIAsync(async () =>
			{
				result = await CreateBitmapImageFromBytesAsync(pngBytes);
			});
			return result;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] Failed to download/cache/create image from {url}: {ex.Message}");
			return null;
		}
	}

	private async Task<BitmapImage?> DownloadAndCreateBitmapImageAsync(string url)
	{
		try
		{
			var bitmap = await DownloadBitmapAsync(url).ConfigureAwait(false);
			if (bitmap == null) return null;
			
			var pngBytes = await EncodeToPngBytesAsync(bitmap).ConfigureAwait(false);

			// Must create BitmapImage on UI thread
			BitmapImage? result = null;
			await EnqueueOnUIAsync(async () =>
			{
				result = await CreateBitmapImageFromBytesAsync(pngBytes);
			});
			return result;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] Failed to download/create image from {url}: {ex.Message}");
			return null;
		}
	}

	private async Task<List<SekaiCard>> GetCardDatabaseAsync()
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
			Debug.WriteLine($"[OwnedCards] GetCardDatabase failed: {ex.Message}");
			return new List<SekaiCard>();
		}
		finally
		{
			_cardDatabaseGate.Release();
		}
	}

	private static CardImageUrls GetCardImageUrls(SekaiCard card, bool afterTraining)
	{
		var suffix = afterTraining ? "_after_training" : "_normal";
		var baseUrl = "https://storage.sekai.best/sekai-jp-assets";
		var characterUrl = $"{baseUrl}/thumbnail/chara/{card.AssetbundleName}{suffix}.webp";
		var frameUrl = $"https://sekai.best/assets/cardFrame_S_{card.Rarity}-DkwuVAqt.png";

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

		var starUrl = afterTraining
			? "https://sekai.best/assets/rarity_star_afterTraining-CUlLhfpl.png"
			: "https://sekai.best/assets/rarity_star_normal-BYSplh9m.png";
		var rarityStars = Enumerable.Repeat(starUrl, Math.Max(card.Rarity, 0)).ToList();

		return new CardImageUrls(characterUrl, frameUrl, attributeUrl, rarityStars);
	}

	private static List<SekaiCard> ParseCardDatabaseJson(string json)
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
				// ok
			}
			else if (root.TryGetProperty("data", out cardsArray) && cardsArray.ValueKind == JsonValueKind.Array)
			{
				// ok
			}
		}

		if (cardsArray.ValueKind != JsonValueKind.Array)
		{
			return new List<SekaiCard>();
		}

		var list = new List<SekaiCard>();
		foreach (var el in cardsArray.EnumerateArray())
		{
			if (TryParseCard(el, out var card))
			{
				list.Add(card);
			}
		}
		return list;
	}

	private static bool TryParseCard(JsonElement el, out SekaiCard card)
	{
		card = new SekaiCard();

		if (!TryGetInt(el, out var id, "id", "cardId", "cardID", "card_id"))
		{
			return false;
		}
		card.Id = id;

		card.AssetbundleName = TryGetString(el, "assetbundleName", "assetBundleName", "assetbundle_name") ?? string.Empty;
		card.Attr = NormalizeAttrValue(TryGetString(el, "attr", "attribute", "cardAttrType", "cardAttributeType", "card_attr", "card_attribute") ?? string.Empty);
		card.SupportUnit = TryGetString(el, "supportUnit", "support_unit") ?? string.Empty;

		if (TryGetInt(el, out var rarity, "rarity"))
		{
			card.Rarity = rarity;
		}
		else if (TryGetString(el, out var rarityText, "cardRarityType", "rarityType"))
		{
			card.Rarity = ParseTrailingInt(rarityText);
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

	private async Task<string?> GetAttributeImageUrlAsync(int cardId, string attrKey)
	{
		if (string.IsNullOrWhiteSpace(attrKey)) return null;
		if (_attributeUrlCache.TryGetValue(attrKey, out var cached)) return cached;

		await _attributeUrlGate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_attributeUrlCache.TryGetValue(attrKey, out cached)) return cached;
			var resolved = await TryResolveAttributeIconUrlFromCardPageAsync(cardId, attrKey).ConfigureAwait(false);
			if (!string.IsNullOrWhiteSpace(resolved))
			{
				_attributeUrlCache[attrKey] = resolved;
			}
			return resolved;
		}
		finally
		{
			_attributeUrlGate.Release();
		}
	}

	private async Task<string?> TryResolveAttributeIconUrlFromCardPageAsync(int cardId, string attrKey)
	{
		try
		{
			var url = $"https://sekai.best/card/{cardId}";
			var html = await _http.GetStringAsync(url).ConfigureAwait(false);

			var escapedAttr = Regex.Escape(attrKey);
			var fullPattern = $@"https://sekai\.best/assets/[^""']*icon_attribute_{escapedAttr}[^""']*\.png";
			var match = Regex.Match(html, fullPattern, RegexOptions.IgnoreCase);
			if (match.Success) return match.Value;

			var relativePattern = $@"icon_attribute_{escapedAttr}[^""']*\.png";
			match = Regex.Match(html, relativePattern, RegexOptions.IgnoreCase);
			if (match.Success)
			{
				return "https://sekai.best/assets/" + match.Value.TrimStart('/');
			}

			return null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] Resolve attribute icon failed: {ex.Message}");
			return null;
		}
	}

	private async Task<SvgCardData?> FetchSvgDataAsync(int cardId)
	{
		try
		{
			var url = $"https://sekai.best/card/{cardId}";
			Debug.WriteLine($"[OwnedCards] Fetching URL: {url}");
			var html = await _http.GetStringAsync(url).ConfigureAwait(false);
			Debug.WriteLine($"[OwnedCards] Downloaded HTML length: {html?.Length ?? 0}");
			var debugPath = Path.Combine(AppPaths.OutputOwnedCardsDir, $"debug_card_{cardId}.html");
			try
			{
				await File.WriteAllTextAsync(debugPath, html);
				Debug.WriteLine($"[OwnedCards] Saved HTML to: {debugPath}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[OwnedCards] Failed to save HTML: {ex.Message}");
			}
			var svg = ExtractSvg(html);
			if (svg == null)
			{
				Debug.WriteLine($"[OwnedCards] ExtractSvg returned null for card {cardId}");
				var preview = html.Length > 1000 ? html.Substring(0, 1000) : html;
				Debug.WriteLine($"[OwnedCards] HTML preview: {preview}");
				return null;
			}
			Debug.WriteLine($"[OwnedCards] Extracted SVG length: {svg.Length}");
			var svgPath = Path.Combine(AppPaths.OutputOwnedCardsDir, $"debug_card_{cardId}.svg");
			try
			{
				await File.WriteAllTextAsync(svgPath, svg);
				Debug.WriteLine($"[OwnedCards] Saved SVG to: {svgPath}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[OwnedCards] Failed to save SVG: {ex.Message}");
			}
			return ParseSvg(svg);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] FetchSvgData failed for card {cardId}: {ex.Message}");
			Debug.WriteLine($"[OwnedCards] Exception type: {ex.GetType().Name}");
			Debug.WriteLine($"[OwnedCards] Stack trace: {ex.StackTrace}");
			return null;
		}
	}

	private static string? ExtractSvg(string html)
	{
		try
		{
			Debug.WriteLine($"[OwnedCards] ExtractSvg called, HTML length: {html?.Length ?? 0}");
			if (string.IsNullOrEmpty(html))
			{
				Debug.WriteLine("[OwnedCards] HTML is null or empty");
				return null;
			}
			bool hasViewBox = html.Contains("viewBox=\"0 0 156 156\"", StringComparison.OrdinalIgnoreCase);
			bool hasSvgTag = html.Contains("<svg", StringComparison.OrdinalIgnoreCase);
			bool hasImageTag = html.Contains("<image", StringComparison.OrdinalIgnoreCase);
			Debug.WriteLine($"[OwnedCards] Has viewBox: {hasViewBox}, Has <svg>: {hasSvgTag}, Has <image>: {hasImageTag}");
			var allSvgMatches = Regex.Matches(html, @"<svg[^>]*>", RegexOptions.IgnoreCase);
			Debug.WriteLine($"[OwnedCards] Found {allSvgMatches.Count} SVG tags");
			var pattern = @"<svg[^>]*viewBox=""0 0 156 156""[^>]*>.*?</svg>";
			var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (match.Success)
			{
				Debug.WriteLine($"[OwnedCards] Found SVG with viewBox, length: {match.Value.Length}");
				return match.Value;
			}
			Debug.WriteLine("[OwnedCards] No SVG found with viewBox pattern");
			pattern = @"<svg[^>]*xmlns[^>]*>.*?thumbnail.*?</svg>";
			match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
			if (match.Success)
			{
				Debug.WriteLine($"[OwnedCards] Found SVG with 'thumbnail', length: {match.Value.Length}");
				return match.Value;
			}
			return null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] ExtractSvg error: {ex.Message}");
			Debug.WriteLine($"[OwnedCards] Stack trace: {ex.StackTrace}");
			return null;
		}
	}

	private static SvgCardData? ParseSvg(string svg)
	{
		var viewBoxMatch = Regex.Match(svg, @"viewBox\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
		double width = 156;
		double height = 156;
		if (viewBoxMatch.Success)
		{
			var parts = viewBoxMatch.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 4)
			{
				double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out width);
				double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out height);
			}
		}

		var matches = Regex.Matches(svg, "<image[^>]*>", RegexOptions.IgnoreCase);
		var layers = new List<SvgLayerData>();
		foreach (Match m in matches)
		{
			var tag = m.Value;
			var href = GetAttribute(tag, "href") ?? GetAttribute(tag, "xlink:href");
			if (string.IsNullOrWhiteSpace(href)) continue;
			var x = ParseDouble(GetAttribute(tag, "x"));
			var y = ParseDouble(GetAttribute(tag, "y"));
			var w = ParseDouble(GetAttribute(tag, "width"));
			var h = ParseDouble(GetAttribute(tag, "height"));
			layers.Add(new SvgLayerData
			{
				Url = NormalizeUrl(href),
				X = x,
				Y = y,
				Width = w,
				Height = h
			});
		}

		return new SvgCardData { Width = width, Height = height, Layers = layers };
	}

	private static string? GetAttribute(string tag, string name)
	{
		var match = Regex.Match(tag, name + @"\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
		return match.Success ? match.Groups[1].Value : null;
	}

	private static double ParseDouble(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return 0;
		if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
		return 0;
	}

	private static string NormalizeUrl(string href)
	{
		if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return href;
		}
		if (href.StartsWith("//", StringComparison.Ordinal))
		{
			return "https:" + href;
		}
		if (href.StartsWith("/", StringComparison.Ordinal))
		{
			return "https://sekai.best" + href;
		}
		return "https://sekai.best/" + href.TrimStart('/');
	}

	private async Task<SoftwareBitmap?> DownloadBitmapAsync(string url)
	{
		try
		{
			if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
			{
				var bytes = TryDecodeDataUri(url);
				if (bytes == null) return null;
				return await DecodeToSoftwareBitmapAsync(bytes).ConfigureAwait(false);
			}
			var downloadedBytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
			return await DecodeToSoftwareBitmapAsync(downloadedBytes).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[OwnedCards] Download failed {url}: {ex.Message}");
			return null;
		}
	}

	private static byte[]? TryDecodeDataUri(string url)
	{
		const string base64Token = ";base64,";
		int idx = url.IndexOf(base64Token, StringComparison.OrdinalIgnoreCase);
		if (idx < 0) return null;
		var data = url.Substring(idx + base64Token.Length);
		if (string.IsNullOrWhiteSpace(data)) return null;
		try
		{
			return Convert.FromBase64String(data);
		}
		catch
		{
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

internal class SekaiCard
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("assetbundleName")]
	public string AssetbundleName { get; set; } = string.Empty;

	[JsonPropertyName("rarity")]
	public int Rarity { get; set; }

	[JsonPropertyName("attr")]
	public string Attr { get; set; } = string.Empty;

	[JsonPropertyName("supportUnit")]
	public string SupportUnit { get; set; } = string.Empty;
}

internal record CardImageUrls(
	string CharacterImage,
	string FrameImage,
	string AttributeImage,
	List<string> RarityStars);

internal class SvgCardData
{
	public double Width { get; set; }
	public double Height { get; set; }
	public List<SvgLayerData> Layers { get; set; } = new();
}

internal class SvgLayerData
{
	public string Url { get; set; } = string.Empty;
	public double X { get; set; }
	public double Y { get; set; }
	public double Width { get; set; }
	public double Height { get; set; }
}

public class OwnedCardEntry : System.ComponentModel.INotifyPropertyChanged
{
	private BitmapImage? _cardImage;
	private string _loadStatus = "Pending";
	private int _rarity;
	private string _attribute = string.Empty;
	private BitmapImage? _frameImage;
	private BitmapImage? _attributeImage;
	private readonly ObservableCollection<BitmapImage> _rarityStars = new();

	public int CardId { get; set; }

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

	public int Rarity
	{
		get => _rarity;
		set
		{
			if (_rarity != value)
			{
				_rarity = value;
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Rarity)));
			}
		}
	}

	public string Attribute
	{
		get => _attribute;
		set
		{
			if (_attribute != value)
			{
				_attribute = value;
				PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Attribute)));
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

	public ObservableCollection<BitmapImage> RarityStars
	{
		get => _rarityStars;
	}

	public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
