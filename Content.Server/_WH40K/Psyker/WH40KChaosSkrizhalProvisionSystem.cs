using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// R2 runtime:
/// - grants default chaos skrizhal on chaos-role spawn;
/// - binds the starter skrizhal to owner progression state.
/// </summary>
public sealed class WH40KChaosSkrizhalProvisionSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    private const string DefaultSkrizhalPrototype = "WH40KRuneSkrizhalChaos";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosRoleStartupEvent>(OnChaosRoleStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Startup relay can be skipped by load-order edge cases.
        // Keep a cheap runtime fallback to guarantee starter skrizhal issuance.
        var query = EntityQueryEnumerator<WH40KChaosGiftRoleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
            if (progression.StarterSkrizhalIssued)
                continue;

            EnsureStarterSkrizhal(uid);
        }
    }

    private void OnChaosRoleStartup(WH40KChaosRoleStartupEvent args)
    {
        EnsureStarterSkrizhal(args.User);
    }

    private void EnsureStarterSkrizhal(EntityUid uid)
    {
        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
        if (progression.StarterSkrizhalIssued)
            return;

        if (TryFindHeldSkrizhal(uid, out var heldSkrizhal))
        {
            BindSkrizhal(uid, heldSkrizhal, progression);
            return;
        }

        var skrizhal = Spawn(DefaultSkrizhalPrototype, Transform(uid).Coordinates);
        BindSkrizhal(uid, skrizhal, progression);

        var picked = _hands.TryPickupAnyHand(
            uid,
            skrizhal,
            checkActionBlocker: false,
            animateUser: false,
            animate: false);

        if (!picked)
        {
            _hands.TryForcePickupAnyHand(
                uid,
                skrizhal,
                checkActionBlocker: false);
        }
    }

    private void BindSkrizhal(EntityUid owner, EntityUid skrizhal, WH40KChaosGiftProgressionComponent progression)
    {
        if (TryComp<WH40KChaosSkrizhalComponent>(skrizhal, out var skrizhalComp))
            skrizhalComp.BoundOwner = owner;

        progression.BoundSkrizhal = skrizhal;
        progression.StarterSkrizhalIssued = true;
        Dirty(owner, progression);
    }

    private bool TryFindHeldSkrizhal(EntityUid uid, out EntityUid skrizhal)
    {
        skrizhal = default;

        if (!TryComp<HandsComponent>(uid, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (TryComp<WH40KChaosSkrizhalComponent>(held, out _))
            {
                skrizhal = held;
                return true;
            }
        }

        return false;
    }
}
