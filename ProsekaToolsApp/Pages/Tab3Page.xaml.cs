using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System.Text.Json;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml.Shapes;
using IOPath = System.IO.Path;
using ProsekaToolsApp.Services;
using Windows.Foundation.Metadata;
using Windows.ApplicationModel;
using System.Net.Http;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ProsekaToolsApp.Pages;

public sealed partial class Tab3Page : Page
{
	private string? _selectedFile;

	// Data for map drawing
	private Dictionary<int, MapScene> _scenes = MapScene.CreateDefaults();
	private Dictionary<int, MapData> _maps = new();
	private int _currentSiteId = 1;

	// music record collection for left list
	private readonly List<MusicRecordEntry> _musicRecords = new();
	private readonly HashSet<int> _collectedMusicRecordIds = new();
	private bool _hasCollectedMusicRecordData;
	
	// Configured HttpClient with proper headers
	private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
	})
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	static Tab3Page()
	{
		// Add browser-like headers to avoid being blocked
		_http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
		_http.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
		_http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");
		_http.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
		_http.DefaultRequestHeaders.Add("Pragma", "no-cache");
	}

	public Tab3Page()
	{
		InitializeComponent();
		InitRightSideDefaults();
	}

	private void InitRightSideDefaults()
	{
		var combo = GetMapSelectCombo();
		if (combo == null) return;
		combo.Items.Clear();
		foreach (var kv in _scenes)
		{
			combo.Items.Add(new ComboBoxItem { Content = $"{kv.Key}: {kv.Value.Name}", Tag = kv.Key });
		}
		combo.SelectedIndex = 0;
	}

	// XAML element getters
	private CheckBox? GetUseLatestCheckBox() => FindName("UseLatestCheckBox") as CheckBox;
	private TextBox? GetSelectedFileTextBox() => FindName("SelectedFileTextBox") as TextBox;
	private ProgressRing? GetWorkingRing() => FindName("WorkingRing") as ProgressRing;
	private TextBlock? GetStatusTextBlock() => FindName("StatusText") as TextBlock;
	private ComboBox? GetRegionCombo() => FindName("RegionCombo") as ComboBox;
	private ComboBox? GetMapSelectCombo() => FindName("MapSelectCombo") as ComboBox;
	private Canvas? GetMapCanvas() => FindName("MapCanvas") as Canvas;
	private Image? GetMapImage() => FindName("MapImage") as Image;
	private ScrollViewer? GetMapScrollViewer() => FindName("MapScrollViewer") as ScrollViewer;
	private CheckBox? GetShowIconsCheckBox() => FindName("ShowIconsCheckBox") as CheckBox;
	private ListView? GetMusicRecordsList() => FindName("MusicRecordsList") as ListView;

	private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
	{
		if (GetUseLatestCheckBox()?.IsChecked == true)
		{
			var latest = TryGetLatestCapture();
			if (latest is null)
			{
				SetStatus("未在 captures/mysekai 找到文件。", isError: true);
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
			var dir = AppPaths.CapturesMysekaiDir;
			if (!Directory.Exists(dir)) return null;
			var latest = Directory.GetFiles(dir)
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

			var outputDir = AppPaths.OutputMysekaiDir;
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

			// Prefer packaged full trust launcher when available (Store/S mode friendly)
			bool launchedViaFullTrust = await TryLaunchFullTrustAsync();
			if (launchedViaFullTrust)
			{
				// 不能动态传参时，提醒用户或等待外部工具按约定路径输出
				var okOut = await WaitForFileAsync(outputPath, TimeSpan.FromSeconds(40));
				if (okOut && File.Exists(outputPath))
				{
					SetStatus($"解密完成: {outputPath}");
					await LoadJsonAndPopulateMapsAsync(outputPath);
				}
				else
				{
					SetStatus("已通过 FullTrust 启动辅助进程。若未生成文件，请确认辅助程序支持在无命令行参数时的工作方式。", isError: true);
				}
				return;
			}

#if DISABLE_EXTERNAL_PROCESS
			// External process launching is disabled and full-trust path wasn't available
			SetStatus("当前构建禁用了外部进程启动。请手动将 JSON 放入输出目录后使用‘载入最新JSON’。", isError: true);
			return;
#else
			// Fallback for unpackaged/dev runs
			var ok = await RunProcessAsync(exePath, args);
			if (ok && File.Exists(outputPath))
			{
				SetStatus($"解密完成: {outputPath}");
				// 自动加载绘图
				await LoadJsonAndPopulateMapsAsync(outputPath);
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
		// Stub when external process launching is disabled
		return Task.FromResult(false);
	}
#endif

	// Right-side: load latest JSON and draw
	private async void LoadLatestJsonButton_Click(object sender, RoutedEventArgs e)
	{
		var latest = TryGetLatestJson();
		if (latest == null)
		{
			SetStatus("未在 output/mysekai 找到 JSON。", isError: true);
			return;
		}
		await LoadJsonAndPopulateMapsAsync(latest);
	}

	private string? TryGetLatestJson()
	{
		try
		{
			var dir = AppPaths.OutputMysekaiDir;
			if (!Directory.Exists(dir)) return null;
			var latest = Directory.GetFiles(dir, "*.json")
				.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
				.FirstOrDefault();
			return latest;
		}
		catch { return null; }
	}

	private async Task LoadJsonAndPopulateMapsAsync(string path)
	{
		try
		{
			var json = await File.ReadAllTextAsync(path);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (!root.TryGetProperty("updatedResources", out var updated) || !updated.TryGetProperty("userMysekaiHarvestMaps", out var mapsArray))
			{
				SetStatus("JSON 缺少 userMysekaiHarvestMaps。", isError: true);
				return;
			}

			// Ensure music mapping and master are loaded for records info/jacket
			await MysekaiMusicRecordMaster.EnsureLoadedAsync(_http);
			await MusicMaster.EnsureLoadedAsync(_http);

			// Collect already obtained music records for status display
			LoadCollectedMusicRecords(root);

			var maps = new Dictionary<int, MapData>();
			_musicRecords.Clear();

			foreach (var mp in mapsArray.EnumerateArray())
			{
				var siteId = mp.GetProperty("mysekaiSiteId").GetInt32();
				var mapDetails = new List<MapPoint>();

				if (mp.TryGetProperty("userMysekaiSiteHarvestFixtures", out var fixtures))
				{
					foreach (var fx in fixtures.EnumerateArray())
					{
						var status = fx.GetProperty("userMysekaiSiteHarvestFixtureStatus").GetString();
						if (string.Equals(status, "spawned", StringComparison.OrdinalIgnoreCase))
						{
							var px = fx.GetProperty("positionX").GetDouble();
							var pz = fx.GetProperty("positionZ").GetDouble();
							var fid = fx.GetProperty("mysekaiSiteHarvestFixtureId").GetInt32();
							mapDetails.Add(new MapPoint
							{
								X = px,
								Y = pz,
								FixtureId = fid,
								Rewards = new Dictionary<string, Dictionary<string, int>>()
							});
						}
					}
				}

				if (mp.TryGetProperty("userMysekaiSiteHarvestResourceDrops", out var drops))
				{
					foreach (var dr in drops.EnumerateArray())
					{
						var px = dr.GetProperty("positionX").GetDouble();
						var pz = dr.GetProperty("positionZ").GetDouble();
						var resourceType = dr.GetProperty("resourceType").GetString()!;
						var resourceId = dr.GetProperty("resourceId").GetInt32();
						var qty = dr.GetProperty("quantity").GetInt32();

						var pt = mapDetails.FirstOrDefault(d => Math.Abs(d.X - px) < 1e-6 && Math.Abs(d.Y - pz) < 1e-6);
						if (pt != null)
						{
							if (!pt.Rewards.TryGetValue(resourceType, out var bag))
							{
								bag = new Dictionary<string, int>();
								pt.Rewards[resourceType] = bag;
							}
							bag[resourceId.ToString()] = bag.TryGetValue(resourceId.ToString(), out var old) ? old + qty : qty;

							if (resourceType == "mysekai_music_record")
							{
								// Use music master to resolve title/creator and jacket path
								var entry = new MusicRecordEntry();
								int viewId = ResolveMusicViewId(resourceId);
								List<string> jacketCandidates;
								if (MusicMaster.TryGet(viewId, out var mm))
								{
									var baseInfo = string.IsNullOrWhiteSpace(mm.Creator)
										? mm.Title
										: $"{mm.Title} · {mm.Creator}";
									entry.Info = AppendCollectStatus(baseInfo, resourceId);
									entry.MetaUrl = $"https://sekai.best/music/{viewId}";
									jacketCandidates = MusicMaster.BuildJacketUrls(mm.AssetbundleName, viewId);
								}
								else
								{
									// Fallback to old behavior if not found
									entry.Info = AppendCollectStatus($"ID: {resourceId} (ViewID: {viewId})", resourceId);
									entry.MetaUrl = $"https://sekai.best/music/{viewId}";
									jacketCandidates = MusicMaster.BuildJacketUrls(null, viewId);
								}
								entry.SetJacketUrls(jacketCandidates);
								_ = entry.InitializeJacketImageAsync(_http);
								_musicRecords.Add(entry);
							}
						}
					}
				}

				maps[siteId] = new MapData
				{
					Name = _scenes.TryGetValue(siteId, out var sc) ? sc.Name : $"未知地图({siteId})",
					Points = mapDetails
				};
			}
			_maps = maps;

			var list = GetMusicRecordsList();
			if (list != null)
			{
				list.ItemsSource = null;
				list.ItemsSource = _musicRecords;
			}

			var combo = GetMapSelectCombo();
			if (combo != null)
			{
				combo.Items.Clear();
				foreach (var kv in _maps)
				{
					combo.Items.Add(new ComboBoxItem { Content = $"{kv.Key}: {kv.Value.Name}", Tag = kv.Key });
				}
				combo.SelectedIndex = _maps.Count > 0 ? 0 : -1;
			}

			if (_maps.Count > 0)
			{
				_currentSiteId = _maps.Keys.First();
				await DrawCurrentMapAsync();
				SetStatus($"已加载 JSON: {IOPath.GetFileName(path)}");
			}
			else
			{
				SetStatus("JSON 中没有可用地图。", isError: true);
			}
		}
		catch (Exception ex)
		{
			SetStatus(ex.Message, isError: true);
		}
	}

	private async Task DrawCurrentMapAsync()
	{
		if (!_maps.TryGetValue(_currentSiteId, out var map) || !_scenes.TryGetValue(_currentSiteId, out var scene))
		{
			return;
		}
		var img = GetMapImage();
		var canvas = GetMapCanvas();
		var sv = GetMapScrollViewer();
		if (img == null || canvas == null || sv == null) return;

		// Load background image from assets/sekai_xray/img
		var bgPath = IOPath.Combine(AppContext.BaseDirectory, "assets", "sekai_xray", scene.ImagePath);
		if (!File.Exists(bgPath))
		{
			SetStatus($"找不到背景图: {bgPath}", isError: true);
			return;
		}

		RoutedEventHandler? onOpened = null;
		onOpened = async (s, e) =>
		{
			img.ImageOpened -= onOpened;
			if (img.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp1)
			{
				if (bmp1.PixelWidth > 0 && bmp1.PixelHeight > 0)
				{
					img.Width = bmp1.PixelWidth;
					img.Height = bmp1.PixelHeight;
					canvas.Width = bmp1.PixelWidth;
					canvas.Height = bmp1.PixelHeight;
				}
			}
			// reset zoom to 1 for new image
			sv.ChangeView(null, null, 1.0f);
			await RenderOverlayAsync(scene, map, canvas, img);
		};
		img.ImageOpened += onOpened;
		img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(bgPath));
	}

	private static bool ContainsRare(MapPoint p, bool super = false)
	{
		var rareMaterial = new HashSet<int> { 5, 12, 20, 24, 32, 33, 61, 62, 63, 64, 65 };
		var superMaterial = new HashSet<int> { 5, 12, 20, 24 };
		var rareItem = new HashSet<int> { 7 };
		var rareFixture = new HashSet<int> { 118, 119, 120, 121 };

		foreach (var (rtype, items) in p.Rewards)
		{
			foreach (var kv in items)
			{
				if (!int.TryParse(kv.Key, out var id)) continue;
				if (rtype == "mysekai_material")
				{
					if (super && superMaterial.Contains(id)) return true;
					if (!super && rareMaterial.Contains(id)) return true;
				}
				else if (rtype == "mysekai_item")
				{
					if (!super && rareItem.Contains(id)) return true;
				}
				else if (rtype == "mysekai_fixture")
				{
					if (!super && rareFixture.Contains(id)) return true;
				}
			}
		}
		return false;
	}

	private async Task RenderOverlayAsync(MapScene scene, MapData map, Canvas canvas, Image img)
	{
		canvas.Children.Clear();
		if (img.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
		{
			canvas.Width = bmp.PixelWidth;
			canvas.Height = bmp.PixelHeight;
		}

		double originX = canvas.Width / 2.0 + scene.OffsetX;
		double originY = canvas.Height / 2.0 + scene.OffsetY;
		double grid = scene.PhysicalWidth;

		bool showIcons = GetShowIconsCheckBox()?.IsChecked == true;

		// Ensure music record mapping and music master available for overlay too
		await MysekaiMusicRecordMaster.EnsureLoadedAsync(_http);
		await MusicMaster.EnsureLoadedAsync(_http);

		foreach (var p in map.Points)
		{
			double x = p.X, y = p.Y;
			if (scene.ReverseXY) (x, y) = (y, x);

			double displayX = scene.XDirection == XDir.Plus ? (originX + x * grid) : (originX - x * grid);
			double displayY = scene.YDirection == YDir.Plus ? (originY + y * grid) : (originY - y * grid);

			bool superRare = ContainsRare(p, true);
			bool rare = superRare || ContainsRare(p, false);

			var dot = new Ellipse
			{
				Width = 10,
				Height = 10,
				Fill = new SolidColorBrush(Colors.Black),
				Stroke = new SolidColorBrush(rare ? (superRare ? Colors.Red : Colors.Blue) : Colors.Gray),
				StrokeThickness = rare ? (superRare ? 3 : 2) : 1
			};
			Canvas.SetLeft(dot, displayX - 5);
			Canvas.SetTop(dot, displayY - 5);
			canvas.Children.Add(dot);

			if (showIcons && p.Rewards.Count > 0)
			{
			 int idx = 0;
				foreach (var kv in p.Rewards)
				{
					foreach (var item in kv.Value)
					{
						// Special handling for music record: resolve via music master
						if (string.Equals(kv.Key, "mysekai_music_record", StringComparison.Ordinal))
						{
							var imgIcon = new Image { Width = 24, Height = 24 };

							if (int.TryParse(item.Key, out var rid))
							{
								int viewId = ResolveMusicViewId(rid);
								List<string> jacketCandidates;
								if (MusicMaster.TryGet(viewId, out var mm))
								{
									jacketCandidates = MusicMaster.BuildJacketUrls(mm.AssetbundleName, viewId);
								}
								else
								{
									jacketCandidates = MusicMaster.BuildJacketUrls(null, viewId);
								}
								SetImageSourceWithFallback(imgIcon, jacketCandidates, $"rid={rid}");
							}

							Canvas.SetLeft(imgIcon, displayX + idx * 28);
							Canvas.SetTop(imgIcon, displayY - 12);
							canvas.Children.Add(imgIcon);

							if (rare)
							{
								// Draw a border rectangle for rare items
								var rect = new Rectangle { Width = 24 + 6, Height = 24 + 6, StrokeThickness = 2 };
								rect.Stroke = new SolidColorBrush(superRare ? Colors.Red : Colors.Blue);
								rect.Fill = new SolidColorBrush(Colors.Transparent);
								Canvas.SetLeft(rect, displayX + idx * 28 - 3);
								Canvas.SetTop(rect, displayY - 15);
								canvas.Children.Add(rect);
							}

							idx++;
							continue;
						}

						var iconRel = IconResolver.GetIconRelativePath(kv.Key, item.Key);
						if (iconRel == null) continue;
						var iconPath = IOPath.Combine(AppContext.BaseDirectory, "assets", "sekai_xray", iconRel);
						if (!File.Exists(iconPath)) continue;
						var bmpIcon = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
						var imgIconDefault = new Image { Source = bmpIcon, Width = 24, Height = 24 };
						var iconContainer = new Grid { Width = 24, Height = 24 };
						iconContainer.Children.Add(imgIconDefault);
						if (string.Equals(kv.Key, "mysekai_material", StringComparison.Ordinal) && item.Value > 1)
						{
							var badge = new Border
							{
								Background = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 0, 0)),
								CornerRadius = new CornerRadius(2),
								Padding = new Thickness(2, 0, 2, 0),
								HorizontalAlignment = HorizontalAlignment.Right,
								VerticalAlignment = VerticalAlignment.Bottom
							};
							badge.Child = new TextBlock
							{
								Text = item.Value.ToString(),
								Foreground = new SolidColorBrush(Colors.White),
								FontSize = 10
							};
							iconContainer.Children.Add(badge);
						}
						Canvas.SetLeft(iconContainer, displayX + idx * 28);
						Canvas.SetTop(iconContainer, displayY - 12);
						canvas.Children.Add(iconContainer);
						if (rare)
						{
							var rect = new Rectangle { Width = 24 + 6, Height = 24 + 6, StrokeThickness = 2 };
							rect.Stroke = new SolidColorBrush(superRare ? Colors.Red : Colors.Blue);
							rect.Fill = new SolidColorBrush(Colors.Transparent);
							Canvas.SetLeft(rect, displayX + idx * 28 - 3);
							Canvas.SetTop(rect, displayY - 15);
							canvas.Children.Add(rect);
						}
						idx++;
					}
				}
			}
		}
	}

	private static int ResolveMusicViewId(int resourceId)
	{
		if (MysekaiMusicRecordMaster.TryGetExternalId(resourceId, out var externalId))
		{
			return externalId;
		}

		Debug.WriteLine($"[MusicRecord] Missing externalId for record {resourceId}, fallback to +30 offset.");
		return resourceId + 30;
	}

	private static void SetImageSourceWithFallback(Image target, IReadOnlyList<string> candidates, string context)
	{
		if (target == null || candidates == null || candidates.Count == 0) return;

		int currentIndex = 0;
		RoutedEventHandler? opened = null;
		ExceptionRoutedEventHandler? failed = null;

		void ApplySource(int index)
		{
			currentIndex = index;
			var url = candidates[index];
			Debug.WriteLine($"[MusicRecord] Try load {context} url={url}");
			target.Source = new BitmapImage(new Uri(url));
		}

		opened = (s, e) =>
		{
			Debug.WriteLine($"[MusicRecord] Success {context} url={candidates[currentIndex]}");
			target.ImageOpened -= opened;
			target.ImageFailed -= failed;
		};

		failed = (s, e) =>
		{
			var currentUrl = candidates[currentIndex];
			Debug.WriteLine($"[MusicRecord] Failed {context} url={currentUrl} error={e.ErrorMessage}");
			if (currentIndex + 1 < candidates.Count)
			{
				ApplySource(currentIndex + 1);
				return;
			}

			Debug.WriteLine($"[MusicRecord] All candidates failed {context}.");
			target.ImageOpened -= opened;
			target.ImageFailed -= failed;
		};

		target.ImageOpened += opened;
		target.ImageFailed += failed;
		ApplySource(0);
	}

	private void LoadCollectedMusicRecords(JsonElement root)
	{
		_collectedMusicRecordIds.Clear();
		_hasCollectedMusicRecordData = false;

		JsonElement lookupRoot = root;
		if (!root.TryGetProperty("userMysekaiMusicRecords", out var collected) && root.TryGetProperty("updatedResources", out var updated))
		{
			lookupRoot = updated;
		}

		if (lookupRoot.TryGetProperty("userMysekaiMusicRecords", out collected) && collected.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in collected.EnumerateArray())
			{
				if (item.TryGetProperty("mysekaiMusicRecordId", out var idEl))
				{
					_collectedMusicRecordIds.Add(idEl.GetInt32());
				}
			}
			_hasCollectedMusicRecordData = _collectedMusicRecordIds.Count > 0;
			Debug.WriteLine($"[MusicRecord] Loaded collected records: {_collectedMusicRecordIds.Count} entries.");
		}
		else
		{
			Debug.WriteLine("[MusicRecord] No userMysekaiMusicRecords found; collection status unavailable.");
		}
	}

	private string AppendCollectStatus(string baseInfo, int resourceId)
	{
		if (!_hasCollectedMusicRecordData) return baseInfo;
		var suffix = _collectedMusicRecordIds.Contains(resourceId) ? "已收集" : "未收集";
		return $"{baseInfo} · {suffix}";
	}

	private void MapSelectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && item.Tag is int id)
		{
			_currentSiteId = id;
			_ = DrawCurrentMapAsync();
		}
	}

	private void ShowIconsCheckBox_Toggled(object sender, RoutedEventArgs e)
	{
		_ = DrawCurrentMapAsync();
	}

	private void ZoomInButton_Click(object sender, RoutedEventArgs e)
	{
		var sv = GetMapScrollViewer();
		if (sv == null) return;
		var newZoom = Math.Min(sv.MaxZoomFactor, (sv.ZoomFactor == 0 ? 1.0f : sv.ZoomFactor) * 1.2f);
		sv.ChangeView(null, null, newZoom);
	}

	private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
	{
		var sv = GetMapScrollViewer();
		if (sv == null) return;
		var newZoom = Math.Max(sv.MinZoomFactor, (sv.ZoomFactor == 0 ? 1.0f : sv.ZoomFactor) / 1.2f);
		sv.ChangeView(null, null, newZoom);
	}

	private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
	{
		var sv = GetMapScrollViewer();
		if (sv == null) return;
		sv.ChangeView(null, null, 1.0f);
	}

	private void SetStatus(string message, bool isError = false)
	{
		_ = DispatcherQueue.TryEnqueue(() =>
		{
			var tb = GetStatusTextBlock();
			if (tb == null) return;
			tb.Text = message;
			if (isError)
			{
				tb.Foreground = new SolidColorBrush(Colors.IndianRed);
			}
			else
			{
				tb.ClearValue(TextBlock.ForegroundProperty);
			}
		});
	}
}

