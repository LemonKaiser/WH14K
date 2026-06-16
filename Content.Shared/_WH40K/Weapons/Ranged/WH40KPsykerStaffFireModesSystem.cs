using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Weapons.Ranged;

public sealed partial class WH40KPsykerStaffFireModesSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KPsykerStaffFireModesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KPsykerStaffFireModesComponent, UseInHandEvent>(OnUseInHandEvent);
        SubscribeLocalEvent<WH40KPsykerStaffFireModesComponent, GetVerbsEvent<Verb>>(OnGetVerb);
        SubscribeLocalEvent<WH40KPsykerStaffFireModesComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KPsykerStaffFireModesComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.FireModes.Count == 0)
            return;

        var index = Math.Clamp(ent.Comp.CurrentFireMode, 0, ent.Comp.FireModes.Count - 1);
        SetFireMode(ent, index);
    }

    private void OnExamined(Entity<WH40KPsykerStaffFireModesComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.FireModes.Count < 2)
            return;

        var fireMode = GetMode(ent.Comp);

        if (!_prototypeManager.TryIndex<EntityPrototype>(fireMode.Prototype, out var proto))
            return;

        args.PushMarkup(Loc.GetString("gun-set-fire-mode-examine", ("mode", proto.Name)));
    }

    private WH40KPsykerStaffFireMode GetMode(WH40KPsykerStaffFireModesComponent component)
    {
        return component.FireModes[component.CurrentFireMode];
    }

    private void OnGetVerb(EntityUid uid, WH40KPsykerStaffFireModesComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        if (component.FireModes.Count < 2)
            return;

        for (var i = 0; i < component.FireModes.Count; i++)
        {
            var fireMode = component.FireModes[i];
            var entProto = _prototypeManager.Index<EntityPrototype>(fireMode.Prototype);
            var index = i;

            var v = new Verb
            {
                Priority = 1,
                Category = VerbCategory.SelectType,
                Text = entProto.Name,
                Disabled = i == component.CurrentFireMode,
                Act = () =>
                {
                    TrySetFireMode((uid, component), index, args.User);
                }
            };

            args.Verbs.Add(v);
        }
    }

    private void OnUseInHandEvent(Entity<WH40KPsykerStaffFireModesComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryCycleFireMode(ent, args.User);
    }

    public void TryCycleFireMode(Entity<WH40KPsykerStaffFireModesComponent> ent, EntityUid? user = null)
    {
        if (ent.Comp.FireModes.Count < 2)
            return;

        var index = (ent.Comp.CurrentFireMode + 1) % ent.Comp.FireModes.Count;
        TrySetFireMode(ent, index, user);
    }

    public bool TrySetFireMode(Entity<WH40KPsykerStaffFireModesComponent> ent, int index, EntityUid? user = null)
    {
        if (index < 0 || index >= ent.Comp.FireModes.Count)
            return false;

        SetFireMode(ent, index, user);
        return true;
    }

    private void SetFireMode(Entity<WH40KPsykerStaffFireModesComponent> ent, int index, EntityUid? user = null)
    {
        var fireMode = ent.Comp.FireModes[index];
        ent.Comp.CurrentFireMode = index;
        Dirty(ent);

        // Update BasicEntityAmmoProvider projectile
        if (TryComp<BasicEntityAmmoProviderComponent>(ent, out var basicAmmo))
        {
            basicAmmo.Proto = fireMode.Prototype;
            Dirty(ent, basicAmmo);
        }

        // Update WH40KPsykerForceStaffComponent instability
        if (TryComp<WH40KPsykerForceStaffComponent>(ent, out var staff))
        {
            if (!MathHelper.CloseTo(staff.ShotInstability, fireMode.ShotInstability))
            {
                staff.ShotInstability = fireMode.ShotInstability;
                Dirty(ent, staff);
            }
        }

        if (TryComp<GunComponent>(ent, out var gun))
        {
            _gun.UpdateBaseConfiguration(
                (ent, gun),
                soundGunshot: fireMode.SoundGunshot,
                fireRate: fireMode.FireRate,
                projectileSpeed: fireMode.ProjectileSpeed,
                minAngle: fireMode.MinAngle is { } minAngle ? Angle.FromDegrees(minAngle) : null,
                maxAngle: fireMode.MaxAngle is { } maxAngle ? Angle.FromDegrees(maxAngle) : null,
                availableModes: fireMode.AvailableModes,
                selectedMode: fireMode.SelectedMode);
        }

        if (user != null &&
            _prototypeManager.TryIndex<EntityPrototype>(fireMode.Prototype, out var prototype))
        {
            _popupSystem.PopupClient(
                Loc.GetString("gun-set-fire-mode-popup", ("mode", prototype.Name)),
                ent,
                user.Value);
        }
    }

}
