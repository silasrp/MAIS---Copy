using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;

namespace MAIS.Modules.IdaLogIngestion.Sidebar;

public partial class TemplateReviewPanel : Window
{
    private readonly TemplateReviewPanelViewModel _vm;

    public TemplateReviewPanel(string serviceBaseUrl, IReadOnlyList<string> appIds)
    {
        InitializeComponent();

        var http = new HttpClient { BaseAddress = new Uri(serviceBaseUrl) };
        _vm = new TemplateReviewPanelViewModel(
            http, appIds,
            NullLogger<TemplateReviewPanelViewModel>.Instance);

        DataContext = _vm;

        PositionLeftOfScreen();

        // Load immediately so items are visible when the window opens.
        _ = _vm.LoadAsync();
    }

    private void PositionLeftOfScreen()
    {
        var screen = SystemParameters.WorkArea;
        Height = screen.Height * 0.80;
        Top    = screen.Top + (screen.Height - Height) / 2;
        // Sidebar occupies roughly the rightmost 280 px; open panel just to its left.
        Left   = screen.Right - 280 - Width - 8;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Close(); return; }
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
