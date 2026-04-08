using System;
using System.Collections.Generic;
using Content.Shared.Kitchen.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Kitchen.UI;

[UsedImplicitly]
public sealed class MicrowaveBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MicrowaveMenu? _menu;

    [ViewVariables]
    private readonly Dictionary<int, EntityUid> _solids = new();

    public MicrowaveBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<MicrowaveMenu>();
        _menu.SetEntityName(EntMan.GetComponent<MetaDataComponent>(Owner).EntityName);

        _menu.StartButton.OnPressed += _ => SendPredictedMessage(new MicrowaveStartCookMessage());
        _menu.EjectButton.OnPressed += _ => SendPredictedMessage(new MicrowaveEjectMessage());
        _menu.OnCookTimeSelected += (buttonIndex, cookTime) =>
            SendPredictedMessage(new MicrowaveSelectCookTimeMessage(buttonIndex, cookTime));
        _menu.OnIngredientSelected += index =>
        {
            if (_solids.TryGetValue(index, out var entity))
            {
                SendPredictedMessage(new MicrowaveEjectSolidIndexedMessage(EntMan.GetNetEntity(entity)));
            }
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_menu == null || state is not MicrowaveUpdateUserInterfaceState microwaveState)
            return;

        RefreshContentsDisplay(EntMan.GetEntityArray(microwaveState.ContainedSolids));
        _menu.SetCookTimeSelection(microwaveState.ActiveButtonIndex, microwaveState.CurrentCookTime);
        _menu.SetBusyState(microwaveState.IsMicrowaveBusy, microwaveState.CurrentCookTimeEnd);
    }

    private void RefreshContentsDisplay(EntityUid[] containedSolids)
    {
        if (_menu == null)
            return;

        _solids.Clear();

        var entries = new List<MicrowaveMenu.MicrowaveIngredientEntry>();
        foreach (var entity in containedSolids)
        {
            if (EntMan.Deleted(entity))
                continue;

            Texture? texture = null;
            if (EntMan.TryGetComponent<IconComponent>(entity, out var iconComponent))
            {
                texture = EntMan.System<SpriteSystem>().GetIcon(iconComponent);
            }
            else if (EntMan.TryGetComponent<SpriteComponent>(entity, out var spriteComponent))
            {
                texture = spriteComponent.Icon?.Default;
            }

            var index = entries.Count;
            entries.Add(new MicrowaveMenu.MicrowaveIngredientEntry(
                index,
                EntMan.GetComponent<MetaDataComponent>(entity).EntityName,
                texture));
            _solids[index] = entity;
        }

        _menu.SetIngredientEntries(entries);
    }
}
