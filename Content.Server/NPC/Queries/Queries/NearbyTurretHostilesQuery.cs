using Content.Server.NPC.Systems;

namespace Content.Server.NPC.Queries.Queries;

/// <summary>
/// Returns nearby hostile entities for turret AI.
/// Unlike the generic hostile query, this keeps mixed-faction entities targetable
/// if they still belong to a hostile faction.
/// </summary>
public sealed partial class NearbyTurretHostilesQuery : UtilityQuery
{

}
