using System.Drawing;

namespace TheInfinity.Win32.Tests;

public class ThumbnailControllerTests
{
    [Fact]
    public void Nothing_is_drawn_until_a_destination_window_is_set()
    {
        using var thumbnails = new ThumbnailController();

        var live = thumbnails.Sync([(1, new PixelRect(0, 0, 200, 100))]);

        Assert.Empty(live);
    }

    [DesktopFact]
    public void A_window_gets_a_live_thumbnail_and_loses_it_when_it_is_no_longer_listed()
    {
        using var source = new TestWindow("Infinity test: thumbnail source", new Rectangle(120, 120, 420, 300));
        using var host = new TestWindow("Infinity test: thumbnail host", new Rectangle(560, 120, 600, 400));
        using var thumbnails = new ThumbnailController();
        thumbnails.SetDestination(host.Handle);

        var live = thumbnails.Sync([(source.Handle, new PixelRect(10, 10, 300, 200))]);
        Assert.Contains(source.Handle, live);

        // Updating the same window again reuses the registration.
        live = thumbnails.Sync([(source.Handle, new PixelRect(20, 20, 280, 180))]);
        Assert.Contains(source.Handle, live);

        live = thumbnails.Sync([]);
        Assert.Empty(live);
    }

    [DesktopFact]
    public void Tiny_or_missing_sources_are_skipped()
    {
        using var host = new TestWindow("Infinity test: thumbnail host", new Rectangle(560, 120, 600, 400));
        using var thumbnails = new ThumbnailController();
        thumbnails.SetDestination(host.Handle);

        var live = thumbnails.Sync([(0, new PixelRect(0, 0, 200, 100)), (host.Handle, new PixelRect(0, 0, 1, 1))]);

        Assert.Empty(live);
    }
}
