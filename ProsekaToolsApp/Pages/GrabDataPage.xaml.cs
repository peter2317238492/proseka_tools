using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using ProsekaToolsApp.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ProsekaToolsApp.Pages
{
	public partial class GrabDataPage : Page, INotifyPropertyChanged
	{
		private string _localIp;
		public string LocalIp
		{
			get => _localIp;
			set
			{
				if (_localIp != value)
				{
					_localIp = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalIp)));
				}
			}
		}

		private string _serverStatus = "未启动";
		public string ServerStatus
		{
			get => _serverStatus;
			set
			{
				if (_serverStatus != value)
				{
					_serverStatus = value;
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ServerStatus)));
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;

		private DispatcherTimer _timer;

		// capture server
		private HttpListener _listener;
		private CancellationTokenSource _serverCts;
		private bool _isServerRunning;

		// capture settings
		private const int ServerPort = 8000;
		private readonly string _outputRoot = AppPaths.GetCapturesRoot();

		// UI: capture logs
		public ObservableCollection<string> CaptureLogs { get; } = new();

		public GrabDataPage()
		{
			InitializeComponent();
			DataContext = this;
			UpdateLocalIp();

			_timer = new DispatcherTimer();
			_timer.Interval = TimeSpan.FromSeconds(2); // 每2秒刷新一次
			_timer.Tick += (s, e) => UpdateLocalIp();
			_timer.Start();

			this.Unloaded += GrabDataPage_Unloaded;

#if DISABLE_EXTERNAL_PROCESS
			// Avoid any UWP/Store check issues: disable firewall helper UI when ext. process is disabled
			Loaded += (s, e) =>
			{
				if (FindName("FirewallButton") is Button fb)
				{
					fb.IsEnabled = false;
					Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fb, "此构建禁用了外部进程启动");
				}
			};
