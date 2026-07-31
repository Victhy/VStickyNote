using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace VSticky;

/// <summary>Segoe MDL2 图标，贴近 Win11 菜单风格。</summary>
internal static class MenuIcons
{
    private static readonly MediaFontFamily GlyphFont = new("Segoe MDL2 Assets");

    public const string Add = "\uE710";
    public const string Color = "\uE790";
    public const string Font = "\uE8D2";
    public const string FontSize = "\uE8F2";
    public const string Adjust = "\uE90F";
    public const string Pin = "\uE840";
    public const string Save = "\uE74E";
    public const string Delete = "\uE74D";
    public const string Exit = "\uE7E8";
    public const string Restore = "\uE777";
    public const string Language = "\uE774";
    public const string Boot = "\uE7E5";
    public const string Keyboard = "\uE765";
    public const string Snap = "\uE8A7";

    public static FrameworkElement Glyph(string code, MediaBrush? brush = null) =>
        new TextBlock
        {
            Text = code,
            FontFamily = GlyphFont,
            FontSize = 14,
            Foreground = brush ?? new SolidColorBrush(MediaColor.FromArgb(220, 40, 40, 40)),
            Width = 16,
            Height = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

    public static FrameworkElement DangerGlyph(string code) =>
        Glyph(code, new SolidColorBrush(MediaColor.FromRgb(0xC6, 0x28, 0x28)));
}
