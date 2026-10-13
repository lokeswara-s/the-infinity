using Microsoft.Extensions.Logging;
using TheInfinity.Core.Models;
using TheInfinity.Core.Services;

namespace TheInfinity.App.Services;

/// <summary>Starts the programs behind remembered windows that are not running. Only used when the user opts in.</summary>
internal static class MissingAppLauncher
{
    public static void Launch(WorkspaceSession session, ILogger logger)
    {
        var running = session.Windows
            .Where(window => window.Hwnd != 0 && !string.IsNullOrWhiteSpace(window.ExecutablePath))
            .Select(window => WindowIdentity.NormalizePath(window.ExecutablePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = session.Windows
            .Where(window => window.IsPlaceholder && !string.IsNullOrWhiteSpace(window.ExecutablePath))
            .Select(window => window.ExecutablePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in missing)
        {
            if (running.Contains(WindowIdentity.NormalizePath(path)) || !File.Exists(path))
                continue;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                logger.LogInformation("Launched {Path} because LaunchMissingApps is enabled.", path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not launch {Path}. Packaged or elevated apps often refuse a normal start.", path);
            }
        }
    }
}
