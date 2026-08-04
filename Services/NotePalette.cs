using MediaColor = System.Windows.Media.Color;

namespace VSticky.Services;

/// <summary>按便签背景与透明度推导高对比文字色。</summary>
internal static class NotePalette
{
    /// <summary>假定桌面为浅色，用于估算半透明便签的实际观感。</summary>
    private const byte DesktopR = 236;
    private const byte DesktopG = 236;
    private const byte DesktopB = 236;

    /// <summary>正文/预览前景。</summary>
    public static MediaColor ForegroundFor(string? colorHex, double opacity = 1.0)
    {
        var bg = Parse(colorHex);
        return ForegroundFor(bg, opacity, muted: false);
    }

    /// <summary>次要标签前景。</summary>
    public static MediaColor MutedForegroundFor(string? colorHex, double opacity = 1.0)
    {
        var bg = Parse(colorHex);
        return ForegroundFor(bg, opacity, muted: true);
    }

    public static MediaColor ForegroundFor(MediaColor bg, double opacity, bool muted)
    {
        opacity = Math.Clamp(opacity, 0.2, 1.0);

        // 半透明时与浅色桌面混合，得到更接近肉眼所见的底色
        var er = Blend(bg.R, DesktopR, opacity);
        var eg = Blend(bg.G, DesktopG, opacity);
        var eb = Blend(bg.B, DesktopB, opacity);

        RgbToHsl(er, eg, eb, out var h, out var s, out _);

        // 越透明 → 文字越深、越接近纯黑，保证压过桌面杂色
        var clear = 1.0 - opacity;
        var textL = muted
            ? Math.Clamp(0.22 - clear * 0.06, 0.14, 0.22)
            : Math.Clamp(0.12 - clear * 0.05, 0.06, 0.12);
        var textS = s < 0.06
            ? 0
            : Math.Clamp((muted ? 0.42 : 0.50) - clear * 0.18, 0.12, 0.50);
        var alpha = muted
            ? (byte)Math.Clamp(0xB0 + (int)(clear * 30), 0xB0, 0xD0)
            : (byte)Math.Clamp(0xF2 + (int)(clear * 13), 0xF2, 0xFF);

        if (textS < 0.01)
        {
            var gray = (byte)Math.Clamp((int)Math.Round(textL * 255), 0, 255);
            return MediaColor.FromArgb(alpha, gray, gray, gray);
        }

        HslToRgb(h, textS, textL, out var r, out var g, out var b);
        return MediaColor.FromArgb(alpha, r, g, b);
    }

    private static byte Blend(byte note, byte desktop, double opacity) =>
        (byte)Math.Clamp((int)Math.Round(note * opacity + desktop * (1 - opacity)), 0, 255);

    private static MediaColor Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return MediaColor.FromRgb(0xC8, 0xE6, 0xC9);

        try
        {
            return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return MediaColor.FromRgb(0xC8, 0xE6, 0xC9);
        }
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-6)
        {
            h = 0;
            s = 0;
            return;
        }

        var d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        if (max == rd)
            h = ((gd - bd) / d + (gd < bd ? 6 : 0)) / 6.0;
        else if (max == gd)
            h = ((bd - rd) / d + 2) / 6.0;
        else
            h = ((rd - gd) / d + 4) / 6.0;
    }

    private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double rd, gd, bd;
        if (s <= 1e-6)
        {
            rd = gd = bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            rd = HueToRgb(p, q, h + 1.0 / 3);
            gd = HueToRgb(p, q, h);
            bd = HueToRgb(p, q, h - 1.0 / 3);
        }

        r = (byte)Math.Clamp((int)Math.Round(rd * 255), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(gd * 255), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(bd * 255), 0, 255);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
