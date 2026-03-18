using System.Globalization;
using Robust.Shared.Localization;

namespace Content.Shared.Localizations;

public static class LocalizationManagerExtensions
{
    public static string? GetCurrentCultureName(this ILocalizationManager localizationManager)
    {
        var cultureName = localizationManager.DefaultCulture?.Name;
        if (!string.IsNullOrWhiteSpace(cultureName))
            return cultureName;

        cultureName = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(cultureName)
            ? null
            : cultureName;
    }
}
