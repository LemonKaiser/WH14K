using Content.Client.Mech.Ui;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Fragments;
using Content.Shared.Mech;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Mech.Ui.Equipment;

public sealed partial class MechWeaponRechargeUi : UIFragment
{
    private BoundUserInterface? _userInterface;
    private EntityUid? _fragmentOwner;
    private BoxContainer? _root;
    private OnOffButton? _toggle;
    private bool _autoRecharge;

    public override Control GetUIFragmentRoot()
    {
        return _root!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        if (fragmentOwner == null)
            return;

        _userInterface = userInterface;
        _fragmentOwner = fragmentOwner.Value;

        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        _root.AddChild(new Label
        {
            HorizontalExpand = true,
            VerticalAlignment = Control.VAlignment.Center,
            Text = Loc.GetString("mech-weapon-recharge-label"),
        });

        _toggle = new OnOffButton
        {
            HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Center,
        };
        _toggle.StateChanged += OnToggleChanged;
        _root.AddChild(_toggle);

        UpdateVisuals();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not MechWeaponRechargeUiState rechargeState)
            return;

        _autoRecharge = rechargeState.AutoRecharge;
        UpdateVisuals();
    }

    private void OnToggleChanged(bool isOn)
    {
        _autoRecharge = isOn;
        UpdateVisuals();

        if (_userInterface == null || _fragmentOwner == null)
            return;

        if (_userInterface is MechBoundUserInterface mechUi)
            mechUi.SendWeaponRechargeToggle(_fragmentOwner.Value, _autoRecharge);
    }

    private void UpdateVisuals()
    {
        if (_toggle == null)
            return;

        if (_toggle.IsOn != _autoRecharge)
            _toggle.IsOn = _autoRecharge;
    }
}
