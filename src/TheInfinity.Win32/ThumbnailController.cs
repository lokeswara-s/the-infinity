namespace TheInfinity.Win32;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed class ThumbnailController : IDisposable
{
    private readonly Dictionary<nint, nint> _thumbs = [];
    private nint _destination;

    public void SetDestination(nint destination) => _destination = destination;

    public HashSet<nint> Sync(IReadOnlyList<(nint Source, PixelRect Destination)> items)
    {
        var live = new HashSet<nint>();
        if (_destination == 0)
            return live;

        foreach (var item in items)
        {
            if (item.Source == 0 || item.Destination.Width < 2 || item.Destination.Height < 2)
                continue;
            live.Add(item.Source);
            if (!_thumbs.TryGetValue(item.Source, out var thumb) || thumb == 0)
            {
                var hr = NativeMethods.DwmRegisterThumbnail((IntPtr)_destination, (IntPtr)item.Source, out var created);
                if (hr != 0 || created == IntPtr.Zero)
                    continue;
                thumb = created;
                _thumbs[item.Source] = thumb;
            }

            var props = new NativeMethods.DwmThumbnailProperties
            {
                Flags = NativeMethods.DwmTnpRectDestination | NativeMethods.DwmTnpVisible | NativeMethods.DwmTnpOpacity | NativeMethods.DwmTnpSourceClientAreaOnly,
                Destination = new NativeMethods.RectNative
                {
                    Left = item.Destination.X,
                    Top = item.Destination.Y,
                    Right = item.Destination.X + item.Destination.Width,
                    Bottom = item.Destination.Y + item.Destination.Height
                },
                Opacity = 255,
                Visible = 1,
                SourceClientAreaOnly = 0
            };
            if (NativeMethods.DwmUpdateThumbnailProperties((IntPtr)thumb, ref props) != 0)
            {
                NativeMethods.DwmUnregisterThumbnail((IntPtr)thumb);
                _thumbs.Remove(item.Source);
                live.Remove(item.Source);
            }
        }

        foreach (var stale in _thumbs.Keys.Where(key => !live.Contains(key)).ToList())
        {
            NativeMethods.DwmUnregisterThumbnail((IntPtr)_thumbs[stale]);
            _thumbs.Remove(stale);
        }

        return live;
    }

    public void Clear()
    {
        foreach (var thumb in _thumbs.Values)
            NativeMethods.DwmUnregisterThumbnail((IntPtr)thumb);
        _thumbs.Clear();
    }

    public void Dispose() => Clear();
}
