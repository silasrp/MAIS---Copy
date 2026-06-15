using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using MAIS.Modules.CrimsAddinHealth.Models;

namespace MAIS.Modules.CrimsAddinHealth.Sidebar;

public partial class AddinHealthPanel : Window
{
    private readonly AddinHealthPanelViewModel _vm;

    public AddinHealthPanel(string serviceBaseUrl, Func<UpdateApproval, Task>? submitApproval = null)
    {
        InitializeComponent();

        var httpClient = new HttpClient { BaseAddress = new Uri(serviceBaseUrl) };

        _vm = new AddinHealthPanelViewModel(
            serviceClient:    httpClient,
            localClientId:    Environment.MachineName,
            localMachineName: Environment.MachineName,
            localUserId:      Environment.UserName,
            submitApproval:   submitApproval);

        DataContext = _vm;

        PositionLeftOfScreen();
        HighlightTab(BtnPending);
    }


    public AddinHealthPanelViewModel ViewModel => _vm;

    private void PositionLeftOfScreen()
    {
        var screen = SystemParameters.WorkArea;
        Height = screen.Height * 0.80;
        Top    = screen.Top + (screen.Height - Height) / 2;
        // Sidebar is typically ~280px on the right edge
        Left   = screen.Right - 280 - Width - 8;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click closes instead of toggling maximise (WindowStyle=None)
            Close();
            return;
        }
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string ?? "Pending";

        PendingPane.Visibility   = tag == "Pending"   ? Visibility.Visible : Visibility.Collapsed;
        ScheduledPane.Visibility = tag == "Scheduled" ? Visibility.Visible : Visibility.Collapsed;
        AuditPane.Visibility     = tag == "Audit"     ? Visibility.Visible : Visibility.Collapsed;

        HighlightTab(btn);

        if (tag == "Audit" && !_vm.AuditRecords.Any())
            _ = _vm.LoadAuditCommand.ExecuteAsync(null);
    }

    private void HighlightTab(Button active)
    {
        foreach (var btn in new[] { BtnPending, BtnScheduled, BtnAudit })
        {
            btn.BorderBrush = btn == active
                ? System.Windows.Media.Brushes.SteelBlue
                : System.Windows.Media.Brushes.Transparent;
            btn.Foreground = btn == active
                ? System.Windows.Media.Brushes.White
                : (System.Windows.Media.Brush)FindResource("TextSecondary");
        }
    }
}