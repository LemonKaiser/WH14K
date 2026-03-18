using System;
using System.Linq;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server.Engineering.EntitySystems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._WH40K.Combat.Inflatable;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat.Inflatable;

public sealed class WH40KInflatableDeploySystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KInflatableDeployComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KInflatableDeployComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WH40KInflatableDeployComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WH40KInflatableDeployComponent> ent, ref MapInitEvent args)
    {
        _useDelay.SetLength(
            (ent.Owner, CompOrNull<UseDelayComponent>(ent.Owner)),
            ent.Comp.ItemCooldown,
            ent.Comp.UseDelayId);
    }

    private async void OnAfterInteract(EntityUid uid, WH40KInflatableDeployComponent component, AfterInteractEvent args)
    {
        if (!args.CanReach && !component.IgnoreDistance)
            return;

        if (!CanUserDeployForTeam(args.User, component))
            return;

        if (!TryResetItemCooldown(uid, component, args.User))
            return;

        if (!TryConsumeUserThrottle(args.User, component))
            return;

        if (!TryGetGridAndTile(args.ClickLocation, out var gridUid, out var grid, out var tileRef))
        {
            PopupCaution(args.User, "wh40k-inflatable-popup-invalid-tile");
            return;
        }

        if (!IsTileClear(tileRef))
        {
            PopupCaution(args.User, "wh40k-inflatable-popup-blocked-tile");
            return;
        }

        if (component.DeployDoAfter > TimeSpan.Zero)
        {
            var doAfterArgs = new DoAfterArgs(
                EntityManager,
                args.User,
                (float) component.DeployDoAfter.TotalSeconds,
                new AwaitedDoAfterEvent(),
                null)
            {
                BreakOnMove = true,
            };

            var result = await _doAfter.WaitDoAfter(doAfterArgs);
            if (result != DoAfterStatus.Finished)
                return;
        }

        if (TerminatingOrDeleted(uid))
            return;

        if (!TryGetGridAndTile(args.ClickLocation, out gridUid, out grid, out tileRef) || !IsTileClear(tileRef))
            return;

        if (component.ConsumeOnDeploy)
        {
            if (TryComp<StackComponent>(uid, out var stackComp))
            {
                if (!_stack.TryUse((uid, stackComp), 1))
                    return;
            }
            else
            {
                TryQueueDel(uid);
            }
        }

        var deployed = Spawn(component.DeployPrototype, args.ClickLocation.SnapToGrid(grid));
        var placed = EnsureComp<WH40KInflatablePlacedComponent>(deployed);
        placed.PlacedBy = args.User;
        placed.PlacedAt = _timing.CurTime;
        Dirty(deployed, placed);
    }

    private void OnExamined(Entity<WH40KInflatableDeployComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(WH40KInflatableDeployComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-inflatable-examine-deploy",
                ("seconds", (int) Math.Ceiling(ent.Comp.DeployDoAfter.TotalSeconds))));
            args.PushMarkup(Loc.GetString(
                "wh40k-inflatable-examine-limits",
                ("active", ent.Comp.MaxActiveDeployables),
                ("windowCount", ent.Comp.MaxDeploysPerWindow),
                ("windowSeconds", (int) Math.Ceiling(ent.Comp.DeployWindow.TotalSeconds))));
        }
    }

    private bool TryResetItemCooldown(EntityUid item, WH40KInflatableDeployComponent component, EntityUid user)
    {
        if (_useDelay.TryResetDelay(item, checkDelayed: true, id: component.UseDelayId))
            return true;

        var seconds = 1;
        if (_useDelay.TryGetDelayInfo((item, CompOrNull<UseDelayComponent>(item)), out var delayInfo, component.UseDelayId))
            seconds = Math.Max(1, (int) Math.Ceiling((delayInfo.EndTime - _timing.CurTime).TotalSeconds));

        PopupCaution(user, "wh40k-inflatable-popup-item-cooldown", ("seconds", seconds));
        return false;
    }

    private bool TryConsumeUserThrottle(EntityUid user, WH40KInflatableDeployComponent component)
    {
        var throttle = EnsureComp<WH40KInflatableDeployUserThrottleComponent>(user);
        var now = _timing.CurTime;

        if (throttle.NextAllowedDeployAt > now)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((throttle.NextAllowedDeployAt - now).TotalSeconds));
            PopupCaution(user, "wh40k-inflatable-popup-user-cooldown", ("seconds", seconds));
            return false;
        }

        while (throttle.RecentDeploys.Count > 0 &&
               now - throttle.RecentDeploys.Peek() > component.DeployWindow)
        {
            throttle.RecentDeploys.Dequeue();
        }

        if (throttle.RecentDeploys.Count >= component.MaxDeploysPerWindow)
        {
            var nextFree = throttle.RecentDeploys.Peek() + component.DeployWindow;
            var seconds = Math.Max(1, (int) Math.Ceiling((nextFree - now).TotalSeconds));
            PopupCaution(
                user,
                "wh40k-inflatable-popup-rate-limit",
                ("seconds", seconds),
                ("count", component.MaxDeploysPerWindow));
            return false;
        }

        var activeDeployables = CountActiveDeployables(user);
        if (activeDeployables >= component.MaxActiveDeployables)
        {
            PopupCaution(
                user,
                "wh40k-inflatable-popup-active-cap",
                ("count", component.MaxActiveDeployables));
            return false;
        }

        throttle.RecentDeploys.Enqueue(now);
        throttle.NextAllowedDeployAt = now + component.UserCooldown;
        return true;
    }

    private int CountActiveDeployables(EntityUid user)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WH40KInflatablePlacedComponent>();
        while (query.MoveNext(out var uid, out var placed))
        {
            if (TerminatingOrDeleted(uid) || placed.PlacedBy != user)
                continue;

            count++;
        }

        return count;
    }

    private bool CanUserDeployForTeam(EntityUid user, WH40KInflatableDeployComponent component)
    {
        if (!component.RequireTeam && component.AllowedTeamIds.Count == 0)
            return true;

        if (!TryResolveUserTeam(user, out var userTeamId))
        {
            if (component.RequireTeam || component.AllowedTeamIds.Count > 0)
                PopupCaution(user, "wh40k-inflatable-popup-no-team");

            return !(component.RequireTeam || component.AllowedTeamIds.Count > 0);
        }

        if (component.AllowedTeamIds.Count == 0)
            return true;

        var allowed = component.AllowedTeamIds.Any(allowedId =>
            string.Equals(allowedId, userTeamId, StringComparison.OrdinalIgnoreCase));
        if (allowed)
            return true;

        PopupCaution(user, "wh40k-inflatable-popup-wrong-team");
        return false;
    }

    private bool TryResolveUserTeam(EntityUid user, out string teamId)
    {
        teamId = string.Empty;

        if (TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (_teamRule.TryGetTeamIdFromEntity(user, out teamId))
            return true;

        if (!TryComp<MindComponent>(user, out var mind))
            return false;

        if (mind.CurrentEntity is { } attached)
        {
            if (TryComp<GhostComponent>(attached, out var attachedGhost) && attachedGhost.CanGhostInteract)
                return true;

            if (_teamRule.TryGetTeamIdFromEntity(attached, out teamId))
                return true;
        }

        if (mind.UserId is not { } userId)
            return false;

        return _teamRule.TryGetRememberedTeam(userId, out teamId);
    }

    private bool TryGetGridAndTile(
        EntityCoordinates clickLocation,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out TileRef tileRef)
    {
        gridUid = default;
        grid = default!;
        tileRef = default;

        var maybeGridUid = _transform.GetGrid(clickLocation);
        if (maybeGridUid == null)
            return false;

        gridUid = maybeGridUid.Value;
        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid) || mapGrid == null)
            return false;

        grid = mapGrid;

        return _maps.TryGetTileRef(gridUid, grid, clickLocation, out tileRef);
    }

    private bool IsTileClear(TileRef tileRef)
    {
        return !tileRef.Tile.IsEmpty && !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private void PopupCaution(EntityUid user, string locKey, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(locKey, args), user, user, PopupType.SmallCaution);
    }
}
