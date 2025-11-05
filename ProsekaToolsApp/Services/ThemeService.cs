using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace ProsekaToolsApp.Services;

public static class ThemeService
{
	private const string SettingKey = "AppTheme";
	private static readonly string FallbackFile =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					 "ProsekaToolsApp", "settings.json");

	public static void ApplySavedTheme()
	{
		ApplyTheme(GetSavedTheme());
	}

	public static ElementTheme GetSavedTheme()
	{
		// Try packaged settings first
		if (TryGetLocalSettings(out var local))
		{
			var value = local.Values[SettingKey] as string;
			return value switch
			{
				"Light" => ElementTheme.Light,
				"Dark" => ElementTheme.Dark,
				_ => ElementTheme.Default
			};
		}

		// Unpackaged fallback: JSON in %LocalAppData%
		if (!File.Exists(FallbackFile)) return ElementTheme.Default;

		var json = File.ReadAllText(FallbackFile);
		var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
		return dict.TryGetValue(SettingKey, out var value2)
			? value2 switch
			{
				"Light" => ElementTheme.Light,
				"Dark" => ElementTheme.Dark,
				_ => ElementTheme.Default
			}
			: ElementTheme.Default;
	}

	public static void SaveTheme(ElementTheme theme)
	{
		var value = theme switch
		{
			ElementTheme.Light => "Light",
			ElementTheme.Dark => "Dark",
			_ => "System"
		};

		if (TryGetLocalSettings(out var local))
		{
			local.Values[SettingKey] = value;
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(FallbackFile)!);
		var dict = File.Exists(FallbackFile)
			? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FallbackFile)) ?? new()
			: new Dictionary<string, string>();
		dict[SettingKey] = value;
		File.WriteAllText(FallbackFile, JsonSerializer.Serialize(dict));
	}

	private static bool TryGetLocalSettings(out ApplicationDataContainer local)
	{
		try
		{
			local = ApplicationData.Current.LocalSettings;
			return local is not null;
		}
		catch
		{
			local = null!;
			return false;
		}
	}

	public static void ApplyTheme(ElementTheme theme)
	{
		SaveTheme(theme);

		if (App.MainWindow?.Content is FrameworkElement root)
		{
			root.RequestedTheme = theme;
		}
		// 方案A：不在运行期写 Application.RequestedTheme，避免 WinRT COM 异常
	}
}