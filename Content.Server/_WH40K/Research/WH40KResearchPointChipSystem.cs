using Content.Server._WH40K.Research.Components;
using Content.Server._WH40K.Command;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Shared.Interaction;
using Content.Shared.Research.Components;
using Content.Shared.Stacks;

namespace Content.Server._WH40K.Research;

public sealed partial class WH40KResearchPointChipSystem : EntitySystem
{
    [Dependency] private  AccessReaderSystem _accessReader = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  ResearchSystem _research = default!;
    [Dependency] private  WH40KResearchTeamSystem _researchTeam = default!;
    [Dependency] private  WH40KCommandTreeBonusSystem _treeBonuses = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KResearchPointChipComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(EntityUid uid, WH40KResearchPointChipComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (args.Target is not { } target)
            return;

        EntityUid serverUid;
        ResearchServerComponent server;
        if (TryComp<ResearchServerComponent>(target, out var directServer))
        {
            serverUid = target;
            server = directServer;
        }
        else if (TryComp<ResearchClientComponent>(target, out var client) &&
                 _research.TryGetClientServer(target, out var linkedServerUid, out var linkedServer, client))
        {
            serverUid = linkedServerUid.Value;
            server = linkedServer;
        }
        else
        {
            return;
        }

        if (TryComp<AccessReaderComponent>(target, out var access) &&
            !_accessReader.IsAllowed(args.User, target, access))
        {
            _popup.PopupEntity(Loc.GetString("research-console-no-access-popup"), target, args.User);
            return;
        }

        if ((TryComp<WH40KResearchTeamComponent>(target, out var targetTeam) &&
             !_researchTeam.IsUserAllowedForTeam(args.User, targetTeam.TeamId)) ||
            (TryComp<WH40KResearchTeamComponent>(serverUid, out var serverTeam) &&
             !_researchTeam.IsUserAllowedForTeam(args.User, serverTeam.TeamId)))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), target, args.User);
            return;
        }

        var count = TryComp<StackComponent>(uid, out var stack) ? stack.Count : 1;
        var addedPoints = count * component.PointsPerUnit;
        if (TryComp<WH40KResearchTeamComponent>(serverUid, out var serverResearchTeam) &&
            !string.IsNullOrWhiteSpace(serverResearchTeam.TeamId))
        {
            var bonuses = _treeBonuses.GetTeamBonuses(serverResearchTeam.TeamId);
            if (bonuses.ResearchPointBonusPercent > 0)
            {
                addedPoints = (int) System.Math.Round(
                    addedPoints * (1f + bonuses.ResearchPointBonusPercent / 100f),
                    System.MidpointRounding.AwayFromZero);
            }
        }

        _research.ModifyServerPoints(serverUid, addedPoints, server);

        _popup.PopupEntity(
            Loc.GetString(
                "wh40k-research-chip-uploaded",
                ("count", count),
                ("points", addedPoints)),
            target,
            args.User);

        QueueDel(uid);
        args.Handled = true;
    }
}
