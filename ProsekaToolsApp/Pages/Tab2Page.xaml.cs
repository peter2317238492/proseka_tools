using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ProsekaToolsApp.Services;

namespace ProsekaToolsApp.Pages;

public sealed partial class Tab2Page : Page
{
    private string? _selectedFile;
    // Warm up the site that sets/validates Origin/Referer
    private static readonly Uri WarmupUri = new("http://go.mikuware.top/");
    // Actual upload target (HTTP, per your capture)
    private static readonly Uri ApiBaseUri = new("http://101.34.19.31:5225");
    private const string UploadPath = "/uploadTwSuite";
    private const string FormFieldName = "files"; // confirmed by capture

    public Tab2Page()
    {
        InitializeComponent();
    }

    private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseLatestCheckBox?.IsChecked == true)
        {
            var latest = TryGetLatestSuiteCapture();
            if (latest == null)
            {
                SetStatus("未找到 suite 捕获文件。", true);
                return;
            }
            SetSelectedFile(latest, fromLatest:true);
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".bin");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            SetSelectedFile(file.Path);
        }
    }

    private void SetSelectedFile(string path, bool fromLatest=false)
    {
        _selectedFile = path;
        if (SelectedFileText != null)
        {
            SelectedFileText.Text = fromLatest ? $"最新: {Path.GetFileName(path)}" : Path.GetFileName(path);
        }
        SetStatus($"已选择文件: {Path.GetFileName(path)}");
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
        catch { return null; }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UploadButton.IsEnabled = false;
            UploadProgress.Visibility = Visibility.Visible;
            UploadProgress.Value = 0;
            SetStatus("准备上传...");

            var path = _selectedFile;
            if ((UseLatestCheckBox?.IsChecked == true) || string.IsNullOrWhiteSpace(path))
            {
                path = TryGetLatestSuiteCapture();
            }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetStatus("未选择有效文件。", true);
                return;
            }

            var result = await UploadFileAsync(path);
            SetStatus($"上传成功: {result}");
        }
        catch (Exception ex)
        {
            SetStatus($"上传失败: {ex.Message}", true);
        }
        finally
        {
            UploadButton.IsEnabled = true;
            UploadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<string> UploadFileAsync(string filePath)
    {
        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        using var client = new HttpClient(handler) { BaseAddress = ApiBaseUri };

        // Browser-like headers the backend might check
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        try { client.DefaultRequestHeaders.Add("Origin", "http://go.mikuware.top"); } catch {}
        client.DefaultRequestHeaders.Referrer = WarmupUri;
        client.Timeout = TimeSpan.FromSeconds(90);

        // Optional: warm up domain (no SSL since http)
        try
        {
            using var warm = await client.GetAsync(WarmupUri);
            // do not enforce success; not critical
        }
        catch { }

        using var form = new MultipartFormDataContent();
        await using var fs = File.OpenRead(filePath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // Field name must be 'files'
        form.Add(fileContent, FormFieldName, Path.GetFileName(filePath));
        // Add uploadtime in the captured format
        var uploadTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        form.Add(new StringContent(uploadTime), "uploadtime");

        var resp = await client.PostAsync(UploadPath, form);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}\n{body}");
        }
        return body;
    }

    private void DropBorder_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void DropBorder_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault();
            if (file == null)
            {
                SetStatus("未检测到文件。", true);
                return;
            }
            if (Path.GetExtension(file.Name).Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                SetSelectedFile(file.Path);
            }
            else
            {
                SetStatus("仅支持 .bin 文件。", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void SetStatus(string message, bool isError=false)
    {
        if (StatusText == null) return;
        StatusText.Text = message;
        if (isError)
        {
            StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        }
        else
        {
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