#endif
		}

		private void UpdateLocalIp()
		{
			LocalIp = GetLocalIpAddress();
		}

		private string GetLocalIpAddress()
		{
			try
			{
				var host = Dns.GetHostEntry(Dns.GetHostName());
				var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
				return ip?.ToString() ?? "未检测到IP";
			}
			catch
			{
				return "获取失败";
			}
		}

		private async void ServerToggle_Toggled(object sender, RoutedEventArgs e)
		{
			var isOn = (sender as ToggleSwitch)?.IsOn ?? false;
			if (isOn)
			{
				await StartServerAsync();
			}
			else
			{
				await StopServerAsync();
			}
		}

		private async Task StartServerAsync()
		{
			if (_isServerRunning) return;

			try
			{
				_listener = new HttpListener();
				// 对外访问：监听所有地址。注意：通常需要 URLACL 权限。
				_listener.Prefixes.Add($"http://+:{ServerPort}/");
				_listener.Start();

				_serverCts = new CancellationTokenSource();
				_isServerRunning = true;
				ServerStatus = $"运行中: http://{LocalIp}:{ServerPort}/ (对外可访问)";
				AppendLog($"Server started on :{ServerPort}");
				AppendLog($"Test page: http://{LocalIp}:{ServerPort}/ (手机可直接访问)");

				_ = Task.Run(() => AcceptLoopAsync(_serverCts.Token));
			}
			catch (HttpListenerException hex)
			{
				_isServerRunning = false;
				ServerStatus = $"启动失败: {hex.Message}. 可能需要管理员权限或执行:\nnetsh http add urlacl url=http://+:{ServerPort}/ user=%USERNAME%";
				AppendLog($"Server start failed: {hex.Message}");
				DispatcherQueue.TryEnqueue(() =>
				{
					if (ServerToggle.IsOn) ServerToggle.IsOn = false;
				});
			}
			catch (Exception ex)
			{
				_isServerRunning = false;
				ServerStatus = $"启动失败: {ex.Message}";
				AppendLog($"Server start failed: {ex.Message}");
				DispatcherQueue.TryEnqueue(() =>
				{
					if (ServerToggle.IsOn) ServerToggle.IsOn = false;
				});
			}
		}

		private async Task StopServerAsync()
		{
			try
			{
				_serverCts?.Cancel();
				_listener?.Stop();
				_listener?.Close();
			}
			catch { }
			finally
			{
				_listener = null;
				_serverCts = null;
				_isServerRunning = false;
				ServerStatus = "已停止";
				AppendLog("Server stopped");
			}

			await Task.CompletedTask;
		}

		private async Task AcceptLoopAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
			{
				HttpListenerContext ctx = null;
				try
				{
					ctx = await _listener.GetContextAsync();
				}
				catch
				{
					break; // 监听关闭
				}

				_ = Task.Run(async () =>
				{
					try
					{
						var request = ctx.Request;
						var response = ctx.Response;

						// 路由分发
						var path = request.Url.AbsolutePath.TrimEnd('/');
						switch (path)
						{
							case "":
								await HandleIndexPageAsync(response);
								break;

							case "/status":
								await WriteJsonAsync(response, new { status = "ok", ip = LocalIp, port = ServerPort, time = DateTimeOffset.Now });
								break;

							case "/upload":
								await HandleUploadAsync(request, response);
								break;

							case "/upload.js":
								await HandleUploadJsAsync(response);
								break;

							default:
								response.StatusCode = 404;
								await WriteTextAsync(response, "Not Found", "text/plain; charset=utf-8");
								break;
						}
					}
					catch (Exception ex)
					{
						try
						{
							ctx.Response.StatusCode = 500;
							await WriteTextAsync(ctx.Response, ex.Message, "text/plain; charset=utf-8");
						}
						catch { }
						AppendLog($"Error: {ex.Message}");
					}
				}, token);
			}
		}

		private async Task HandleIndexPageAsync(HttpListenerResponse response)
		{
			var html = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Proseka Tools Capture Test</title>
  <style>
    body { font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; margin: 16px; }
    .ok { color: #0a7a0a; }
    .fail { color: #b00020; }
    .card { border: 1px solid #ddd; border-radius: 8px; padding: 12px; margin: 12px 0; box-shadow: 0 1px 2px rgba(0,0,0,.04); }
    button { padding: 8px 14px; border-radius: 6px; border: 1px solid #ccc; background: #f7f7f7; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
  </style>
</head>
<body>
  <h2>Capture Server Reachability</h2>
  <div class="card">
    <div>Server: <b>http://__IP__:__PORT__/</b></div>
    <div id="ua">Client: <code></code></div>
    <div id="status">Status: <span>unknown</span></div>
    <div style="margin-top:8px">
      <button id="btn">Ping /status</button>
    </div>
  </div>

  <div class="card">
    <div>Upload helper: <a href="/upload.js">/upload.js</a></div>
  </div>

  <script>
    const $ = s => document.querySelector(s);
    $('#ua').querySelector('code').textContent = navigator.userAgent;
    async function ping(){
      const el = $('#status').querySelector('span');
      el.textContent = 'checking...';
      try {
        const r = await fetch('/status', { cache: 'no-store' });
        if(!r.ok) throw new Error('HTTP ' + r.status);
        const j = await r.json();
        el.textContent = 'OK @ ' + j.time;
        el.className = 'ok';
      } catch(e){
        el.textContent = 'FAILED: ' + e.message;
        el.className = 'fail';
      }
    }
    $('#btn').addEventListener('click', ping);
    ping();
  </script>
</body>
</html>
""";

			html = html.Replace("__IP__", LocalIp).Replace("__PORT__", ServerPort.ToString());

			response.StatusCode = 200;
			await WriteTextAsync(response, html, "text/html; charset=utf-8");
		}

		private async Task HandleUploadAsync(HttpListenerRequest request, HttpListenerResponse response)
		{
			// 仅允许 POST
			if (request.HttpMethod?.Equals("POST", StringComparison.OrdinalIgnoreCase) != true)
			{
				response.StatusCode = 405;
				response.Headers.Add("Allow", "POST");
				await WriteTextAsync(response, "Method Not Allowed", "text/plain; charset=utf-8");
				return;
			}

			var originalUrl = request.Headers["X-Original-Url"] ?? string.Empty;
			var apiType = ExtractApiType(originalUrl);
			var filename = GenerateFilename(apiType, originalUrl);

			byte[] body;
			using (var ms = new MemoryStream())
			{
				await request.InputStream.CopyToAsync(ms);
				body = ms.ToArray();
			}

			var savePath = BuildSavePath(apiType, filename);
			Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
			await File.WriteAllBytesAsync(savePath, body);

			Debug.WriteLine($"Saved [{apiType.ToUpper()}]: {savePath} ({body.Length / 1024.0:F2} KB)\nFrom: {originalUrl}");
			AppendLog($"[{apiType}] saved {Path.GetFileName(savePath)} ({body.Length/1024.0:F2} KB)");

			response.StatusCode = 200;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			await WriteTextAsync(response, "ok", "text/plain; charset=utf-8");
		}

		private async Task HandleUploadJsAsync(HttpListenerResponse response)
		{
			var js = $@"
            const upload = () => {{

                $httpClient.post({{
                    url: 'http://{LocalIp}:{ServerPort}/upload',
                    headers: {{ 
                        'X-Original-Url': $request.url,
                        'X-Request-Path': $request.path
                    }},
                    body: $response.body
                }}, (error) => $done({{}}));
            }};
            upload();
            ";

			var jsContent = js.Trim();
			response.StatusCode = 200;
			response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
			response.Headers.Add("Pragma", "no-cache");
			response.Headers.Add("Expires", "0");
			await WriteTextAsync(response, jsContent, "application/javascript; charset=utf-8");
		}

		private static string ExtractApiType(string url)
		{
			if (string.IsNullOrEmpty(url)) return "unknown";
			if (Regex.IsMatch(url, @"/mysekai(\?|$)", RegexOptions.IgnoreCase)) return "mysekai";
			if (Regex.IsMatch(url, @"/suite/", RegexOptions.IgnoreCase)) return "suite";
			return "unknown";
		}

		private static string GenerateFilename(string apiType, string originalUrl)
		{
			var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var uidMatch = Regex.Match(originalUrl ?? string.Empty, @"/user/(\d+)", RegexOptions.IgnoreCase);
			var userStr = uidMatch.Success ? $"_user{uidMatch.Groups[1].Value}" : string.Empty;
#if DISABLE_EXTERNAL_PROCESS
			// Avoid using Process APIs when external processes are disabled
			var pid = Environment.ProcessId;
#else
			var pid = Process.GetCurrentProcess().Id;
#endif
			return $"{apiType}{userStr}_{ts}_{pid}.bin";
		}

		private string BuildSavePath(string apiType, string filename)
		{
			var category = (apiType == "mysekai" || apiType == "suite") ? apiType : "unknown";
			var folder = Path.Combine(_outputRoot, category);
			return Path.Combine(folder, filename);
		}

		private static async Task WriteJsonAsync(HttpListenerResponse response, object obj)
		{
			var json = System.Text.Json.JsonSerializer.Serialize(obj);
			await WriteTextAsync(response, json, "application/json; charset=utf-8");
		}

		private static async Task WriteTextAsync(HttpListenerResponse response, string text, string contentType)
		{
			var bytes = Encoding.UTF8.GetBytes(text);
			response.ContentType = contentType;
			response.ContentLength64 = bytes.Length;
			using (var os = response.OutputStream)
			{
				await os.WriteAsync(bytes, 0, bytes.Length);
			}
		}

		private void AppendLog(string line)
		{
			var stamp = DateTime.Now.ToString("HH:mm:ss");
			var text = $"[{stamp}] {line}";
			DispatcherQueue.TryEnqueue(() =>
			{
				CaptureLogs.Add(text);
				// 自动滚动到底部
				if (LogsList != null && LogsList.Items?.Count > 0)
				{
					LogsList.ScrollIntoView(LogsList.Items[LogsList.Items.Count - 1]);
				}
			});
		}

		private void ClearLogs_Click(object sender, RoutedEventArgs e)
		{
			CaptureLogs.Clear();
		}

		private async void GrabDataPage_Unloaded(object sender, RoutedEventArgs e)
		{
			await StopServerAsync();
		}

		private async void AddFirewallRule_Click(object sender, RoutedEventArgs e)
		{
#if DISABLE_EXTERNAL_PROCESS
			AppendLog("此构建禁用了外部进程启动，跳过添加防火墙规则。");
			return;
#else
			AppendLog("请求添加防火墙规则 (TCP 8000)...");
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = "netsh",
					Arguments = "advfirewall firewall add rule name=\"Allow8000\" dir=in action=allow protocol=TCP localport=8000",
					UseShellExecute = true,
					Verb = "runas",
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				};

				var proc = Process.Start(psi);
				if (proc == null)
				{
					AppendLog("无法启动 netsh 进程。");
					return;
				}

				await Task.Run(() => proc.WaitForExit());
				if (proc.ExitCode == 0)
				{
					AppendLog("已添加防火墙规则：Allow8000 (TCP 8000)。");
				}
				else
				{
					AppendLog($"添加防火墙规则可能失败，ExitCode={proc.ExitCode}。");
				}
			}
			catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				// 用户取消了 UAC 提示
				AppendLog("已取消提升权限，未添加规则。");
			}
			catch (Exception ex)
			{
				AppendLog($"添加防火墙规则异常: {ex.Message}");
			}
#endif
		}
	}
}