internal class MusicRecordEntry : System.ComponentModel.INotifyPropertyChanged
{
	public string Info { get; set; } = string.Empty;
	public string MetaUrl { get; set; } = string.Empty;
	public string JacketUrl { get; set; } = string.Empty;
	private readonly List<string> _jacketUrls = new();
	public Uri MetaUri => Uri.TryCreate(MetaUrl, UriKind.Absolute, out var u) ? u : new Uri("https://sekai.best/");
	
	public Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource JacketImage { get; set; } = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
	
	// Property to show loading status (for debugging)
	private string _loadStatus = "Loading...";
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

	public void SetJacketUrls(IEnumerable<string> urls)
	{
		_jacketUrls.Clear();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var raw in urls)
		{
			if (string.IsNullOrWhiteSpace(raw)) continue;
			var url = raw.Trim();
			if (!Uri.TryCreate(url, UriKind.Absolute, out _)) continue;
			if (seen.Add(url))
			{
				_jacketUrls.Add(url);
			}
		}

		JacketUrl = _jacketUrls.FirstOrDefault() ?? string.Empty;
	}

	public async Task InitializeJacketImageAsync(HttpClient httpClient)
	{
		var urls = _jacketUrls.Count > 0
			? _jacketUrls
			: (string.IsNullOrWhiteSpace(JacketUrl) ? new List<string>() : new List<string> { JacketUrl });

		if (urls.Count == 0)
		{
			LoadStatus = "No URL";
			return;
		}

		string lastStatus = "Load failed";
		foreach (var url in urls)
		{
			try
			{
				// Try to download the WebP image
				Debug.WriteLine($"[MusicRecordEntry] Downloading {url}");
				LoadStatus = "Downloading...";

				using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
				Debug.WriteLine($"[MusicRecordEntry] HTTP Status: {response.StatusCode} for {url}");

				if (!response.IsSuccessStatusCode)
				{
					lastStatus = $"HTTP {(int)response.StatusCode}";
					Debug.WriteLine($"[MusicRecordEntry] ✗ HTTP error {response.StatusCode} for {url}");
					var errorBody = await response.Content.ReadAsStringAsync();
					Debug.WriteLine($"[MusicRecordEntry] Response: {errorBody.Substring(0, Math.Min(500, errorBody.Length))}");
					continue;
				}

				LoadStatus = "Decoding...";
				var imageBytes = await response.Content.ReadAsByteArrayAsync();
				Debug.WriteLine($"[MusicRecordEntry] Downloaded {imageBytes.Length} bytes");

				// Convert byte array to IRandomAccessStream
				using var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
				using var dataWriter = new Windows.Storage.Streams.DataWriter(memStream);
				dataWriter.WriteBytes(imageBytes);
				await dataWriter.StoreAsync();
				memStream.Seek(0);

				// Use SoftwareBitmap to decode WebP
				Debug.WriteLine("[MusicRecordEntry] Creating decoder...");
				var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(memStream);
				Debug.WriteLine($"[MusicRecordEntry] Decoder created: {decoder.DecoderInformation.CodecId}");

				var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
				Debug.WriteLine($"[MusicRecordEntry] SoftwareBitmap: {softwareBitmap.PixelWidth}x{softwareBitmap.PixelHeight}, Format={softwareBitmap.BitmapPixelFormat}");

				// Convert to BGRA8 format that XAML supports
				if (softwareBitmap.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8 ||
					softwareBitmap.BitmapAlphaMode == Windows.Graphics.Imaging.BitmapAlphaMode.Straight)
				{
					Debug.WriteLine("[MusicRecordEntry] Converting format...");
					softwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap.Convert(
						softwareBitmap,
						Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
						Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
				}

				Debug.WriteLine("[MusicRecordEntry] Setting bitmap to UI...");
				await JacketImage.SetBitmapAsync(softwareBitmap);
				JacketUrl = url;
				LoadStatus = "Success";
				Debug.WriteLine($"[MusicRecordEntry] ✓ Successfully loaded WebP from {url}");
				return;
			}
			catch (TaskCanceledException ex)
			{
				lastStatus = "Timeout";
				Debug.WriteLine($"[MusicRecordEntry] ✗ Timeout loading {url}: {ex.Message}");
			}
			catch (HttpRequestException ex)
			{
				lastStatus = "Network error";
				Debug.WriteLine($"[MusicRecordEntry] ✗ Network error loading {url}: {ex.Message}");
			}
			catch (Exception ex)
			{
				lastStatus = $"Error: {ex.GetType().Name}";
				Debug.WriteLine($"[MusicRecordEntry] ✗✗✗ Exception loading {url}");
				Debug.WriteLine($"[MusicRecordEntry] Exception Type: {ex.GetType().Name}");
				Debug.WriteLine($"[MusicRecordEntry] Exception Message: {ex.Message}");
				Debug.WriteLine($"[MusicRecordEntry] Stack Trace: {ex.StackTrace}");
				if (ex.InnerException != null)
				{
					Debug.WriteLine($"[MusicRecordEntry] Inner Exception: {ex.InnerException.Message}");
				}
			}
		}

		LoadStatus = lastStatus;
	}

	private async Task LoadFallbackImageAsync(string fallbackPath)
	{
		if (!File.Exists(fallbackPath))
		{
			Debug.WriteLine($"[MusicRecordEntry] ✗ Fallback file not found: {fallbackPath}");
			LoadStatus = "No fallback";
			return;
		}

		try
		{
			Debug.WriteLine($"[MusicRecordEntry] Loading fallback from {fallbackPath}");
			var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(fallbackPath);
			using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
			var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
			var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
			
			if (softwareBitmap.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8 ||
				softwareBitmap.BitmapAlphaMode == Windows.Graphics.Imaging.BitmapAlphaMode.Straight)
			{
				softwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap.Convert(
					softwareBitmap, 
					Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, 
					Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
			}
			
			await JacketImage.SetBitmapAsync(softwareBitmap);
			LoadStatus = "Fallback";
			Debug.WriteLine($"[MusicRecordEntry] ✓ Loaded fallback from {fallbackPath}");
		}
		catch (Exception ex)
		{
			LoadStatus = "Fallback failed";
			Debug.WriteLine($"[MusicRecordEntry] ✗ Failed to load fallback: {ex.Message}");
		}
	}
}

internal class MapData
{
	public string Name { get; set; } = string.Empty;
	public List<MapPoint> Points { get; set; } = new();
}

internal class MapPoint
{
	public double X { get; set; }
	public double Y { get; set; }
	public int FixtureId { get; set; }
	public Dictionary<string, Dictionary<string, int>> Rewards { get; set; } = new();
}

internal enum XDir { Plus, Minus }
internal enum YDir { Plus, Minus }

internal class MapScene
{
	public string Name { get; init; } = string.Empty;
	public double PhysicalWidth { get; init; }
	public double OffsetX { get; init; }
	public double OffsetY { get; init; }
	public string ImagePath { get; init; } = string.Empty; // relative under assets/sekai_xray/img
	public XDir XDirection { get; init; }
	public YDir YDirection { get; init; }
	public bool ReverseXY { get; init; }

	public static Dictionary<int, MapScene> CreateDefaults() => new()
	{
		[1] = new MapScene { Name = "マイホーム", PhysicalWidth = 33.333, OffsetX = 0, OffsetY = -40, ImagePath = IOPath.Combine("img", "grassland.png"), XDirection = XDir.Minus, YDirection = YDir.Minus, ReverseXY = true },
		[2] = new MapScene { Name = "1F", PhysicalWidth = 24.806, OffsetX = -62.015, OffsetY = 20.672, ImagePath = IOPath.Combine("img", "flowergarden.png"), XDirection = XDir.Minus, YDirection = YDir.Minus, ReverseXY = true },
		[3] = new MapScene { Name = "2F", PhysicalWidth = 20.513, OffsetX = 0, OffsetY = 80, ImagePath = IOPath.Combine("img", "beach.png"), XDirection = XDir.Plus, YDirection = YDir.Minus, ReverseXY = false },
		[4] = new MapScene { Name = "3F", PhysicalWidth = 21.333, OffsetX = 0, OffsetY = -106.667, ImagePath = IOPath.Combine("img", "memorialplace.png"), XDirection = XDir.Plus, YDirection = YDir.Minus, ReverseXY = false },
		[5] = new MapScene { Name = "さいしょの原っぱ", PhysicalWidth = 33.333, OffsetX = 0, OffsetY = -40, ImagePath = IOPath.Combine("img", "grassland.png"), XDirection = XDir.Minus, YDirection = YDir.Minus, ReverseXY = true },
		[6] = new MapScene { Name = "願いの砂浜", PhysicalWidth = 20.513, OffsetX = 0, OffsetY = 80, ImagePath = IOPath.Combine("img", "beach.png"), XDirection = XDir.Plus, YDirection = YDir.Minus, ReverseXY = false },
		[7] = new MapScene { Name = "彩りの花畑", PhysicalWidth = 24.806, OffsetX = -62.015, OffsetY = 20.672, ImagePath = IOPath.Combine("img", "flowergarden.png"), XDirection = XDir.Minus, YDirection = YDir.Minus, ReverseXY = true },
		[8] = new MapScene { Name = "忘れ去られた場所", PhysicalWidth = 21.333, OffsetX = 0, OffsetY = -106.667, ImagePath = IOPath.Combine("img", "memorialplace.png"), XDirection = XDir.Plus, YDirection = YDir.Minus, ReverseXY = false },
	};
}

internal static class IconResolver
{
	public static string? GetIconRelativePath(string resourceType, string itemId)
	{
		return resourceType switch
		{
			"mysekai_item" when itemId == "7" => IOPath.Combine("icon", "Texture2D", "item_blueprint_fragment.png"),
			"mysekai_material" => itemId switch
			{
				"1" => IOPath.Combine("icon", "Texture2D", "item_wood_1.png"),
				"2" => IOPath.Combine("icon", "Texture2D", "item_wood_2.png"),
				"3" => IOPath.Combine("icon", "Texture2D", "item_wood_3.png"),
				"4" => IOPath.Combine("icon", "Texture2D", "item_wood_4.png"),
				"5" => IOPath.Combine("icon", "Texture2D", "item_wood_5.png"),
				"6" => IOPath.Combine("icon", "Texture2D", "item_mineral_1.png"),
				"7" => IOPath.Combine("icon", "Texture2D", "item_mineral_2.png"),
				"8" => IOPath.Combine("icon", "Texture2D", "item_mineral_3.png"),
				"9" => IOPath.Combine("icon", "Texture2D", "item_mineral_4.png"),
				"10" => IOPath.Combine("icon", "Texture2D", "item_mineral_5.png"),
				"11" => IOPath.Combine("icon", "Texture2D", "item_mineral_6.png"),
				"12" => IOPath.Combine("icon", "Texture2D", "item_mineral_7.png"),
				"13" => IOPath.Combine("icon", "Texture2D", "item_junk_1.png"),
				"14" => IOPath.Combine("icon", "Texture2D", "item_junk_2.png"),
				"15" => IOPath.Combine("icon", "Texture2D", "item_junk_3.png"),
				"16" => IOPath.Combine("icon", "Texture2D", "item_junk_4.png"),
				"17" => IOPath.Combine("icon", "Texture2D", "item_junk_5.png"),
				"18" => IOPath.Combine("icon", "Texture2D", "item_junk_6.png"),
				"19" => IOPath.Combine("icon", "Texture2D", "item_junk_7.png"),
				"20" => IOPath.Combine("icon", "Texture2D", "item_plant_1.png"),
				"21" => IOPath.Combine("icon", "Texture2D", "item_plant_2.png"),
				"22" => IOPath.Combine("icon", "Texture2D", "item_plant_3.png"),
				"23" => IOPath.Combine("icon", "Texture2D", "item_plant_4.png"),
				"24" => IOPath.Combine("icon", "Texture2D", "item_tone_8.png"),
				"32" => IOPath.Combine("icon", "Texture2D", "item_junk_8.png"),
				"33" => IOPath.Combine("icon", "Texture2D", "item_mineral_8.png"),
				"34" => IOPath.Combine("icon", "Texture2D", "item_junk_9.png"),
				"61" => IOPath.Combine("icon", "Texture2D", "item_junk_10.png"),
				"62" => IOPath.Combine("icon", "Texture2D", "item_junk_11.png"),
				"63" => IOPath.Combine("icon", "Texture2D", "item_junk_12.png"),
				"64" => IOPath.Combine("icon", "Texture2D", "item_mineral_9.png"),
				"65" => IOPath.Combine("icon", "Texture2D", "item_mineral_10.png"),
				_ => null
			},
			"mysekai_fixture" => itemId switch
			{
				"118" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sapling1_118.png"),
				"119" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sapling1_119.png"),
				"120" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sapling1_120.png"),
				"121" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sapling1_121.png"),
				"126" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_126.png"),
				"127" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_127.png"),
				"128" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_128.png"),
				"129" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_129.png"),
				"130" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_130.png"),
				"474" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_474.png"),
				"475" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_475.png"),
				"476" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_476.png"),
				"477" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_477.png"),
				"478" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_478.png"),
				"479" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_479.png"),
				"480" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_480.png"),
				"481" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_481.png"),
				"482" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_482.png"),
				"483" => IOPath.Combine("icon", "Texture2D", "mdl_non1001_before_sprout1_483.png"),
				_ => null
			},
			"mysekai_music_record" => IOPath.Combine("icon", "Texture2D", "item_surplus_music_record.png"),
			_ => null
		};
	}
}

// Mapper for mysekaiMusicRecords.json to resolve internal ids to external music ids
internal static class MysekaiMusicRecordMaster
{
	private const string SourceUrl = "https://raw.githubusercontent.com/Team-Haruki/haruki-sekai-sc-master/main/master/mysekaiMusicRecords.json";
	private static readonly string LocalFallbackPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "mysekaiMusicRecords.json");
	private static readonly Dictionary<int, int> _map = new();
	private static Task? _loading;

	public static async Task EnsureLoadedAsync(HttpClient http)
	{
		if (_map.Count > 0) return;
		if (_loading != null) { await _loading; return; }

		_loading = LoadAsync(http);
		try { await _loading; }
		finally { _loading = null; }
	}

	private static async Task LoadAsync(HttpClient http)
	{
		if (await TryLoadFromRemoteAsync(http)) return;
		if (await TryLoadFromLocalAsync()) return;

		Debug.WriteLine("[MysekaiMusicRecordMaster] Failed to load mapping from both remote and local sources.");
	}

	private static async Task<bool> TryLoadFromRemoteAsync(HttpClient http)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
			req.Headers.Accept.Clear();
			req.Headers.Accept.ParseAdd("application/json, text/plain;q=0.9, */*;q=0.8");
			using var res = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
			var bytes = await res.Content.ReadAsByteArrayAsync();
			if (!res.IsSuccessStatusCode)
			{
				Debug.WriteLine($"[MysekaiMusicRecordMaster] Remote status {(int)res.StatusCode}: {JsonPayloadGuard.Preview(bytes)}");
				return false;
			}
			if (!JsonPayloadGuard.LooksLikeJson(bytes))
			{
				Debug.WriteLine($"[MysekaiMusicRecordMaster] Remote payload is not JSON: {JsonPayloadGuard.Preview(bytes)}");
				return false;
			}
			if (TryPopulateMap(bytes))
			{
				Debug.WriteLine($"[MysekaiMusicRecordMaster] Loaded mapping for {_map.Count} entries from remote.");
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MysekaiMusicRecordMaster] Remote load failed: {ex.Message}");
		}

		return false;
	}

	private static async Task<bool> TryLoadFromLocalAsync()
	{
		try
		{
			if (!File.Exists(LocalFallbackPath))
			{
				Debug.WriteLine($"[MysekaiMusicRecordMaster] Local mapping not found: {LocalFallbackPath}");
				return false;
			}

			var bytes = await File.ReadAllBytesAsync(LocalFallbackPath);
			if (TryPopulateMap(bytes))
			{
				Debug.WriteLine($"[MysekaiMusicRecordMaster] Loaded mapping for {_map.Count} entries from local fallback.");
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MysekaiMusicRecordMaster] Local load failed: {ex.Message}");
		}

		return false;
	}

	private static bool TryPopulateMap(byte[] bytes)
	{
		try
		{
			using var doc = JsonDocument.Parse(bytes);
			return TryPopulateMap(doc.RootElement);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MysekaiMusicRecordMaster] Parse failed: {ex.Message}");
			_map.Clear();
			return false;
		}
	}

	private static bool TryPopulateMap(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Array) return false;

		_map.Clear();
		foreach (var el in root.EnumerateArray())
		{
			if (!el.TryGetProperty("id", out var idEl) || !el.TryGetProperty("externalId", out var extEl)) continue;

			var trackType = el.TryGetProperty("mysekaiMusicTrackType", out var typeEl) ? typeEl.GetString() : null;
			if (!string.Equals(trackType, "music", StringComparison.OrdinalIgnoreCase)) continue;

			_map[idEl.GetInt32()] = extEl.GetInt32();
		}

		return _map.Count > 0;
	}

	public static bool TryGetExternalId(int id, out int externalId) => _map.TryGetValue(id, out externalId);
}

