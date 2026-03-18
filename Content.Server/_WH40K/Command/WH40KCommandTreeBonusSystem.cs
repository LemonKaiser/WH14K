using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.Command.Components;
using Content.Shared._WH40K.Command;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Command;

/// <summary>
/// Aggregates persistent command-tree gameplay bonuses from purchased team nodes.
/// </summary>
public sealed class WH40KCommandTreeBonusSystem : EntitySystem
{
    private const string CommandTreeTeamMapId = "WH40KCommandTreeTeamMap";
    private const string CommandTreeDefaultProfileId = "WH40KCommandTreeProfileDefault";

    [Dependency] private readonly IPrototypeManager _proto = default!;

    public WH40KCommandTreeTeamBonuses GetTeamBonuses(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId) || !TryResolveTreeProfileForTeam(teamId, out var profile))
            return default;

        var purchasedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out _, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var nodeId in node.PurchasedTreeNodeIds)
            {
                if (!string.IsNullOrWhiteSpace(nodeId))
                    purchasedNodeIds.Add(nodeId);
            }
        }

        if (purchasedNodeIds.Count == 0)
            return default;

        var bonuses = new WH40KCommandTreeTeamBonuses();
        foreach (var node in profile.Nodes)
        {
            if (!purchasedNodeIds.Contains(node.Id))
                continue;

            bonuses.MachineSpeedBonusPercent += Math.Max(0, node.MachineSpeedBonusPercent);
            bonuses.MachineStorageBonus += Math.Max(0, node.MachineStorageBonus);
            bonuses.CargoDeliverySpeedBonusPercent += Math.Max(0, node.CargoDeliverySpeedBonusPercent);
            bonuses.CargoMaxItemsBonusPercent += Math.Max(0, node.CargoMaxItemsBonusPercent);
            bonuses.CargoPriceDiscountPercent += Math.Max(0, node.CargoPriceDiscountPercent);
            bonuses.ResearchTimeSpeedBonusPercent += Math.Max(0, node.ResearchTimeSpeedBonusPercent);
            bonuses.ResearchPointBonusPercent += Math.Max(0, node.ResearchPointBonusPercent);
        }

        return bonuses;
    }

    private bool TryResolveTreeProfileForTeam(string teamId, out WH40KCommandTreeProfilePrototype profile)
    {
        profile = default!;

        if (!_proto.TryIndex<WH40KCommandTreeTeamMapPrototype>(CommandTreeTeamMapId, out var teamMap))
        {
            if (!_proto.TryIndex<WH40KCommandTreeProfilePrototype>(CommandTreeDefaultProfileId, out var defaultProfile) ||
                defaultProfile == null)
            {
                return false;
            }

            profile = defaultProfile;
            return true;
        }

        var profileId = teamMap.DefaultProfile;
        if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
        {
            profileId = directProfile;
        }
        else
        {
            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                profileId = mappedProfile;
                break;
            }
        }

        if (!_proto.TryIndex<WH40KCommandTreeProfilePrototype>(profileId, out var resolvedProfile) ||
            resolvedProfile == null)
        {
            return false;
        }

        profile = resolvedProfile;
        return true;
    }
}

public struct WH40KCommandTreeTeamBonuses
{
    public int MachineSpeedBonusPercent;
    public int MachineStorageBonus;
    public int CargoDeliverySpeedBonusPercent;
    public int CargoMaxItemsBonusPercent;
    public int CargoPriceDiscountPercent;
    public int ResearchTimeSpeedBonusPercent;
    public int ResearchPointBonusPercent;
}
