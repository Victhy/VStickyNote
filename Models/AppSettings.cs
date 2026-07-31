namespace VSticky.Models;

public sealed class ColorOption
{
    /// <summary>本地化键，如 color.green；自定义颜色可为空。</summary>
    public string? Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hex { get; set; } = "#C8E6C9";
}

public sealed class AppSettings
{
    /// <summary>界面语言，如 zh-CN / en-US</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>开机启动</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>启用全局热键 Ctrl+Win+Alt+N 新建便签</summary>
    public bool HotkeyNewNoteEnabled { get; set; } = true;

    /// <summary>拖动时靠近其他便签自动吸附对齐</summary>
    public bool SnapEnabled { get; set; } = true;

    public double Opacity { get; set; } = 0.92;
    public double CornerRadius { get; set; } = 12;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 14;
    public bool Topmost { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 220;

    public List<string> FontChoices { get; set; } =
    [
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Segoe UI",
        "SimSun",
        "SimHei",
        "KaiTi",
        "FangSong",
        "Consolas",
        "Arial",
        "Times New Roman"
    ];

    public List<double> FontSizeChoices { get; set; } = [12, 14, 16, 18, 20, 24, 28, 32];

    public List<ColorOption> Colors { get; set; } =
    [
        new() { Key = "color.green", Name = "浅绿", Hex = "#C8E6C9" },
        new() { Key = "color.orange", Name = "浅橙", Hex = "#FFE0B2" },
        new() { Key = "color.yellow", Name = "浅黄", Hex = "#FFF59D" },
        new() { Key = "color.pink", Name = "浅粉", Hex = "#F8BBD0" },
        new() { Key = "color.blue", Name = "浅蓝", Hex = "#BBDEFB" },
        new() { Key = "color.purple", Name = "浅紫", Hex = "#E1BEE7" },
        new() { Key = "color.white", Name = "白色", Hex = "#F5F5F5" }
    ];

    public NoteData CreateNote(double left, double top, string colorHex) => new()
    {
        Left = left,
        Top = top,
        Width = Width,
        Height = Height,
        Opacity = Opacity,
        CornerRadius = CornerRadius,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Topmost = Topmost,
        ColorHex = colorHex,
        Text = string.Empty
    };

    /// <summary>用当前便签样式更新全局默认（不含背景色）。</summary>
    public void UpdateFromNote(NoteData note)
    {
        Opacity = note.Opacity;
        CornerRadius = note.CornerRadius;
        FontFamily = note.FontFamily;
        FontSize = note.FontSize;
        Topmost = note.Topmost;
        Width = note.Width;
        Height = note.Height;
    }
}
