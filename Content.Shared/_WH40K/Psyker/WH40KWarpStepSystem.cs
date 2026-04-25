namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// WH40K combat blink backend.
/// TargetAction validation keeps range/same-map checks; this avoids the base magic line-of-sight cast check.
/// </summary>
public sealed class WH40KWarpStepSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KWarpStepActionEvent>(OnWarpStep);
    }

    private void OnWarpStep(WH40KWarpStepActionEvent args)
    {
        if (args.Handled)
            return;

        var xform = Transform(args.Performer);
        if (xform.MapID != _transform.GetMapId(args.Target))
            return;

        _transform.SetCoordinates(args.Performer, args.Target);
        _transform.AttachToGridOrMap(args.Performer, xform);
        args.Handled = true;
    }
}
