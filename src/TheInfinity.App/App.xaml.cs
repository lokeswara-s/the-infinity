using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TheInfinity.App.Logging;
using TheInfinity.App.Services;
using TheInfinity.App.ViewModels;
using TheInfinity.App.Views;
using TheInfinity.Core.Persistence;
using TheInfinity.Core.Services;
using TheInfinity.Persistence;
using TheInfinity.Win32;
using Forms = System.Windows.Forms;

namespace TheInfinity.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private ServiceProvider? _services;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private HudWindow? _hud;
    private OverviewWindow? _overview;
    private MinimapWindow? _minimap;
    private bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Developer aid: `TheInfinity.exe --render-ui <folder>` draws the UI to PNG files without touching the desktop.
        var previewAt = Array.IndexOf(e.Args, "--render-ui");
        if (previewAt >= 0 && previewAt + 1 < e.Args.Length)
        {
            try
            {
                UiPreview.Render(BuildServices(), e.Args[previewAt + 1]);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(e.Args[previewAt + 1], "error.txt"), ex.ToString());
            }

            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            _services?.GetService<ILogger<App>>()?.LogError(args.Exception, "Unhandled UI exception.");
            args.Handled = true;
        };

        _instance = new Mutex(true, @"Local\TheInfinity.SingleInstance", out var created);
        if (!created)
        {
            try
            {
                EventWaitHandle.OpenExisting(@"Local\TheInfinity.Show").Set();
            }
            catch
            {
                // The first instance is not ready yet.
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TheInfinity.Show");
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowFromSecondInstance), null, Timeout.Infinite, false);

        _services = BuildServices();

        var controller = _services.GetRequiredService<CanvasController>();
        var viewModel = _services.GetRequiredService<ShellViewModel>();
        _hud = new HudWindow(viewModel);
        _overview = new OverviewWindow(viewModel);
        _minimap = new MinimapWindow(viewModel);
        viewModel.SettingsRequested += () => OpenSettings(viewModel);
        _hud.Closing += (_, args) =>
        {
            if (_shuttingDown)
                return;
            args.Cancel = true;
            _hud.Hide();
        };
        _overview.Closing += (_, args) =>
        {
            if (_shuttingDown)
                return;
            args.Cancel = true;
            if (viewModel.IsOverview)
                controller.ExitOverview();
        };
        _minimap.Closing += (_, args) =>
        {
            if (!_shuttingDown)
                args.Cancel = true;
        };
        controller.FocusSearchRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _hud.Show();
            _hud.Activate();
            _hud.FocusSearch();
        });
        // The command bar and minimap live in their own windows so DWM thumbnails cannot cover them.
        controller.StateChanged += () => SyncChrome(viewModel.IsOverview);
        _overview.Presented += () => RaiseChrome();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        CreateTray(controller, viewModel);
        _hud.Show();

        try
        {
            await controller.StartAsync();
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<App>>().LogError(ex, "Startup failed.");
            System.Windows.MessageBox.Show("Infinity could not start. See the log in %LocalAppData%\\TheInfinity\\logs.", "The Infinity");
            await ExitAsync();
            return;
        }

        viewModel.Pull();
        _overview.OnStateChanged();
        if (controller.FailedHotkeys.Count > 0)
            _services.GetRequiredService<ILogger<App>>().LogWarning(
                "Hotkey ids {Ids} are owned by another application and were not registered.", string.Join(",", controller.FailedHotkeys));
        if (controller.RecoveredFromInterrupt)
            _tray?.ShowBalloonTip(4000, "The Infinity", "Restored the workspace after an interrupted shutdown.", Forms.ToolTipIcon.Info);
    }

    private bool _chromeShown;

    private void SyncChrome(bool overview)
    {
        if (_minimap is null || overview == _chromeShown)
            return;
        _chromeShown = overview;
        if (overview)
        {
            _minimap.Place();
            _minimap.Show();
            _minimap.Update();
            RaiseChrome();
        }
        else
        {
            _minimap.Hide();
        }
    }

    private void RaiseChrome()
    {
        if (_hud is { IsVisible: true })
            NativeWindow.RaiseTopmost(new System.Windows.Interop.WindowInteropHelper(_hud).Handle);
        if (_minimap is { IsVisible: true })
            NativeWindow.RaiseTopmost(new System.Windows.Interop.WindowInteropHelper(_minimap).Handle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        if (_services is not null)
        {
            var controller = _services.GetRequiredService<CanvasController>();
            // Hooks must be released on the thread that installed them, so do that here first.
            controller.ReleaseNative();
            Task.Run(controller.ShutdownAsync).GetAwaiter().GetResult();
        }
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _services?.Dispose();
        _showEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ShowFromSecondInstance()
    {
        _hud?.Show();
        _services?.GetRequiredService<CanvasController>().ShowOverview();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => _services?.GetRequiredService<CanvasController>().OnDisplayChanged());

    private void CreateTray(CanvasController controller, ShellViewModel viewModel)
    {
        _trayIcon = CreateTrayIcon();
        _tray = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "The Infinity"
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show overview", null, (_, _) =>
        {
            _hud?.Show();
            controller.ShowOverview();
        });
        menu.Items.Add("Home", null, (_, _) => controller.ResetView());
        menu.Items.Add("Save workspace", null, async (_, _) => await controller.SaveAsync());
        menu.Items.Add("Refresh windows", null, (_, _) => controller.Refresh(silent: false));
        menu.Items.Add("Settings", null, (_, _) => OpenSettings(viewModel));
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) =>
        {
            _hud?.Show();
            controller.ShowOverview();
        };
    }

    private static ServiceProvider BuildServices()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheInfinity");
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider(Path.Combine(root, "logs", "infinity.log")));
        });
        services.AddSingleton<WorkspaceSession>();
        services.AddSingleton<IWorkspaceStore, SqliteWorkspaceStore>();
        services.AddSingleton<IWindowCatalog, WindowCatalog>();
        services.AddSingleton<WindowSnapshotCache>();
        services.AddSingleton<IWindowDriver, WindowDriver>();
        services.AddSingleton<CanvasController>();
        services.AddSingleton<ShellViewModel>();
        return services.BuildServiceProvider();
    }
    private SettingsWindow? _settings;

    private void OpenSettings(ShellViewModel viewModel)
    {
        // One settings window at a time; a second request just brings it forward.
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(viewModel);
        _settings.ShowDialog();
        _settings = null;
    }

    private Task ExitAsync()
    {
        if (_shuttingDown)
            return Task.CompletedTask;
        _shuttingDown = true;
        Shutdown();
        return Task.CompletedTask;
    }

    private System.Drawing.Icon CreateTrayIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        return new System.Drawing.Icon(info.Stream, Forms.SystemInformation.SmallIconSize);
    }
}
