using System.Linq;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._WH40K.MurderMystery;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Station.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.MurderMystery;

public sealed partial class WH40KMurderMysteryRuleSystem
{
    private static readonly CollisionGroup ClueSpawnBlockMask = CollisionGroup.Impassable;

    /// <summary>
    /// Periodically spawns clue entities on a random free tile of the play
    /// grid. Runs only while the round is active (roles assigned, not waiting,
    /// no winner yet) and the concurrent clue cap has not been reached.
    /// </summary>
    private void UpdateClueSpawning(WH40KMurderMysteryRuleComponent rule)
    {
        if (!rule.RolesAssigned || rule.WaitingForPlayers || rule.WinnerTeam != null)
            return;

        var now = _timing.CurTime;
        if (rule.NextClueSpawnAt == TimeSpan.Zero)
            rule.NextClueSpawnAt = now + rule.ClueSpawnInterval;

        if (now < rule.NextClueSpawnAt)
            return;

        rule.NextClueSpawnAt = now + rule.ClueSpawnInterval;

        var existing = CountActiveClues();
        if (existing >= rule.MaxConcurrentClues)
            return;

        if (TryFindFreeGridTile(out var gridUid, out var grid, out var tileIndices, out var coordinates))
        {
            var clue = Spawn(rule.CluePrototype, coordinates);
            _sawmill.Debug(
                "Spawned Murder Mystery clue {Clue} at {Coords} on grid {Grid} ({Existing} now exist)",
                ToPrettyString(clue),
                coordinates,
                gridUid,
                existing + 1);
        }
        else
        {
            _sawmill.Warning("Could not find a free tile for Murder Mystery clue spawn");
        }
    }

