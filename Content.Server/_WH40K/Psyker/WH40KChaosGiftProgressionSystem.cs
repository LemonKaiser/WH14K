using System.Collections.Generic;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Popups;
using Content.Server._WH40K.Localizations;
using Content.Shared.Atmos.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Content.Shared._WH40K.Psyker;
using Content.Server._WH40K.Stats;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Chaos progression runtime:
/// - skrizhal attunement and patron lock;
/// - altar sacrifice rewards and ritual buff windows;
/// - personal chaos level + development points progression;
/// - R5 branch unlock economy (free primary + paid unlocks).
/// </summary>
public sealed partial class WH40KChaosGiftProgressionSystem : EntitySystem
{
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  WH40KChaosCultSystem _cult = default!;
    [Dependency] private  WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private  FlammableSystem _flammableSystem = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  ItemToggleSystem _itemToggle = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  WH40KPlayerStatsSystem _stats = default!;

    private const string ChaosSkrizhalPrototype = "WH40KRuneSkrizhalChaos";
    private const string KhorneSkrizhalPrototype = "WH40KRuneSkrizhalKhorn";
    private const string NurgleSkrizhalPrototype = "WH40KRuneSkrizhalNurgk";
    private const string SlaaneshSkrizhalPrototype = "WH40KRuneSkrizhalSlaanesh";
    private const string TzeentchSkrizhalPrototype = "WH40KRuneSkrizhalTzinch";
    private const int ChaosMaxLevel = 10;
    private const int ChaosPointsPerLevel = 3;
    private const int ChaosGiftUnlockCost = 3;
    private const int ChaosUpgradeTierCost = 1;
    private const int ChaosUpgradeExCost = 3;
    private const float ChaosXpPerLevelStep = 100f;
    private const float ChaosPassiveXpBasePerTick = 1f;
    private const float ChaosPassiveXpPerLevelBonus = 0.025f;
    private const float ChaosWarpRegenPerLevel = 0.1f;
    private static readonly TimeSpan ChaosPassiveXpInterval = TimeSpan.FromMinutes(1);
    private readonly HashSet<EntityUid> _nearbyEntities = new();
    private readonly List<EntityUid> _soulTargets = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftProgressionComponent, ComponentStartup>(OnChaosProgressionStartup);
        SubscribeLocalEvent<WH40KChaosSkrizhalComponent, UseInHandEvent>(OnSkrizhalUseInHand);
        SubscribeLocalEvent<WH40KChaosSkrizhalComponent, ActivateInWorldEvent>(OnSkrizhalActivateInWorld);
        SubscribeLocalEvent<WH40KChaosSkrizhalComponent, ItemToggledEvent>(OnSkrizhalToggled);
        SubscribeLocalEvent<WH40KChaosAltarComponent, InteractHandEvent>(OnAltarInteractHand);
        SubscribeLocalEvent<WH40KChaosAltarComponent, InteractUsingEvent>(OnAltarInteractUsing);
        SubscribeLocalEvent<WH40KChaosAltarComponent, GetVerbsEvent<InteractionVerb>>(OnAltarGetVerbs);
        SubscribeLocalEvent<WH40KChaosSkrizhalComponent, BoundUIOpenedEvent>(OnAnySkrizhalUiOpened);
        SubscribeLocalEvent<WH40KChaosSkrizhalComponent, BoundUIClosedEvent>(OnAnySkrizhalUiClosed);

        Subs.BuiEvents<WH40KChaosSkrizhalComponent>(WH40KChaosSkrizhalUiKey.PatronSelection, subs =>
        {
            subs.Event<WH40KChaosSkrizhalSelectPatronMessage>(OnSelectPatron);
        });

