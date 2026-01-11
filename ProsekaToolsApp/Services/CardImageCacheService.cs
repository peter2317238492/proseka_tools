using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ProsekaToolsApp.Services;

public class CardImageCacheService
{
	private readonly string _cacheDir;

	public CardImageCacheService()
	{
		_cacheDir = Path.Combine(AppPaths.OutputOwnedCardsDir, "ImageCache");
		Directory.CreateDirectory(_cacheDir);
	}

	public string GetCachePath(int cardId) => Path.Combine(_cacheDir, $"card_{cardId}.png");

	public bool IsCached(int cardId) => File.Exists(GetCachePath(cardId));

	public async Task<BitmapImage?> LoadCachedImageAsync(int cardId)
	{
		var path = GetCachePath(cardId);
		if (!File.Exists(path)) return null;

		try
		{
			var bitmap = new BitmapImage();
			using var stream = File.OpenRead(path);
			using var ras = stream.AsRandomAccessStream();
			await bitmap.SetSourceAsync(ras);
			return bitmap;
		}
		catch
		{
			return null;
		}
	}

	public async Task<bool> SaveCompositedImageAsync(int cardId, SoftwareBitmap compositeBitmap)
	{
		var path = GetCachePath(cardId);
		try
		{
			using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
			using var ras = stream.AsRandomAccessStream();
			var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
			encoder.SetSoftwareBitmap(compositeBitmap);
			await encoder.FlushAsync();
			return true;
		}
		catch
		{
			return false;
		}
	}
}
