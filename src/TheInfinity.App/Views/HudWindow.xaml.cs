using System.Windows;
using System.Windows.Interop;
using TheInfinity.App.ViewModels;
using TheInfinity.Win32;

namespace TheInfinity.App.Views;

public partial class HudWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public HudWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => Place();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _viewModel.Controller.AttachHotkeys(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not System.Windows.Controls.TextBox)
                DragMove();
        };
    }

    public void Place()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + 16;
    }

    public void FocusSearch() => Bar.FocusSearch();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            _viewModel.Controller.HandleHotkey(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }
}
