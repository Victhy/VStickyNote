using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using ColorConverter = System.Windows.Media.ColorConverter;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace VSticky.Services;

/// <summary>Formats language tokens as native WPF text; no browser or background process.</summary>
internal sealed class CodeHighlighter : CodeColorizerBase
{
    // Large pasted files remain readable without creating thousands of formatted elements.
    private const int MaxHighlightedLength = 32_768;
    private readonly Span _output = new();
    private readonly StyleDictionary _styles;
    private readonly byte _alpha;

    private CodeHighlighter(StyleDictionary styles, byte alpha) : base(styles, null)
    {
        _styles = styles;
        _alpha = alpha;
    }

    internal static void Append(InlineCollection target, string source, string? language, MediaBrush foreground)
    {
        var token = language?.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        var definition = string.IsNullOrEmpty(token) ? null : Languages.All.FirstOrDefault(
            item => item.Id.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Equals(token, StringComparison.OrdinalIgnoreCase) || item.HasAlias(token.ToLowerInvariant()));
        if (definition == null || source.Length > MaxHighlightedLength)
        {
            target.Add(new Run(source));
            return;
        }

        var color = (foreground as SolidColorBrush)?.Color ?? Colors.Black;
        var lightText = color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722 > 150;
        var formatter = new CodeHighlighter(lightText ? StyleDictionary.DefaultDark : StyleDictionary.DefaultLight, color.A);
        try
        {
            formatter.languageParser.Parse(source, definition, formatter.Write);
            // Build off-document so a parser failure cannot leave a partially highlighted block.
            target.Add(formatter._output);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
                                   KeyNotFoundException or RegexMatchTimeoutException)
        {
            target.Add(new Run(source));
        }
    }

    protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        => AppendScopes(_output.Inlines, parsedSourceCode, scopes, 0, parsedSourceCode.Length);

    private void AppendScopes(InlineCollection target, string source, IList<Scope> scopes, int start, int end)
    {
        var position = start;
        foreach (var scope in scopes)
        {
            var scopeStart = Math.Clamp(scope.Index, position, end);
            var scopeEnd = (int)Math.Clamp((long)scope.Index + scope.Length, scopeStart, end);
            if (scopeStart > position)
                target.Add(new Run(source[position..scopeStart]));
            if (scopeEnd > scopeStart)
            {
                var span = new Span();
                if (_styles.Contains(scope.Name))
                {
                    var style = _styles[scope.Name];
                    if (!string.IsNullOrEmpty(style.Foreground))
                    {
                        var color = (MediaColor)ColorConverter.ConvertFromString(style.Foreground);
                        var brush = new SolidColorBrush(MediaColor.FromArgb(_alpha, color.R, color.G, color.B));
                        brush.Freeze();
                        span.Foreground = brush;
                    }
                    if (style.Bold) span.FontWeight = FontWeights.Bold;
                    if (style.Italic) span.FontStyle = FontStyles.Italic;
                }
                target.Add(span);
                AppendScopes(span.Inlines, source, scope.Children, scopeStart, scopeEnd);
            }
            position = scopeEnd;
        }
        if (position < end)
            target.Add(new Run(source[position..end]));
    }
}
