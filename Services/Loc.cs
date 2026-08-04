using System.Globalization;
using System.IO;
using System.Text.Json;

namespace VSticky.Services;

public static class Loc
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<LanguageInfo> _languages = [];

    public static string CurrentCode { get; private set; } = "zh-CN";

    public static IReadOnlyList<LanguageInfo> Languages => _languages;

    public static event Action? Changed;

    public static void Initialize(string? preferredCode = null)
    {
        DiscoverLanguages();
        var code = Normalize(preferredCode)
                   ?? Normalize(CultureInfo.CurrentUICulture.Name)
                   ?? "zh-CN";

        if (_languages.All(l => !l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            code = _languages.FirstOrDefault()?.Code ?? "zh-CN";

        LoadLanguage(code, raiseEvent: false);
    }

    public static void SetLanguage(string code)
    {
        code = Normalize(code) ?? code;
        if (code.Equals(CurrentCode, StringComparison.OrdinalIgnoreCase) && _strings.Count > 0)
            return;

        LoadLanguage(code, raiseEvent: true);
    }

    public static string T(string key)
    {
        if (_strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;
        return key;
    }

    public static string T(string key, params object[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static void DiscoverLanguages()
    {
        _languages.Clear();
        foreach (var dir in GetSearchDirs())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (_languages.Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var name = code;
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(file), JsonOptions);
                    if (dict != null && dict.TryGetValue("lang.name", out var langName) && !string.IsNullOrWhiteSpace(langName))
                        name = langName;
                }
                catch
                {
                    // keep code as name
                }

                _languages.Add(new LanguageInfo(code, name, file));
            }
        }

        if (_languages.Count == 0)
        {
            _languages.Add(new LanguageInfo("zh-CN", "简体中文", null));
            _languages.Add(new LanguageInfo("en-US", "English", null));
        }

        _languages.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase));
    }

    private static void LoadLanguage(string code, bool raiseEvent)
    {
        var info = _languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        Dictionary<string, string>? dict = null;

        if (info?.FilePath != null && File.Exists(info.FilePath))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(info.FilePath), JsonOptions);
            }
            catch
            {
                dict = null;
            }
        }

        dict ??= GetEmbeddedFallback(code);
        _strings = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        CurrentCode = info?.Code ?? code;

        if (raiseEvent)
            Changed?.Invoke();
    }

    private static Dictionary<string, string> GetEmbeddedFallback(string code)
    {
        // 兜底：防止 locale 文件缺失导致空白文案
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["lang.name"] = "English",
                ["app.name"] = "VSticky Sticky Notes",
                ["window.title"] = "Note",
                ["menu.new"] = "New note",
                ["menu.color"] = "Color",
                ["menu.font"] = "Font",
                ["menu.fontSize"] = "Font size",
                ["menu.adjust"] = "Opacity / corners",
                ["menu.topmost"] = "Pin on top",
                ["menu.badge"] = "Collapse to badge",
                ["menu.unbadge"] = "Expand to note",
                ["menu.badgeStayOpen"] = "Keep expanded",
                ["menu.hide"] = "Hide note",
                ["menu.showAll"] = "Show all",
                ["menu.delete"] = "Delete note",
                ["menu.exit"] = "Exit",
                ["menu.language"] = "Language",
                ["menu.startWithWindows"] = "Start with Windows",
                ["menu.hotkeyManage"] = "Hotkeys",
                ["menu.hotkeyNewNote"] = "New note [Ctrl+Win+Alt+N]",
                ["menu.snap"] = "Snap when near",
                ["label.opacity"] = "Opacity",
                ["label.corner"] = "Corners",
                ["badge.empty"] = "Note",
                ["tip.hotkeyFailTitle"] = "VSticky",
                ["tip.hotkeyFail"] = "Failed to register the global hotkey; it may already be in use",
                ["color.green"] = "Light green",
                ["color.orange"] = "Light orange",
                ["color.yellow"] = "Light yellow",
                ["color.pink"] = "Light pink",
                ["color.blue"] = "Light blue",
                ["color.purple"] = "Light purple",
                ["color.white"] = "White"
            };
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["lang.name"] = "简体中文",
            ["app.name"] = "VSticky 桌面便签",
            ["window.title"] = "便签",
            ["menu.new"] = "新建便签",
            ["menu.color"] = "颜色",
            ["menu.font"] = "字体",
            ["menu.fontSize"] = "字号",
            ["menu.adjust"] = "调节透明/圆角",
            ["menu.topmost"] = "钉在桌面（置顶）",
            ["menu.badge"] = "收起为角标",
            ["menu.unbadge"] = "展开为便签",
            ["menu.badgeStayOpen"] = "保持展开",
            ["menu.hide"] = "隐藏便签",
            ["menu.showAll"] = "显示所有",
            ["menu.delete"] = "删除便签",
            ["menu.exit"] = "退出",
            ["menu.language"] = "语言",
            ["menu.startWithWindows"] = "开机启动",
            ["menu.hotkeyManage"] = "热键管理",
            ["menu.hotkeyNewNote"] = "新增便签 [Ctrl+Win+Alt+N]",
            ["menu.snap"] = "靠近吸附",
            ["label.opacity"] = "透明",
            ["label.corner"] = "圆角",
            ["badge.empty"] = "便签",
            ["tip.hotkeyFailTitle"] = "VSticky",
            ["tip.hotkeyFail"] = "全局热键注册失败，可能已被其他程序占用",
            ["color.green"] = "浅绿",
            ["color.orange"] = "浅橙",
            ["color.yellow"] = "浅黄",
            ["color.pink"] = "浅粉",
            ["color.blue"] = "浅蓝",
            ["color.purple"] = "浅紫",
            ["color.white"] = "白色"
        };
    }

    private static IEnumerable<string> GetSearchDirs()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Locales");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VSticky",
            "Locales");
    }

    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (code.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";
        if (code.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            return "en-US";
        return code;
    }
}

public sealed record LanguageInfo(string Code, string NativeName, string? FilePath);
