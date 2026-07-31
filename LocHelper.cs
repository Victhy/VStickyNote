using VSticky.Models;
using VSticky.Services;

namespace VSticky;

internal static class LocHelper
{
    public static string ColorDisplayName(ColorOption color)
    {
        if (!string.IsNullOrWhiteSpace(color.Key))
        {
            var localized = Loc.T(color.Key);
            if (!string.Equals(localized, color.Key, StringComparison.Ordinal))
                return localized;
        }

        return string.IsNullOrWhiteSpace(color.Name) ? color.Hex : color.Name;
    }
}
