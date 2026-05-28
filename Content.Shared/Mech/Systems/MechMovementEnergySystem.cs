using System.Linq;
using System.Numerics;
using Content.Shared.FixedPoint;
using Content.Shared.Mech.Components;
using Content.Shared.Movement.Components;
using Content.Shared.PowerCell;
using Content.Shared.Power.EntitySystems;

namespace Content.Shared.Mech.Systems;

/// <summary>
/// Handles per-frame movement energy drain for mechs to avoid.
/// </summary>
public sealed partial class MechMovementEnergySystem : EntitySystem
{
    [Dependency] private  PowerCellSystem _powerCell = default!;
    [Dependency] private  SharedBatterySystem _battery = default!;
    [Dependency] private  SharedMechSystem _mech = default!;

    private readonly HashSet<EntityUid> _activeMechs = [];
    private readonly Dictionary<EntityUid, float> _passiveEnergyBuffers = [];

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechComponent, MechMovementDrainToggleEvent>(OnDrainToggle);
    }

    private void OnDrainToggle(EntityUid uid, MechComponent component, ref MechMovementDrainToggleEvent args)
    {
        if (args.Enabled)
        {
            _activeMechs.Add(uid);
        }
        else
        {
            _activeMechs.Remove(uid);
            _passiveEnergyBuffers.Remove(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeMechs.Count == 0)
            return;

        foreach (var mechUid in _activeMechs.ToArray())
        {
            if (!TryComp<MechComponent>(mechUid, out var mechComp) || !TryComp<InputMoverComponent>(mechUid, out var mover))
            {
                _activeMechs.Remove(mechUid);
                _passiveEnergyBuffers.Remove(mechUid);
                continue;
            }

            if (!_powerCell.TryGetBatteryFromSlot(mechUid, out var mechBattery))
            {
                _mech.RefreshEnergyDependentState(mechUid);
                _activeMechs.Remove(mechUid);
                _passiveEnergyBuffers.Remove(mechUid);
                continue;
            }

            if (mechComp.PilotPassiveEnergyPerSecond > 0f)
            {
                _passiveEnergyBuffers.TryGetValue(mechUid, out var passiveEnergy);
                passiveEnergy += mechComp.PilotPassiveEnergyPerSecond * frameTime;

                if (passiveEnergy >= 0.01f)
                {
                    var toDrain = passiveEnergy;
                    passiveEnergy = 0f;

                    if (!_mech.HasUsableEnergy(mechUid, toDrain)
                        || !_mech.TryChangeEnergy(mechUid, -FixedPoint2.New(toDrain)))
                    {
                        _battery.SetCharge(mechBattery.Value.AsNullable(), 0f);
                        _mech.UpdateMechUi(mechUid);
                        _mech.UpdateBatteryAlert(mechUid);
                        _mech.RefreshEnergyDependentState(mechUid);
                        _activeMechs.Remove(mechUid);
                        _passiveEnergyBuffers.Remove(mechUid);
                        continue;
                    }
                }

                if (passiveEnergy > 0f)
                    _passiveEnergyBuffers[mechUid] = passiveEnergy;
                else
                    _passiveEnergyBuffers.Remove(mechUid);
            }

            if (!_mech.HasUsableEnergy(mechUid))
            {
                _battery.SetCharge(mechBattery.Value.AsNullable(), 0f);
                _mech.UpdateMechUi(mechUid);
                _mech.UpdateBatteryAlert(mechUid);
                _mech.RefreshEnergyDependentState(mechUid);
                _activeMechs.Remove(mechUid);
                _passiveEnergyBuffers.Remove(mechUid);
                continue;
            }

            if (mechComp.MovementEnergyPerSecond <= 0f)
                continue;

            if (!mover.CanMove || mover.WishDir == Vector2.Zero)
                continue;

            var movementDrain = mechComp.MovementEnergyPerSecond * frameTime;
            if (!_mech.HasUsableEnergy(mechUid, movementDrain)
                || !_mech.TryChangeEnergy(mechUid, -FixedPoint2.New(movementDrain)))
            {
                _battery.SetCharge(mechBattery.Value.AsNullable(), 0f);
                _mech.UpdateMechUi(mechUid);
                _mech.UpdateBatteryAlert(mechUid);
                _mech.RefreshEnergyDependentState(mechUid);
                _activeMechs.Remove(mechUid);
                _passiveEnergyBuffers.Remove(mechUid);
            }
        }
    }
}
