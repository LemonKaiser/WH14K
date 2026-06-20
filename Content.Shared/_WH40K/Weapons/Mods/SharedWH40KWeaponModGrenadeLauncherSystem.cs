using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Weapons.Mods;

public abstract partial class SharedWH40KWeaponModGrenadeLauncherSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedWH40KWeaponModSystem _weaponMods = default!;
    [Dependency] private IGameTiming _timing = default!;

    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, MapInitEvent>(OnGrenadeLauncherMapInit);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, ComponentShutdown>(OnGrenadeLauncherShutdown);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, AfterAutoHandleStateEvent>(OnGrenadeLauncherAutoHandleState);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, WH40KToggleWeaponGrenadeLauncherActionEvent>(OnToggleGrenadeLauncherAction);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, EntGotRemovedFromContainerMessage>(OnGrenadeLauncherRemovedFromContainer);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, GotEquippedHandEvent>(OnGrenadeLauncherHeldStateChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, GotUnequippedHandEvent>(OnGrenadeLauncherHeldStateChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, HandSelectedEvent>(OnGrenadeLauncherHeldStateChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, HandDeselectedEvent>(OnGrenadeLauncherHeldStateChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, EntInsertedIntoContainerMessage>(OnGrenadeLauncherContainerChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, EntRemovedFromContainerMessage>(OnGrenadeLauncherContainerChanged);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, GunShotEvent>(OnGrenadeLauncherShot);
        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, ShotAttemptedEvent>(
            OnGrenadeLauncherShotAttempted,
            after: [typeof(SharedWieldableSystem)]);

        SubscribeLocalEvent<HandsComponent, GetActiveWeaponEvent>(OnGetActiveWeapon);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, ShotAttemptedEvent>(
            OnHostShotAttempted,
            after: [typeof(SharedWieldableSystem)]);
        SubscribeLocalEvent<WH40KWeaponModHostComponent, InteractUsingEvent>(OnHostInteractUsing);
    }

    private void OnGrenadeLauncherMapInit(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherShutdown(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, ref ComponentShutdown args)
    {
        if (TryGetHostedGun(ent.Owner, out var gunUid))
            SetHostedPresentation(gunUid, false, string.Empty, string.Empty, string.Empty);

        if (ent.Comp.ToggleActionEntity != null)
            _actions.RemoveAction(ent.Comp.ToggleActionEntity);
    }

    /// <summary>
    /// Server-authoritative Active state arrived on the client. The toggle action handler is
    /// gated behind IsFirstTimePredicted to avoid prediction-replay oscillation, so the client
    /// does not call SetActive on replays. This handler re-applies the presentation (name,
    /// combat reticle, appearance) from the replicated Active field so the UI stays in sync
    /// with the server without the client having to predict the toggle.
    /// </summary>
    private void OnGrenadeLauncherAutoHandleState(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_net.IsServer)
            return;

        RefreshHostedPresentation(ent);
        RefreshHostedMode(ent);
    }

    private void OnToggleGrenadeLauncherAction(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref WH40KToggleWeaponGrenadeLauncherActionEvent args)
    {
        if (args.Handled ||
            !_timing.IsFirstTimePredicted ||
            !TryGetHostedGun(ent.Owner, out var gunUid) ||
            !_hands.TryGetActiveItem(args.Performer, out var activeItem) ||
            activeItem != gunUid)
        {
            return;
        }

        SetActive(ent, !ent.Comp.Active);
        _audio.PlayPredicted(ent.Comp.ToggleSound, gunUid, args.Performer);
        args.Handled = true;
    }

    private void OnGrenadeLauncherRemovedFromContainer(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref EntGotRemovedFromContainerMessage args)
    {
        SetActive(ent, false);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherHeldStateChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref GotEquippedHandEvent args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherHeldStateChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref GotUnequippedHandEvent args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherHeldStateChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref HandSelectedEvent args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherHeldStateChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref HandDeselectedEvent args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherContainerChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref EntInsertedIntoContainerMessage args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherContainerChanged(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref EntRemovedFromContainerMessage args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherShot(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref GunShotEvent args)
    {
        RefreshHostedPresentation(ent);
        OnHostedContextChanged(ent);
    }

    private void OnGrenadeLauncherShotAttempted(
        Entity<WH40KWeaponModGrenadeLauncherComponent> ent,
        ref ShotAttemptedEvent args)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid))
        {
            args.Cancel();
            return;
        }

        if (TryComp<GunRequiresWieldComponent>(gunUid, out _) &&
            TryComp(gunUid, out WieldableComponent? wieldable) &&
            !wieldable.Wielded)
        {
            args.Cancel();
        }
    }

    private void OnHostShotAttempted(Entity<WH40KWeaponModHostComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled ||
            args.Used.Owner != ent.Owner ||
            args.Used.Comp.ShootCoordinates == null ||
            !TryGetActiveGrenadeLauncher(ent, out var launcherUid, out _, out var launcherGun))
        {
            return;
        }

        args.Cancel();
        _gun.AttemptShoot(
            args.User,
            (launcherUid, launcherGun),
            args.Used.Comp.ShootCoordinates.Value,
            args.Used.Comp.Target);
    }

    private void OnHostInteractUsing(Entity<WH40KWeaponModHostComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryGetInstalledGrenadeLauncher(ent, out var launcherUid, out _, out _, out var provider))
        {
            return;
        }

        if (_gun.TryBallisticInsert((launcherUid, provider), args.Used, args.User))
            args.Handled = true;
    }

    private void SetActive(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, bool active)
    {
        if (ent.Comp.Active == active)
            return;

        ent.Comp.Active = active;

        if (_net.IsServer)
            Dirty(ent.Owner, ent.Comp);

        if (ent.Comp.ToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);

        RefreshHostedPresentation(ent);
        RefreshHostedMode(ent);
    }

    protected virtual void OnHostedContextChanged(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryGetHoldingUser(gunUid, out var user) ||
            !_hands.TryGetActiveItem(user, out var activeItem) ||
            activeItem != gunUid)
        {
            ClearGrantedAction(ent);
            RefreshHostedMode(ent);
            return;
        }

        EnsureActionGranted(ent, user);
        RefreshHostedMode(ent);
    }

    protected virtual void OnActiveChanged(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
    }

    protected bool TryGetInstalledGrenadeLauncher(
        Entity<WH40KWeaponModHostComponent> hostEnt,
        out EntityUid modUid,
        out WH40KWeaponModGrenadeLauncherComponent grenadeLauncher,
        out GunComponent gun,
        out BallisticAmmoProviderComponent provider)
    {
        modUid = default;
        grenadeLauncher = default!;
        gun = default!;
        provider = default!;

        foreach (var slot in hostEnt.Comp.ModSlots.Values)
        {
            if (slot.Item is not { } installed ||
                !TryComp(installed, out WH40KWeaponModGrenadeLauncherComponent? resolvedGrenadeLauncher) ||
                !TryComp(installed, out GunComponent? resolvedGun) ||
                !TryComp(installed, out BallisticAmmoProviderComponent? resolvedProvider))
            {
                continue;
            }

            modUid = installed;
            grenadeLauncher = resolvedGrenadeLauncher;
            gun = resolvedGun;
            provider = resolvedProvider;
            return true;
        }

        return false;
    }

    protected bool TryGetActiveGrenadeLauncher(
        Entity<WH40KWeaponModHostComponent> hostEnt,
        out EntityUid modUid,
        out WH40KWeaponModGrenadeLauncherComponent grenadeLauncher,
        out GunComponent gun)
    {
        if (TryGetInstalledGrenadeLauncher(hostEnt, out modUid, out grenadeLauncher, out gun, out _) &&
            grenadeLauncher.Active)
        {
            return true;
        }

        modUid = default;
        grenadeLauncher = default!;
        gun = default!;
        return false;
    }

    private void OnGetActiveWeapon(Entity<HandsComponent> ent, ref GetActiveWeaponEvent args)
    {
        if (args.Handled ||
            !_hands.TryGetActiveItem(ent.AsNullable(), out var activeItem) ||
            !TryComp(activeItem, out WH40KWeaponModHostComponent? host) ||
            !TryGetActiveGrenadeLauncher((activeItem.Value, host), out var launcherUid, out _, out _))
        {
            return;
        }

        args.Weapon = launcherUid;
        args.Handled = true;
    }

    protected bool TryGetHostedGun(EntityUid modUid, out EntityUid gunUid, out WH40KWeaponModHostComponent host)
    {
        gunUid = default;
        host = default!;

        if (!TryComp(modUid, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !TryComp(parent, out WH40KWeaponModHostComponent? resolvedHost))
            return false;

        gunUid = parent;
        host = resolvedHost;
        return true;
    }

    protected bool TryGetHostedGun(EntityUid modUid, out EntityUid gunUid)
    {
        return TryGetHostedGun(modUid, out gunUid, out _);
    }

    protected bool IsPresentationActive(EntityUid gunUid, WH40KWeaponModGrenadeLauncherComponent grenadeLauncher)
    {
        return grenadeLauncher.Active &&
               TryGetHoldingUser(gunUid, out var user) &&
               _hands.TryGetActiveItem(user, out var activeItem) &&
               activeItem == gunUid;
    }

    protected void RefreshHostedPresentation(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid))
            return;

        var presentationActive = IsPresentationActive(gunUid, ent.Comp);
        var presentationState = ent.Comp.PresentationLoadedState;

        if (TryComp(ent.Owner, out BallisticAmmoProviderComponent? provider) &&
            provider.Entities.Count + provider.UnspawnedCount <= 0)
        {
            presentationState = ent.Comp.PresentationEmptyState;
        }

        SetHostedPresentation(
            gunUid,
            presentationActive,
            presentationActive ? ent.Comp.PresentationSprite : string.Empty,
            presentationActive ? presentationState : string.Empty,
            presentationActive ? ent.Comp.PresentationItemSprite : string.Empty);
    }

    private void SetHostedPresentation(
        EntityUid gunUid,
        bool active,
        string sprite,
        string state,
        string itemSprite)
    {
        _appearance.SetData(gunUid, WH40KWeaponModVisuals.PresentationActive, active);
        _appearance.SetData(gunUid, WH40KWeaponModVisuals.PresentationSprite, sprite);
        _appearance.SetData(gunUid, WH40KWeaponModVisuals.PresentationState, state);
        _appearance.SetData(gunUid, WH40KWeaponModVisuals.PresentationItemSprite, itemSprite);
    }

    protected void RefreshHostedMode(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid, out var host))
            return;

        var presentationActive = IsPresentationActive(gunUid, ent.Comp);
        UpdateHostedMetadata(gunUid, host, ent.Owner, presentationActive);
        UpdateHostedSight((gunUid, host), ent, presentationActive);
    }

    private void UpdateHostedMetadata(
        EntityUid gunUid,
        WH40KWeaponModHostComponent host,
        EntityUid launcherUid,
        bool presentationActive)
    {
        if (_net.IsServer)
            return;

        if (!TryComp(gunUid, out MetaDataComponent? gunMeta))
            return;

        if (presentationActive)
        {
            EnsureBasePresentation(host, gunMeta);

            var launcherMeta = MetaData(launcherUid);
            if (gunMeta.EntityName != launcherMeta.EntityName)
                _metaData.SetEntityName(gunUid, launcherMeta.EntityName, gunMeta);

            if (gunMeta.EntityDescription != launcherMeta.EntityDescription)
                _metaData.SetEntityDescription(gunUid, launcherMeta.EntityDescription, gunMeta);

            return;
        }

        RestoreHostedMetadata(gunUid, gunMeta, host);
    }

    private void UpdateHostedSight(
        Entity<WH40KWeaponModHostComponent> hostEnt,
        Entity<WH40KWeaponModGrenadeLauncherComponent> launcherEnt,
        bool presentationActive)
    {
        if (_net.IsServer)
            return;

        if (!presentationActive)
        {
            _weaponMods.RefreshHost(hostEnt.Owner, hostEnt.Comp);
            RestoreHostedSightDirect(hostEnt.Owner, hostEnt.Comp);
            return;
        }

        if (!TryComp(hostEnt.Owner, out CombatSightComponent? sight))
            return;

        _weaponMods.EnsureBaseCombatSight((hostEnt.Owner, hostEnt.Comp), sight);

        if (!Equals(sight.Sight, launcherEnt.Comp.PresentationSight) ||
            !Equals(sight.Unavailable, launcherEnt.Comp.PresentationUnavailableSight))
        {
            sight.Sight = launcherEnt.Comp.PresentationSight;
            sight.Unavailable = launcherEnt.Comp.PresentationUnavailableSight;
            Dirty(hostEnt.Owner, sight);
        }
    }

    private void RestoreHostedSightDirect(EntityUid gunUid, WH40KWeaponModHostComponent host)
    {
        if (_net.IsServer)
            return;

        if (!TryComp(gunUid, out CombatSightComponent? sight))
            return;

        _weaponMods.EnsureBaseCombatSight((gunUid, host), sight);

        var nextSight = host.BaseCombatSight;
        var nextUnavailable = host.BaseCombatSightUnavailable;

        if (!Equals(sight.Sight, nextSight) || !Equals(sight.Unavailable, nextUnavailable))
        {
            sight.Sight = nextSight;
            sight.Unavailable = nextUnavailable;
            Dirty(gunUid, sight);
        }
    }

    protected void RestoreHostedMode(EntityUid gunUid, WH40KWeaponModHostComponent host)
    {
        if (!TryComp(gunUid, out MetaDataComponent? meta))
            return;

        RestoreHostedMetadata(gunUid, meta, host);
        _weaponMods.RefreshHost(gunUid, host);
        RestoreHostedSightDirect(gunUid, host);
    }

    private static void EnsureBasePresentation(WH40KWeaponModHostComponent host, MetaDataComponent meta)
    {
        if (host.BasePresentationInitialized)
            return;

        host.BasePresentationName = meta.EntityName;
        host.BasePresentationDescription = meta.EntityDescription;
        host.BasePresentationInitialized = true;
    }

    private void RestoreHostedMetadata(EntityUid gunUid, MetaDataComponent meta, WH40KWeaponModHostComponent? host = null)
    {
        if (host is { BasePresentationInitialized: true })
        {
            if (host.BasePresentationName != null && meta.EntityName != host.BasePresentationName)
                _metaData.SetEntityName(gunUid, host.BasePresentationName, meta);

            if (host.BasePresentationDescription != null && meta.EntityDescription != host.BasePresentationDescription)
                _metaData.SetEntityDescription(gunUid, host.BasePresentationDescription, meta);

            return;
        }

        var prototype = meta.EntityPrototype;
        if (prototype == null)
            return;

        if (meta.EntityName != prototype.Name)
            _metaData.SetEntityName(gunUid, prototype.Name, meta);

        if (meta.EntityDescription != prototype.Description)
            _metaData.SetEntityDescription(gunUid, prototype.Description, meta);
    }

    protected virtual void EnsureActionGranted(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, EntityUid user)
    {
    }

    protected virtual void ClearGrantedAction(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
    }

    protected bool TryGetHoldingUser(EntityUid item, out EntityUid user)
    {
        user = default;

        if (!TryComp(item, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !HasComp<HandsComponent>(parent))
            return false;

        user = parent;
        return true;
    }
}
