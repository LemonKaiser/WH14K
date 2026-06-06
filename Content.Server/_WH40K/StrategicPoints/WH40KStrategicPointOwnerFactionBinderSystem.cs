using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.NPC.Prototypes;
using Content.Shared._WH40K.StrategicPoints;


namespace Content.Server._WH40K.StrategicPoints;

public sealed partial class WH40KStrategicPointOwnerFactionBinderSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Content.Shared.NPC.Systems.NpcFactionSystem _npcFaction = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KStrategicPointBuiltEvent>(OnPointBuilt);
        SubscribeLocalEvent<WH40KStrategicPointUpgradedEvent>(OnPointUpgraded);
        SubscribeLocalEvent<WH40KStrategicPointDestroyedEvent>(OnPointDestroyed);
    }

    private void OnPointBuilt(WH40KStrategicPointBuiltEvent ev)
    {
        TryBind(ev.PointUid);
    }

    private void OnPointUpgraded(WH40KStrategicPointUpgradedEvent ev)
    {
        TryBind(ev.PointUid);
    }

    private void OnPointDestroyed(WH40KStrategicPointDestroyedEvent ev)
    {
        // nothing
    }

    private void TryBind(EntityUid pointUid)
    {
        if (!TryComp<WH40KStrategicPointOwnerFactionBinderComponent>(pointUid, out var binder) ||
            !TryComp<WH40KStrategicPointComponent>(pointUid, out var point) ||
            !TryComp<NpcFactionMemberComponent>(pointUid, out var factionMember))
        {
            return;
        }

        var owner = point.OwnerTeamId;
        if (string.IsNullOrWhiteSpace(owner))
        {
            _npcFaction.ClearFactions((pointUid, factionMember), dirty: true);
            return;
        }

        ProtoId<NpcFactionPrototype>? factionId = null;

        if (string.Equals(owner, binder.ImperiumTeamId, StringComparison.OrdinalIgnoreCase))
            factionId = binder.ImperiumFaction;
        else if (string.Equals(owner, binder.HereticsTeamId, StringComparison.OrdinalIgnoreCase))
            factionId = binder.HereticsFaction;

        if (factionId is null)
        {
            _npcFaction.ClearFactions((pointUid, factionMember), dirty: true);
            return;
        }

        _npcFaction.ClearFactions((pointUid, factionMember), dirty: false);
        _npcFaction.AddFactions((pointUid, factionMember), new HashSet<ProtoId<NpcFactionPrototype>> { factionId.Value }, dirty: true);

    }
}
