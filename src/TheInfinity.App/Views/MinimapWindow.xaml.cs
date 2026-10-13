using System.Windows;
using System.Windows.Input;
using TheInfinity.App.ViewModels;

namespace TheInfinity.App.Views;

public partial class MinimapWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MinimapWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Map.Navigate += point => _viewModel.Controller.CenterOnWorld(point.X, point.Y);
        Map.PanWorld += (dx, dy) => _viewModel.Controller.PanCanvas(dx, dy);
        _viewModel.Controller.StateChanged += Update;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _viewModel.Controller.ExitOverview();
                e.Handled = true;
            }
        };
    }

    public void Place()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 16;
        Top = work.Bottom - Height - 16;
    }

    public void Update()
    {
        if (!IsVisible)
            return;
        var session = _viewModel.Controller.Session;
        Map.Items = session.Windows.Select(window => window.CanvasBounds).ToList();
        Map.ViewportWorld = session.VisibleCanvas();
        var selected = session.Windows.FirstOrDefault(window => session.Selection.Contains(window.Id));
        Map.Selected = selected?.CanvasBounds ?? default;
    }
}
