using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

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

		public event PropertyChangedEventHandler PropertyChanged;

		private DispatcherTimer _timer;

		public GrabDataPage()
		{
			InitializeComponent();
			DataContext = this;
			UpdateLocalIp();

			_timer = new DispatcherTimer();
			_timer.Interval = TimeSpan.FromSeconds(2); // 每2秒刷新一次
			_timer.Tick += (s, e) => UpdateLocalIp();
			_timer.Start();
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
	}
}