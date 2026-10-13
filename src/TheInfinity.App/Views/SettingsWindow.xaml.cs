using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TheInfinity.App.ViewModels;
using TheInfinity.Core.Models;

namespace TheInfinity.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly UserPreferences _draft;

    public SettingsWindow(ShellViewModel shell)
    {
        _shell = shell;
        var source = shell.Preferences;
        _draft = new UserPreferences
        {
            MinZoom = source.MinZoom,
            SnapEnabled = source.SnapEnabled,
            SnapThreshold = source.SnapThreshold,
            ShowPlaceholders = source.ShowPlaceholders,
            RestoreLayoutOnStartup = source.RestoreLayoutOnStartup,
            LaunchMissingApps = source.LaunchMissingApps,
            StartWithWindows = source.StartWithWindows,
            AutoSave = source.AutoSave,
            EnableGlobalPan = source.EnableGlobalPan
        };
        InitializeComponent();
        DataContext = new SettingsContext(_draft, shell, Shortcuts);
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += async (_, _) => await shell.ReloadLayoutsAsync();
    }

    public sealed record Shortcut(string[] Keys, string Action);

    private static readonly Shortcut[] Shortcuts =
    [
        new(["Ctrl", "Alt", "Scroll"], "Zoom out or in, from any app"),
        new(["Ctrl", "Alt", "Middle-drag"], "Pan, from any app"),
        new(["Ctrl", "Alt", "O"], "Overview, and back to where you were"),
        new(["Ctrl", "Alt", "H"], "Back to the start"),
        new(["Ctrl", "Alt", "F"], "Fit every window on screen"),
        new(["Ctrl", "Alt", "Arrows"], "Pan with the keyboard"),
        new(["Ctrl", "Alt", "=", "-"], "Zoom with the keyboard"),
        new(["Ctrl", "Alt", "S"], "Search windows"),
        new(["Ctrl", "Alt", "J"], "Jump to the selected window"),
        new(["Esc"], "Leave the overview")
    ];

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        _shell.Controller.UpdatePreferences(_draft);
        DialogResult = true;
    }

    private sealed record SettingsContext(UserPreferences Draft, ShellViewModel Shell, Shortcut[] Shortcuts);

    private async void LayoutActivated(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: LayoutItemViewModel layout })
        {
            await _shell.Controller.RestoreLayoutAsync(layout.Id);
            Close();
        }
    }

    // Matches the title bar to the dark theme. These attributes are documented; older Windows
    // versions simply ignore them and keep the standard title bar.
    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));          // dark mode
            var caption = 0x00140E0B;                                       // COLORREF 0x00BBGGRR for #0B0E14
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));      // caption colour (Windows 11)
            var text = 0x00F7EEE8;
            DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));         // caption text colour
        }
        catch
        {
            // Cosmetic only.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
