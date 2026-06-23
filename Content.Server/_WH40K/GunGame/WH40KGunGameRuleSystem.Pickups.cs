using Content.Server.GameTicking.Rules.Components;
using Content.Server.Popups;
using Content.Shared._WH40K.GunGame;
using Content.Shared.Administration.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private SharedTransformSystem _transformPickup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public void InitializePickups()
    {
        SubscribeLocalEvent<WH40KGunGamePickupSpawnerComponent, MapInitEvent>(OnPickupSpawnerMapInit);
    }

    private void OnPickupSpawnerMapInit(Entity<WH40KGunGamePickupSpawnerComponent> ent, ref MapInitEvent args)
    {
        if (!TryGetActiveRule(out _, out _))
            return;

        SpawnPickupItem((ent.Owner, ent.Comp, Transform(ent.Owner)));
    }

    private void UpdatePickups()
    {
        var query = EntityQueryEnumerator<WH40KGunGamePickupSpawnerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawner, out var xform))
        {
            if (spawner.CurrentItem is { } currentItem && TerminatingOrDeleted(currentItem))
            {
                spawner.CurrentItem = null;
                spawner.LastTaken = _timing.CurTime;
            }

            if (spawner.CurrentItem != null)
            {
                HandlePickupTouch(spawner);
                continue;
            }

            if (!spawner.HasSpawnedItem || _timing.CurTime - spawner.LastTaken >= spawner.RespawnDelay)
                SpawnPickupItem((uid, spawner, xform));
        }
    }

    private void SpawnPickupItem(Entity<WH40KGunGamePickupSpawnerComponent, TransformComponent> ent)
    {
        var coords = _transformPickup.GetMapCoordinates(ent.Owner, ent.Comp2);
        var item = Spawn(ent.Comp1.ItemPrototype, coords);
        ent.Comp1.HasSpawnedItem = true;
        ent.Comp1.CurrentItem = item;
    }

    private void HandlePickupTouch(WH40KGunGamePickupSpawnerComponent spawner)
    {
        if (spawner.CurrentItem is not { } item)
            return;

        var playerQuery = EntityQueryEnumerator<WH40KGunGamePlayerComponent, MobStateComponent, TransformComponent>();
        while (playerQuery.MoveNext(out var playerUid, out _, out var mobState, out _))
        {
            if (mobState.CurrentState != Shared.Mobs.MobState.Alive)
                continue;

            var pickupRadius = MathF.Max(0f, spawner.PickupRadius);
            var playerPosition = _transformPickup.GetWorldPosition(playerUid);
            var itemPosition = _transformPickup.GetWorldPosition(item);
            if ((playerPosition - itemPosition).LengthSquared() > pickupRadius * pickupRadius)
                continue;

            ApplyPickupEffect(playerUid, spawner);
            return;
        }
    }

    private void ApplyPickupEffect(EntityUid player, WH40KGunGamePickupSpawnerComponent spawner)
    {
        switch (spawner.Type)
        {
            case WH40KGunGamePickupType.Ammo:
                FillActiveWeapon(player);
                _popup.PopupEntity(Loc.GetString("wh40k-gun-game-ammo-refilled"), player, player);
                break;
            case WH40KGunGamePickupType.Medkit:
                FullHeal(player);
                _popup.PopupEntity(Loc.GetString("wh40k-gun-game-health-restored"), player, player);
                break;
        }

        if (spawner.CurrentItem != null)
            QueueDel(spawner.CurrentItem.Value);

        spawner.CurrentItem = null;
        spawner.LastTaken = _timing.CurTime;
    }

    private void FillActiveWeapon(EntityUid player)
    {
        if (TryComp<WH40KGunGamePlayerComponent>(player, out var playerComp)
            && playerComp.CurrentWeapon is { } trackedWeapon
            && !TerminatingOrDeleted(trackedWeapon))
        {
            if (TryComp(trackedWeapon, out GunComponent? _))
                FillWeaponToFull(trackedWeapon);
            return;
        }

        if (_hands.TryGetActiveItem(player, out var activeItem) && TryComp(activeItem, out GunComponent? _))
            FillWeaponToFull(activeItem.Value);
    }

    private void FillWeaponToFull(EntityUid gun)
    {
        if (TryComp<ChamberMagazineAmmoProviderComponent>(gun, out _))
        {
            var magEntity = GetChamberMagazineEntity(gun);
            if (magEntity is { } mag && TryComp<BallisticAmmoProviderComponent>(mag, out var magBallistic))
            {
                var magTarget = magBallistic.Capacity - magBallistic.Entities.Count;
                _gun.SetBallisticUnspawned((mag, magBallistic), magTarget);
            }

            RefreshWeaponVisuals(gun);
            return;
        }

        if (TryComp<MagazineAmmoProviderComponent>(gun, out _))
        {
            var magEntity = GetChamberMagazineEntity(gun);
            if (magEntity is { } mag)
            {
                if (TryComp<BatteryComponent>(mag, out var battery))
                {
                    _battery.SetCharge(mag, battery.MaxCharge);
                }
                else if (TryComp<BallisticAmmoProviderComponent>(mag, out var magBallistic))
                {
                    var magTarget = magBallistic.Capacity - magBallistic.Entities.Count;
                    _gun.SetBallisticUnspawned((mag, magBallistic), magTarget);
                }
            }

            RefreshWeaponVisuals(gun);
            return;
        }

        if (TryComp<BatteryAmmoProviderComponent>(gun, out _))
        {
            var getEv = new GetChargeEvent();
            RaiseLocalEvent(gun, ref getEv);
            if (getEv.MaxCharge > getEv.CurrentCharge)
            {
                var changeEv = new ChangeChargeEvent(getEv.MaxCharge - getEv.CurrentCharge);
                RaiseLocalEvent(gun, ref changeEv);
            }

            return;
        }

        if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
        {
            var target = ballistic.Capacity - ballistic.Entities.Count;
            _gun.SetBallisticUnspawned((gun, ballistic), target);
            RefreshWeaponVisuals(gun);
            return;
        }

        if (TryComp<BasicEntityAmmoProviderComponent>(gun, out var basic) && basic.Capacity != null)
        {
            _gun.UpdateBasicEntityAmmoCount((gun, basic), basic.Capacity.Value);
            RefreshWeaponVisuals(gun);
        }
    }

    private EntityUid? GetChamberMagazineEntity(EntityUid gun)
    {
        if (!_container.TryGetContainer(gun, "gun_magazine", out var container) ||
            container is not ContainerSlot slot)
        {
            return null;
        }

        return slot.ContainedEntity;
    }

    private void FullHeal(EntityUid target)
    {
        _rejuvenate.PerformRejuvenate(target);
    }

    private void RefreshWeaponVisuals(EntityUid gun)
    {
        if (!TryComp<AppearanceComponent>(gun, out var appearance))
            return;

        if (TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamberMagazine))
        {
            var magEntity = GetChamberMagazineEntity(gun);
            var chambered = _gun.GetChamberEntity(gun) != null;
            var ammoCount = new GetAmmoCountEvent();

            if (magEntity != null)
                RaiseLocalEvent(magEntity.Value, ref ammoCount);

            _appearance.SetData(gun, AmmoVisuals.MagLoaded, magEntity != null, appearance);
            _appearance.SetData(gun, AmmoVisuals.HasAmmo, chambered || ammoCount.Count != 0, appearance);
            _appearance.SetData(gun, AmmoVisuals.AmmoCount, ammoCount.Count + (chambered ? 1 : 0), appearance);
            _appearance.SetData(gun, AmmoVisuals.AmmoMax, ammoCount.Capacity + 1, appearance);

            if (chamberMagazine.BoltClosed != null)
                _appearance.SetData(gun, AmmoVisuals.BoltClosed, chamberMagazine.BoltClosed.Value, appearance);
            return;
        }

        if (TryComp<MagazineAmmoProviderComponent>(gun, out _))
        {
            var magEntity = GetChamberMagazineEntity(gun);
            var ammoCount = new GetAmmoCountEvent();

            if (magEntity != null)
                RaiseLocalEvent(magEntity.Value, ref ammoCount);

            _appearance.SetData(gun, AmmoVisuals.MagLoaded, magEntity != null, appearance);
            _appearance.SetData(gun, AmmoVisuals.HasAmmo, ammoCount.Count != 0, appearance);
            _appearance.SetData(gun, AmmoVisuals.AmmoCount, ammoCount.Count, appearance);
            _appearance.SetData(gun, AmmoVisuals.AmmoMax, ammoCount.Capacity, appearance);
        }
    }
}
