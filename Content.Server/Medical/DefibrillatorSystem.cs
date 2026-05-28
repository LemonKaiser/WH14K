using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Robust.Shared.Player;

namespace Content.Server.Medical;

public sealed partial class DefibrillatorSystem : SharedDefibrillatorSystem
{
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private RespiratorSystem _respirator = default!;
    [Dependency] private SharedMindSystem _mind = default!;

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