// Music master loader for musics.json (sekai-world)
internal static class MusicMaster
{
	private const string SourceUrl = "https://sekai-world.github.io/sekai-master-db-cn-diff/musics.json";
	private static readonly string LocalFallbackPath = IOPath.Combine(AppContext.BaseDirectory, "Assets", "musics.json");
	private static readonly Dictionary<int, MusicMasterEntry> _map = new();
	private static Task? _loading;

	public static async Task EnsureLoadedAsync(HttpClient http)
	{
		if (_map.Count > 0) return;
		if (_loading != null) { await _loading; return; }

		_loading = LoadAsync(http);
		try { await _loading; }
		finally { _loading = null; }
	}

	private static async Task LoadAsync(HttpClient http)
	{
		if (await TryLoadFromRemoteAsync(http)) return;
		if (await TryLoadFromLocalAsync()) return;

		Debug.WriteLine("[MusicMaster] Failed to load from both network and bundled musics.json.");
	}

	private static async Task<bool> TryLoadFromRemoteAsync(HttpClient http)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
			req.Headers.Accept.Clear();
			req.Headers.Accept.ParseAdd("application/json, text/plain;q=0.9, */*;q=0.8");
			using var res = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
			var bytes = await res.Content.ReadAsByteArrayAsync();
			if (!res.IsSuccessStatusCode)
			{
				Debug.WriteLine($"[MusicMaster] Remote status {(int)res.StatusCode}: {JsonPayloadGuard.Preview(bytes)}");
				return false;
			}
			if (!JsonPayloadGuard.LooksLikeJson(bytes))
			{
				Debug.WriteLine($"[MusicMaster] Remote payload is not JSON: {JsonPayloadGuard.Preview(bytes)}");
				return false;
			}
			if (TryPopulateMap(bytes))
			{
				Debug.WriteLine($"[MusicMaster] Loaded {_map.Count} entries from remote source.");
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MusicMaster] Remote load failed: {ex.Message}");
		}

		return false;
	}

	private static async Task<bool> TryLoadFromLocalAsync()
	{
		try
		{
			if (!File.Exists(LocalFallbackPath))
			{
				Debug.WriteLine($"[MusicMaster] Local musics.json not found: {LocalFallbackPath}");
				return false;
			}

			var bytes = await File.ReadAllBytesAsync(LocalFallbackPath);
			if (TryPopulateMap(bytes))
			{
				Debug.WriteLine($"[MusicMaster] Loaded {_map.Count} entries from local musics.json.");
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MusicMaster] Local load failed: {ex.Message}");
		}

		return false;
	}

	private static bool TryPopulateMap(byte[] bytes)
	{
		try
		{
			using var doc = JsonDocument.Parse(bytes);
			return TryPopulateMap(doc.RootElement);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MusicMaster] Parse failed: {ex.Message}");
			_map.Clear();
			return false;
		}
	}

	private static bool TryPopulateMap(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Array) return false;

		_map.Clear();
		foreach (var el in root.EnumerateArray())
		{
			if (!el.TryGetProperty("id", out var idEl)) continue;
			int id = idEl.GetInt32();
			string asset = el.TryGetProperty("assetbundleName", out var ab) ? ab.GetString() ?? string.Empty : string.Empty;

			string title = el.TryGetProperty("title", out var t0) ? (t0.GetString() ?? string.Empty) : string.Empty;
			string creator = string.Empty;
			if (el.TryGetProperty("infos", out var infos) && infos.ValueKind == JsonValueKind.Array)
			{
				var first = infos.EnumerateArray().FirstOrDefault();
				if (first.ValueKind == JsonValueKind.Object)
				{
					title = first.TryGetProperty("title", out var ti) ? (ti.GetString() ?? title) : title;
					creator = first.TryGetProperty("creator", out var cr) ? (cr.GetString() ?? string.Empty) : string.Empty;
				}
			}

			if (!string.IsNullOrWhiteSpace(asset))
			{
				_map[id] = new MusicMasterEntry { Id = id, AssetbundleName = asset, Title = title, Creator = creator };
			}
		}

		return _map.Count > 0;
	}

	public static bool TryGet(int id, out MusicMasterEntry entry) => _map.TryGetValue(id, out entry!);

	public static string BuildJacketUrl(string assetbundleName)
	{
		return BuildJacketUrls(assetbundleName, fallbackViewId: 0).FirstOrDefault() ?? string.Empty;
	}

	public static List<string> BuildJacketUrls(string? assetbundleName, int fallbackViewId)
	{
		var urls = new List<string>();
		AddJacketUrl(urls, BuildJacketUrlForRoot("sekai-jp-assets", assetbundleName));
		AddJacketUrl(urls, BuildJacketUrlForRoot("sekai-cn-assets", assetbundleName));

		if (fallbackViewId > 0)
		{
			var fallbackAsset = $"jacket_s_{fallbackViewId:D3}";
			AddJacketUrl(urls, BuildJacketUrlForRoot("sekai-jp-assets", fallbackAsset));
			AddJacketUrl(urls, BuildJacketUrlForRoot("sekai-cn-assets", fallbackAsset));
		}

		return urls;
	}

	private static string BuildJacketUrlForRoot(string assetRoot, string? assetbundleName)
	{
		if (string.IsNullOrWhiteSpace(assetbundleName)) return string.Empty;
		return $"https://storage.sekai.best/{assetRoot}/music/jacket/{assetbundleName}/{assetbundleName}.webp";
	}

	private static void AddJacketUrl(List<string> urls, string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate)) return;
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out _)) return;
		if (urls.Any(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase))) return;
		urls.Add(candidate);
	}
}

internal static class JsonPayloadGuard
{
	public static bool LooksLikeJson(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0) return false;

		int i = 0;
		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
		{
			i = 3;
		}

		for (; i < bytes.Length; i++)
		{
			byte b = bytes[i];
			if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
			return b == (byte)'{' || b == (byte)'[';
		}

		return false;
	}

	public static string Preview(byte[] bytes, int maxChars = 120)
	{
		if (bytes == null || bytes.Length == 0) return "<empty>";
		int len = Math.Min(bytes.Length, 1024);
		var text = Encoding.UTF8.GetString(bytes, 0, len).Replace('\r', ' ').Replace('\n', ' ').Trim();
		if (text.Length > maxChars) text = text[..maxChars] + "...";
		return text;
	}
}

internal class MusicMasterEntry
{
	public int Id { get; set; }
	public string AssetbundleName { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Creator { get; set; } = string.Empty;
}
