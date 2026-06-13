using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace MAIS.Modules.CrimsSeverity.Sidebar
{
    public partial class CrimsSeverityCard : System.Windows.Controls.UserControl
    {
        private bool _initialised;

        public CrimsSeverityCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialised) return;

            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MAIS", "WebView2");

                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await SeverityWebView.EnsureCoreWebView2Async(env);

                SeverityWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                SeverityWebView.CoreWebView2.Settings.AreDevToolsEnabled             = false;
                SeverityWebView.CoreWebView2.Settings.IsStatusBarEnabled             = false;
                SeverityWebView.CoreWebView2.Settings.IsZoomControlEnabled           = false;

                // Handle certificate validation for development
                SeverityWebView.CoreWebView2.NavigationStarting += (s, e) =>
                {
#if DEBUG
                    // In development, suppress certificate validation errors
                    if (e.Uri.Contains("localhost"))
                    {
                        e.Cancel = false;
                    }
#endif
                };

                _initialised = true;
                LoadingPlaceholder.Visibility = Visibility.Collapsed;

                var vm = DataContext as CrimsSeverityCardViewModel;
                var panelUrl = vm?.PanelUrl ?? "http://localhost:5002/severity-panel.html";
                var hubUrl = vm?.HubUrl ?? "http://localhost:5002/hubs/severity";

                // Inject the hub URL into the page so it connects to the right service
                await SeverityWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    $"window.__MAIS_HUB_URL__ = '{hubUrl}';");

                SeverityWebView.CoreWebView2.Navigate(panelUrl);


            }
            catch (Exception ex)
            {
                LoadingPlaceholder.Visibility = Visibility.Visible;
                if (LoadingPlaceholder.Child is TextBlock tb)
                    tb.Text = $"WebView2 failed: {ex.Message}";
            }
        }
    }
}
