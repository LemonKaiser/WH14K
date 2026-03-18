using System;
using System.Globalization;
using Robust.Shared.Localization;

namespace Content.Shared.Localizations;

public readonly struct LocalizationCultureScope : IDisposable
{
    private readonly ILocalizationManager _localizationManager;
    private readonly CultureInfo? _previousCulture;
    private readonly bool _active;

    public LocalizationCultureScope(ILocalizationManager localizationManager, string? cultureName)
    {
        _localizationManager = localizationManager;
        _previousCulture = localizationManager.DefaultCulture;
        _active = false;

        if (string.IsNullOrWhiteSpace(cultureName))
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName, predefinedOnly: false);
            localizationManager.SetCulture(culture);
            _active = true;
        }
        catch (Exception)
        {
            _previousCulture = null;
        }
    }

    public void Dispose()
    {
        if (!_active || _previousCulture == null)
            return;

        _localizationManager.SetCulture(_previousCulture);
    }
}
