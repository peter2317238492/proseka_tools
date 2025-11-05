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

namespace ProsekaToolsApp.Pages;

public sealed partial class Tab3Page : Page
{
    private string? _selectedFile;

    public Tab3Page()
    {
        InitializeComponent();
    }

    private CheckBox? GetUseLatestCheckBox() => FindName("UseLatestCheckBox") as CheckBox;
    private TextBox? GetSelectedFileTextBox() => FindName("SelectedFileTextBox") as TextBox;
    private ProgressRing? GetWorkingRing() => FindName("WorkingRing") as ProgressRing;
    private TextBlock? GetStatusTextBlock() => FindName("StatusText") as TextBlock;
    private ComboBox? GetRegionCombo() => FindName("RegionCombo") as ComboBox;

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
            SetStatus($"已选择最新: {Path.GetFileName(_selectedFile)}");
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".json");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        // Initialize with window handle
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
            var dir = Path.Combine(root, "captures", "mysekai");
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

            var outputDir = Path.Combine(AppContext.BaseDirectory, "output", "mysekai");
            Directory.CreateDirectory(outputDir);
            var outName = Path.GetFileNameWithoutExtension(inputFile) + ".json";
            var outputPath = Path.Combine(outputDir, outName);

            var exePath = Path.Combine(AppContext.BaseDirectory, "Services", "sssekai.exe");
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
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
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

    private void SetStatus(string message, bool isError = false)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var tb = GetStatusTextBlock();
            if (tb == null) return;
            tb.Text = message;
            if (isError)
            {
                tb.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }
            else
            {
                tb.ClearValue(TextBlock.ForegroundProperty);
            }
        });
    }
}
