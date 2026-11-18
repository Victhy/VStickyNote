using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace VSticky.Services;

/// <summary>Markdown shortcuts for the plain-text editor, grouped into one undo operation.</summary>
internal static class MarkdownEditing
{
    internal readonly record struct Edit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

    private static readonly Regex LinePrefix = new(@"^([ \t]*(?:>[ \t]*)*)(.*)$");
    private static readonly Regex Heading = new(@"^#{1,6}(?:[ \t]+|$)");
    private static readonly Regex ListMarker = new(@"^(?:[-+*]|\d+[.)])(?:[ \t]+|$)");
    private static readonly Regex TaskMarker = new(@"^\[[ xX]\](?:[ \t]+|$)");

    internal static bool TryApply(TextBox editor, Key key, ModifierKeys modifiers)
    {
        var edit = CreateEdit(editor.Text, editor.SelectionStart, editor.SelectionLength, key, modifiers);
        if (edit is not { } change) return false;
        editor.BeginChange();
        try
        {
            editor.Select(change.Start, change.Length);
            editor.SelectedText = change.Text;
            editor.Select(change.SelectionStart, change.SelectionLength);
        }
        finally
        {
            editor.EndChange();
        }
        return true;
    }

    internal static Edit? CreateEdit(string text, int start, int length, Key key, ModifierKeys modifiers)
    {
        var shifted = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if (modifiers != ModifierKeys.Control && !shifted) return null;
        if (shifted)
        {
            return key switch
            {
                Key.T => Lines(text, start, length, "task-done"),
                Key.I => Lines(text, start, length, "ordered"),
                Key.B => CodeBlock(text, start, length),
                _ => null
            };
        }
        if (key is >= Key.D1 and <= Key.D6)
            return Lines(text, start, length, new string('#', (int)key - (int)Key.D1 + 1));
        return key switch
        {
            Key.I => Wrap(text, start, length, "*"),
            Key.B => Wrap(text, start, length, "**"),
            Key.D => Wrap(text, start, length, "~~"),
            Key.H => Wrap(text, start, length, "=="),
            Key.Q => InlineCode(text, start, length),
            Key.R => Lines(text, start, length, "quote"),
            Key.T => Lines(text, start, length, "task"),
            _ => null
        };
    }

    private static Edit Wrap(string text, int start, int length, string marker)
    {
        // Whitespace must remain outside emphasis delimiters for Markdown to parse them.
        var end = start + length;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        var content = text[start..end];
        return new Edit(start, content.Length, marker + content + marker, start + marker.Length, content.Length);
    }

    private static int LongestBacktickRun(string content)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in content)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    private static Edit InlineCode(string text, int start, int length)
    {
        var content = text.Substring(start, length);
        var marker = new string('`', LongestBacktickRun(content) + 1);
        var padding = content.StartsWith('`') || content.EndsWith('`') ||
            (content.StartsWith(' ') && content.EndsWith(' ') && !string.IsNullOrWhiteSpace(content)) ? " " : "";
        return new Edit(start, length, marker + padding + content + padding + marker,
            start + marker.Length + padding.Length, length);
    }

    private static string Newline(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    private static Edit CodeBlock(string text, int start, int length)
    {
        var content = text.Substring(start, length);
        var newline = Newline(text);
        var marker = new string('`', Math.Max(3, LongestBacktickRun(content) + 1));
        var before = start > 0 && text[start - 1] != '\n' ? newline : "";
        var after = start + length < text.Length && text[start + length] is not ('\r' or '\n') ? newline : "";
        var opening = before + marker + newline;
        var closing = content.EndsWith('\n') ? "" : newline;
        return new Edit(start, length, opening + content + closing + marker + after,
            start + opening.Length, length);
    }

    private static Edit Lines(string text, int start, int length, string command)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var selectedEnd = start + length;
        // A selection ending at the next line's start should not modify that line.
        var last = length > 0 ? selectedEnd - 1 : start;
        if (length > 0 && text[last] == '\n') last--;
        if (last >= lineStart && last < text.Length && text[last] == '\r') last--;
        var lineEnd = text.IndexOf('\n', Math.Max(lineStart, last));
        if (lineEnd < 0) lineEnd = text.Length;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
        var original = text[lineStart..lineEnd];
        var result = new StringBuilder();
        var number = 1;
        var cursor = 0;
        var caret = 0;
        while (cursor <= original.Length)
        {
            var next = original.IndexOf('\n', cursor);
            if (next < 0) next = original.Length;
            var end = next > cursor && original[next - 1] == '\r' ? next - 1 : next;
            var line = original[cursor..end];
            var match = LinePrefix.Match(line);
            var prefix = match.Groups[1].Value;
            var body = match.Groups[2].Value;
            var newBody = body;
            if (!string.IsNullOrWhiteSpace(body) || original.Length == 0 || length == 0)
            {
                if (command == "quote")
                    prefix += "> ";
                else if (command.StartsWith('#'))
                    newBody = command + " " + Heading.Replace(body, "", 1);
                else
                {
                    body = ListMarker.Replace(body, "", 1);
                    body = TaskMarker.Replace(body, "", 1);
                    newBody = command == "ordered" ? $"{number++}. {body}" :
                        command == "task-done" ? "- [x] " + body : "- [ ] " + body;
                }
            }
            if (length == 0)
                caret = result.Length + Math.Clamp(start - lineStart - cursor + prefix.Length + newBody.Length - line.Length,
                    0, prefix.Length + newBody.Length);
            result.Append(prefix).Append(newBody);
            if (next == original.Length) break;
            result.Append(original.AsSpan(end, next + 1 - end));
            cursor = next + 1;
        }
        var replacement = result.ToString();
        return new Edit(lineStart, original.Length, replacement,
            lineStart + (length == 0 ? caret : 0), length == 0 ? 0 : replacement.Length);
    }
}