    private int CountActiveClues()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WH40KMurderMysteryClueComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!TerminatingOrDeleted(uid))
                count++;
        }

        return count;
    }

    private bool TryFindFreeGridTile(
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices,
        out EntityCoordinates coordinates)
    {
        gridUid = EntityUid.Invalid;
        grid = default!;
        tileIndices = default;
        coordinates = EntityCoordinates.Invalid;

        var grids = CollectStationGrids();
        if (grids.Count == 0)
            return false;

        const int maxAttempts = 24;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = grids[_random.Next(grids.Count)];

            var allTiles = _map.GetAllTiles(candidate.Owner, candidate.Comp).ToList();
            if (allTiles.Count == 0)
                continue;

            var tileRef = allTiles[_random.Next(allTiles.Count)];
            if (tileRef.Tile.IsEmpty)
                continue;

            if (_turf.IsSpace(tileRef))
                continue;

            if (_turf.IsTileBlocked(tileRef, ClueSpawnBlockMask))
                continue;

            gridUid = candidate.Owner;
            grid = candidate.Comp;
            tileIndices = tileRef.GridIndices;
            coordinates = new EntityCoordinates(candidate.Owner, tileIndices.X, tileIndices.Y);
            return true;
        }

        return false;
    }

    private List<Entity<MapGridComponent>> CollectStationGrids()
    {
        var result = new List<Entity<MapGridComponent>>();

        var stationQuery = EntityQueryEnumerator<StationDataComponent>();
        while (stationQuery.MoveNext(out var stationUid, out var stationData))
        {
            foreach (var gridId in stationData.Grids)
            {
                if (gridId == EntityUid.Invalid)
                    continue;

                if (!TryComp<MapGridComponent>(gridId, out var gridComp))
                    continue;

                if (Transform(gridId).MapID == MapId.Nullspace)
                    continue;

                result.Add((gridId, gridComp));
            }
        }

        if (result.Count == 0)
        {
            var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var gridUid, out var gridComp, out var xform))
            {
                if (xform.MapID == MapId.Nullspace)
                    continue;

                result.Add((gridUid, gridComp));
            }
        }

        return result;
    }

    /// <summary>
    /// Left-click on a clue entity on the ground.
    /// </summary>
    private void OnClueActivate(Entity<WH40KMurderMysteryClueComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        if (!CanCollectClue(args.User, rule, out var userId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-murder-mystery-clue-cannot-collect"), ent.Owner, args.User);
            args.Handled = true;
            return;
        }

        CollectClue(ent.Owner, args.User, userId, rule);
        args.Handled = true;
    }

    /// <summary>
    /// Right-click verb "Pick up clue".
    /// </summary>
    private void OnClueActivationVerbs(Entity<WH40KMurderMysteryClueComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryGetActiveRule(out _, out var rule))
            return;

        if (!CanCollectClue(args.User, rule, out _))
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("wh40k-murder-mystery-clue-pickup-verb"),
            Act = () => CollectClue(ent.Owner, user, default, rule),
        });
    }

    /// <summary>
    /// Blocks the clue from being picked up into hands/inventory by anyone.
    /// Collection goes through ActivateInWorldEvent / the verb instead.
    /// </summary>
    private void OnCluePickupAttempt(Entity<WH40KMurderMysteryClueComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    /// Only civilians (and unassigned players, who become civilians unless they
    /// later touch the revolver) may collect clues. Sheriffs and murders may not,
    /// so the sheriff can't farm clues to re-arm and murders can't deny them.
    /// </summary>
    private bool CanCollectClue(EntityUid user, WH40KMurderMysteryRuleComponent rule, out NetUserId userId)
    {
        if (!TryComp<WH40KMurderMysteryPlayerComponent>(user, out var playerComp))
        {
            userId = default;
            return false;
        }

        if (playerComp.Role is WH40KMurderMysteryRole.Sheriff or WH40KMurderMysteryRole.Murder)
        {
            userId = default;
            return false;
        }

        if (!TryGetUserId(user, out userId))
            return false;

        if (rule.CluesCollected.GetValueOrDefault(userId) >= rule.CluesToRevolver)
            return false;

        return true;
    }

    /// <summary>
    /// Collects a clue: deletes the entity, increments the player's tally, and
    /// if the threshold is reached promotes them to sheriff with the revolver.
    /// </summary>
    private void CollectClue(EntityUid clueEntity, EntityUid user, NetUserId knownUserId, WH40KMurderMysteryRuleComponent rule)
    {
        if (!TryGetUserId(user, out var userId))
            return;

        if (rule.CluesCollected.GetValueOrDefault(userId) >= rule.CluesToRevolver)
            return;

        var newCount = rule.CluesCollected.GetValueOrDefault(userId) + 1;
        rule.CluesCollected[userId] = newCount;

        var remaining = rule.CluesToRevolver - newCount;
        var popupKey = remaining > 0
            ? "wh40k-murder-mystery-clue-collected"
            : "wh40k-murder-mystery-clue-collected-final";
        var popupText = Loc.GetString(popupKey, ("remaining", remaining), ("collected", newCount), ("required", rule.CluesToRevolver));
        _popup.PopupEntity(popupText, user, user);

        if (!TerminatingOrDeleted(clueEntity))
            Del(clueEntity);

        if (newCount >= rule.CluesToRevolver)
            PromoteClueCollectorToSheriff(user, userId, rule);
    }

    /// <summary>
    /// Converts a civilian who collected enough clues into the sheriff: assigns
    /// the role, spawns the sheriff revolver, and broadcasts the promotion.
    /// </summary>
    private void PromoteClueCollectorToSheriff(EntityUid mob, NetUserId userId, WH40KMurderMysteryRuleComponent rule)
    {
        if (!TryComp<WH40KMurderMysteryPlayerComponent>(mob, out var playerComp))
            return;

        if (playerComp.Role is WH40KMurderMysteryRole.Sheriff or WH40KMurderMysteryRole.Murder)
            return;

        playerComp.Role = WH40KMurderMysteryRole.Sheriff;
        rule.PlayerRoles[userId] = WH40KMurderMysteryRole.Sheriff;
        EnsureSheriffRevolver(mob, rule);
        SendRoleBriefing(userId, WH40KMurderMysteryRole.Sheriff, promotedSheriff: true);

        var announcement = Loc.GetString("wh40k-murder-mystery-clue-sheriff-emerged");
        foreach (var session in _player.Sessions)
        {
            if (session.Status == Robust.Shared.Enums.SessionStatus.InGame)
                _chat.DispatchServerMessage(session, announcement);
        }
    }
}
