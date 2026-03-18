using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Robust.Shared.Player;

namespace Content.Server.Medical;

public sealed class DefibrillatorSystem : SharedDefibrillatorSystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RespiratorComponent, TargetDefibrillatedEvent>(OnTargetDefibrillated);
    }

    private void OnTargetDefibrillated(Entity<RespiratorComponent> ent, ref TargetDefibrillatedEvent args)
    {
        if (!args.RevivedFromDead)
            return;

        _respirator.RestoreSaturationBuffer((ent.Owner, ent.Comp));
    }

    protected override void OpenReturnToBodyEui(Entity<MindComponent> mind, ICommonSession session)
    {
        _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _player), session);
    }
}
