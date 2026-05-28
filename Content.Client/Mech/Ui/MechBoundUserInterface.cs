using JetBrains.Annotations;
using Content.Client.UserInterface;
using Content.Shared.Mech;
using Content.Shared.Mech.Systems;
using Robust.Client.Timing;
using Robust.Client.UserInterface;

namespace Content.Client.Mech.Ui;

[UsedImplicitly]
public sealed partial class MechBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey), IBuiPreTickUpdate
{
    [Dependency] private  IClientGameTiming _gameTiming = null!;

    [ViewVariables]
    private MechMenu? _menu;

    private BuiPredictionState? _pred;

    // Input coalescers for performance optimization
    private InputCoalescer<bool> _tankCoalescer;
    private InputCoalescer<MechTankMode> _tankModeCoalescer;
    private InputCoalescer<float> _tankPressureCoalescer;
    private InputCoalescer<bool> _fanCoalescer;
    private InputCoalescer<bool> _filterCoalescer;
    private InputCoalescer<bool> _compressorCoalescer;

    private readonly Dictionary<NetEntity, bool> _pendingWeaponRecharge = new();

    protected override void Open()
    {
        base.Open();

        _pred = new BuiPredictionState(this, _gameTiming);

        _menu = this.CreateWindowCenteredLeft<MechMenu>();
        _menu.SetEntity(Owner);
        _menu.SetParentBui(this);

        // Equipment and module removal
        _menu.OnRemoveButtonPressed += uid =>
        {
            _pred.SendMessage(new MechEquipmentRemoveMessage(EntMan.GetNetEntity(uid)));
        };
        _menu.OnRemoveModuleButtonPressed += uid =>
        {
            _pred.SendMessage(new MechModuleRemoveMessage(EntMan.GetNetEntity(uid)));
        };

        // Cabin control
        _menu.OnTankToggle += enabled => _tankCoalescer.Set(enabled);
        _menu.OnTankModeChanged += mode => _tankModeCoalescer.Set(mode);
        _menu.OnTankPressureChanged += pressure => _tankPressureCoalescer.Set(pressure);
        _menu.OnFanToggle += isActive => _fanCoalescer.Set(isActive);
        _menu.OnFilterToggle += enabled => _filterCoalescer.Set(enabled);
        _menu.OnCompressorToggle += enabled => _compressorCoalescer.Set(enabled);

        // DNA lock
        _menu.OnDnaLockRegister += () => _pred.SendMessage(new MechDnaLockRegisterMessage());
        _menu.OnDnaLockToggle += () => _pred.SendMessage(new MechDnaLockToggleMessage());
        _menu.OnDnaLockReset += () => _pred.SendMessage(new MechDnaLockResetMessage());

        // Card lock
        _menu.OnCardLockRegister += () => _pred.SendMessage(new MechCardLockRegisterMessage());
        _menu.OnCardLockToggle += () => _pred.SendMessage(new MechCardLockToggleMessage());
        _menu.OnCardLockReset += () => _pred.SendMessage(new MechCardLockResetMessage());
    }

    public void SendWeaponRechargeToggle(EntityUid equipment, bool enabled)
    {
        var netEquipment = EntMan.GetNetEntity(equipment);
        _pendingWeaponRecharge[netEquipment] = enabled;

        _menu?.UpdateEquipmentFragmentState(netEquipment, new MechWeaponRechargeUiState
        {
            AutoRecharge = enabled,
        });

        SendMessage(new MechWeaponRechargeToggleMessage(netEquipment, enabled));
    }

    void IBuiPreTickUpdate.PreTickUpdate()
    {
        // Send coalesced input events
        if (_tankCoalescer.CheckIsModified(out var tankValue))
            _pred!.SendMessage(new MechTankToggleMessage(tankValue));

        if (_tankModeCoalescer.CheckIsModified(out var tankMode))
            _pred!.SendMessage(new MechTankModeMessage(tankMode));

        if (_tankPressureCoalescer.CheckIsModified(out var tankPressure))
            _pred!.SendMessage(new MechTankPressureMessage(tankPressure));

        if (_fanCoalescer.CheckIsModified(out var fanValue))
            _pred!.SendMessage(new MechFanToggleMessage(fanValue));

        if (_filterCoalescer.CheckIsModified(out var filterValue))
            _pred!.SendMessage(new MechFilterToggleMessage(filterValue));

        if (_compressorCoalescer.CheckIsModified(out var compressorValue))
            _pred!.SendMessage(new MechFanCompressorToggleMessage(compressorValue));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not MechBoundUiState mechState)
            return;

        // Apply any pending predicted messages to state
        foreach (var replayMsg in _pred!.MessagesToReplay())
        {
            switch (replayMsg)
            {
                case MechTankToggleMessage tankToggle:
                    mechState.TankEnabled = tankToggle.Enabled;
                    break;

                case MechTankModeMessage tankMode:
                    mechState.TankMode = tankMode.Mode;
                    break;

                case MechTankPressureMessage tankPressure:
                    mechState.TankTargetPressure = tankPressure.Pressure;
                    break;

                case MechFanToggleMessage fanToggle:
                    mechState.FanActive = fanToggle.IsActive;
                    mechState.FanState = fanToggle.IsActive ? MechFanState.On : MechFanState.Off;
                    break;

                case MechFilterToggleMessage filterToggle:
                    mechState.FilterEnabled = filterToggle.Enabled;
                    break;

                case MechFanCompressorToggleMessage compressorToggle:
                    mechState.CompressorEnabled = compressorToggle.Enabled;
                    break;

            }
        }

        foreach (var (equipment, enabled) in _pendingWeaponRecharge)
        {
            mechState.EquipmentUiStates[equipment] = new MechWeaponRechargeUiState
            {
                AutoRecharge = enabled,
            };
        }

        _menu?.UpdateState(mechState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is not MechWeaponRechargeStateMessage rechargeState)
            return;

        _pendingWeaponRecharge.Remove(rechargeState.Equipment);
        _menu?.UpdateEquipmentFragmentState(rechargeState.Equipment, new MechWeaponRechargeUiState
        {
            AutoRecharge = rechargeState.Enabled,
        });
        _menu?.UpdateEnergyDrainDisplay(rechargeState.EnergyDrainRate);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Close();
        _menu = null;
    }
}
