using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WH40K.Weapons.Plasma;

public sealed partial class WH40KPlasmaFireModesSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPlasmaFireModesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KPlasmaFireModesComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WH40KPlasmaFireModesComponent, GetVerbsEvent<Verb>>(OnGetVerb);
        SubscribeLocalEvent<WH40KPlasmaFireModesComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WH40KPlasmaFireModesComponent, EntInsertedIntoContainerMessage>(OnMagazineInserted);
    }

    private void OnMapInit(Entity<WH40KPlasmaFireModesComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.FireModes.Count == 0)
            return;

        var index = Math.Clamp(ent.Comp.CurrentFireMode, 0, ent.Comp.FireModes.Count - 1);
        SetFireMode(ent, index);
    }

    private void OnUseInHand(Entity<WH40KPlasmaFireModesComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryCycleFireMode(ent, args.User);
    }

    private void OnGetVerb(EntityUid uid, WH40KPlasmaFireModesComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract || component.FireModes.Count < 2)
            return;

        for (var i = 0; i < component.FireModes.Count; i++)
        {
            var fireMode = component.FireModes[i];
            var index = i;

            args.Verbs.Add(new Verb
            {
                Priority = 1,
                Category = VerbCategory.SelectType,
                Text = Loc.GetString(fireMode.Name),
                Disabled = i == component.CurrentFireMode,
                Act = () => TrySetFireMode((uid, component), index, args.User)
            });
        }
    }

    private void OnExamined(Entity<WH40KPlasmaFireModesComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.FireModes.Count < 2)
            return;

        args.PushMarkup(Loc.GetString("wh40k-plasma-fire-mode-examine", ("mode", Loc.GetString(GetMode(ent.Comp).Name))));
    }

    private void OnMagazineInserted(Entity<WH40KPlasmaFireModesComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != "gun_magazine" || ent.Comp.FireModes.Count == 0)
            return;

        var index = Math.Clamp(ent.Comp.CurrentFireMode, 0, ent.Comp.FireModes.Count - 1);
        ApplyMode(ent, ent.Comp.FireModes[index]);
    }

    public void TryCycleFireMode(Entity<WH40KPlasmaFireModesComponent> ent, EntityUid? user = null)
    {
        if (ent.Comp.FireModes.Count < 2)
            return;

        var index = (ent.Comp.CurrentFireMode + 1) % ent.Comp.FireModes.Count;
        TrySetFireMode(ent, index, user);
    }

    public bool TrySetFireMode(Entity<WH40KPlasmaFireModesComponent> ent, int index, EntityUid? user = null)
    {
        if (index < 0 || index >= ent.Comp.FireModes.Count)
            return false;

        SetFireMode(ent, index, user);
        return true;
    }

    private void SetFireMode(Entity<WH40KPlasmaFireModesComponent> ent, int index, EntityUid? user = null)
    {
        var fireMode = ent.Comp.FireModes[index];
        ent.Comp.CurrentFireMode = index;
        Dirty(ent);

        ApplyMode(ent, fireMode);

        if (user != null)
        {
            _popup.PopupClient(
                Loc.GetString("wh40k-plasma-fire-mode-popup", ("mode", Loc.GetString(fireMode.Name))),
                ent,
                user.Value);
        }
    }

    private void ApplyMode(Entity<WH40KPlasmaFireModesComponent> ent, WH40KPlasmaFireMode fireMode)
    {
        if (TryGetMagazineBatteryProvider(ent.Owner, out var batteryProvider))
        {
            batteryProvider.Comp.Prototype = fireMode.Prototype;
            Dirty(batteryProvider);
        }

        if (TryComp<WH40KPlasmaOverheatComponent>(ent, out var overheat))
        {
            overheat.Chance = Math.Max(0f, fireMode.OverheatChance);
        }
    }

    private WH40KPlasmaFireMode GetMode(WH40KPlasmaFireModesComponent component)
    {
        return component.FireModes[component.CurrentFireMode];
    }

    private bool TryGetMagazineBatteryProvider(EntityUid uid, out Entity<BatteryAmmoProviderComponent> batteryProvider)
    {
        batteryProvider = default;

        if (!_container.TryGetContainer(uid, Shared.Weapons.Ranged.Systems.SharedGunSystem.MagazineSlot, out var container) ||
            container is not ContainerSlot slot)
            return false;

        if (slot.ContainedEntity is not { } magazineUid ||
            !TryComp<BatteryAmmoProviderComponent>(magazineUid, out var ammoProvider))
        {
            return false;
        }

        batteryProvider = (magazineUid, ammoProvider!);
        return true;
    }
}
