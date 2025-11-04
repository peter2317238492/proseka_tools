using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace ProsekaToolsApp
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            Title = "Proseka Tools";
			ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
			// Select the first item by default
			NavView.SelectedItem = NavView.MenuItems[0];
        }

		private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
		{
			// ① 内置“设置”按钮走这里
			if (args.IsSettingsSelected)
			{
				ContentFrame.Navigate(typeof(Pages.SettingsPage));
				return;
			}
			if (args.SelectedItem is NavigationViewItem item)
			{
				switch (item.Tag)
				{
					case "Tab1":
						ContentFrame.Navigate(typeof(Pages.GrabDataPage));
						break;

					case "Tab2":
						ContentFrame.Navigate(typeof(Pages.Tab2Page));
						break;

					case "Tab3":
						ContentFrame.Navigate(typeof(Pages.Tab3Page));
						break;
				}
			}
		}
		     
	}
}
