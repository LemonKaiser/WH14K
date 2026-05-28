using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Research.Components;
using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Research.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.Research;

/// <summary>
/// Team-locks WH40K research consoles and keeps research clients bound to a same-team R&D server.
/// </summary>
public sealed partial class WH40KResearchTeamSystem : EntitySystem
{
    [Dependency] private  WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  ResearchSystem _research = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KResearchTeamComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<WH40KResearchTeamComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KResearchTeamComponent, ResearchRegistrationChangedEvent>(OnResearchRegistrationChanged);
    }

    private void OnOpenAttempt(EntityUid uid, WH40KResearchTeamComponent component, ref ActivatableUIOpenAttemptEvent args)
    {
        if (IsUserAllowedForTeam(args.User, component.TeamId))
            return;

        if (!args.Silent)
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), uid, args.User);

        args.Cancel();
    }

    private void OnMapInit(EntityUid uid, WH40KResearchTeamComponent component, MapInitEvent args)
    {
        EnsureTeamServer(uid, component);
    }

    private void OnResearchRegistrationChanged(
        EntityUid uid,
        WH40KResearchTeamComponent component,
        ref ResearchRegistrationChangedEvent args)
    {
        EnsureTeamServer(uid, component);
    }

    private void EnsureTeamServer(EntityUid uid, WH40KResearchTeamComponent component)
    {
        if (!TryComp<ResearchClientComponent>(uid, out var client))
            return;

        if (string.IsNullOrWhiteSpace(component.TeamId))
            return;

        if (Transform(uid).GridUid is not { } gridUid)
            return;

        if (client.Server is { } currentServer &&
            IsServerMatchingTeamOnGrid(currentServer, component.TeamId, gridUid))
        {
            return;
        }

        if (client.Server != null)
            _research.UnregisterClient(uid, client);

        var targetServer = FindTeamServerOnGrid(gridUid, component.TeamId);
        if (targetServer != null)
            _research.RegisterClient(uid, targetServer.Value, client);
    }

    private EntityUid? FindTeamServerOnGrid(EntityUid gridUid, string teamId)
    {
        var query = EntityQueryEnumerator<ResearchServerComponent, WH40KResearchTeamComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var serverTeam, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            if (!string.Equals(serverTeam.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            return uid;
        }

        return null;
    }

    private bool IsServerMatchingTeamOnGrid(EntityUid serverUid, string teamId, EntityUid gridUid)
    {
        if (!TryComp<ResearchServerComponent>(serverUid, out _))
            return false;

        if (!TryComp<WH40KResearchTeamComponent>(serverUid, out var serverTeam))
            return false;

        if (Transform(serverUid).GridUid != gridUid)
            return false;

        return string.Equals(serverTeam.TeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsUserAllowedForTeam(EntityUid user, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return true;

        if (TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (_teamRule.TryGetTeamIdFromEntity(user, out var directTeamId) &&
            string.Equals(directTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryComp<MindComponent>(user, out var mind))
            return false;

        if (mind.CurrentEntity is { } currentEntity)
        {
            if (TryComp<GhostComponent>(currentEntity, out var currentGhost) && currentGhost.CanGhostInteract)
                return true;

            if (_teamRule.TryGetTeamIdFromEntity(currentEntity, out var currentTeamId) &&
                string.Equals(currentTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (mind.UserId is not { } userId)
            return false;

        return _teamRule.TryGetRememberedTeam(userId, out var rememberedTeamId) &&
               string.Equals(rememberedTeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }
}
