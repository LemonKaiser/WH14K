using System.Globalization;

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
    private readonly bool _flushEntityCache;

    /// <summary>
    ///     Optional callback to flush engine caches when culture changes.
    ///     Must be registered by the server during initialization (reflection
    ///     is not allowed in the client-side sandbox).
    /// </summary>
    public static Action<ILocalizationManager>? FlushCacheAction { get; set; }

    public LocalizationCultureScope(ILocalizationManager localizationManager, string? cultureName)
        : this(localizationManager, cultureName, flushEntityCache: true)
    {
    }

    /// <param name="flushEntityCache">
    ///     When <c>false</c>, skip the expensive entity-name cache flush.
    ///     Safe to set <c>false</c> when the scope is only used for <see cref="Loc.GetString"/>
    ///     calls that do not reference entity data (e.g. chat message formatting).
    /// </param>
    public LocalizationCultureScope(ILocalizationManager localizationManager, string? cultureName, bool flushEntityCache)
    {
        _localizationManager = localizationManager;
        _previousCulture = localizationManager.DefaultCulture;
        _active = false;
        _flushEntityCache = flushEntityCache;

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
            if (flushEntityCache)
                FlushEntityCacheImpl(localizationManager);
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
        if (_flushEntityCache)
            FlushEntityCacheImpl(_localizationManager);
    }

    /// <summary>
    ///     Clears the engine's <c>_entityCache</c> so that subsequent
    ///     <see cref="ILocalizationManager.GetEntityData"/> calls re-resolve
    ///     names and descriptions using the currently active culture.
    /// </summary>
    private static void FlushEntityCacheImpl(ILocalizationManager loc)
    {
        FlushCacheAction?.Invoke(loc);
    }
}
