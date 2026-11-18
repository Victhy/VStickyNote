using System.IO;

namespace VSticky.Services;

/// <summary>数据始终放在可执行文件所在目录下的 Data。</summary>
public static class AppPaths
{
    public const string AppName = "VSticky";

    private static readonly Lazy<string> RootLazy = new(ResolveRoot);

    public static string Root => RootLazy.Value;

    public static string DatabasePath => Path.Combine(Root, "vsticky.db");

    public static string LocalesDirectory => Path.Combine(Root, "Locales");

    /// <summary>旧版 JSON 目录（%AppData%\VSticky），仅用于迁移。</summary>
    public static string LegacyRoamingDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    /// <summary>上一版误用的 %LocalAppData%\VSticky，仅用于迁移。</summary>
    public static string LegacyLocalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    private static string ResolveRoot()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }
}
