namespace TheInfinity.Core.Geometry;

public static class SnapEngine
{
    public static SnapAdjustment SnapMove(CanvasRect moving, IEnumerable<CanvasRect> others, double threshold)
    {
        if (threshold <= 0)
            return new SnapAdjustment(0, 0, []);

        double bestDx = 0;
        double bestDy = 0;
        var bestAbsX = threshold + 1;
        var bestAbsY = threshold + 1;
        double? guideX = null;
        double? guideY = null;

        foreach (var other in others)
        {
            if (other.IsEmpty)
                continue;

            Consider(moving.X - other.X, other.X, vertical: true);
            Consider(moving.X - other.Right, other.Right, vertical: true);
            Consider(moving.Right - other.X, other.X, vertical: true);
            Consider(moving.Right - other.Right, other.Right, vertical: true);
            Consider(moving.Y - other.Y, other.Y, vertical: false);
            Consider(moving.Y - other.Bottom, other.Bottom, vertical: false);
            Consider(moving.Bottom - other.Y, other.Y, vertical: false);
            Consider(moving.Bottom - other.Bottom, other.Bottom, vertical: false);
        }

        var guides = new List<GuideLine>(2);
        if (guideX is double gx)
            guides.Add(new GuideLine(true, gx));
        if (guideY is double gy)
            guides.Add(new GuideLine(false, gy));
        return new SnapAdjustment(bestAbsX <= threshold ? bestDx : 0, bestAbsY <= threshold ? bestDy : 0, guides);

        void Consider(double delta, double guidePosition, bool vertical)
        {
            var abs = Math.Abs(delta);
            if (abs > threshold)
                return;
            if (vertical)
            {
                if (abs < bestAbsX)
                {
                    bestAbsX = abs;
                    bestDx = -delta;
                    guideX = guidePosition;
                }
            }
            else if (abs < bestAbsY)
            {
                bestAbsY = abs;
                bestDy = -delta;
                guideY = guidePosition;
            }
        }
    }
}
