using System.Windows;
using Point = System.Windows.Point;

namespace VSticky.Services;

/// <summary>
/// 便签拖动吸附：边缘对齐、中心对齐、相邻间距对齐。
/// </summary>
public static class NoteSnapper
{
    /// <summary>进入吸附的距离阈值（DIP）。</summary>
    public const double Threshold = 12;

    /// <summary>相邻贴合时的间距，与新建便签排列间距一致。</summary>
    public const double Gap = 12;

    public static Point Snap(Rect moving, IEnumerable<Rect> others, double threshold = Threshold, double gap = Gap)
    {
        double bestDx = 0;
        double bestDy = 0;
        var bestAbsDx = threshold + 1;
        var bestAbsDy = threshold + 1;

        foreach (var other in others)
        {
            Consider(other.Left - moving.Left, ref bestDx, ref bestAbsDx, threshold);
            Consider(other.Right - moving.Right, ref bestDx, ref bestAbsDx, threshold);
            Consider(other.Right + gap - moving.Left, ref bestDx, ref bestAbsDx, threshold);
            Consider(other.Left - gap - moving.Width - moving.Left, ref bestDx, ref bestAbsDx, threshold);
            Consider(other.Left + other.Width / 2 - (moving.Left + moving.Width / 2),
                ref bestDx, ref bestAbsDx, threshold);

            Consider(other.Top - moving.Top, ref bestDy, ref bestAbsDy, threshold);
            Consider(other.Bottom - moving.Bottom, ref bestDy, ref bestAbsDy, threshold);
            Consider(other.Bottom + gap - moving.Top, ref bestDy, ref bestAbsDy, threshold);
            Consider(other.Top - gap - moving.Height - moving.Top, ref bestDy, ref bestAbsDy, threshold);
            Consider(other.Top + other.Height / 2 - (moving.Top + moving.Height / 2),
                ref bestDy, ref bestAbsDy, threshold);
        }

        return new Point(
            moving.Left + (bestAbsDx <= threshold ? bestDx : 0),
            moving.Top + (bestAbsDy <= threshold ? bestDy : 0));
    }

    private static void Consider(double delta, ref double best, ref double bestAbs, double threshold)
    {
        var abs = Math.Abs(delta);
        if (abs <= threshold && abs < bestAbs)
        {
            best = delta;
            bestAbs = abs;
        }
    }
}
