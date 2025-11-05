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
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml.Shapes;
using IOPath = System.IO.Path;

namespace ProsekaToolsApp.Pages;

public sealed partial class Tab3Page : Page
{
	private string? _selectedFile;

	// Data for map drawing
	private Dictionary<int, MapScene> _scenes = MapScene.CreateDefaults();
	private Dictionary<int, MapData> _maps = new();
	private int _currentSiteId = 1;

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

	// XAML element getters (avoid compile-time name binding issues)
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
			var root = AppContext.BaseDirectory;
			var dir = IOPath.Combine(root, "captures", "mysekai");
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

			var outputDir = IOPath.Combine(AppContext.BaseDirectory, "output", "mysekai");
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
		}
		finally
		{
			var ring = GetWorkingRing();
			if (ring != null) ring.IsActive = false;
		}
	}

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
			var dir = IOPath.Combine(AppContext.BaseDirectory, "output", "mysekai");
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

			var maps = new Dictionary<int, MapData>();
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
						var resourceId = dr.GetProperty("resourceId").GetInt32().ToString();
						var qty = dr.GetProperty("quantity").GetInt32();

						var pt = mapDetails.FirstOrDefault(d => Math.Abs(d.X - px) < 1e-6 && Math.Abs(d.Y - pz) < 1e-6);
						if (pt != null)
						{
							if (!pt.Rewards.TryGetValue(resourceType, out var bag))
							{
								bag = new Dictionary<string, int>();
								pt.Rewards[resourceType] = bag;
							}
							bag[resourceId] = bag.TryGetValue(resourceId, out var old) ? old + qty : qty;
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

			// Refresh combo items and draw current
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
						var iconRel = IconResolver.GetIconRelativePath(kv.Key, item.Key);
						if (iconRel == null) continue;
						var iconPath = IOPath.Combine(AppContext.BaseDirectory, "assets", "sekai_xray", iconRel);
						if (!File.Exists(iconPath)) continue;
						var bmpIcon = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
						var imgIcon = new Image { Source = bmpIcon, Width = 24, Height = 24 };
						Canvas.SetLeft(imgIcon, displayX + idx * 28);
						Canvas.SetTop(imgIcon, displayY - 12);
						canvas.Children.Add(imgIcon);
						// draw a colored rectangle behind icon to emphasize rarity
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
	// map resource types to icon files relative to assets/sekai_xray
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
