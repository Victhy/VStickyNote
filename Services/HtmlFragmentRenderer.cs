using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace VSticky.Services;

/// <summary>Maps a limited HTML fragment to WPF text. CSS layout, scripts and resources are not loaded.</summary>
internal static class HtmlFragmentRenderer
{
    private static readonly HashSet<string> BlockTags =
        ["p", "div", "section", "blockquote", "pre", "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "hr"];
    private static readonly HashSet<string> IgnoredTags = ["script", "style", "iframe", "object"];
    private static readonly Regex Whitespace = new(@"[ \t\r\n\f]+", RegexOptions.CultureInvariant);

    internal static void AppendBlocks(BlockCollection target, string html, MediaFontFamily family,
        double fontSize, MediaBrush foreground)
    {
        var section = new Section
        {
            FontFamily = family, FontSize = fontSize, Foreground = foreground,
            Margin = new Thickness(0), TextAlignment = TextAlignment.Left
        };
        target.Add(section);
        AppendNodes(section.Blocks, Parse(html).DocumentNode.ChildNodes, fontSize, foreground);
        if (section.Blocks.Count == 0) target.Remove(section);
    }

    internal static void AppendInlines(InlineCollection target, string html, double fontSize)
    {
        foreach (var node in Parse(html).DocumentNode.ChildNodes)
            AppendInline(target, node, fontSize);
    }

    private static HtmlDocument Parse(string html)
    {
        var document = new HtmlDocument { OptionFixNestedTags = true };
        document.LoadHtml(html);
        return document;
    }

    private static void AppendNodes(BlockCollection target, IEnumerable<HtmlNode> nodes,
        double fontSize, MediaBrush foreground)
    {
        Paragraph? pending = null;
        foreach (var node in nodes)
        {
            if (node.NodeType == HtmlNodeType.Comment || IgnoredTags.Contains(node.Name)) continue;
            if (node.NodeType == HtmlNodeType.Text && pending == null && string.IsNullOrWhiteSpace(node.InnerText)) continue;
            if (!BlockTags.Contains(node.Name) &&
                !node.ChildNodes.Any(child => BlockTags.Contains(child.Name)))
            {
                if (pending == null)
                {
                    pending = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                    target.Add(pending);
                }
                AppendInline(pending.Inlines, node, fontSize);
                continue;
            }

            pending = null;
            switch (node.Name)
            {
                case "pre":
                {
                    var code = node.SelectSingleNode("./code") ?? node;
                    var language = code.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(value => value.StartsWith("language-", StringComparison.OrdinalIgnoreCase));
                    var paragraph = MarkdownRenderer.CreateCodeParagraph(
                        WebUtility.HtmlDecode(node.InnerText).TrimEnd('\r', '\n'),
                        language?["language-".Length..], fontSize, foreground);
                    ApplyStyle(paragraph, node, fontSize);
                    target.Add(paragraph);
                    break;
                }
                case "hr":
                    target.Add(new BlockUIContainer(new System.Windows.Controls.Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(MediaColor.FromArgb(0x40, 0, 0, 0)),
                        Margin = new Thickness(0, 8, 0, 8)
                    }));
                    break;
                case "ul":
                case "ol":
                {
                    var list = new System.Windows.Documents.List
                    {
                        MarkerStyle = node.Name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(8, 0, 0, 2), Padding = new Thickness(14, 0, 0, 0)
                    };
                    if (node.Name == "ol" && int.TryParse(node.GetAttributeValue("start", ""), out var start) && start > 0)
                        list.StartIndex = start;
                    ApplyStyle(list, node, fontSize);
                    target.Add(list);
                    foreach (var child in node.ChildNodes.Where(child => child.Name == "li"))
                    {
                        var item = new ListItem();
                        ApplyStyle(item, child, list.FontSize);
                        list.ListItems.Add(item);
                        AppendNodes(item.Blocks, child.ChildNodes, item.FontSize, item.Foreground);
                        if (item.Blocks.Count == 0) item.Blocks.Add(new Paragraph());
                    }
                    break;
                }
                case "div":
                case "section":
                case "blockquote":
                {
                    var section = new Section { Margin = new Thickness(0) };
                    if (node.Name == "blockquote")
                    {
                        section.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x55, 0, 0, 0));
                        section.BorderThickness = new Thickness(3, 0, 0, 0);
                        section.Padding = new Thickness(10, 2, 0, 2);
                        section.Margin = new Thickness(0, 2, 0, 8);
                    }
                    target.Add(section);
                    ApplyStyle(section, node, fontSize);
                    AppendNodes(section.Blocks, node.ChildNodes, section.FontSize, section.Foreground);
                    break;
                }
                default:
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                    target.Add(paragraph);
                    if (node.Name.Length == 2 && node.Name[0] == 'h' && node.Name[1] is >= '1' and <= '6')
                    {
                        paragraph.FontWeight = FontWeights.SemiBold;
                        paragraph.FontSize = fontSize * (node.Name[1] switch
                        {
                            '1' => 1.45, '2' => 1.28, '3' => 1.14, _ => 1.05
                        });
                    }
                    ApplyStyle(paragraph, node, fontSize);
                    foreach (var child in node.ChildNodes)
                        AppendInline(paragraph.Inlines, child, paragraph.FontSize);
                    break;
                }
            }
        }
    }

    private static void AppendInline(InlineCollection target, HtmlNode node, double fontSize)
    {
        if (node.NodeType == HtmlNodeType.Comment || IgnoredTags.Contains(node.Name)) return;
        if (node.NodeType == HtmlNodeType.Text)
        {
            // Collapse HTML whitespace before decoding so &nbsp; stays nonbreaking.
            target.Add(new Run(WebUtility.HtmlDecode(Whitespace.Replace(node.InnerText, " "))));
            return;
        }
        if (node.Name == "br")
        {
            target.Add(new LineBreak());
            return;
        }
        if (node.Name == "img")
        {
            target.Add(new Run(WebUtility.HtmlDecode(node.GetAttributeValue("alt", ""))));
            return;
        }
        if (node.Name == "input")
        {
            if (node.GetAttributeValue("type", "").Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                target.Add(new Run(node.Attributes["checked"] != null ? "☑ " : "☐ "));
            return;
        }

        var span = new Span();
        target.Add(span);
        switch (node.Name)
        {
            case "b":
            case "strong": span.FontWeight = FontWeights.Bold; break;
            case "i":
            case "em": span.FontStyle = FontStyles.Italic; break;
            case "u":
            case "ins": span.TextDecorations = TextDecorations.Underline; break;
            case "s":
            case "strike":
            case "del": span.TextDecorations = TextDecorations.Strikethrough; break;
            case "mark":
                span.Background = Brushes.Yellow;
                span.Foreground = Brushes.Black;
                break;
            case "sub":
                span.BaselineAlignment = BaselineAlignment.Subscript;
                span.FontSize = fontSize * 0.8;
                break;
            case "sup":
                span.BaselineAlignment = BaselineAlignment.Superscript;
                span.FontSize = fontSize * 0.8;
                break;
            case "code":
                span.FontFamily = new MediaFontFamily("Consolas, Cascadia Mono, Courier New");
                span.FontSize = Math.Max(11, fontSize - 1);
                span.Background = new SolidColorBrush(MediaColor.FromArgb(0x30, 0, 0, 0));
                break;
            case "a":
                span.Foreground = new SolidColorBrush(MediaColor.FromArgb(0xDD, 0x1A, 0x56, 0x8A));
                span.TextDecorations = TextDecorations.Underline;
                break;
        }
        if (node.Name == "font" && TryBrush(node.GetAttributeValue("color", ""), out var color))
            span.Foreground = color;
        ApplyStyle(span, node, fontSize);
        if (node.Name == "code")
            span.Inlines.Add(new Run(WebUtility.HtmlDecode(node.InnerText)));
        else
            foreach (var child in node.ChildNodes)
                AppendInline(span.Inlines, child, span.FontSize);
    }

    private static void ApplyStyle(TextElement element, HtmlNode node, double parentFontSize)
    {
        foreach (var declaration in node.GetAttributeValue("style", "").Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0) continue;
            var property = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            switch (property)
            {
                case "color":
                    if (TryBrush(value, out var color)) element.Foreground = color;
                    break;
                case "background":
                case "background-color":
                    if (TryBrush(value, out var background)) element.Background = background;
                    break;
                case "font-size":
                    if (TryFontSize(value, parentFontSize, out var size)) element.FontSize = size;
                    break;
                case "font-weight":
                    if (value.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                        (int.TryParse(value, out var weight) && weight >= 600))
                        element.FontWeight = FontWeights.Bold;
                    else if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
                        element.FontWeight = FontWeights.Normal;
                    break;
                case "font-style":
                    if (value.Equals("italic", StringComparison.OrdinalIgnoreCase)) element.FontStyle = FontStyles.Italic;
                    else if (value.Equals("normal", StringComparison.OrdinalIgnoreCase)) element.FontStyle = FontStyles.Normal;
                    break;
                case "text-decoration" when element is Span span:
                    var decorations = new TextDecorationCollection();
                    if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
                        decorations.Add(TextDecorations.Underline);
                    if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase))
                        decorations.Add(TextDecorations.Strikethrough);
                    span.TextDecorations = decorations;
                    break;
                case "text-align" when element is System.Windows.Documents.Block block:
                    if (Enum.TryParse<TextAlignment>(value, true, out var alignment) && Enum.IsDefined(alignment))
                        block.TextAlignment = alignment;
                    break;
            }
        }
    }

    private static bool TryFontSize(string value, double parent, out double size)
    {
        value = value.Trim().ToLowerInvariant();
        var multiplier = 1.0;
        var suffix = value.EndsWith("px") ? "px" : value.EndsWith("pt") ? "pt" :
            value.EndsWith("em") ? "em" : value.EndsWith('%') ? "%" : "";
        if (suffix == "pt") multiplier = 96.0 / 72.0;
        else if (suffix == "em") multiplier = parent;
        else if (suffix == "%") multiplier = parent / 100;
        if (suffix.Length > 0) value = value[..^suffix.Length];
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
        {
            size *= multiplier;
            return double.IsFinite(size) && size is >= 1 and <= 256;
        }
        return false;
    }

    private static bool TryBrush(string value, out MediaBrush brush)
    {
        brush = Brushes.Transparent;
        value = value.Trim();
        if (value.Length == 0) return false;
        try
        {
            MediaColor color;
            if (value.StartsWith('#'))
            {
                var hex = value[1..];
                if (hex.Length is 3 or 4)
                    hex = string.Concat(hex.Select(c => new string(c, 2)));
                if (hex.Length == 8) hex = hex[6..] + hex[..6]; // CSS RGBA -> WPF ARGB
                if (hex.Length is not (6 or 8)) return false;
                color = (MediaColor)ColorConverter.ConvertFromString("#" + hex);
            }
            else if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
                     value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                var open = value.IndexOf('(');
                if (!value.EndsWith(')')) return false;
                var components = value[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries);
                var expected = open == 3 ? 3 : 4;
                if (components.Length != expected) return false;
                var channels = new byte[4] { 0, 0, 0, 255 };
                for (var i = 0; i < components.Length; i++)
                {
                    var percent = components[i].EndsWith('%');
                    var number = percent ? components[i][..^1] : components[i];
                    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel) ||
                        !double.IsFinite(channel)) return false;
                    channels[i] = (byte)Math.Round(Math.Clamp(channel * (percent ? 2.55 : i == 3 ? 255 : 1), 0, 255));
                }
                color = MediaColor.FromArgb(channels[3], channels[0], channels[1], channels[2]);
            }
            else
                color = (MediaColor)ColorConverter.ConvertFromString(value);
            var result = new SolidColorBrush(color);
            result.Freeze();
            brush = result;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
