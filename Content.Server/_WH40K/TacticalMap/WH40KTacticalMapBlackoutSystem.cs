using Content.Shared._WH40K.TacticalMap;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._WH40K.TacticalMap;

public sealed partial class WH40KTacticalMapBlackoutSystem : SharedWH40KTacticalMapBlackoutSystem
{
    [Dependency] private  SharedMapSystem _maps = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        SubscribeLocalEvent<SetWH40KTacticalMapBlackoutComponent, ComponentStartup>(OnFlagStartup);
        SubscribeLocalEvent<SetWH40KTacticalMapBlackoutComponent, MapInitEvent>(OnFlagMapInit);
        SubscribeLocalEvent<SetWH40KTacticalMapBlackoutComponent, AnchorStateChangedEvent>(OnFlagAnchorChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SetWH40KTacticalMapBlackoutComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            TryApplyAndDelete((uid, marker), xform);
        }
    }

    private void OnFlagStartup(Entity<SetWH40KTacticalMapBlackoutComponent> ent, ref ComponentStartup args)
    {
        TryApplyAndDelete(ent);
    }

    private void OnFlagMapInit(Entity<SetWH40KTacticalMapBlackoutComponent> ent, ref MapInitEvent args)
    {
        TryApplyAndDelete(ent);
    }

    private void OnFlagAnchorChanged(Entity<SetWH40KTacticalMapBlackoutComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored || args.Detaching)
            return;

        TryApplyAndDelete(ent, args.Transform);
    }

    private bool TryApplyAndDelete(
        Entity<SetWH40KTacticalMapBlackoutComponent> ent,
        TransformComponent? xform = null)
    {
        xform ??= Transform(ent.Owner);

        if (xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return false;

        var index = _maps.LocalToTile(gridUid, grid, xform.Coordinates);
        SetBlackout((gridUid, grid, null), index, ent.Comp.Value);
        QueueDel(ent.Owner);
        return true;
    }
}
