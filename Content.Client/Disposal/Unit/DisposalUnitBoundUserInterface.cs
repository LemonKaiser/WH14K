using Content.Client.Disposal.Mailing;
using Content.Client.Power.EntitySystems;
using Content.Shared.Disposal.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Disposal.Unit
{
    /// <summary>
    /// Initializes a <see cref="MailingUnitWindow"/> or a <see cref="_disposalUnitWindow"/> and updates it when new server messages are received.
    /// </summary>
    [UsedImplicitly]
    public sealed class DisposalUnitBoundUserInterface : BoundUserInterface
    {
        [ViewVariables] private DisposalUnitWindow? _disposalUnitWindow;

        public DisposalUnitBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        private void ButtonPressed(DisposalUnitComponent.UiButton button)
        {
            SendPredictedMessage(new DisposalUnitComponent.UiButtonPressedMessage(button));
            // If we get client-side power stuff then we can predict the button presses but for now we won't as it stuffs
            // the pressure lerp up.
        }

        protected override void Open()
        {
            base.Open();

            _disposalUnitWindow = this.CreateWindow<DisposalUnitWindow>();

            _disposalUnitWindow.OpenCentered();

            _disposalUnitWindow.Eject.OnPressed += _ => ButtonPressed(DisposalUnitComponent.UiButton.Eject);
            _disposalUnitWindow.Engage.OnPressed += _ => ButtonPressed(DisposalUnitComponent.UiButton.Engage);
            _disposalUnitWindow.Power.OnPressed += _ => ButtonPressed(DisposalUnitComponent.UiButton.Power);

            if (EntMan.TryGetComponent(Owner, out DisposalUnitComponent? component))
            {
                Refresh((Owner, component));
            }
        }

        public void Refresh(Entity<DisposalUnitComponent> entity)
        {
            if (_disposalUnitWindow == null)
                return;

            var disposalSystem = EntMan.System<DisposalUnitSystem>();
            var state = disposalSystem.GetState(entity.Owner, entity.Comp);
            var powered = EntMan.System<PowerReceiverSystem>().IsPowered(Owner);
            var engaged = entity.Comp.Engaged;
            var fullPressure = disposalSystem.EstimatedFullPressure(entity.Owner, entity.Comp);
            var machineName = EntMan.GetComponent<MetaDataComponent>(entity.Owner).EntityName;

            _disposalUnitWindow.Power.Pressed = powered;
            _disposalUnitWindow.Engage.Pressed = engaged;
            _disposalUnitWindow.RefreshState(machineName, state, powered, engaged, fullPressure);
        }
    }
}
