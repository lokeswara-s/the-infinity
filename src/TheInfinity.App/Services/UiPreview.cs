using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using TheInfinity.App.ViewModels;
using TheInfinity.App.Views;

namespace TheInfinity.App.Services;

/// <summary>
/// Renders windows to PNG files off-screen (nothing is shown, no hooks are installed, no other
/// window is touched). Used to check the look of the UI during development.
/// </summary>
public static class UiPreview
{
    public static void Render(ServiceProvider services, string folder)
    {
        Directory.CreateDirectory(folder);
        var viewModel = services.GetRequiredService<ShellViewModel>();
        viewModel.Layouts.Add(new LayoutItemViewModel { Id = Guid.NewGuid(), Name = "Morning setup", Created = "10/14/2026 9:12 AM" });
        viewModel.Layouts.Add(new LayoutItemViewModel { Id = Guid.NewGuid(), Name = "Focus: code + docs", Created = "10/13/2026 6:40 PM" });

        var hud = new HudWindow(viewModel);
        Save(hud, Path.Combine(folder, "commandbar.png"));
        viewModel.SearchText = "";
        var settings = new SettingsWindow(viewModel) { Height = 1750 }; // tall, so the whole scrolling page is visible
        Save(settings, Path.Combine(folder, "settings.png"));
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var width = double.IsNaN(window.Width) ? 800 : window.Width;
        var height = double.IsNaN(window.Height) ? 600 : window.Height;
        // Window chrome is not part of Content, so draw the window background behind it.
        var host = new Border { Background = window.Background is { } b && b != Brushes.Transparent ? b : new SolidColorBrush(Color.FromRgb(0x47, 0x4C, 0x58)), Child = content, DataContext = window.DataContext };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        var dpi = 144d;
        var scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