        Subs.BuiEvents<WH40KChaosSkrizhalComponent>(WH40KChaosSkrizhalUiKey.PatronBranch, subs =>
        {
            subs.Event<WH40KChaosSkrizhalSelectPrimaryGiftMessage>(OnSelectPrimaryGift);
            subs.Event<WH40KChaosSkrizhalUnlockGiftMessage>(OnUnlockGift);
            subs.Event<WH40KChaosSkrizhalUpgradeTierMessage>(OnUpgradeTier);
            subs.Event<WH40KChaosSkrizhalUnlockExMessage>(OnUnlockEx);
        });
    }

    private void OnAnySkrizhalUiOpened(Entity<WH40KChaosSkrizhalComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not WH40KChaosSkrizhalUiKey uiKey)
            return;

        switch (uiKey)
        {
            case WH40KChaosSkrizhalUiKey.PatronSelection:
                OnSkrizhalUiOpened(ent, ref args);
                break;
            case WH40KChaosSkrizhalUiKey.PatronCultistInfo:
                OnSkrizhalCultistUiOpened(ent, ref args);
                break;
            case WH40KChaosSkrizhalUiKey.PatronBranch:
                OnSkrizhalBranchUiOpened(ent, ref args);
                break;
        }

        SyncToggleWithUiOpen(ent, args.Actor);
    }

    private void OnAnySkrizhalUiClosed(Entity<WH40KChaosSkrizhalComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not WH40KChaosSkrizhalUiKey)
            return;

        if (AnySkrizhalUiOpenForActor(ent.Owner, args.Actor))
            return;

        if (!TryComp<ItemToggleComponent>(ent.Owner, out var toggle) || !toggle.Activated)
            return;

        _itemToggle.TryDeactivate((ent.Owner, toggle), args.Actor, predicted: false, showPopup: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent, WH40KChaosGiftRoleComponent>();
        while (query.MoveNext(out var uid, out var progression, out _))
        {
            var changed = false;

            SyncChaosWarpRegen(uid, progression);

            if (progression.RitualBonusExpiresAt != TimeSpan.Zero && now >= progression.RitualBonusExpiresAt)
            {
                progression.RitualBonusMultiplier = 1f;
                progression.RitualBonusExpiresAt = TimeSpan.Zero;
                Dirty(uid, progression);
                changed = true;
            }

            changed |= TryApplyPassiveXpTick(uid, progression, now);

            if (changed)
                TryUpdateBoundProgressionUi(uid, progression);
        }
    }

    private void OnChaosProgressionStartup(Entity<WH40KChaosGiftProgressionComponent> ent, ref ComponentStartup args)
    {
        var changed = false;

        if (ent.Comp.MaxLevel != ChaosMaxLevel)
        {
            ent.Comp.MaxLevel = ChaosMaxLevel;
            changed = true;
        }

        var freshProfile =
            !ent.Comp.StarterSkrizhalIssued &&
            ent.Comp.AttunedPatron == WH40KChaosPatron.None &&
            ent.Comp.PrimaryGiftSlot == 0 &&
            !ent.Comp.GiftSlotOneUnlocked &&
            !ent.Comp.GiftSlotTwoUnlocked &&
            !ent.Comp.GiftSlotThreeUnlocked;

        if (freshProfile)
        {
            if (ent.Comp.Level != 1)
            {
                ent.Comp.Level = 1;
                changed = true;
            }

            if (Math.Abs(ent.Comp.LevelXp) > 0.0001f)
            {
                ent.Comp.LevelXp = 0f;
                changed = true;
            }

            if (Math.Abs(ent.Comp.TotalXp) > 0.0001f)
            {
                ent.Comp.TotalXp = 0f;
                changed = true;
            }

            if (ent.Comp.DevelopmentPoints != 0)
            {
                ent.Comp.DevelopmentPoints = 0;
                changed = true;
            }
        }

        if (ent.Comp.Level < 1 || ent.Comp.Level > ent.Comp.MaxLevel)
        {
            ent.Comp.Level = Math.Clamp(ent.Comp.Level, 1, ent.Comp.MaxLevel);
            changed = true;
        }

        if (ent.Comp.AttunementXpMultiplier <= 0f)
        {
            ent.Comp.AttunementXpMultiplier = 1f;
            changed = true;
        }

        if (ent.Comp.RitualBonusMultiplier <= 0f)
        {
            ent.Comp.RitualBonusMultiplier = 1f;
            changed = true;
        }

        if (ent.Comp.PointsPerLevel != ChaosPointsPerLevel)
        {
            ent.Comp.PointsPerLevel = ChaosPointsPerLevel;
            changed = true;
        }

        if (ent.Comp.DevelopmentPoints < 0)
        {
            ent.Comp.DevelopmentPoints = 0;
            changed = true;
        }

        if (ent.Comp.GiftUnlockCost != ChaosGiftUnlockCost)
        {
            ent.Comp.GiftUnlockCost = ChaosGiftUnlockCost;
            changed = true;
        }

        if (Math.Abs(ent.Comp.XpPerLevelStep - ChaosXpPerLevelStep) > 0.0001f)
        {
            ent.Comp.XpPerLevelStep = ChaosXpPerLevelStep;
            changed = true;
        }

        if (Math.Abs(ent.Comp.PassiveXpBasePerTick - ChaosPassiveXpBasePerTick) > 0.0001f)
        {
            ent.Comp.PassiveXpBasePerTick = ChaosPassiveXpBasePerTick;
            changed = true;
        }

        if (Math.Abs(ent.Comp.PassiveXpPerLevelBonus - ChaosPassiveXpPerLevelBonus) > 0.0001f)
        {
            ent.Comp.PassiveXpPerLevelBonus = ChaosPassiveXpPerLevelBonus;
            changed = true;
        }

        if (ent.Comp.PassiveXpInterval != ChaosPassiveXpInterval)
        {
            ent.Comp.PassiveXpInterval = ChaosPassiveXpInterval;
            changed = true;
        }

        if (ent.Comp.NextPassiveXpAt == TimeSpan.Zero)
        {
            ent.Comp.NextPassiveXpAt = _timing.CurTime + ent.Comp.PassiveXpInterval;
            changed = true;
        }

        if (ent.Comp.LevelXp < 0f)
        {
            ent.Comp.LevelXp = 0f;
            changed = true;
        }

        if (ent.Comp.Level >= ent.Comp.MaxLevel && Math.Abs(ent.Comp.LevelXp) > 0.0001f)
        {
            ent.Comp.LevelXp = 0f;
            changed = true;
        }

        if (ent.Comp.TotalXp < 0f)
        {
            ent.Comp.TotalXp = 0f;
            changed = true;
        }

        var expectedSoulCount = ent.Comp.AttunedPatron == WH40KChaosPatron.None
            ? 0
            : GetPatronSoulCount(ent.Comp.AttunedPatron);

        if (ent.Comp.PatronSoulOfferCount != expectedSoulCount)
        {
            ent.Comp.PatronSoulOfferCount = expectedSoulCount;
            changed = true;
        }

        if (ent.Comp.PrimaryGiftSlot is < 0 or > 3)
        {
            ent.Comp.PrimaryGiftSlot = 0;
            changed = true;
        }

        if (ent.Comp.PrimaryGiftSlot == 0 &&
            (ent.Comp.GiftSlotOneUnlocked || ent.Comp.GiftSlotTwoUnlocked || ent.Comp.GiftSlotThreeUnlocked))
        {
            ent.Comp.GiftSlotOneUnlocked = false;
            ent.Comp.GiftSlotTwoUnlocked = false;
            ent.Comp.GiftSlotThreeUnlocked = false;
            changed = true;
        }

        if (ent.Comp.PrimaryGiftSlot == 0 &&
            (ent.Comp.KhorneGiftOnePowerTier > 0 ||
             ent.Comp.KhorneGiftOneCooldownTier > 0 ||
             ent.Comp.KhorneGiftOneUtilityTier > 0 ||
             ent.Comp.KhorneGiftOneExUnlocked ||
             ent.Comp.KhorneGiftTwoPowerTier > 0 ||
             ent.Comp.KhorneGiftTwoCooldownTier > 0 ||
             ent.Comp.KhorneGiftTwoUtilityTier > 0 ||
             ent.Comp.KhorneGiftTwoExUnlocked ||
             ent.Comp.KhorneGiftThreePowerTier > 0 ||
             ent.Comp.KhorneGiftThreeCooldownTier > 0 ||
             ent.Comp.KhorneGiftThreeUtilityTier > 0 ||
             ent.Comp.KhorneGiftThreeExUnlocked))
        {
            ResetKhorneUpgradeState(ent.Comp);
            changed = true;
        }

        if (ent.Comp.PrimaryGiftSlot > 0 && !IsGiftSlotUnlocked(ent.Comp, ent.Comp.PrimaryGiftSlot))
        {
            SetGiftSlotUnlocked(ent.Comp, ent.Comp.PrimaryGiftSlot, true);
            changed = true;
        }

        changed |= SanitizeKhorneUpgradeState(ent.Comp);

        if (!ent.Comp.AllowPatronSwitch &&
            ent.Comp.AttunedPatron != WH40KChaosPatron.None &&
            !ent.Comp.PatronSelectionLocked)
        {
            ent.Comp.PatronSelectionLocked = true;
            changed = true;
        }

        if (changed)
            Dirty(ent, ent.Comp);

        SyncChaosWarpRegen(ent.Owner, ent.Comp);
        _cult.AttachMemberToCult(ent.Owner, ent.Comp, WH40KChaosPatron.None);
    }

    private bool TryApplyPassiveXpTick(EntityUid uid, WH40KChaosGiftProgressionComponent progression, TimeSpan now)
    {
        if (progression.PassiveXpInterval <= TimeSpan.Zero)
            return false;

        if (progression.NextPassiveXpAt == TimeSpan.Zero)
        {
            progression.NextPassiveXpAt = now + progression.PassiveXpInterval;
            Dirty(uid, progression);
            return true;
        }

        if (now < progression.NextPassiveXpAt)
            return false;

        var tickCount = 0;
        var changed = false;

        while (now >= progression.NextPassiveXpAt && tickCount < 128)
        {
            var xpGain = GetPassiveXpPerTick(progression);
            if (xpGain > 0f)
                GainProjectedProgressionXp(uid, progression, xpGain);

            progression.NextPassiveXpAt += progression.PassiveXpInterval;
            tickCount++;
            changed = true;
        }

        if (changed)
            Dirty(uid, progression);

        return changed;
    }

    private static float GetPassiveXpPerTick(WH40KChaosGiftProgressionComponent progression)
    {
        var levelBonus = Math.Max(0, progression.Level - 1) * ChaosPassiveXpPerLevelBonus;
        return Math.Max(0f, ChaosPassiveXpBasePerTick + levelBonus);
    }

    private void OnSkrizhalUseInHand(Entity<WH40KChaosSkrizhalComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryConsumeSkrizhalUiThrottle((ent.Owner, ent.Comp), args.User))
        {
            args.Handled = true;
            return;
        }

        var handled = false;
        TryOpenSkrizhalUi(ent, args.User, ref handled);
        args.Handled |= handled;
    }

    private void OnSkrizhalActivateInWorld(Entity<WH40KChaosSkrizhalComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!TryConsumeSkrizhalUiThrottle((ent.Owner, ent.Comp), args.User))
        {
            args.Handled = true;
            return;
        }

        var handled = false;
        TryOpenSkrizhalUi(ent, args.User, ref handled);
        args.Handled |= handled;
    }

    private void OnSkrizhalToggled(Entity<WH40KChaosSkrizhalComponent> ent, ref ItemToggledEvent args)
    {
        if (args.User is not { } user)
            return;

        if (!args.Activated)
        {
            _ui.CloseUis(ent.Owner, user);
            return;
        }

        if (AnySkrizhalUiOpenForActor(ent.Owner, user))
            return;

        if (!TryConsumeSkrizhalUiThrottle((ent.Owner, ent.Comp), user))
        {
            if (TryComp<ItemToggleComponent>(ent.Owner, out var toggle) && toggle.Activated)
                _itemToggle.TryDeactivate((ent.Owner, toggle), user, predicted: false, showPopup: false);

            return;
        }

        var handled = false;
        TryOpenSkrizhalUi(ent, user, ref handled);
    }

    private void TryOpenSkrizhalUi(
        Entity<WH40KChaosSkrizhalComponent> ent,
        EntityUid user,
        ref bool handled)
    {
        var hasChaosRole = HasComp<WH40KChaosGiftRoleComponent>(user);
        if (!hasChaosRole)
            return;

        handled = true;

        if (TryEnforceCatastropheLockdown(ent, user))
            return;

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(user);
        var changed = false;

        if (ent.Comp.BoundOwner == null && ent.Comp.BindOnFirstUse)
            ent.Comp.BoundOwner = user;

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != user)
        {
            PopupCaution(user, "w40k-cg-owner-mismatch");
            return;
        }

        if (progression.BoundSkrizhal != ent.Owner)
        {
            progression.BoundSkrizhal = ent.Owner;
            changed = true;
        }

        if (!progression.StarterSkrizhalIssued)
        {
            progression.StarterSkrizhalIssued = true;
            changed = true;
        }

        var canOpenSelector = progression.AttunedPatron == WH40KChaosPatron.None ||
                              !progression.PatronSelectionLocked ||
                              progression.AllowPatronSwitch;

        if (canOpenSelector)
        {
            OpenSelectorUiForUser(ent.Owner, user);
            UpdatePatronSelectorUi(ent.Owner, progression);

            if (changed)
                Dirty(user, progression);

            return;
        }

        OpenProgressionUiForUser(ent.Owner, user, progression);

        if (changed)
            Dirty(user, progression);
    }

    private void OpenSelectorUiForUser(EntityUid skrizhal, EntityUid user)
    {
        if (_ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, user))
            return;

        _ui.TryOpenUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, user);
    }

    private void OpenBranchUiForUser(EntityUid skrizhal, EntityUid user, WH40KChaosPatron patron)
    {
        if (_ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, user))
            return;

        _ui.TryOpenUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, user);
    }

    private void OpenCultistUiForUser(EntityUid skrizhal, EntityUid user, WH40KChaosPatron patron)
    {
        if (_ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, user))
            return;

        _ui.TryOpenUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, user);
    }

    private void OpenProgressionUiForUser(EntityUid skrizhal, EntityUid user, WH40KChaosGiftProgressionComponent progression)
    {
        if (ShouldOpenLeaderUi(user, progression))
        {
            _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, user);
            OpenBranchUiForUser(skrizhal, user, progression.AttunedPatron);
            UpdatePatronBranchUi(skrizhal, progression);
            return;
        }

        _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, user);
        OpenCultistUiForUser(skrizhal, user, progression.AttunedPatron);
        UpdatePatronCultistUi(skrizhal, progression);
    }

    private bool AnySkrizhalUiOpenForActor(EntityUid skrizhal, EntityUid actor)
    {
        return _ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, actor) ||
               _ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, actor) ||
               _ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, actor);
    }

    private bool TryEnforceCatastropheLockdown(Entity<WH40KChaosSkrizhalComponent> ent, EntityUid actor)
    {
        if (!_globalWarp.CatastropheTriggered)
            return false;

        PopupCaution(actor, "w40k-cg-catastrophe-lockdown");
        CloseSkrizhalUis(ent.Owner, actor);

        if (TryComp<ItemToggleComponent>(ent.Owner, out var toggle) && toggle.Activated)
            _itemToggle.TryDeactivate((ent.Owner, toggle), actor, predicted: false, showPopup: false);

        return true;
    }

    private void CloseSkrizhalUis(EntityUid skrizhal, EntityUid actor)
    {
        _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, actor);
        _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, actor);
        _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, actor);
    }

    private bool ShouldOpenLeaderUi(EntityUid actor, WH40KChaosGiftProgressionComponent progression)
    {
        return progression.AttunedPatron != WH40KChaosPatron.None && _cult.IsEffectiveLeader(actor, progression);
    }

    private bool TryConsumeSkrizhalUiThrottle(Entity<WH40KChaosSkrizhalComponent> ent, EntityUid user)
    {
        var cooldownSeconds = Math.Max(0.05f, ent.Comp.UiInteractionCooldownSeconds);
        var now = _timing.CurTime;
        var throttle = EnsureComp<WH40KSkrizhalUiUserThrottleComponent>(user);
        if (throttle.NextAllowedUiInteractionAt > now)
            return false;

        throttle.NextAllowedUiInteractionAt = now + TimeSpan.FromSeconds(cooldownSeconds);
        return true;
    }

    private void SyncToggleWithUiOpen(Entity<WH40KChaosSkrizhalComponent> ent, EntityUid actor)
    {
        if (!TryComp<ItemToggleComponent>(ent.Owner, out var toggle))
            return;

        if (toggle.Activated)
            return;

        _itemToggle.TryActivate((ent.Owner, toggle), actor, predicted: false, showPopup: false);
    }

    private void OnSkrizhalUiOpened(Entity<WH40KChaosSkrizhalComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            return;
        }

        if (ent.Comp.BoundOwner == null && ent.Comp.BindOnFirstUse)
            ent.Comp.BoundOwner = args.Actor;

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        var changed = false;

        if (progression.PatronSelectionLocked &&
            progression.AttunedPatron != WH40KChaosPatron.None &&
            !progression.AllowPatronSwitch)
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            OpenProgressionUiForUser(ent.Owner, args.Actor, progression);
            return;
        }

        if (progression.BoundSkrizhal != ent.Owner)
        {
            progression.BoundSkrizhal = ent.Owner;
            changed = true;
        }

        if (!progression.StarterSkrizhalIssued)
        {
            progression.StarterSkrizhalIssued = true;
            changed = true;
        }

        if (changed)
            Dirty(args.Actor, progression);

        UpdatePatronSelectorUi(ent.Owner, progression);
    }

    private void OnSkrizhalCultistUiOpened(Entity<WH40KChaosSkrizhalComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronCultistInfo, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronCultistInfo, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronCultistInfo, args.Actor);
            OpenSelectorUiForUser(ent.Owner, args.Actor);
            UpdatePatronSelectorUi(ent.Owner, progression);
            return;
        }

        if (ShouldOpenLeaderUi(args.Actor, progression))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronCultistInfo, args.Actor);
            OpenBranchUiForUser(ent.Owner, args.Actor, progression.AttunedPatron);
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (progression.BoundSkrizhal != ent.Owner)
        {
            progression.BoundSkrizhal = ent.Owner;
            Dirty(args.Actor, progression);
        }

        UpdatePatronCultistUi(ent.Owner, progression);
    }

    private void OnSkrizhalBranchUiOpened(Entity<WH40KChaosSkrizhalComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            OpenSelectorUiForUser(ent.Owner, args.Actor);
            UpdatePatronSelectorUi(ent.Owner, progression);
            return;
        }

        if (!ShouldOpenLeaderUi(args.Actor, progression))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            OpenCultistUiForUser(ent.Owner, args.Actor, progression.AttunedPatron);
            UpdatePatronCultistUi(ent.Owner, progression);
            return;
        }

        if (progression.BoundSkrizhal != ent.Owner)
        {
            progression.BoundSkrizhal = ent.Owner;
            Dirty(args.Actor, progression);
        }

        UpdatePatronBranchUi(ent.Owner, progression);
    }

    private void OnSelectPatron(Entity<WH40KChaosSkrizhalComponent> ent, ref WH40KChaosSkrizhalSelectPatronMessage args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            return;
        }

        if (!IsSelectablePatron(args.Patron))
        {
            PopupCaution(args.Actor, "wh40k-chaos-selector-popup-invalid");
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            return;
        }

        if (ent.Comp.BoundOwner == null && ent.Comp.BindOnFirstUse)
            ent.Comp.BoundOwner = args.Actor;

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        var previousPatron = progression.AttunedPatron;
        var firstAttunement = progression.AttunedPatron == WH40KChaosPatron.None;

        if (progression.PatronSelectionLocked &&
            progression.AttunedPatron == args.Patron &&
            !progression.AllowPatronSwitch)
        {
            PopupCaution(
                args.Actor,
                "w40k-cg-already-attuned",
                ("patron", Loc.GetString(GetPatronLocKey(progression.AttunedPatron))));
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronSelection, args.Actor);
            return;
        }

        if (progression.PatronSelectionLocked &&
            progression.AttunedPatron != WH40KChaosPatron.None &&
            progression.AttunedPatron != args.Patron &&
            !progression.AllowPatronSwitch)
        {
            PopupCaution(args.Actor, "w40k-cg-switch-blocked");
            UpdatePatronSelectorUi(ent.Owner, progression);
            return;
        }

        ApplyPatronSelection(ent, args.Actor, args.Patron, progression);
    }

    internal void ApplyPatronSelection(
        Entity<WH40KChaosSkrizhalComponent> ent,
        EntityUid actor,
        WH40KChaosPatron patron,
        WH40KChaosGiftProgressionComponent progression,
        bool updateUi = true)
    {
        var previousPatron = progression.AttunedPatron;
        var firstAttunement = progression.AttunedPatron == WH40KChaosPatron.None;

        progression.AttunedPatron = patron;
        progression.PatronSoulOfferCount = GetPatronSoulCount(patron);
        progression.PatronSelectionLocked = true;
        progression.StarterSkrizhalIssued = true;
        progression.BoundSkrizhal = ent.Owner;
        if (firstAttunement &&
            _players.TryGetSessionByEntity(actor, out ICommonSession? session) &&
            TryGetPatronAttunementStatKey(patron, out var statKey))
        {
            _stats.Record(session.UserId, statKey);
        }

        if (firstAttunement || previousPatron != patron)
        {
            _cult.RegisterLeadershipCandidate(actor, progression);
            ResetGiftUnlockState(progression);
            ResetKhorneUpgradeState(progression);
        }

        ApplyPatronProfile(ent.Comp, patron);
        progression.AttunementXpMultiplier = MathF.Max(1f, ent.Comp.AttunementXpMultiplier);

        if (ent.Comp.AttunementInstabilityGain > 0f)
            AddInstability(actor, ent.Comp.AttunementInstabilityGain);

        _cult.AttachMemberToCult(actor, progression, previousPatron);
        Dirty(actor, progression);

        if (!updateUi)
            return;

        _ui.CloseUserUis<WH40KChaosSkrizhalUiKey>(actor);
        var activeSkrizhal = EnsurePatronSkrizhalVariant((ent.Owner, ent.Comp), actor, progression);
        RefreshAllOpenPatronSelectorUis();

        PopupSuccess(
            actor,
            "w40k-cg-attuned",
            ("patron", Loc.GetString(GetPatronLocKey(patron))));

        OpenProgressionUiForUser(activeSkrizhal, actor, progression);
    }

    private void OnSelectPrimaryGift(Entity<WH40KChaosSkrizhalComponent> ent, ref WH40KChaosSkrizhalSelectPrimaryGiftMessage args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (!_cult.IsEffectiveLeader(args.Actor, progression))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-leader-only");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            PopupCaution(args.Actor, "w40k-ch-popup-attunement-required");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (!IsValidGiftSlot(args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-invalid-slot");
            return;
        }

        if (progression.PrimaryGiftSlot != 0)
        {
            PopupCaution(
                args.Actor,
                "w40k-ch-popup-primary-already-set",
                ("slot", progression.PrimaryGiftSlot));
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        progression.PrimaryGiftSlot = args.GiftSlot;
        SetGiftSlotUnlocked(progression, args.GiftSlot, true);
        _cult.CaptureSharedProgression(args.Actor, progression);

        PopupSuccess(
            args.Actor,
            "w40k-ch-popup-primary-selected",
            ("slot", args.GiftSlot));

        UpdatePatronBranchUi(ent.Owner, progression);
    }

    private void OnUnlockGift(Entity<WH40KChaosSkrizhalComponent> ent, ref WH40KChaosSkrizhalUnlockGiftMessage args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (!_cult.IsEffectiveLeader(args.Actor, progression))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-leader-only");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            PopupCaution(args.Actor, "w40k-ch-popup-attunement-required");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (!IsValidGiftSlot(args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-invalid-slot");
            return;
        }

        if (progression.PrimaryGiftSlot == 0)
        {
            PopupCaution(args.Actor, "w40k-ch-popup-primary-required");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (progression.PrimaryGiftSlot == args.GiftSlot)
        {
            PopupCaution(args.Actor, "w40k-ch-popup-primary-cannot-purchase");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (IsGiftSlotUnlocked(progression, args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-already-unlocked");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        var cost = Math.Max(1, progression.GiftUnlockCost);
        if (progression.DevelopmentPoints < cost)
        {
            PopupCaution(
                args.Actor,
                "w40k-ch-popup-not-enough-points",
                ("cost", cost),
                ("points", progression.DevelopmentPoints));
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        progression.DevelopmentPoints -= cost;
        SetGiftSlotUnlocked(progression, args.GiftSlot, true);
        _cult.CaptureSharedProgression(args.Actor, progression);

        PopupSuccess(
            args.Actor,
            "w40k-ch-popup-unlocked",
            ("slot", args.GiftSlot),
            ("cost", cost));

        UpdatePatronBranchUi(ent.Owner, progression);
    }

    private void OnUpgradeTier(Entity<WH40KChaosSkrizhalComponent> ent, ref WH40KChaosSkrizhalUpgradeTierMessage args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (!_cult.IsEffectiveLeader(args.Actor, progression))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-leader-only");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!HasTierUpgradeRuntime(progression.AttunedPatron))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-runtime-unavailable");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (args.GiftSlot == (int) WH40KChaosGiftUpgradeSlot.Passive &&
            !HasPassiveUpgradeRuntime(progression.AttunedPatron))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-passive-runtime-unavailable");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!IsValidUpgradeSlot(args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-invalid-slot");
            return;
        }

        if (!IsUpgradeSlotOpenForProgression(progression, args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-slot-locked");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!IsValidUpgradeTier(args.Tier))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-invalid-tier");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        var currentTier = GetUpgradeTier(progression, args.GiftSlot, args.Path);
        if (args.Tier != currentTier + 1)
        {
            PopupCaution(
                args.Actor,
                "w40k-ch-popup-upgrade-order",
                ("current", currentTier),
                ("requested", args.Tier));
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        var cost = ChaosUpgradeTierCost;
        if (progression.DevelopmentPoints < cost)
        {
            PopupCaution(
                args.Actor,
                "w40k-ch-popup-not-enough-points",
                ("cost", cost),
                ("points", progression.DevelopmentPoints));
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        progression.DevelopmentPoints -= cost;
        SetUpgradeTier(progression, args.GiftSlot, args.Path, (byte) args.Tier);
        _cult.CaptureSharedProgression(args.Actor, progression);

        PopupSuccess(
            args.Actor,
            "w40k-ch-popup-upgrade-success",
            ("slot", args.GiftSlot),
            ("tier", args.Tier));

        UpdatePatronBranchUi(ent.Owner, progression);
    }

    private void OnUnlockEx(Entity<WH40KChaosSkrizhalComponent> ent, ref WH40KChaosSkrizhalUnlockExMessage args)
    {
        if (TryEnforceCatastropheLockdown(ent, args.Actor))
            return;

        if (!HasComp<WH40KChaosGiftRoleComponent>(args.Actor))
        {
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        if (ent.Comp.RestrictToBoundOwner &&
            ent.Comp.BoundOwner is { } boundOwner &&
            boundOwner != args.Actor)
        {
            PopupCaution(args.Actor, "w40k-cg-owner-mismatch");
            _ui.CloseUi(ent.Owner, WH40KChaosSkrizhalUiKey.PatronBranch, args.Actor);
            return;
        }

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(args.Actor);
        if (!_cult.IsEffectiveLeader(args.Actor, progression))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-leader-only");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!HasTierUpgradeRuntime(progression.AttunedPatron))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-runtime-unavailable");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (args.GiftSlot == (int) WH40KChaosGiftUpgradeSlot.Passive &&
            !HasPassiveUpgradeRuntime(progression.AttunedPatron))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-passive-runtime-unavailable");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!IsValidUpgradeSlot(args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-invalid-slot");
            return;
        }

        if (!IsUpgradeSlotOpenForProgression(progression, args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-slot-locked");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (IsUpgradeExUnlocked(progression, args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-ex-already");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        if (!HasMaxedUpgradePaths(progression, args.GiftSlot))
        {
            PopupCaution(args.Actor, "w40k-ch-popup-upgrade-ex-prereq");
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        var cost = ChaosUpgradeExCost;
        if (progression.DevelopmentPoints < cost)
        {
            PopupCaution(
                args.Actor,
                "w40k-ch-popup-not-enough-points",
                ("cost", cost),
                ("points", progression.DevelopmentPoints));
            UpdatePatronBranchUi(ent.Owner, progression);
            return;
        }

        progression.DevelopmentPoints -= cost;
        SetUpgradeExUnlocked(progression, args.GiftSlot, true);
        _cult.CaptureSharedProgression(args.Actor, progression);

        PopupSuccess(
            args.Actor,
            "w40k-ch-popup-upgrade-ex-success",
            ("slot", args.GiftSlot));

        UpdatePatronBranchUi(ent.Owner, progression);
    }

    private void OnAltarGetVerbs(Entity<WH40KChaosAltarComponent> altar, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<WH40KChaosGiftRoleComponent>(args.User))
            return;

        var user = args.User;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("wh40k-chaos-altar-verb-sacrifice"),
            Act = () =>
            {
                if (!TryGetAltarInteractionContext(altar, user, out var progression))
                    return;

                if (TryPerformSoulRitual(altar, user, progression))
                    return;

                PopupCaution(user, "w40k-cg-soul-ritual-no-corpses");
            }
        });
    }

    private void OnAltarInteractHand(Entity<WH40KChaosAltarComponent> altar, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetAltarInteractionContext(altar, args.User, out var progression))
            return;

        args.Handled = true;

        if (TryPerformSoulRitual(altar, args.User, progression))
            return;

        PopupCaution(args.User, "w40k-cg-soul-ritual-no-corpses");
    }

    private void OnAltarInteractUsing(Entity<WH40KChaosAltarComponent> altar, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetAltarInteractionContext(altar, args.User, out var progression))
            return;

        args.Handled = true;

        if (TryPerformSoulRitual(altar, args.User, progression))
            return;

        PopupCaution(args.User, "w40k-cg-soul-ritual-no-corpses");
    }

    private bool TryGetAltarInteractionContext(
        Entity<WH40KChaosAltarComponent> altar,
        EntityUid user,
        out WH40KChaosGiftProgressionComponent progression)
    {
        progression = default!;

        if (!HasComp<WH40KChaosGiftRoleComponent>(user))
            return false;

        progression = EnsureComp<WH40KChaosGiftProgressionComponent>(user);

        if (altar.Comp.RequireAttunement && progression.AttunedPatron == WH40KChaosPatron.None)
        {
            PopupCaution(user, "w40k-cg-attunement-required");
            return false;
        }

        var now = _timing.CurTime;
        if (now < progression.NextSacrificeAt)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((progression.NextSacrificeAt - now).TotalSeconds));
            PopupCaution(user, "w40k-cg-sacrifice-cooldown", ("seconds", seconds));
            return false;
        }

        return true;
    }

    private bool TryPerformSoulRitual(
        Entity<WH40KChaosAltarComponent> altar,
        EntityUid user,
        WH40KChaosGiftProgressionComponent progression)
    {
        if (progression.AttunedPatron == WH40KChaosPatron.None)
            return false;

        var soulsConsumed = TryConsumeNearbySouls(altar, user);
        if (soulsConsumed <= 0)
            return false;

        ApplySoulRitualRewards(user, progression.AttunedPatron, soulsConsumed, altar.Comp);
        ApplyAltarRitualAftermath(user, progression, altar.Comp);
        return true;
    }

    private int TryConsumeNearbySouls(Entity<WH40KChaosAltarComponent> altar, EntityUid user)
    {
        _nearbyEntities.Clear();
        _soulTargets.Clear();

        var range = Math.Max(0.1f, altar.Comp.SoulHarvestRange);
        _lookup.GetEntitiesInRange(
            Transform(altar.Owner).Coordinates,
            range,
            _nearbyEntities,
            LookupFlags.Dynamic | LookupFlags.Uncontained);

        foreach (var candidate in _nearbyEntities)
        {
            if (candidate == altar.Owner || candidate == user || TerminatingOrDeleted(candidate))
                continue;

            if (!TryComp<MobStateComponent>(candidate, out var mobState))
                continue;

            if (!_mobState.IsDead(candidate, mobState))
                continue;

            _soulTargets.Add(candidate);
        }

        if (_soulTargets.Count == 0)
            return 0;

        var consumed = 0;
        foreach (var target in _soulTargets)
        {
            if (TerminatingOrDeleted(target))
                continue;

            IgniteAndDustSoulTarget(target, user);
            consumed++;
        }

        return consumed;
    }

    private void IgniteAndDustSoulTarget(EntityUid target, EntityUid user)
    {
        var coords = Transform(target).Coordinates;

        if (TryComp<FlammableComponent>(target, out var flammable))
        {
            flammable.FireStacks = flammable.MaximumFireStacks;
            Dirty(target, flammable);
            _flammableSystem.Ignite(target, user);
        }

        QueueDel(target);
        Spawn("Ash", coords);
    }

    private void ApplySoulRitualRewards(
        EntityUid actor,
        WH40KChaosPatron actorPatron,
        int soulsConsumed,
        WH40KChaosAltarComponent altar)
    {
        var samePatronXp = ComputeScaledSamePatronSoulReward(
            soulsConsumed,
            Math.Max(0f, altar.SoulXpSamePatron));
        var otherPatronXp = Math.Max(0f, altar.SoulXpOtherPatron) * soulsConsumed;
        _cult.AddCultSoulCount(actorPatron, soulsConsumed);

        if (samePatronXp > 0f)
            _cult.AddCultXp(actorPatron, samePatronXp);

        if (otherPatronXp > 0f)
        {
            var patrons = new[]
            {
                WH40KChaosPatron.Khorne,
                WH40KChaosPatron.Nurgle,
                WH40KChaosPatron.Slaanesh,
                WH40KChaosPatron.Tzeentch,
            };

            foreach (var patron in patrons)
            {
                if (patron == actorPatron || !_cult.HasCultMembers(patron))
                    continue;

                _cult.AddCultXp(patron, otherPatronXp);
            }
        }

        PopupSuccess(
            actor,
            "w40k-cg-soul-ritual-success",
            ("souls", soulsConsumed),
            ("patron", Loc.GetString(GetPatronLocKey(actorPatron))),
            ("sameXp", MathF.Round(samePatronXp, 1)),
            ("otherXp", MathF.Round(otherPatronXp, 1)));
    }

    private static float ComputeScaledSamePatronSoulReward(int soulsConsumed, float baseXpPerSoul)
    {
        if (soulsConsumed <= 0 || baseXpPerSoul <= 0f)
            return 0f;

        var total = 0f;
        for (var index = 1; index <= soulsConsumed; index++)
        {
            // 1..5 souls: base XP per soul (default 50),
            // each next 5 souls reduce per-soul reward by 10, down to minimum 10.
            var reductionStep = (index - 1) / 5;
            var perSoul = MathF.Max(10f, baseXpPerSoul - reductionStep * 10f);
            total += perSoul;
        }

        return total;
    }

    private void ApplyAltarRitualAftermath(
        EntityUid user,
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosAltarComponent altar)
    {
        var now = _timing.CurTime;
        progression.NextSacrificeAt = now + altar.SacrificeCooldown;

        if (altar.RitualBoostMultiplier > 1f && altar.RitualBoostDuration > TimeSpan.Zero)
        {
            progression.RitualBonusMultiplier = MathF.Max(
                progression.RitualBonusMultiplier,
                altar.RitualBoostMultiplier);

            var extensionStart = progression.RitualBonusExpiresAt > now
                ? progression.RitualBonusExpiresAt
                : now;

            progression.RitualBonusExpiresAt = extensionStart + altar.RitualBoostDuration;
        }

        if (altar.WarpChargeRestore > 0f)
            RestoreWarpCharge(user, altar.WarpChargeRestore);

        if (altar.InstabilityGain > 0f)
            AddInstability(user, altar.InstabilityGain);

        Dirty(user, progression);
    }

    private void GainProjectedProgressionXp(EntityUid uid, WH40KChaosGiftProgressionComponent progression, float amount)
    {
        if (progression.AttunedPatron == WH40KChaosPatron.None)
        {
            GainProgressionXp(uid, progression, amount);
            return;
        }

        _cult.AddCultXp(progression.AttunedPatron, amount);
    }

    private void GainProgressionXp(EntityUid uid, WH40KChaosGiftProgressionComponent progression, float amount)
    {
        if (amount <= 0f || progression.MaxLevel <= 0)
            return;

        progression.TotalXp += amount;

        if (progression.Level >= progression.MaxLevel)
        {
            progression.LevelXp = 0f;
            Dirty(uid, progression);
            return;
        }

        progression.LevelXp += amount;
        var levelUps = 0;

        while (progression.Level < progression.MaxLevel)
        {
            var needed = GetXpRequiredForNextLevel(progression);
            if (progression.LevelXp + 0.0001f < needed)
                break;

            progression.LevelXp -= needed;
            progression.Level++;
            levelUps++;
        }

        if (levelUps > 0 && progression.PointsPerLevel > 0)
            progression.DevelopmentPoints += levelUps * progression.PointsPerLevel;

        if (levelUps > 0)
            SyncChaosWarpRegen(uid, progression);

        if (progression.Level >= progression.MaxLevel)
            progression.LevelXp = 0f;

        Dirty(uid, progression);
        TryUpdateBoundProgressionUi(uid, progression);
    }

    private static float GetXpRequiredForNextLevel(WH40KChaosGiftProgressionComponent progression)
    {
        var currentLevel = Math.Clamp(progression.Level, 1, progression.MaxLevel);
        var xp = progression.XpPerLevelStep * currentLevel;
        return MathF.Max(1f, xp);
    }

    private void SyncChaosWarpRegen(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        if (!TryComp<WH40KWarpResourceComponent>(uid, out var warp))
            return;

        var expectedRegen = Math.Clamp(progression.Level, 1, ChaosMaxLevel) * ChaosWarpRegenPerLevel;
        if (Math.Abs(warp.RegenPerSecond - expectedRegen) <= 0.0001f)
            return;

        warp.RegenPerSecond = expectedRegen;
        Dirty(uid, warp);
    }

    private void RestoreWarpCharge(EntityUid uid, float amount)
    {
        if (!TryComp<WH40KWarpResourceComponent>(uid, out var warp) || amount <= 0f)
            return;

        var next = Math.Clamp(warp.CurrentCharge + amount, 0f, warp.MaxCharge);
        if (next <= warp.CurrentCharge)
            return;

        warp.CurrentCharge = next;
        Dirty(uid, warp);
    }

    private void AddInstability(EntityUid uid, float amount)
    {
        if (amount <= 0f)
            return;

        RaiseLocalEvent(new WH40KWarpInstabilityContributionEvent(uid, amount, "chaos.progression"));
    }

    private void UpdatePatronSelectorUi(EntityUid skrizhal, WH40KChaosGiftProgressionComponent progression)
    {
        var locked = progression.PatronSelectionLocked &&
                     progression.AttunedPatron != WH40KChaosPatron.None &&
                     !progression.AllowPatronSwitch;

        var state = new WH40KChaosSkrizhalPatronSelectorBuiState(
            locked,
            progression.AttunedPatron,
            ResolvePatronLeaderName(WH40KChaosPatron.Khorne),
            ResolvePatronLeaderName(WH40KChaosPatron.Nurgle),
            ResolvePatronLeaderName(WH40KChaosPatron.Slaanesh),
            ResolvePatronLeaderName(WH40KChaosPatron.Tzeentch));

        _ui.SetUiState(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, state);
    }

    private string ResolvePatronLeaderName(WH40KChaosPatron patron)
    {
        var leader = _cult.ResolveActiveLeader(patron);

        return leader is { } leaderUid
            ? Name(leaderUid)
            : Loc.GetString("wh40k-chaos-selector-no-leader");
    }

    private void RefreshAllOpenPatronSelectorUis()
    {
        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent>();
        while (query.MoveNext(out var uid, out var progression))
        {
            TryUpdateBoundSelectorUi(uid, progression);
        }
    }

    private void TryUpdateBoundSelectorUi(EntityUid owner, WH40KChaosGiftProgressionComponent progression)
    {
        if (progression.BoundSkrizhal is not { } skrizhal || TerminatingOrDeleted(skrizhal))
            return;

        if (!_ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronSelection, owner))
            return;

        UpdatePatronSelectorUi(skrizhal, progression);
    }

    private void TryUpdateBoundProgressionUi(EntityUid owner, WH40KChaosGiftProgressionComponent progression)
    {
        if (progression.BoundSkrizhal is not { } skrizhal || TerminatingOrDeleted(skrizhal))
            return;

        var branchOpen = _ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, owner);
        var cultistOpen = _ui.IsUiOpen(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, owner);

        if (!branchOpen && !cultistOpen)
            return;

        if (ShouldOpenLeaderUi(owner, progression))
        {
            if (cultistOpen)
            {
                _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, owner);
                OpenBranchUiForUser(skrizhal, owner, progression.AttunedPatron);
            }

            UpdatePatronBranchUi(skrizhal, progression);
            return;
        }

        if (branchOpen)
        {
            _ui.CloseUi(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, owner);
            OpenCultistUiForUser(skrizhal, owner, progression.AttunedPatron);
        }

        UpdatePatronCultistUi(skrizhal, progression);
    }

    private (bool HasActiveLeader, string ActiveLeaderName, bool AwaitingLeaderSuccessor) ResolveActiveLeaderMetadata(WH40KChaosPatron patron)
    {
        var leaderState = _cult.ResolveLeaderState(patron);
        var activeLeader = leaderState.ActiveLeader;
        var hasActiveLeader = activeLeader is not null;
        var activeLeaderName = Loc.GetString("wh40k-chaos-selector-no-leader");

        if (activeLeader is { } leaderUid)
            activeLeaderName = Name(leaderUid);

        return (hasActiveLeader, activeLeaderName, leaderState.AwaitingLeaderSuccessor);
    }

    private void UpdatePatronBranchUi(EntityUid skrizhal, WH40KChaosGiftProgressionComponent progression)
    {
        var nextLevelXp = progression.Level >= progression.MaxLevel
            ? 0f
            : GetXpRequiredForNextLevel(progression);
        var passiveXpPerTick = GetPassiveXpPerTick(progression);
        var passiveIntervalSeconds = Math.Max(1, (int) Math.Round(progression.PassiveXpInterval.TotalSeconds));
        var activeLeader = ResolveActiveLeaderMetadata(progression.AttunedPatron);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);
        var giftThreeExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 3);
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);

        var state = new WH40KChaosSkrizhalPatronBranchBuiState(
            progression.AttunedPatron,
            progression.Level,
            progression.MaxLevel,
            progression.LevelXp,
            nextLevelXp,
            progression.DevelopmentPoints,
            progression.EffectiveLeader,
            activeLeader.HasActiveLeader,
            activeLeader.ActiveLeaderName,
            activeLeader.AwaitingLeaderSuccessor,
            progression.PrimaryGiftSlot,
            progression.GiftSlotOneUnlocked,
            progression.GiftSlotTwoUnlocked,
            progression.GiftSlotThreeUnlocked,
            Math.Max(1, progression.GiftUnlockCost),
            Math.Max(0, progression.PatronSoulOfferCount),
            passiveXpPerTick,
            passiveIntervalSeconds,
            progression.KhorneGiftOnePowerTier,
            progression.KhorneGiftOneCooldownTier,
            progression.KhorneGiftOneUtilityTier,
            giftOneExUnlocked,
            progression.KhorneGiftTwoPowerTier,
            progression.KhorneGiftTwoCooldownTier,
            progression.KhorneGiftTwoUtilityTier,
            giftTwoExUnlocked,
            progression.KhorneGiftThreePowerTier,
            progression.KhorneGiftThreeCooldownTier,
            progression.KhorneGiftThreeUtilityTier,
            giftThreeExUnlocked,
            progression.KhornePassiveSpeedTier,
            progression.KhornePassiveHealthTier,
            progression.KhornePassiveMeleeTier,
            passiveExUnlocked);

        _ui.SetUiState(skrizhal, WH40KChaosSkrizhalUiKey.PatronBranch, state);
    }

    private void UpdatePatronCultistUi(EntityUid skrizhal, WH40KChaosGiftProgressionComponent progression)
    {
        var nextLevelXp = progression.Level >= progression.MaxLevel
            ? 0f
            : GetXpRequiredForNextLevel(progression);
        var passiveXpPerTick = GetPassiveXpPerTick(progression);
        var passiveIntervalSeconds = Math.Max(1, (int) Math.Round(progression.PassiveXpInterval.TotalSeconds));
        var activeLeader = ResolveActiveLeaderMetadata(progression.AttunedPatron);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);
        var giftThreeExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 3);
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);

        var state = new WH40KChaosSkrizhalCultistBuiState(
            progression.AttunedPatron,
            progression.Level,
            progression.MaxLevel,
            progression.LevelXp,
            nextLevelXp,
            progression.DevelopmentPoints,
            activeLeader.HasActiveLeader,
            activeLeader.ActiveLeaderName,
            activeLeader.AwaitingLeaderSuccessor,
            progression.PrimaryGiftSlot,
            progression.GiftSlotOneUnlocked,
            progression.GiftSlotTwoUnlocked,
            progression.GiftSlotThreeUnlocked,
            Math.Max(1, progression.GiftUnlockCost),
            Math.Max(0, progression.PatronSoulOfferCount),
            passiveXpPerTick,
            passiveIntervalSeconds,
            progression.KhorneGiftOnePowerTier,
            progression.KhorneGiftOneCooldownTier,
            progression.KhorneGiftOneUtilityTier,
            giftOneExUnlocked,
            progression.KhorneGiftTwoPowerTier,
            progression.KhorneGiftTwoCooldownTier,
            progression.KhorneGiftTwoUtilityTier,
            giftTwoExUnlocked,
            progression.KhorneGiftThreePowerTier,
            progression.KhorneGiftThreeCooldownTier,
            progression.KhorneGiftThreeUtilityTier,
            giftThreeExUnlocked,
            progression.KhornePassiveSpeedTier,
            progression.KhornePassiveHealthTier,
            progression.KhornePassiveMeleeTier,
            passiveExUnlocked);

        _ui.SetUiState(skrizhal, WH40KChaosSkrizhalUiKey.PatronCultistInfo, state);
    }

    private static void ResetGiftUnlockState(WH40KChaosGiftProgressionComponent progression)
    {
        progression.PrimaryGiftSlot = 0;
        progression.GiftSlotOneUnlocked = false;
        progression.GiftSlotTwoUnlocked = false;
        progression.GiftSlotThreeUnlocked = false;
    }

    private static void ResetKhorneUpgradeState(WH40KChaosGiftProgressionComponent progression)
    {
        progression.KhorneGiftOnePowerTier = 0;
        progression.KhorneGiftOneCooldownTier = 0;
        progression.KhorneGiftOneUtilityTier = 0;
        progression.KhorneGiftOneExUnlocked = false;
        progression.KhorneGiftTwoPowerTier = 0;
        progression.KhorneGiftTwoCooldownTier = 0;
        progression.KhorneGiftTwoUtilityTier = 0;
        progression.KhorneGiftTwoExUnlocked = false;
        progression.KhorneGiftThreePowerTier = 0;
        progression.KhorneGiftThreeCooldownTier = 0;
        progression.KhorneGiftThreeUtilityTier = 0;
        progression.KhorneGiftThreeExUnlocked = false;
        progression.KhornePassiveSpeedTier = 0;
        progression.KhornePassiveHealthTier = 0;
        progression.KhornePassiveMeleeTier = 0;
        progression.KhornePassiveExUnlocked = false;
    }

    private static bool SanitizeKhorneUpgradeState(WH40KChaosGiftProgressionComponent progression)
    {
        var changed = false;

        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftOnePowerTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftOneCooldownTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftOneUtilityTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftTwoPowerTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftTwoCooldownTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftTwoUtilityTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftThreePowerTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftThreeCooldownTier);
        changed |= SanitizeUpgradeTier(ref progression.KhorneGiftThreeUtilityTier);
        changed |= SanitizeUpgradeTier(ref progression.KhornePassiveSpeedTier);
        changed |= SanitizeUpgradeTier(ref progression.KhornePassiveHealthTier);
        changed |= SanitizeUpgradeTier(ref progression.KhornePassiveMeleeTier);

        if (progression.KhorneGiftOneExUnlocked &&
            !HasMaxedUpgradePaths(progression, (int) WH40KChaosGiftUpgradeSlot.GiftOne))
        {
            progression.KhorneGiftOneExUnlocked = false;
            changed = true;
        }

        if (progression.KhorneGiftTwoExUnlocked &&
            !HasMaxedUpgradePaths(progression, (int) WH40KChaosGiftUpgradeSlot.GiftTwo))
        {
            progression.KhorneGiftTwoExUnlocked = false;
            changed = true;
        }

        if (progression.KhorneGiftThreeExUnlocked &&
            !HasMaxedUpgradePaths(progression, (int) WH40KChaosGiftUpgradeSlot.GiftThree))
        {
            progression.KhorneGiftThreeExUnlocked = false;
            changed = true;
        }

        if (progression.KhornePassiveExUnlocked &&
            !HasMaxedUpgradePaths(progression, (int) WH40KChaosGiftUpgradeSlot.Passive))
        {
            progression.KhornePassiveExUnlocked = false;
            changed = true;
        }

        return changed;
    }

    private static bool SanitizeUpgradeTier(ref byte tier)
    {
        var clamped = (byte) Math.Clamp(tier, (byte) 0, (byte) 3);
        if (clamped == tier)
            return false;

        tier = clamped;
        return true;
    }

    private static bool IsValidGiftSlot(int slot)
    {
        return slot is >= 1 and <= 3;
    }

    private static bool IsValidUpgradeSlot(int slot)
    {
        return slot is >= 1 and <= 4;
    }

    private static bool IsValidUpgradeTier(int tier)
    {
        return tier is >= 1 and <= 3;
    }

    private static bool IsGiftSlotUnlocked(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        return slot switch
        {
            1 => progression.GiftSlotOneUnlocked,
            2 => progression.GiftSlotTwoUnlocked,
            3 => progression.GiftSlotThreeUnlocked,
            _ => false,
        };
    }

    private static void SetGiftSlotUnlocked(WH40KChaosGiftProgressionComponent progression, int slot, bool value)
    {
        switch (slot)
        {
            case 1:
                progression.GiftSlotOneUnlocked = value;
                break;
            case 2:
                progression.GiftSlotTwoUnlocked = value;
                break;
            case 3:
                progression.GiftSlotThreeUnlocked = value;
                break;
        }
    }

    private static bool IsUpgradeSlotOpenForProgression(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        if (slot == (int) WH40KChaosGiftUpgradeSlot.Passive)
            return true;

        return IsGiftSlotUnlocked(progression, slot);
    }

    private static byte GetUpgradeTier(
        WH40KChaosGiftProgressionComponent progression,
        int slot,
        WH40KChaosGiftUpgradePath path)
    {
        return (slot, path) switch
        {
            ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Power) => progression.KhorneGiftOnePowerTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Cooldown) => progression.KhorneGiftOneCooldownTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Utility) => progression.KhorneGiftOneUtilityTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Power) => progression.KhorneGiftTwoPowerTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Cooldown) => progression.KhorneGiftTwoCooldownTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Utility) => progression.KhorneGiftTwoUtilityTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Power) => progression.KhorneGiftThreePowerTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Cooldown) => progression.KhorneGiftThreeCooldownTier,
            ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Utility) => progression.KhorneGiftThreeUtilityTier,
            ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Power) => progression.KhornePassiveSpeedTier,
            ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Cooldown) => progression.KhornePassiveHealthTier,
            ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Utility) => progression.KhornePassiveMeleeTier,
            _ => 0,
        };
    }

    private static void SetUpgradeTier(
        WH40KChaosGiftProgressionComponent progression,
        int slot,
        WH40KChaosGiftUpgradePath path,
        byte tier)
    {
        switch ((slot, path))
        {
            case ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Power):
                progression.KhorneGiftOnePowerTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Cooldown):
                progression.KhorneGiftOneCooldownTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftOne, WH40KChaosGiftUpgradePath.Utility):
                progression.KhorneGiftOneUtilityTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Power):
                progression.KhorneGiftTwoPowerTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Cooldown):
                progression.KhorneGiftTwoCooldownTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftTwo, WH40KChaosGiftUpgradePath.Utility):
                progression.KhorneGiftTwoUtilityTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Power):
                progression.KhorneGiftThreePowerTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Cooldown):
                progression.KhorneGiftThreeCooldownTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.GiftThree, WH40KChaosGiftUpgradePath.Utility):
                progression.KhorneGiftThreeUtilityTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Power):
                progression.KhornePassiveSpeedTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Cooldown):
                progression.KhornePassiveHealthTier = tier;
                break;
            case ((int) WH40KChaosGiftUpgradeSlot.Passive, WH40KChaosGiftUpgradePath.Utility):
                progression.KhornePassiveMeleeTier = tier;
                break;
        }
    }

    private static bool IsUpgradeExUnlocked(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        return slot switch
        {
            (int) WH40KChaosGiftUpgradeSlot.GiftOne => progression.KhorneGiftOneExUnlocked,
            (int) WH40KChaosGiftUpgradeSlot.GiftTwo => progression.KhorneGiftTwoExUnlocked,
            (int) WH40KChaosGiftUpgradeSlot.GiftThree => progression.KhorneGiftThreeExUnlocked,
            (int) WH40KChaosGiftUpgradeSlot.Passive => progression.KhornePassiveExUnlocked,
            _ => false,
        };
    }

    private static void SetUpgradeExUnlocked(
        WH40KChaosGiftProgressionComponent progression,
        int slot,
        bool value)
    {
        switch (slot)
        {
            case (int) WH40KChaosGiftUpgradeSlot.GiftOne:
                progression.KhorneGiftOneExUnlocked = value;
                break;
            case (int) WH40KChaosGiftUpgradeSlot.GiftTwo:
                progression.KhorneGiftTwoExUnlocked = value;
                break;
            case (int) WH40KChaosGiftUpgradeSlot.GiftThree:
                progression.KhorneGiftThreeExUnlocked = value;
                break;
            case (int) WH40KChaosGiftUpgradeSlot.Passive:
                progression.KhornePassiveExUnlocked = value;
                break;
        }
    }

    private static bool HasMaxedUpgradePaths(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        return GetUpgradeTier(progression, slot, WH40KChaosGiftUpgradePath.Power) >= 3 &&
               GetUpgradeTier(progression, slot, WH40KChaosGiftUpgradePath.Cooldown) >= 3 &&
               GetUpgradeTier(progression, slot, WH40KChaosGiftUpgradePath.Utility) >= 3;
    }

    private int GetPatronSoulCount(WH40KChaosPatron patron)
    {
        return _cult.GetCultSoulCount(patron);
    }

    private int AddPatronSoulCount(WH40KChaosPatron patron, int amount)
    {
        return _cult.AddCultSoulCount(patron, amount);
    }

    private EntityUid EnsurePatronSkrizhalVariant(
        Entity<WH40KChaosSkrizhalComponent> skrizhal,
        EntityUid user,
        WH40KChaosGiftProgressionComponent progression)
    {
        if (!TryGetPatronSkrizhalPrototype(progression.AttunedPatron, out var prototype))
            return skrizhal.Owner;

        var currentPrototype = MetaData(skrizhal.Owner).EntityPrototype?.ID;
        if (string.Equals(currentPrototype, prototype, StringComparison.Ordinal))
        {
            progression.BoundSkrizhal = skrizhal.Owner;
            return skrizhal.Owner;
        }

        var replacement = Spawn(prototype, Transform(skrizhal.Owner).Coordinates);
        if (!TryComp(replacement, out WH40KChaosSkrizhalComponent? replacementComp))
            return skrizhal.Owner;

        replacementComp.BoundOwner = skrizhal.Comp.BoundOwner;
        replacementComp.BindOnFirstUse = skrizhal.Comp.BindOnFirstUse;
        replacementComp.RestrictToBoundOwner = skrizhal.Comp.RestrictToBoundOwner;
        replacementComp.Patron = skrizhal.Comp.Patron;
        replacementComp.AttunementXpReward = skrizhal.Comp.AttunementXpReward;
        replacementComp.AttunementXpMultiplier = skrizhal.Comp.AttunementXpMultiplier;
        replacementComp.AttunementInstabilityGain = skrizhal.Comp.AttunementInstabilityGain;

        _hands.TryDrop(user, skrizhal.Owner, checkActionBlocker: false, doDropInteraction: false);

        var picked = _hands.TryPickupAnyHand(
            user,
            replacement,
            checkActionBlocker: false,
            animateUser: false,
            animate: false);

        if (!picked)
        {
            _hands.TryForcePickupAnyHand(
                user,
                replacement,
                checkActionBlocker: false);
        }

        progression.BoundSkrizhal = replacement;
        QueueDel(skrizhal.Owner);
        return replacement;
    }

    private static bool TryGetPatronSkrizhalPrototype(WH40KChaosPatron patron, out string prototype)
    {
        prototype = patron switch
        {
            WH40KChaosPatron.Khorne => KhorneSkrizhalPrototype,
            WH40KChaosPatron.Nurgle => NurgleSkrizhalPrototype,
            WH40KChaosPatron.Slaanesh => SlaaneshSkrizhalPrototype,
            WH40KChaosPatron.Tzeentch => TzeentchSkrizhalPrototype,
            WH40KChaosPatron.Undivided => ChaosSkrizhalPrototype,
            _ => string.Empty,
        };

        return !string.IsNullOrEmpty(prototype);
    }

    private static bool IsSelectablePatron(WH40KChaosPatron patron)
    {
        return patron is WH40KChaosPatron.Khorne or
               WH40KChaosPatron.Nurgle or
               WH40KChaosPatron.Slaanesh or
               WH40KChaosPatron.Tzeentch;
    }

    private static bool TryGetPatronAttunementStatKey(WH40KChaosPatron patron, out string statKey)
    {
        statKey = patron switch
        {
            WH40KChaosPatron.Khorne => WH40KPlayerStatKeys.ChaosPatronAttunementKhorne,
            WH40KChaosPatron.Nurgle => WH40KPlayerStatKeys.ChaosPatronAttunementNurgle,
            WH40KChaosPatron.Slaanesh => WH40KPlayerStatKeys.ChaosPatronAttunementSlaanesh,
            WH40KChaosPatron.Tzeentch => WH40KPlayerStatKeys.ChaosPatronAttunementTzeentch,
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(statKey);
    }

    private static bool HasTierUpgradeRuntime(WH40KChaosPatron patron)
    {
        return patron is WH40KChaosPatron.Khorne or
               WH40KChaosPatron.Nurgle or
               WH40KChaosPatron.Slaanesh or
               WH40KChaosPatron.Tzeentch;
    }

    private static bool HasPassiveUpgradeRuntime(WH40KChaosPatron patron)
    {
        return patron is WH40KChaosPatron.Khorne or
               WH40KChaosPatron.Nurgle or
               WH40KChaosPatron.Slaanesh;
    }

    private static void ApplyPatronProfile(WH40KChaosSkrizhalComponent skrizhal, WH40KChaosPatron patron)
    {
        skrizhal.Patron = patron;

        switch (patron)
        {
            case WH40KChaosPatron.Khorne:
                skrizhal.AttunementXpReward = 26f;
                skrizhal.AttunementXpMultiplier = 1.2f;
                skrizhal.AttunementInstabilityGain = 3f;
                break;
            case WH40KChaosPatron.Nurgle:
                skrizhal.AttunementXpReward = 24f;
                skrizhal.AttunementXpMultiplier = 1.18f;
                skrizhal.AttunementInstabilityGain = 2.8f;
                break;
            case WH40KChaosPatron.Slaanesh:
                skrizhal.AttunementXpReward = 25f;
                skrizhal.AttunementXpMultiplier = 1.22f;
                skrizhal.AttunementInstabilityGain = 3.2f;
                break;
            case WH40KChaosPatron.Tzeentch:
                skrizhal.AttunementXpReward = 27f;
                skrizhal.AttunementXpMultiplier = 1.25f;
                skrizhal.AttunementInstabilityGain = 3.5f;
                break;
            default:
                skrizhal.AttunementXpReward = 22f;
                skrizhal.AttunementXpMultiplier = 1.15f;
                skrizhal.AttunementInstabilityGain = 2.5f;
                break;
        }
    }

    private void PopupCaution(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.SmallCaution);
    }

    private void PopupSuccess(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.Small);
    }

    private static string GetPatronLocKey(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => "wh40k-chaos-patron-khorne",
            WH40KChaosPatron.Nurgle => "wh40k-chaos-patron-nurgle",
            WH40KChaosPatron.Slaanesh => "wh40k-chaos-patron-slaanesh",
            WH40KChaosPatron.Tzeentch => "wh40k-chaos-patron-tzeentch",
            _ => "wh40k-chaos-patron-undivided",
        };
    }
}
