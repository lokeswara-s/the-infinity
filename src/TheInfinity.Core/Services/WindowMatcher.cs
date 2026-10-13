using TheInfinity.Core.Models;

namespace TheInfinity.Core.Services;

public static class WindowMatcher
{
    public const int MinimumScore = 50;

    public static int Score(ManagedWindow saved, WindowObservation live)
    {
        var savedExe = WindowIdentity.NormalizePath(saved.ExecutablePath);
        var liveExe = WindowIdentity.NormalizePath(live.ExecutablePath);
        var score = 0;
        if (!string.IsNullOrEmpty(savedExe) && savedExe == liveExe)
            score += 60;
        else if (!string.IsNullOrEmpty(saved.ProcessName) &&
                 string.Equals(saved.ProcessName, live.ProcessName, StringComparison.OrdinalIgnoreCase))
            score += 40;
        else
            return 0;

        if (!string.IsNullOrEmpty(saved.WindowClass) &&
            string.Equals(saved.WindowClass, live.WindowClass, StringComparison.OrdinalIgnoreCase))
            score += 15;

        var savedTitle = WindowIdentity.NormalizeTitle(saved.Title);
        var liveTitle = WindowIdentity.NormalizeTitle(live.Title);
        if (savedTitle.Length > 0 && savedTitle == liveTitle)
            score += 25;
        else if (savedTitle.Length > 3 && liveTitle.Contains(savedTitle, StringComparison.Ordinal))
            score += 10;

        return score;
    }

    public static Dictionary<WindowObservation, ManagedWindow> Match(
        IReadOnlyList<ManagedWindow> unbound,
        IReadOnlyList<WindowObservation> live)
    {
        var pairs = new List<(int Score, ManagedWindow Window, WindowObservation Observation)>();
        foreach (var window in unbound)
        {
            foreach (var observation in live)
            {
                var score = Score(window, observation);
                if (score >= MinimumScore)
                    pairs.Add((score, window, observation));
            }
        }

        pairs.Sort((a, b) => b.Score.CompareTo(a.Score));
        var usedWindows = new HashSet<Guid>();
        var usedHwnds = new HashSet<nint>();
        var result = new Dictionary<WindowObservation, ManagedWindow>();
        foreach (var pair in pairs)
        {
            if (!usedWindows.Add(pair.Window.Id) || !usedHwnds.Add(pair.Observation.Hwnd))
                continue;
            result[pair.Observation] = pair.Window;
        }

        return result;
    }
}
