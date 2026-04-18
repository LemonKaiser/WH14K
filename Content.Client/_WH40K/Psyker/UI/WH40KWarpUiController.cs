using System;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed class WH40KWarpUiController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private WH40KWarpHudControl? _hud;
    private WH40KPsykerProgressionWindow? _psykerWindow;
    private WH40KChaosGiftsWindow? _chaosWindow;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;

        _cfg.OnValueChanged(CVars.LocCultureName, OnCultureChanged);
    }

    private void OnCultureChanged(string _)
    {
        // Force an immediate refresh of open Psyker/Chaos windows so labels update in the new language.
        if (_player.LocalEntity is not { } uid)
            return;

        if (_psykerWindow is { IsOpen: true } && EntityManager.HasComponent<WH40KPsykerRoleComponent>(uid))
            RefreshPsykerWindow(uid);

        if (_chaosWindow is { IsOpen: true } &&
            EntityManager.HasComponent<WH40KChaosGiftRoleComponent>(uid) &&
            EntityManager.TryGetComponent<WH40KChaosGiftProgressionComponent>(uid, out var prog) &&
            prog?.EffectiveLeader == true)
        {
            RefreshChaosWindow(uid);
        }
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsureHud();
    }

    public void OnStateExited(GameplayState state)
    {
        ShutdownUi();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_hud is { Disposed: true })
            _hud = null;

        if (_hud == null)
            return;

        if (_player.LocalEntity is not { } uid)
        {
            HideAllUi();
            return;
        }

        var hasPsykerRole = EntityManager.HasComponent<WH40KPsykerRoleComponent>(uid);
        var hasChaosRole = EntityManager.HasComponent<WH40KChaosGiftRoleComponent>(uid);
        var chaosProgression = EntityManager.GetComponentOrNull<WH40KChaosGiftProgressionComponent>(uid);
        var hasChaosHudRole = hasChaosRole;
        var hasChaosUiRole = hasChaosRole && chaosProgression?.EffectiveLeader == true;
        if (!hasPsykerRole && !hasChaosHudRole)
        {
            HideAllUi();
            return;
        }

        WH40KWarpResourceComponent? warp = EntityManager.GetComponentOrNull<WH40KWarpResourceComponent>(uid);
        WH40KWarpInstabilityComponent? instability = EntityManager.GetComponentOrNull<WH40KWarpInstabilityComponent>(uid);

        var warpCurrent = warp?.CurrentCharge ?? 0f;
        var warpMax = Math.Max(1f, warp?.MaxCharge ?? 100f);
        var instabilityCurrent = instability?.CurrentInstability ?? 0f;
        var instabilityMax = Math.Max(1f, instability?.MaxInstability ?? 100f);
        var warpFraction = Math.Clamp(warpCurrent / warpMax, 0f, 1f);
        var instabilityFraction = Math.Clamp(instabilityCurrent / instabilityMax, 0f, 1f);
        var chaosTheme = hasChaosHudRole && !hasPsykerRole;
        var patron = WH40KChaosPatron.None;

        string detailPrimaryText;
        string detailSecondaryText;
        if (chaosTheme && chaosProgression != null)
        {
            patron = chaosProgression.AttunedPatron;
            var nextLevelXp = GetXpForNextChaosLevel(chaosProgression);
            var xpText = FormatLevelXp(chaosProgression.LevelXp, nextLevelXp);
            detailPrimaryText = Loc.GetString(
                "wh40k-warp-ui-chaos-summary-primary",
                ("level", chaosProgression.Level),
                ("max", chaosProgression.MaxLevel),
                ("xp", xpText));
            detailSecondaryText = Loc.GetString(
                "wh40k-warp-ui-chaos-summary-secondary",
                ("patron", patron == WH40KChaosPatron.None
                    ? Loc.GetString("wh40k-chaos-window-patron-none")
                    : Loc.GetString(GetPatronLocKey(patron))),
                ("points", chaosProgression.DevelopmentPoints));
        }
        else if (!chaosTheme && EntityManager.TryGetComponent<WH40KPsykerProgressionComponent>(uid, out var psykerProgression) && psykerProgression != null)
        {
            var astralProgression = EntityManager.GetComponentOrNull<WH40KPsykerAstralProgressionComponent>(uid);
            var nextLevelXp = GetXpForNextPsykerLevel(psykerProgression);
            var xpText = FormatLevelXp(psykerProgression.LevelXp, nextLevelXp);
            detailPrimaryText = Loc.GetString(
                "wh40k-warp-ui-psyker-summary-primary",
                ("level", psykerProgression.Level),
                ("max", psykerProgression.MaxLevel),
                ("xp", xpText));
            detailSecondaryText = Loc.GetString(
                "wh40k-warp-ui-psyker-summary-secondary",
                ("points", astralProgression?.DisciplinePoints ?? 0),
                ("depth", Math.Max(1, astralProgression?.AstralDepth ?? 1)),
                ("strain", MathF.Round(astralProgression?.AstralStrain ?? 0f, 1)));
        }
        else
        {
            detailPrimaryText = Loc.GetString(chaosTheme
                ? "wh40k-chaos-window-role"
                : "wh40k-psyker-window-role");
            detailSecondaryText = string.Empty;
        }

        _hud.ApplyState(new WH40KWarpHudViewState(
            true,
            $"{MathF.Round(warpCurrent, 1)}/{MathF.Round(warpMax, 1)}",
            $"{MathF.Round(instabilityCurrent, 1)}/{MathF.Round(instabilityMax, 1)}",
            warpFraction,
            instabilityFraction,
            chaosTheme,
            patron,
            detailPrimaryText,
            detailSecondaryText));

        if (_psykerWindow != null && _psykerWindow.IsOpen && hasPsykerRole)
            RefreshPsykerWindow(uid);

        if (_chaosWindow != null && _chaosWindow.IsOpen && hasChaosUiRole)
            RefreshChaosWindow(uid);

        var psykerWindow = _psykerWindow;
        if (psykerWindow is { IsOpen: true } && !hasPsykerRole)
            psykerWindow.Close();

        var chaosWindow = _chaosWindow;
        if (chaosWindow is { IsOpen: true } && !hasChaosUiRole)
            chaosWindow.Close();
    }

    private void OnScreenLoad()
    {
        EnsureHud();
    }

    private void OnScreenUnload()
    {
        ShutdownUi();
    }

    private void EnsureHud()
    {
        if (_hud is { Disposed: true })
            _hud = null;

        if (_hud != null)
            return;

        if (UIManager.ActiveScreen == null)
            return;

        _hud = new WH40KWarpHudControl();

        if (UIManager.ActiveScreen.GetWidget<MainViewport>()?.Parent is LayoutContainer layout)
        {
            layout.AddChild(_hud);
            LayoutContainer.SetAnchorAndMarginPreset(_hud, LayoutContainer.LayoutPreset.BottomRight, margin: 10);
        }
        else
        {
            UIManager.RootControl.AddChild(_hud);
        }
    }

    public void TogglePsykerWindow()
    {
        if (_player.LocalEntity is not { } uid || !EntityManager.HasComponent<WH40KPsykerRoleComponent>(uid))
            return;

        _psykerWindow ??= UIManager.CreateWindow<WH40KPsykerProgressionWindow>();
        if (_psykerWindow.IsOpen)
        {
            _psykerWindow.Close();
            return;
        }

        RefreshPsykerWindow(uid);
        _psykerWindow.Open();
    }

    private void ToggleChaosWindow()
    {
        if (_player.LocalEntity is not { } uid ||
            !EntityManager.HasComponent<WH40KChaosGiftRoleComponent>(uid) ||
            !EntityManager.TryGetComponent<WH40KChaosGiftProgressionComponent>(uid, out var progression) ||
            progression?.EffectiveLeader != true)
            return;

        _chaosWindow ??= UIManager.CreateWindow<WH40KChaosGiftsWindow>();
        if (_chaosWindow.IsOpen)
        {
            _chaosWindow.Close();
            return;
        }

        RefreshChaosWindow(uid);
        _chaosWindow.Open();
    }

    private void RefreshPsykerWindow(EntityUid uid)
    {
        if (_psykerWindow == null)
            return;

        if (!EntityManager.TryGetComponent<WH40KPsykerProgressionComponent>(uid, out var progressionComponent) ||
            progressionComponent == null)
            return;
        var progression = progressionComponent;

        WH40KWarpResourceComponent? warp = EntityManager.GetComponentOrNull<WH40KWarpResourceComponent>(uid);
        WH40KWarpInstabilityComponent? instability = EntityManager.GetComponentOrNull<WH40KWarpInstabilityComponent>(uid);

        var warpCurrent = warp?.CurrentCharge ?? 0f;
        var warpMax = Math.Max(1f, warp?.MaxCharge ?? 100f);
        var instabilityCurrent = instability?.CurrentInstability ?? 0f;
        var instabilityMax = Math.Max(1f, instability?.MaxInstability ?? 100f);

        var nextLevelXp = GetXpForNextPsykerLevel(progression);
        var levelProgress = progression.Level >= progression.MaxLevel || nextLevelXp <= 0f
            ? 1f
            : Math.Clamp(progression.LevelXp / nextLevelXp, 0f, 1f);

        _psykerWindow.ApplyState(new WH40KPsykerProgressionViewState(
            Loc.GetString("wh40k-psyker-window-role"),
            Loc.GetString("wh40k-psyker-window-charge", ("current", MathF.Round(warpCurrent, 1)), ("max", MathF.Round(warpMax, 1))),
            Loc.GetString("wh40k-psyker-window-instability", ("current", MathF.Round(instabilityCurrent, 1)), ("max", MathF.Round(instabilityMax, 1))),
            Loc.GetString("wh40k-psyker-window-level", ("level", progression.Level), ("max", progression.MaxLevel)),
            Loc.GetString("wh40k-psyker-window-xp", ("current", MathF.Round(progression.LevelXp, 1)), ("next", MathF.Round(nextLevelXp, 1)), ("total", MathF.Round(progression.TotalXp, 1))),
            Loc.GetString("wh40k-psyker-window-meditation",
                ("interval", Math.Max(1, (int) Math.Round(progression.MeditationInterval.TotalSeconds))),
                ("xp", MathF.Round(progression.MeditationXpPerInterval, 2)),
                ("bed", MathF.Round(progression.MeditationBedBonusMultiplier, 2))),
            Loc.GetString("wh40k-psyker-window-cast-xp", ("base", MathF.Round(progression.CastXpBase, 2))),
            Loc.GetString("wh40k-psyker-window-repeat",
                ("window", Math.Max(1, (int) Math.Round(progression.CastRepeatWindow.TotalSeconds))),
                ("falloff", MathF.Round(progression.CastRepeatFalloff, 2)),
                ("min", MathF.Round(progression.CastMinMultiplier, 2))),
            Loc.GetString("wh40k-psyker-window-hint"),
            levelProgress));
    }

    private void RefreshChaosWindow(EntityUid uid)
    {
        if (_chaosWindow == null)
            return;

        if (!EntityManager.TryGetComponent<WH40KChaosGiftProgressionComponent>(uid, out var progressionComponent) ||
            progressionComponent == null)
            return;
        var progression = progressionComponent;

        WH40KWarpResourceComponent? warp = EntityManager.GetComponentOrNull<WH40KWarpResourceComponent>(uid);
        WH40KWarpInstabilityComponent? instability = EntityManager.GetComponentOrNull<WH40KWarpInstabilityComponent>(uid);

        var warpCurrent = warp?.CurrentCharge ?? 0f;
        var warpMax = Math.Max(1f, warp?.MaxCharge ?? 100f);
        var instabilityCurrent = instability?.CurrentInstability ?? 0f;
        var instabilityMax = Math.Max(1f, instability?.MaxInstability ?? 100f);
        var warpFraction = Math.Clamp(warpCurrent / warpMax, 0f, 1f);
        var instabilityFraction = Math.Clamp(instabilityCurrent / instabilityMax, 0f, 1f);

        var nextLevelXp = GetXpForNextChaosLevel(progression);
        var levelProgress = progression.Level >= progression.MaxLevel || nextLevelXp <= 0f
            ? 1f
            : Math.Clamp(progression.LevelXp / nextLevelXp, 0f, 1f);

        var ritualRemaining = progression.RitualBonusExpiresAt > _timing.CurTime
            ? Math.Max(0, (int) Math.Ceiling((progression.RitualBonusExpiresAt - _timing.CurTime).TotalSeconds))
            : 0;

        var sacrificeRemaining = progression.NextSacrificeAt > _timing.CurTime
            ? Math.Max(0, (int) Math.Ceiling((progression.NextSacrificeAt - _timing.CurTime).TotalSeconds))
            : 0;

        var passiveIncome = GetChaosPassiveXpPerTick(progression);
        var passiveIncomeSeconds = Math.Max(1, (int) Math.Round(progression.PassiveXpInterval.TotalSeconds));

        _chaosWindow.ApplyState(new WH40KChaosGiftsViewState(
            progression.AttunedPatron,
            Loc.GetString("wh40k-chaos-window-role"),
            Loc.GetString("wh40k-chaos-window-patron", ("patron", Loc.GetString(GetPatronLocKey(progression.AttunedPatron)))),
            Loc.GetString(
                "wh40k-chaos-window-souls",
                ("patron", Loc.GetString(GetPatronLocKey(progression.AttunedPatron))),
                ("count", progression.PatronSoulOfferCount)),
            Loc.GetString("wh40k-chaos-window-charge", ("current", MathF.Round(warpCurrent, 1)), ("max", MathF.Round(warpMax, 1))),
            Loc.GetString("wh40k-chaos-window-instability", ("current", MathF.Round(instabilityCurrent, 1)), ("max", MathF.Round(instabilityMax, 1))),
            Loc.GetString("wh40k-chaos-window-level", ("level", progression.Level), ("max", progression.MaxLevel)),
            Loc.GetString("wh40k-chaos-window-xp", ("current", MathF.Round(progression.LevelXp, 1)), ("next", MathF.Round(nextLevelXp, 1)), ("total", MathF.Round(progression.TotalXp, 1))),
            Loc.GetString("wh40k-chaos-window-passive-income", ("xp", MathF.Round(passiveIncome, 1)), ("seconds", passiveIncomeSeconds)),
            Loc.GetString("wh40k-chaos-window-altar-title"),
            Loc.GetString("wh40k-chaos-window-attunement", ("multiplier", MathF.Round(progression.AttunementXpMultiplier, 2))),
            Loc.GetString("wh40k-chaos-window-ritual", ("multiplier", MathF.Round(progression.RitualBonusMultiplier, 2)), ("seconds", ritualRemaining)),
            Loc.GetString("wh40k-chaos-window-cooldown", ("seconds", sacrificeRemaining)),
            Loc.GetString("wh40k-chaos-window-altar-guide"),
            Loc.GetString("wh40k-chaos-window-altar-risk"),
            Loc.GetString("wh40k-chaos-window-dev-points", ("points", progression.DevelopmentPoints)),
            Loc.GetString("wh40k-chaos-window-hint"),
            warpFraction,
            instabilityFraction,
            levelProgress));
    }

    private void HideAllUi()
    {
        if (_hud is { Disposed: true })
            _hud = null;

        if (_psykerWindow is { Disposed: true })
            _psykerWindow = null;

        if (_chaosWindow is { Disposed: true })
            _chaosWindow = null;

        _hud?.ApplyState(WH40KWarpHudViewState.Hidden);

        if (_psykerWindow != null)
            _psykerWindow.Close();

        if (_chaosWindow != null)
            _chaosWindow.Close();
    }

    private void ShutdownUi()
    {
        if (_hud != null)
        {
            if (!_hud.Disposed)
                _hud.Orphan();

            _hud = null;
        }

        if (_psykerWindow != null)
        {
            if (!_psykerWindow.Disposed)
                _psykerWindow.Close();

            _psykerWindow = null;
        }

        if (_chaosWindow != null)
        {
            if (!_chaosWindow.Disposed)
                _chaosWindow.Close();

            _chaosWindow = null;
        }
    }

    private static float GetXpForNextPsykerLevel(WH40KPsykerProgressionComponent progression)
    {
        if (progression.Level >= progression.MaxLevel)
            return 0f;

        var levelIndex = Math.Max(0, progression.Level - 1);
        var xp = progression.BaseXpForNextLevel * MathF.Pow(progression.XpGrowthFactor, levelIndex);
        return Math.Max(1f, xp);
    }

    private static float GetXpForNextChaosLevel(WH40KChaosGiftProgressionComponent progression)
    {
        if (progression.Level >= progression.MaxLevel)
            return 0f;

        var currentLevel = Math.Clamp(progression.Level, 1, progression.MaxLevel);
        var xp = progression.XpPerLevelStep * currentLevel;
        return Math.Max(1f, xp);
    }

    private static float GetChaosPassiveXpPerTick(WH40KChaosGiftProgressionComponent progression)
    {
        var bonus = Math.Max(0, progression.Level - 1) * progression.PassiveXpPerLevelBonus;
        return Math.Max(0f, progression.PassiveXpBasePerTick + bonus);
    }

    private static string FormatLevelXp(float currentXp, float nextLevelXp)
    {
        if (nextLevelXp <= 0f)
            return Loc.GetString("wh40k-warp-ui-xp-max");

        return $"{MathF.Round(currentXp, 1)}/{MathF.Round(nextLevelXp, 1)}";
    }

    private static string GetPatronLocKey(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => "wh40k-chaos-patron-khorne",
            WH40KChaosPatron.Nurgle => "wh40k-chaos-patron-nurgle",
            WH40KChaosPatron.Slaanesh => "wh40k-chaos-patron-slaanesh",
            WH40KChaosPatron.Tzeentch => "wh40k-chaos-patron-tzeentch",
            WH40KChaosPatron.Undivided => "wh40k-chaos-patron-undivided",
            _ => "wh40k-chaos-window-patron-none",
        };
    }
}

