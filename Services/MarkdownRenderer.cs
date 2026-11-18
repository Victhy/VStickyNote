using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Renderers;
using Markdig.Extensions.TaskLists;
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
/// Markdown 和常见 HTML → 原生 FlowDocument（预览用，链接不可点击）。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static FlowDocument ToFlowDocument(
        string? markdown,
        string fontFamily,
        double fontSize,
        MediaColor? foregroundColor = null)
    {
        var family = new MediaFontFamily(fontFamily);
        var fg = foregroundColor ?? MediaColor.FromArgb(0xDD, 0x00, 0x00, 0x00);
        var foreground = new SolidColorBrush(fg);
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
            AppendBlock(doc.Blocks, block, family, fontSize, foreground);

        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });

        return doc;
    }

    private static void AppendBlock(
        BlockCollection blocks,
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
                p.FontSize = fontSize * (heading.Level switch
                {
                    1 => 1.45,
                    2 => 1.28,
                    3 => 1.14,
                    _ => 1.05
                });
                p.Margin = new Thickness(0, heading.Level == 1 ? 2 : 6, 0, 4);
                blocks.Add(p);
                break;
            }
            case ParagraphBlock paragraph:
            {
                var p = CreateParagraph(paragraph, family, fontSize, foreground);
                p.Margin = new Thickness(0, 0, 0, 6);
                blocks.Add(p);
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
                    AppendBlock(section.Blocks, child, family, fontSize, foreground);
                blocks.Add(section);
                break;
            }
            case ListBlock list:
            {
                var listItemIndex = int.TryParse(list.OrderedStart, out var firstIndex) ? firstIndex : 1;
                foreach (var item in list)
                {
                    if (item is not ListItemBlock listItem) continue;
                    var task = (listItem.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild is TaskList;
                    var prefix = task ? "" : list.IsOrdered ? $"{listItemIndex}. " : "• ";
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
                    blocks.Add(p);
                    foreach (var child in listItem.Where(child => child is ContainerBlock))
                    {
                        var nested = new Section { Margin = new Thickness(16, 0, 0, 0) };
                        blocks.Add(nested);
                        AppendBlock(nested.Blocks, child, family, fontSize, foreground);
                    }
                    listItemIndex++;
                }
                break;
            }
            case FencedCodeBlock code:
            {
                var content = code.Lines.ToString().TrimEnd('\r', '\n');
                blocks.Add(CreateCodeParagraph(content, code.Info, fontSize, foreground));
                break;
            }
            case CodeBlock plainCode:
            {
                var content = plainCode.Lines.ToString().TrimEnd('\r', '\n');
                blocks.Add(CreateCodeParagraph(content, null, fontSize, foreground));
                break;
            }
            case HtmlBlock html:
                HtmlFragmentRenderer.AppendBlocks(blocks, html.Lines.ToString(), family, fontSize, foreground);
                break;
            case ThematicBreakBlock:
            {
                blocks.Add(new BlockUIContainer(new System.Windows.Controls.Border
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
                blocks.Add(p);
                break;
            }
        }
    }

    internal static Paragraph CreateCodeParagraph(string content, string? language, double fontSize, MediaBrush foreground)
    {
        var p = new Paragraph
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 8),
            Foreground = foreground
        };
        var code = new Span
        {
            FontFamily = new MediaFontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = Math.Max(11, fontSize - 1)
        };
        CodeHighlighter.Append(code.Inlines, content, language, foreground);
        p.Inlines.Add(code);
        return p;
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
        if (ContainsHtml(inline))
        {
            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            for (var current = inline; current != null; current = current.NextSibling)
                renderer.Render(current);
            HtmlFragmentRenderer.AppendInlines(target, writer.ToString(), fontSize);
            return;
        }

        for (var current = inline; current != null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()) { Foreground = foreground });
                    break;
                case TaskList task:
                    target.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = foreground });
                    break;
                case EmphasisInline emphasis:
                {
                    var span = new Span { Foreground = foreground };
                    if (emphasis.DelimiterChar == '~')
                    {
                        span.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else if (emphasis.DelimiterChar == '=')
                    {
                        span.Background = WpfBrushes.Yellow;
                        span.Foreground = WpfBrushes.Black;
                    }
                    else
                    {
                        if (emphasis.DelimiterCount >= 2)
                            span.FontWeight = FontWeights.Bold;
                        if (emphasis.DelimiterCount % 2 == 1)
                            span.FontStyle = FontStyles.Italic;
                    }

                    AppendInlines(span.Inlines, emphasis.FirstChild, family, fontSize, span.Foreground);
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

    private static bool ContainsHtml(MdInline? inline)
    {
        for (var current = inline; current != null; current = current.NextSibling)
        {
            if (current is HtmlInline) return true;
            if (current is ContainerInline container && ContainsHtml(container.FirstChild)) return true;
        }
        return false;
    }
}
