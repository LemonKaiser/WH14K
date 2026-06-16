using Content.Shared.Clothing;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared._WH40K.Clothing;

namespace Content.Server._WH40K.Clothing;

/// <summary>
/// Relays <see cref="StealthComponent"/> and <see cref="StealthOnMoveComponent"/>
/// from a chameleoline cloak to its wearer on equip, and removes them on unequip.
/// </summary>
public sealed partial class WH40KChameleonCloakSystem : EntitySystem
{
    [Dependency] private SharedStealthSystem _stealth = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChameleonCloakComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WH40KChameleonCloakComponent, ClothingGotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(Entity<WH40KChameleonCloakComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (!TryComp(args.Wearer, out StealthComponent? stealth))
        {
            stealth = EnsureComp<StealthComponent>(args.Wearer);
            ent.Comp.AddedStealth = true;
            _stealth.SetVisibility(args.Wearer, 1f, stealth);
        }
        else
        {
            ent.Comp.AddedStealth = false;
        }

        if (!TryComp(args.Wearer, out StealthOnMoveComponent? stealthOnMove))
        {
            stealthOnMove = EnsureComp<StealthOnMoveComponent>(args.Wearer);
            ent.Comp.AddedStealthOnMove = true;
        }
        else
        {
            ent.Comp.AddedStealthOnMove = false;
        }

        ent.Comp.PreviousPassiveVisibilityRate = stealthOnMove.PassiveVisibilityRate;
        ent.Comp.PreviousMovementVisibilityRate = stealthOnMove.MovementVisibilityRate;
        stealthOnMove.PassiveVisibilityRate = ent.Comp.PassiveVisibilityRate;
        stealthOnMove.MovementVisibilityRate = ent.Comp.MovementVisibilityRate;
        Dirty(args.Wearer, stealthOnMove);
    }

    private void OnUnequipped(Entity<WH40KChameleonCloakComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        if (ent.Comp.AddedStealthOnMove)
        {
            RemCompDeferred<StealthOnMoveComponent>(args.Wearer);
        }
        else if (TryComp(args.Wearer, out StealthOnMoveComponent? stealthOnMove))
        {
            stealthOnMove.PassiveVisibilityRate = ent.Comp.PreviousPassiveVisibilityRate;
            stealthOnMove.MovementVisibilityRate = ent.Comp.PreviousMovementVisibilityRate;
            Dirty(args.Wearer, stealthOnMove);
        }

        if (ent.Comp.AddedStealth)
            RemCompDeferred<StealthComponent>(args.Wearer);

        ent.Comp.AddedStealth = false;
        ent.Comp.AddedStealthOnMove = false;
    }
}