public readonly record struct WH40KWarpHudViewState(
    bool Visible,
    string WarpChargeText,
    string WarpInstabilityText,
    float WarpChargeFraction,
    float WarpInstabilityFraction,
    bool ChaosTheme,
    WH40KChaosPatron Patron,
    string DetailPrimaryText,
    string DetailSecondaryText)
{
    public static readonly WH40KWarpHudViewState Hidden = new(
        false,
        string.Empty,
        string.Empty,
        0f,
        0f,
        false,
        WH40KChaosPatron.None,
        string.Empty,
        string.Empty);
}

public readonly record struct WH40KPsykerProgressionViewState(
    string RoleText,
    string WarpChargeText,
    string WarpInstabilityText,
    string LevelText,
    string XpText,
    string MeditationText,
    string CastText,
    string RepeatText,
    string HintText,
    float LevelProgress);

public readonly record struct WH40KChaosGiftsViewState(
    WH40KChaosPatron Patron,
    string RoleText,
    string PatronText,
    string SoulsText,
    string WarpChargeText,
    string WarpInstabilityText,
    string LevelText,
    string XpText,
    string PassiveIncomeText,
    string AltarTitleText,
    string AttunementText,
    string RitualText,
    string CooldownText,
    string AltarGuideText,
    string AltarRiskText,
    string DevelopmentPointsText,
    string HintText,
    float WarpChargeFraction,
    float WarpInstabilityFraction,
    float LevelProgress);
