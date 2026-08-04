namespace VSticky.Models;

public sealed class NoteData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 220;
    public string ColorHex { get; set; } = "#C8E6C9";
    /// <summary>背景不透明度 0.15–1.0</summary>
    public double Opacity { get; set; } = 0.92;
    /// <summary>圆角半径（像素）</summary>
    public double CornerRadius { get; set; } = 12;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 14;
    public bool Topmost { get; set; }
    /// <summary>角标模式：收起为胶囊条，点击可临时展开。</summary>
    public bool IsBadge { get; set; }
    /// <summary>角标模式下保持展开，失焦不自动收起。</summary>
    public bool BadgeStayOpen { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
