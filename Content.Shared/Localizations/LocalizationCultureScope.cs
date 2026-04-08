using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Content.Shared.Localizations;

/// <summary>
///     Temporarily switches the active localization culture and flushes the engine's
///     entity-name/description cache so that all entity data resolved within the scope
///     uses the correct language.  Restores both the culture and the cache on disposal.
/// </summary>
public readonly struct LocalizationCultureScope : IDisposable
{
    private readonly ILocalizationManager _localizationManager;
    private readonly CultureInfo? _previousCulture;
    private readonly bool _active;

    // Lazily-resolved reflection accessor for the engine's entity loc cache.
    private static Action<ILocalizationManager>? _flushAction;
    private static bool _flushResolved;

    public LocalizationCultureScope(ILocalizationManager localizationManager, string? cultureName)
    {
        _localizationManager = localizationManager;
        _previousCulture = localizationManager.DefaultCulture;
        _active = false;

        var validated = ContentLocalizationManager.ValidateCultureName(cultureName);
        if (validated == null)
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(validated, predefinedOnly: true);

            // Skip work if already in the requested culture.
            if (_previousCulture != null &&
                string.Equals(_previousCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
                return;

            localizationManager.SetCulture(culture);
            FlushEntityCache(localizationManager);
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
        FlushEntityCache(_localizationManager);
    }

    /// <summary>
    ///     Clears the engine's <c>_entityCache</c> so that subsequent
    ///     <see cref="ILocalizationManager.GetEntityData"/> calls re-resolve
    ///     names and descriptions using the currently active culture.
    /// </summary>
    private static void FlushEntityCache(ILocalizationManager loc)
    {
        if (!_flushResolved)
        {
            _flushResolved = true;
            try
            {
                // Walk up to the base LocalizationManager that owns _entityCache.
                FieldInfo? field = null;
                var type = loc.GetType();
                while (type != null)
                {
                    field = type.GetField("_entityCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                        break;
                    type = type.BaseType;
                }

                if (field != null)
                {
                    var captured = field;
                    _flushAction = manager =>
                    {
                        if (captured.GetValue(manager) is IDictionary dict)
                            dict.Clear();
                    };
                }
            }
            catch
            {
                _flushAction = null;
            }
        }

        _flushAction?.Invoke(loc);
    }
}
