using Content.Server.Pinpointer;
using Content.Shared.Pinpointer;
using Content.Shared._WH40K.MurderMystery;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.MurderMystery;

/// <summary>
/// Keeps every Murder Mystery sheriff pinpointer pointed at the sheriff
/// revolver while it is loose on the ground (not nested inside any player's
/// inventory). While the revolver is held the pinpointer is forced into an
/// unknown-distance state so the holder (the sheriff) is not exposed.
/// </summary>
public sealed partial class WH40KMurderMysterySheriffPinpointerSystem : EntitySystem
{
    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // First, locate the loose sheriff revolver for this tick.
        EntityUid? looseRevolver = null;
        var revolverQuery = EntityQueryEnumerator<WH40KMurderMysterySheriffRevolverComponent, TransformComponent>();
        while (revolverQuery.MoveNext(out var revolverUid, out _, out var revolverXform))
        {
            if (TerminatingOrDeleted(revolverUid))
                continue;

            if (IsNestedInsideAnyPlayer(revolverUid, revolverXform))
                continue;

            looseRevolver = revolverUid;
            break;
        }

        // Update every sheriff pinpointer.
        var pinQuery = EntityQueryEnumerator<WH40KMurderMysterySheriffPinpointerComponent, PinpointerComponent>();
        while (pinQuery.MoveNext(out var uid, out var sheriffPin, out var pinpointer))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (sheriffPin.NextRefreshAt > now)
                continue;

            sheriffPin.NextRefreshAt = now + sheriffPin.RefreshInterval;
            RefreshPinpointer(uid, pinpointer, looseRevolver);
        }
    }

    private void RefreshPinpointer(
        EntityUid uid,
        PinpointerComponent pinpointer,
        EntityUid? looseRevolver)
    {
        // Always keep the screen active so the arrow state is visible.
        if (!pinpointer.IsActive)
            _pinpointer.SetActive((uid, pinpointer), true);

        if (looseRevolver is { } revolver && !TerminatingOrDeleted(revolver))
        {
            _pinpointer.SetTarget((uid, pinpointer), revolver);
            _pinpointer.SetTargetName(uid, Loc.GetString("wh40k-murder-mystery-pinpointer-target"), pinpointer);
        }
        else
        {
            // Revolver is held or does not exist: hide the direction.
            _pinpointer.SetTarget((uid, pinpointer), null);
            _pinpointer.SetTargetName(uid, Loc.GetString("wh40k-murder-mystery-pinpointer-hidden"), pinpointer);
            _pinpointer.SetDistance((uid, pinpointer), Distance.Unknown);
        }
    }

    /// <summary>
    /// Walks the parent chain looking for any entity carrying a
    /// <see cref="WH40KMurderMysteryPlayerComponent"/>. If the revolver is
    /// nested under such an entity (in hands, inventory, backpack, etc.) it
    /// counts as "held" and pinpointers should not track it.
    /// </summary>
    private bool IsNestedInsideAnyPlayer(EntityUid entity, TransformComponent? startXform = null)
    {
        if (!_xformQuery.TryGetComponent(entity, out var xform))
            return false;

        var current = xform.ParentUid;
        var depth = 0;
        while (current != EntityUid.Invalid && depth < 16)
        {
            if (HasComp<WH40KMurderMysteryPlayerComponent>(current))
                return true;

            if (!_xformQuery.TryGetComponent(current, out var parentXform))
                return false;

            if (parentXform.ParentUid == EntityUid.Invalid || parentXform.ParentUid == current)
                return false;

            current = parentXform.ParentUid;
            depth++;
        }

        return false;
    }
}
