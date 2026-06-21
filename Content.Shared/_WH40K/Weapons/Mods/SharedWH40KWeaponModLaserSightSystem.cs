using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Weapons.Mods;

public abstract partial class SharedWH40KWeaponModLaserSightSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, MapInitEvent>(OnLaserSightMapInit);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, ComponentShutdown>(OnLaserSightShutdown);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, WH40KToggleWeaponLaserSightActionEvent>(OnToggleLaserSightAction);
    }

    private void OnLaserSightMapInit(Entity<WH40KWeaponModLaserSightComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
    }

    private void OnLaserSightShutdown(Entity<WH40KWeaponModLaserSightComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ToggleActionEntity != null)
            _actions.RemoveAction(ent.Comp.ToggleActionEntity);
    }

    private void OnToggleLaserSightAction(
        Entity<WH40KWeaponModLaserSightComponent> ent,
        ref WH40KToggleWeaponLaserSightActionEvent args)
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

    protected void SetActive(Entity<WH40KWeaponModLaserSightComponent> ent, bool active)
    {
        if (ent.Comp.Active == active)
            return;

        ent.Comp.Active = active;

        if (_net.IsServer)
            Dirty(ent.Owner, ent.Comp);

        if (ent.Comp.ToggleActionEntity != null)
            _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);

        OnActiveChanged(ent);
    }

    protected virtual void OnActiveChanged(Entity<WH40KWeaponModLaserSightComponent> ent)
    {
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
}
