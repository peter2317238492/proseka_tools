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
	private readonly string _framesCacheDir;
	private readonly string _attributesCacheDir;
	private readonly string _starsCacheDir;

	public CardImageCacheService()
	{
		_cacheDir = Path.Combine(AppPaths.OutputOwnedCardsDir, "ImageCache");
		_framesCacheDir = Path.Combine(AppPaths.OutputOwnedCardsDir, "FramesCache");
		_attributesCacheDir = Path.Combine(AppPaths.OutputOwnedCardsDir, "AttributesCache");
		_starsCacheDir = Path.Combine(AppPaths.OutputOwnedCardsDir, "StarsCache");
		
		Directory.CreateDirectory(_cacheDir);
		Directory.CreateDirectory(_framesCacheDir);
		Directory.CreateDirectory(_attributesCacheDir);
		Directory.CreateDirectory(_starsCacheDir);
	}

	public string GetCachePath(int cardId) => Path.Combine(_cacheDir, $"card_{cardId}.png");
	
	public string GetFrameCachePath(int rarity) => Path.Combine(_framesCacheDir, $"frame_{rarity}.png");
	
	public string GetAttributeCachePath(string attribute) => Path.Combine(_attributesCacheDir, $"attr_{attribute}.png");
	
	public string GetStarCachePath(bool afterTraining) => Path.Combine(_starsCacheDir, $"star_{(afterTraining ? "after" : "normal")}.png");

	public bool IsCached(int cardId) => File.Exists(GetCachePath(cardId));
	
	public bool IsFrameCached(int rarity) => File.Exists(GetFrameCachePath(rarity));
	
	public bool IsAttributeCached(string attribute) => File.Exists(GetAttributeCachePath(attribute));
	
	public bool IsStarCached(bool afterTraining) => File.Exists(GetStarCachePath(afterTraining));

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
	
	public async Task<BitmapImage?> LoadCachedFrameAsync(int rarity)
	{
		return await LoadCachedImageFromPathAsync(GetFrameCachePath(rarity));
	}
	
	public async Task<BitmapImage?> LoadCachedAttributeAsync(string attribute)
	{
		return await LoadCachedImageFromPathAsync(GetAttributeCachePath(attribute));
	}
	
	public async Task<BitmapImage?> LoadCachedStarAsync(bool afterTraining)
	{
		return await LoadCachedImageFromPathAsync(GetStarCachePath(afterTraining));
	}
	
	private async Task<BitmapImage?> LoadCachedImageFromPathAsync(string path)
	{
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
	
	public async Task<bool> SaveFrameAsync(int rarity, SoftwareBitmap bitmap)
	{
		return await SaveImageAsync(GetFrameCachePath(rarity), bitmap);
	}
	
	public async Task<bool> SaveAttributeAsync(string attribute, SoftwareBitmap bitmap)
	{
		return await SaveImageAsync(GetAttributeCachePath(attribute), bitmap);
	}
	
	public async Task<bool> SaveStarAsync(bool afterTraining, SoftwareBitmap bitmap)
	{
		return await SaveImageAsync(GetStarCachePath(afterTraining), bitmap);
	}
	
	private async Task<bool> SaveImageAsync(string path, SoftwareBitmap bitmap)
	{
		try
		{
			using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
			using var ras = stream.AsRandomAccessStream();
			var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
			encoder.SetSoftwareBitmap(bitmap);
			await encoder.FlushAsync();
			return true;
		}
		catch
		{
			return false;
		}
	}
}
