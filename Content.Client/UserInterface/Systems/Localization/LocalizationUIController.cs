using System;
using System.Collections.Generic;
using System.Globalization;
using Content.Client.Gameplay;
using Content.Client.Localization;
using Content.Client.Lobby;
using Content.Client.Options.UI;
using Content.Client.UserInterface.Systems.Admin;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Hotbar;
using Content.Client.UserInterface.Systems.Info;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client.UserInterface.Systems.Ghost;
using Content.Client._WH40K.Roadmap;
using Content.Shared.Localizations;
using Content.Shared.Trigger.Systems;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client.UserInterface.Systems.Localization;

public sealed class LocalizationUIController : UIController
{
    private const string DefaultCultureName = "ru-RU";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ContentLocalizationManager _contentLoc = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly Robust.Client.State.IStateManager _state = default!;

    private bool _suppressCultureChanged;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CVars.LocCultureName, OnCultureNameChanged, invokeImmediately: true);
    }

    private void OnCultureNameChanged(string cultureName)
    {
        if (_suppressCultureChanged)
            return;

        try
        {
            var culture = ResolveSupportedCultureOrFallback(cultureName);
            ApplyCulture(culture);

            if (string.Equals(cultureName, culture.Name, StringComparison.OrdinalIgnoreCase))
                return;

            _suppressCultureChanged = true;
            _cfg.SetCVar(CVars.LocCultureName, culture.Name);
            _suppressCultureChanged = false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to switch localization culture to '{cultureName}': {e}");
        }
    }

    private void ApplyCulture(CultureInfo culture)
    {
        _contentLoc.EnsureCulturePrepared(culture);
        _loc.SetCulture(culture);
        _loc.ReloadLocalizations();
        VerbCategory.RefreshStaticLocalizations();
        TriggerSystem.TimerOptions.RefreshLocalization();

        RefreshCurrentCulture();
    }

    public void RefreshCurrentCulture()
    {
        RefreshControllersAndScreens();
        RefreshLocalizedControls();
    }

    private void RefreshControllersAndScreens()
    {
        switch (_state.CurrentState)
        {
            case GameplayState:
                TryRefresh(() => UIManager.GetUIController<HotbarUIController>().ReloadHotbar());
                TryRefresh(() => UIManager.GetUIController<InventoryUIController>().ReloadSlots());
                break;
            case LobbyState lobby:
                TryRefresh(lobby.RefreshLocalization);
                break;
        }

        TryRefresh(() => UIManager.GetUIController<OptionsUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<AdminUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<GhostUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<EscapeUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<ChangelogUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<InfoUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<RoadmapUIController>().RefreshLocalization());
    }

    private void RefreshLocalizedControls()
    {
        foreach (var root in UIManager.AllRoots)
        {
            RelocalizeRecursive(root);
        }
    }

    private void RelocalizeRecursive(Control control)
    {
        if (control is ILocalizedControl localized && !control.Disposed)
        {
            TryRefresh(localized.Relocalize);
        }

        foreach (var child in control.Children)
        {
            RelocalizeRecursive(child);
        }
    }

    private void TryRefresh(Action refresh)
    {
        try
        {
            refresh();
        }
        catch (Exception e)
        {
            Log.Debug($"Skipped localization refresh step: {e}");
        }
    }

    private CultureInfo ResolveSupportedCultureOrFallback(string cultureName)
    {
        var supported = GetSupportedCultures();

        if (TryParseCulture(cultureName, out var culture) &&
            TryResolveSupportedCulture(culture, supported, out var resolved))
        {
            return resolved;
        }

        return GetFallbackCulture(supported);
    }

    private List<CultureInfo> GetSupportedCultures()
    {
        var cultures = new List<CultureInfo>();
        foreach (var culture in _loc.GetFoundCultures())
        {
            if (ContainsCulture(cultures, culture.Name))
                continue;

            cultures.Add(culture);
        }

        return cultures;
    }

    private static bool TryResolveSupportedCulture(
        CultureInfo requested,
        IReadOnlyList<CultureInfo> supported,
        out CultureInfo resolved)
    {
        foreach (var culture in supported)
        {
            if (!string.Equals(culture.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            resolved = culture;
            return true;
        }

        resolved = default!;
        return false;
    }

    private static CultureInfo GetFallbackCulture(IReadOnlyList<CultureInfo> supported)
    {
        if (TryParseCulture(DefaultCultureName, out var fallback) &&
            TryResolveSupportedCulture(fallback, supported, out var resolved))
        {
            return resolved;
        }

        return supported.Count > 0
            ? supported[0]
            : CultureInfo.GetCultureInfo(DefaultCultureName, predefinedOnly: false);
    }

    private static bool TryParseCulture(string? cultureName, out CultureInfo culture)
    {
        culture = default!;

        if (string.IsNullOrWhiteSpace(cultureName))
            return false;

        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName, predefinedOnly: false);
            return !string.IsNullOrWhiteSpace(culture.Name);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ContainsCulture(IReadOnlyList<CultureInfo> cultures, string cultureName)
    {
        foreach (var culture in cultures)
        {
            if (string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
