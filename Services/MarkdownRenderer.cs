using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBrushes = System.Windows.Media.Brushes;

namespace VSticky.Services;

/// <summary>
/// 常用 Markdown → FlowDocument（预览用，链接不可点击）。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static FlowDocument ToFlowDocument(string? markdown, string fontFamily, double fontSize)
    {
        var family = new MediaFontFamily(fontFamily);
        var foreground = new SolidColorBrush(MediaColor.FromArgb(0xDD, 0x00, 0x00, 0x00));
        var doc = new FlowDocument
        {
            FontFamily = family,
            FontSize = fontSize,
            Foreground = foreground,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Background = WpfBrushes.Transparent
        };

        var text = markdown ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            doc.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0) });
            return doc;
        }

        var parsed = Markdown.Parse(text, Pipeline);
        foreach (var block in parsed)
            AppendBlock(doc, block, family, fontSize, foreground);

        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });

        return doc;
    }

    private static void AppendBlock(
        FlowDocument doc,
        MdBlock block,
        MediaFontFamily family,
        double fontSize,
        MediaBrush foreground)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var p = CreateParagraph(heading, family, fontSize, foreground);
                p.FontWeight = FontWeights.SemiBold;
                p.FontSize = fontSize * heading.Level switch
                {
                    1 => 1.45,
                    2 => 1.28,
                    3 => 1.14,
                    _ => 1.05
                };
                p.Margin = new Thickness(0, heading.Level == 1 ? 2 : 6, 0, 4);
                doc.Blocks.Add(p);
                break;
            }
            case ParagraphBlock paragraph:
            {
                var p = CreateParagraph(paragraph, family, fontSize, foreground);
                p.Margin = new Thickness(0, 0, 0, 6);
                doc.Blocks.Add(p);
                break;
            }
            case QuoteBlock quote:
            {
                var section = new Section
                {
                    BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x55, 0x00, 0x00, 0x00)),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 2, 0, 2),
                    Margin = new Thickness(0, 2, 0, 8)
                };
                foreach (var child in quote)
                    AppendBlockToSection(section, child, family, fontSize, foreground);
                doc.Blocks.Add(section);
                break;
            }
            case ListBlock list:
            {
                var listItemIndex = 1;
                foreach (var item in list)
                {
                    if (item is not ListItemBlock listItem) continue;
                    var prefix = list.IsOrdered ? $"{listItemIndex}. " : "• ";
                    var p = new Paragraph
                    {
                        Margin = new Thickness(8, 0, 0, 2),
                        FontFamily = family,
                        FontSize = fontSize,
                        Foreground = foreground
                    };
                    p.Inlines.Add(new Run(prefix));
                    var first = true;
                    foreach (var child in listItem)
                    {
                        if (child is ParagraphBlock pb)
                        {
                            if (!first) p.Inlines.Add(new LineBreak());
                            AppendInlines(p.Inlines, pb.Inline, family, fontSize, foreground);
                            first = false;
                        }
                        else if (child is LeafBlock leaf && leaf.Inline != null)
                        {
                            if (!first) p.Inlines.Add(new LineBreak());
                            AppendInlines(p.Inlines, leaf.Inline, family, fontSize, foreground);
                            first = false;
                        }
                    }
                    doc.Blocks.Add(p);
                    listItemIndex++;
                }
                break;
            }
            case FencedCodeBlock code:
            {
                var content = code.Lines.ToString().TrimEnd('\r', '\n');
                var p = new Paragraph(new Run(content)
                {
                    FontFamily = new MediaFontFamily("Consolas, Cascadia Mono, Courier New"),
                    FontSize = Math.Max(11, fontSize - 1)
                })
                {
                    Background = new SolidColorBrush(MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 2, 0, 8),
                    Foreground = foreground
                };
                doc.Blocks.Add(p);
                break;
            }
            case CodeBlock plainCode:
            {
                var content = plainCode.Lines.ToString().TrimEnd('\r', '\n');
                var p = new Paragraph(new Run(content)
                {
                    FontFamily = new MediaFontFamily("Consolas, Cascadia Mono, Courier New"),
                    FontSize = Math.Max(11, fontSize - 1)
                })
                {
                    Background = new SolidColorBrush(MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 2, 0, 8),
                    Foreground = foreground
                };
                doc.Blocks.Add(p);
                break;
            }
            case ThematicBreakBlock:
            {
                doc.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(MediaColor.FromArgb(0x40, 0x00, 0x00, 0x00)),
                    Margin = new Thickness(0, 8, 0, 8)
                }));
                break;
            }
            case LeafBlock leaf when leaf.Inline != null:
            {
                var p = CreateParagraph(leaf, family, fontSize, foreground);
                p.Margin = new Thickness(0, 0, 0, 6);
                doc.Blocks.Add(p);
                break;
            }
        }
    }

    private static void AppendBlockToSection(
        Section section,
        MdBlock block,
        MediaFontFamily family,
        double fontSize,
        MediaBrush foreground)
    {
        if (block is LeafBlock leaf && leaf.Inline != null)
        {
            var p = CreateParagraph(leaf, family, fontSize, foreground);
            p.Margin = new Thickness(0, 0, 0, 4);
            section.Blocks.Add(p);
        }
        else if (block is ContainerBlock container)
        {
            foreach (var child in container)
                AppendBlockToSection(section, child, family, fontSize, foreground);
        }
    }

    private static Paragraph CreateParagraph(
        LeafBlock leaf,
        MediaFontFamily family,
        double fontSize,
        MediaBrush foreground)
    {
        var p = new Paragraph
        {
            FontFamily = family,
            FontSize = fontSize,
            Foreground = foreground,
            TextAlignment = TextAlignment.Left
        };
        if (leaf.Inline != null)
            AppendInlines(p.Inlines, leaf.Inline, family, fontSize, foreground);
        return p;
    }

    private static void AppendInlines(
        InlineCollection target,
        MdInline? inline,
        MediaFontFamily family,
        double fontSize,
        MediaBrush foreground)
    {
        for (var current = inline; current != null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()) { Foreground = foreground });
                    break;
                case EmphasisInline emphasis:
                {
                    var span = new Span { Foreground = foreground };
                    if (emphasis.DelimiterChar == '~')
                    {
                        span.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else
                    {
                        if (emphasis.DelimiterCount >= 2)
                            span.FontWeight = FontWeights.Bold;
                        if (emphasis.DelimiterCount % 2 == 1)
                            span.FontStyle = FontStyles.Italic;
                    }

                    AppendInlines(span.Inlines, emphasis.FirstChild, family, fontSize, foreground);
                    target.Add(span);
                    break;
                }
                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = new MediaFontFamily("Consolas, Cascadia Mono, Courier New"),
                        FontSize = Math.Max(11, fontSize - 1),
                        Background = new SolidColorBrush(MediaColor.FromArgb(0x30, 0x00, 0x00, 0x00)),
                        Foreground = foreground
                    });
                    break;
                case LinkInline link:
                {
                    var label = new Span
                    {
                        Foreground = new SolidColorBrush(MediaColor.FromArgb(0xDD, 0x1A, 0x56, 0x8A)),
                        TextDecorations = TextDecorations.Underline
                    };
                    if (link.FirstChild != null)
                        AppendInlines(label.Inlines, link.FirstChild, family, fontSize, label.Foreground);
                    else
                        label.Inlines.Add(new Run(link.Url ?? string.Empty));
                    target.Add(label);
                    break;
                }
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()) { Foreground = foreground });
                    break;
                case ContainerInline container:
                    AppendInlines(target, container.FirstChild, family, fontSize, foreground);
                    break;
            }
        }
    }
}
