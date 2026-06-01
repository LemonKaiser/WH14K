using Content.Client.Power.EntitySystems;
using Content.Shared.Disposal.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Disposal.Unit
{
    [UsedImplicitly]
    public sealed class DisposalUnitBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private DisposalUnitWindow? _disposalUnitWindow;

        public DisposalUnitBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        private void ButtonPressed(DisposalUnitUiButton button)
        {
            SendPredictedMessage(new DisposalUnitUiButtonPressedMessage(button));
        }

        protected override void Open()
        {
            base.Open();

            _disposalUnitWindow = this.CreateWindow<DisposalUnitWindow>();
            _disposalUnitWindow.OpenCenteredRight();

            _disposalUnitWindow.Eject.OnPressed += _ => ButtonPressed(DisposalUnitUiButton.Eject);
            _disposalUnitWindow.Engage.OnPressed += _ => ButtonPressed(DisposalUnitUiButton.Engage);
            _disposalUnitWindow.Power.OnPressed += _ => ButtonPressed(DisposalUnitUiButton.Power);

            if (EntMan.TryGetComponent(Owner, out DisposalUnitComponent? component))
            {
                Refresh((Owner, component));
            }
        }

        public override void Update()
        {
            base.Update();

            if (EntMan.TryGetComponent(Owner, out DisposalUnitComponent? component))
            {
                Refresh((Owner, component));
            }
        }

        public void Refresh(Entity<DisposalUnitComponent> entity)
        {
            if (_disposalUnitWindow == null)
                return;

            var name = EntMan.GetComponent<MetaDataComponent>(entity.Owner).EntityName;

            if (!EntMan.TryGetComponent(entity.Owner, out DisposalUnitComponent? disposals))
                return;

            var disposalUnit = EntMan.System<DisposalUnitSystem>();
            var disposalState = disposalUnit.GetState(entity);
            var fullPressure = disposalUnit.EstimatedFullPressure((Owner, disposals));
            var pressurePerSecond = disposals.PressurePerSecond;
            var powered = EntMan.System<PowerReceiverSystem>().IsPowered(Owner);
            var engaged = entity.Comp.Engaged;

            _disposalUnitWindow.RefreshState(name, disposalState, powered, engaged, fullPressure, pressurePerSecond);
            _disposalUnitWindow.Power.Pressed = powered;
            _disposalUnitWindow.Engage.Pressed = engaged;
        }
    }
}
