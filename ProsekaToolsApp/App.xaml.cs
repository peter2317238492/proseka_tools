using Microsoft.UI.Xaml;
using ProsekaToolsApp.Services;

namespace ProsekaToolsApp
{
    public partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            // Apply saved theme before showing the window to avoid flash
            ThemeService.ApplySavedTheme();

            MainWindow.Activate();
        }

        public static Window? MainWindow { get; private set; }
    }
}
