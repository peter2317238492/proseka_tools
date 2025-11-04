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
            
            // Select the first item by default
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString();
                
                switch (tag)
                {
                    case "Tab1":
                        PageTitle.Text = "Tab 1 - Home";
                        PageContent.Text = "This is the content for Tab 1. You can add your home page content here.";
                        break;
                    case "Tab2":
                        PageTitle.Text = "Tab 2 - Documents";
                        PageContent.Text = "This is the content for Tab 2. You can add your document management features here.";
                        break;
                    case "Tab3":
                        PageTitle.Text = "Tab 3 - Library";
                        PageContent.Text = "This is the content for Tab 3. You can add your library features here.";
                        break;
                    case "Tab4":
                        PageTitle.Text = "Tab 4 - Settings";
                        PageContent.Text = "This is the content for Tab 4. You can add your settings and preferences here.";
                        break;
                }
            }
        }
    }
}
