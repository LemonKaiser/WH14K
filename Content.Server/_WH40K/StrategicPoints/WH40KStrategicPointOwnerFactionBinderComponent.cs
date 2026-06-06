using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Server._WH40K.StrategicPoints;

/// <summary>
/// Binds strategic point ownership (OwnerTeamId) to NpcFactionMember.factions on the same entity.
/// Used so embedded turrets can always target enemies correctly for the owning team.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KStrategicPointOwnerFactionBinderComponent : Component
{
    /// <summary>Faction proto ID to use when strategic point is owned by the Imperium side.</summary>
    [DataField("imperiumFaction")]
    public ProtoId<NpcFactionPrototype> ImperiumFaction = default!;

    /// <summary>Faction proto ID to use when strategic point is owned by the Heretics side.</summary>
    [DataField("hereticsFaction")]
    public ProtoId<NpcFactionPrototype> HereticsFaction = default!;

    /// <summary>Team id strings that should be considered Imperium/Heretics owners.</summary>
    [DataField("imperiumTeamId")]
    public string ImperiumTeamId = "Imperium";

    [DataField("hereticsTeamId")]
    public string HereticsTeamId = "Heretics";
}


