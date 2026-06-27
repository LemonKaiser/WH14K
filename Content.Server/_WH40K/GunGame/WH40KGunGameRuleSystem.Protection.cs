using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Strip.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    public void InitializeProtection()
    {
        _damageProtection.RegisterHandler(OnBeforeDamageChanged);
    }

    private void OnBeforeDamageChanged(EntityUid uid, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryGetActiveRule(out _, out _))
            return;

        if (HasComp<MobStateComponent>(uid))
            return;

        args.Cancelled = true;
    }

    private void ApplyPlayerProtection(EntityUid mob, WH40KGunGamePlayerComponent playerComp)
    {
        if (TryComp<HandsComponent>(mob, out var hands))
        {
            playerComp.PreviousHandsCanBeStripped = hands.CanBeStripped;
            _hands.SetCanBeStripped((mob, hands), false);
        }

        if (HasComp<StrippableComponent>(mob))
        {
            RemComp<StrippableComponent>(mob);
            playerComp.RemovedStrippable = true;
        }
    }

    private void RemovePlayerProtection(EntityUid mob, WH40KGunGamePlayerComponent playerComp)
    {
        if (TryComp<HandsComponent>(mob, out var hands))
            _hands.SetCanBeStripped((mob, hands), playerComp.PreviousHandsCanBeStripped);

        if (playerComp.RemovedStrippable)
        {
            EnsureComp<StrippableComponent>(mob);
            playerComp.RemovedStrippable = false;
        }
    }

    private void ApplyMapStabilitySafeguards()
    {
        var mapId = GameTicker.DefaultMap;
        if (mapId == MapId.Nullspace)
            return;

        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            _shuttles.Disable(grid.Owner);
            EnsureInherentGravity(grid.Owner, raiseGravityChangedEvent: true);
        }

        if (_map.TryGetMap(mapId, out var mapUid))
        {
            EnsureInherentGravity(mapUid.Value, raiseGravityChangedEvent: false);
            EnsureAmbientAir(mapUid.Value);
        }
    }

    private void EnsureInherentGravity(EntityUid uid, bool raiseGravityChangedEvent)
    {
        var gravity = EnsureComp<GravityComponent>(uid);
        var wasEnabled = gravity.Enabled;

        if (!gravity.Enabled || !gravity.Inherent)
        {
            gravity.Enabled = true;
            gravity.Inherent = true;
            Dirty(uid, gravity);
        }

        if (raiseGravityChangedEvent && !wasEnabled)
        {
            var ev = new GravityChangedEvent(uid, true);
            RaiseLocalEvent(uid, ref ev, true);
        }
    }

    private void EnsureAmbientAir(EntityUid mapUid)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;

        _atmos.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
    }
}
