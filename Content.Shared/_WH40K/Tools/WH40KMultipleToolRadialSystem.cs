using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;

namespace Content.Shared._WH40K.Tools;

public sealed partial class WH40KMultipleToolRadialSystem : EntitySystem
{
    [Dependency] private  SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KMultipleToolRadialComponent, WH40KMultipleToolRadialSelectMessage>(OnSelect);
    }

    private void OnSelect(EntityUid uid, WH40KMultipleToolRadialComponent component, WH40KMultipleToolRadialSelectMessage args)
    {
        if (!TryComp<MultipleToolComponent>(uid, out var multiple))
            return;

        _tool.SetMultipleToolEntry(uid, args.Entry, multiple, args.Actor);
    }
}
