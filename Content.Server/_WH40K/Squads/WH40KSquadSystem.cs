using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.Actions;
using Content.Shared._WH40K.Squads;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Squads;

public sealed class WH40KSquadSystem : EntitySystem
{
    private static readonly Color DefaultImperiumColor = Color.FromHex("#D6B24A".AsSpan());
    private static readonly Color DefaultHereticsColor = Color.FromHex("#A64747".AsSpan());

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KSquadLeaderComponent, MapInitEvent>(OnLeaderMapInit);
        SubscribeLocalEvent<WH40KSquadLeaderComponent, ComponentStartup>(OnLeaderStartup);
        SubscribeLocalEvent<WH40KSquadLeaderComponent, ComponentShutdown>(OnLeaderShutdown);
        SubscribeLocalEvent<WH40KSquadLeaderComponent, MobStateChangedEvent>(OnLeaderMobStateChanged);

        SubscribeLocalEvent<WH40KSquadAssignableComponent, ComponentShutdown>(OnAssignableShutdown);
        SubscribeLocalEvent<WH40KSquadAssignableComponent, MobStateChangedEvent>(OnAssignableMobStateChanged);
        SubscribeLocalEvent<WH40KSquadConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);

        Subs.BuiEvents<WH40KSquadConsoleComponent>(WH40KSquadUiKey.Key, subs =>
        {
            subs.Event<WH40KSquadCreateMessage>(OnCreateRequested);
            subs.Event<WH40KSquadDisbandMessage>(OnDisbandRequested);
            subs.Event<WH40KSquadAssignMessage>(OnAssignRequested);
            subs.Event<WH40KSquadRemoveMessage>(OnRemoveRequested);
            subs.Event<WH40KSquadRefreshMessage>(OnRefreshRequested);
        });
    }

    private void OnLeaderMapInit(Entity<WH40KSquadLeaderComponent> ent, ref MapInitEvent args)
    {
        EnsureLeaderRuntime(ent);
    }

    private void OnLeaderStartup(Entity<WH40KSquadLeaderComponent> ent, ref ComponentStartup args)
    {
        EnsureLeaderRuntime(ent);
    }

    private void OnLeaderShutdown(Entity<WH40KSquadLeaderComponent> ent, ref ComponentShutdown args)
    {
        DisbandSquad(ent, notifyAll: false);

        if (ent.Comp.ActionEntity is { } action)
            _actions.RemoveAction(ent.Owner, action);

        ent.Comp.ActionEntity = null;

        if (ent.Comp.ControllerEntity is { } controller && !TerminatingOrDeleted(controller))
        {
            _ui.CloseUi(controller, WH40KSquadUiKey.Key);
            QueueDel(controller);
        }

        ent.Comp.ControllerEntity = null;
    }

    private void OnLeaderMobStateChanged(Entity<WH40KSquadLeaderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (ent.Comp.ControllerEntity is { } controller && !TerminatingOrDeleted(controller))
            _ui.CloseUi(controller, WH40KSquadUiKey.Key);

        DisbandSquad(ent);
    }

    private void OnAssignableShutdown(Entity<WH40KSquadAssignableComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.AssignedLeader is not { } leaderUid)
            return;

        ent.Comp.AssignedLeader = null;
        ent.Comp.AssignedSlot = 0;
        ent.Comp.TeamId = string.Empty;

        if (TryComp<WH40KSquadLeaderComponent>(leaderUid, out var leaderComp))
            RefreshUi((leaderUid, leaderComp));

        RefreshAllOpenUis();
    }

    private void OnAssignableMobStateChanged(Entity<WH40KSquadAssignableComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.OldMobState == args.NewMobState)
            return;

        RefreshAllOpenUis();
    }

    private void OnUiOpened(Entity<WH40KSquadConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
        {
            _ui.CloseUi(ent.Owner, WH40KSquadUiKey.Key, args.Actor);
            return;
        }

        EnsureLeaderRuntime(leader);
        RefreshUi(leader);
    }

    private void OnCreateRequested(Entity<WH40KSquadConsoleComponent> ent, ref WH40KSquadCreateMessage args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
            return;

        UpdateLeaderTeamState(leader);
        if (string.IsNullOrWhiteSpace(leader.Comp.TeamId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-squad-popup-no-team"), leader.Owner, args.Actor);
            return;
        }

        if (!leader.Comp.SquadActive)
        {
            leader.Comp.SquadActive = true;
            Dirty(leader);
        }

        RefreshAllOpenUis();
    }

    private void OnDisbandRequested(Entity<WH40KSquadConsoleComponent> ent, ref WH40KSquadDisbandMessage args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
            return;

        DisbandSquad(leader);
    }

    private void OnAssignRequested(Entity<WH40KSquadConsoleComponent> ent, ref WH40KSquadAssignMessage args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
            return;

        if (!TryGetEntity(args.Target, out var targetUidNullable) ||
            targetUidNullable is not { } targetUid)
            return;

        EnsureLeaderRuntime(leader);
        UpdateLeaderTeamState(leader);
        if (!leader.Comp.SquadActive)
            return;

        if (!TryComp(targetUid, out WH40KSquadAssignableComponent? assignable) ||
            !TryComp(targetUid, out WH40KTeamMemberComponent? member) ||
            string.IsNullOrWhiteSpace(leader.Comp.TeamId))
        {
            return;
        }

        if (!SameTeam(leader.Comp.TeamId, member.TeamId) || IsDead(targetUid))
            return;

        if (assignable.AssignedLeader is { } otherLeader && otherLeader != leader.Owner)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-squad-popup-already-assigned"), leader.Owner, args.Actor);
            RefreshAllOpenUis();
            return;
        }

        CleanupLeaderAssignments(leader, leader.Comp.TeamId);

        if (assignable.AssignedLeader == leader.Owner)
        {
            RefreshUi(leader);
            return;
        }

        if (!TryGetFirstFreeSlot(leader, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-squad-popup-full"), leader.Owner, args.Actor);
            RefreshUi(leader);
            return;
        }

        assignable.AssignedLeader = leader.Owner;
        assignable.AssignedSlot = slot;
        assignable.TeamId = leader.Comp.TeamId;
        Dirty(targetUid, assignable);

        RefreshAllOpenUis();
    }

    private void OnRemoveRequested(Entity<WH40KSquadConsoleComponent> ent, ref WH40KSquadRemoveMessage args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
            return;

        var removed = false;
        var query = EntityQueryEnumerator<WH40KSquadAssignableComponent>();
        while (query.MoveNext(out var memberUid, out var assignable))
        {
            if (assignable.AssignedLeader != leader.Owner || assignable.AssignedSlot != args.SlotIndex)
                continue;

            assignable.AssignedLeader = null;
            assignable.AssignedSlot = 0;
            assignable.TeamId = string.Empty;
            Dirty(memberUid, assignable);
            removed = true;
            break;
        }

        if (removed)
            RefreshAllOpenUis();
        else
            RefreshUi(leader);
    }

    private void OnRefreshRequested(Entity<WH40KSquadConsoleComponent> ent, ref WH40KSquadRefreshMessage args)
    {
        if (!TryResolveConsoleLeader(ent, args.Actor, out var leader))
            return;

        RefreshUi(leader);
    }

    private void EnsureLeaderRuntime(Entity<WH40KSquadLeaderComponent> leader)
    {
        UpdateLeaderTeamState(leader);
        var controller = EnsureController(leader);
        _actions.AddAction(leader.Owner, ref leader.Comp.ActionEntity, leader.Comp.ActionPrototype, controller);
    }

    private EntityUid EnsureController(Entity<WH40KSquadLeaderComponent> leader)
    {
        if (leader.Comp.ControllerEntity is { } existing &&
            !TerminatingOrDeleted(existing) &&
            TryComp<WH40KSquadConsoleComponent>(existing, out var existingConsole))
        {
            existingConsole.Leader = leader.Owner;
            return existing;
        }

        var controller = Spawn("WH40KSquadConsoleController", new EntityCoordinates(leader.Owner, Vector2.Zero));
        var console = EnsureComp<WH40KSquadConsoleComponent>(controller);
        console.Leader = leader.Owner;
        leader.Comp.ControllerEntity = controller;
        Dirty(leader);
        return controller;
    }

    private bool TryResolveConsoleLeader(
        Entity<WH40KSquadConsoleComponent> console,
        EntityUid actor,
        out Entity<WH40KSquadLeaderComponent> leader)
    {
        leader = default;

        if (console.Comp.Leader is not { } leaderUid ||
            TerminatingOrDeleted(leaderUid) ||
            actor != leaderUid ||
            !TryComp<WH40KSquadLeaderComponent>(leaderUid, out var leaderComp))
        {
            return false;
        }

        leader = (leaderUid, leaderComp);
        return true;
    }

    private void UpdateLeaderTeamState(Entity<WH40KSquadLeaderComponent> leader)
    {
        var resolved = ResolveTeamId(leader.Owner, leader.Comp.TeamId);
        if (resolved == leader.Comp.TeamId)
            return;

        leader.Comp.TeamId = resolved;
        Dirty(leader);
    }

    private string ResolveTeamId(EntityUid uid, string? fallback = null)
    {
        if (_teamRule.TryGetTeamIdFromEntity(uid, out var teamId) && TryCanonicalizeTeamId(teamId, out var canonical))
            return canonical;

        if (TryCanonicalizeTeamId(fallback, out canonical))
            return canonical;

        return string.Empty;
    }

    private WH40KSquadBuiState BuildState(Entity<WH40KSquadLeaderComponent> leader)
    {
        UpdateLeaderTeamState(leader);

        var teamId = leader.Comp.TeamId;
        CleanupLeaderAssignments(leader, teamId);

        var slotCount = Math.Max(1, leader.Comp.MaxMembers);
        var slots = new WH40KSquadSlotEntry[slotCount];
        for (byte i = 0; i < slots.Length; i++)
        {
            slots[i] = new WH40KSquadSlotEntry((byte) (i + 1), NetEntity.Invalid, string.Empty, string.Empty, false, false);
        }

        var candidates = new List<WH40KSquadMemberEntry>();
        var memberCount = 0;

        var query = EntityQueryEnumerator<WH40KSquadAssignableComponent, WH40KTeamMemberComponent>();
        while (query.MoveNext(out var memberUid, out var assignable, out var teamMember))
        {
            if (!SameTeam(teamId, teamMember.TeamId))
                continue;

            if (assignable.AssignedLeader == leader.Owner &&
                assignable.AssignedSlot >= 1 &&
                assignable.AssignedSlot <= slots.Length)
            {
                var slotIndex = assignable.AssignedSlot - 1;
                slots[slotIndex] = new WH40KSquadSlotEntry(
                    assignable.AssignedSlot,
                    GetNetEntity(memberUid),
                    Name(memberUid),
                    ResolveRoleName(memberUid),
                    true,
                    !IsDead(memberUid));
                memberCount++;
                continue;
            }

            if (assignable.AssignedLeader != null || IsDead(memberUid))
                continue;

            candidates.Add(new WH40KSquadMemberEntry(
                GetNetEntity(memberUid),
                Name(memberUid),
                ResolveRoleName(memberUid),
                true));
        }

        candidates.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return new WH40KSquadBuiState(
            teamId,
            ResolveTeamColor(teamId).ToHexNoAlpha(),
            leader.Comp.SquadActive,
            Name(leader.Owner),
            ResolveRoleName(leader.Owner),
            !IsDead(leader.Owner),
            memberCount,
            slotCount,
            candidates.Count,
            slots,
            candidates.ToArray());
    }

    private void RefreshUi(Entity<WH40KSquadLeaderComponent> leader)
    {
        EnsureLeaderRuntime(leader);

        if (leader.Comp.ControllerEntity is not { } controller || TerminatingOrDeleted(controller))
            return;

        _ui.SetUiState(controller, WH40KSquadUiKey.Key, BuildState(leader));
    }

    private void RefreshAllOpenUis()
    {
        var query = EntityQueryEnumerator<WH40KSquadLeaderComponent>();
        while (query.MoveNext(out var leaderUid, out var leaderComp))
        {
            if (leaderComp.ControllerEntity is not { } controller || TerminatingOrDeleted(controller))
                continue;

            if (!_ui.IsUiOpen(controller, WH40KSquadUiKey.Key))
                continue;

            RefreshUi((leaderUid, leaderComp));
        }
    }

    private void DisbandSquad(Entity<WH40KSquadLeaderComponent> leader, bool notifyAll = true)
    {
        var changed = false;

        var query = EntityQueryEnumerator<WH40KSquadAssignableComponent>();
        while (query.MoveNext(out var memberUid, out var assignable))
        {
            if (assignable.AssignedLeader != leader.Owner)
                continue;

            assignable.AssignedLeader = null;
            assignable.AssignedSlot = 0;
            assignable.TeamId = string.Empty;
            Dirty(memberUid, assignable);
            changed = true;
        }

        if (leader.Comp.SquadActive)
        {
            leader.Comp.SquadActive = false;
            Dirty(leader);
            changed = true;
        }

        if (!notifyAll)
            return;

        if (changed)
            RefreshAllOpenUis();
        else
            RefreshUi(leader);
    }

    private void CleanupLeaderAssignments(Entity<WH40KSquadLeaderComponent> leader, string teamId)
    {
        var usedSlots = new HashSet<byte>();

        var query = EntityQueryEnumerator<WH40KSquadAssignableComponent, WH40KTeamMemberComponent>();
        while (query.MoveNext(out var memberUid, out var assignable, out var member))
        {
            if (assignable.AssignedLeader != leader.Owner)
                continue;

            var invalid = !leader.Comp.SquadActive ||
                string.IsNullOrWhiteSpace(teamId) ||
                !SameTeam(teamId, member.TeamId) ||
                assignable.AssignedSlot < 1 ||
                assignable.AssignedSlot > leader.Comp.MaxMembers ||
                !usedSlots.Add(assignable.AssignedSlot);

            if (!invalid)
                continue;

            assignable.AssignedLeader = null;
            assignable.AssignedSlot = 0;
            assignable.TeamId = string.Empty;
            Dirty(memberUid, assignable);
        }
    }

    private bool TryGetFirstFreeSlot(Entity<WH40KSquadLeaderComponent> leader, out byte slot)
    {
        var occupied = new HashSet<byte>();

        var query = EntityQueryEnumerator<WH40KSquadAssignableComponent>();
        while (query.MoveNext(out _, out var assignable))
        {
            if (assignable.AssignedLeader == leader.Owner && assignable.AssignedSlot >= 1)
                occupied.Add(assignable.AssignedSlot);
        }

        for (byte i = 1; i <= leader.Comp.MaxMembers; i++)
        {
            if (occupied.Contains(i))
                continue;

            slot = i;
            return true;
        }

        slot = 0;
        return false;
    }

    private string ResolveRoleName(EntityUid uid)
    {
        if (_mind.TryGetMind(uid, out _, out var mind) &&
            _roles.GetRoleCompByTime(mind) is { Comp.JobPrototype: { } jobId } &&
            _prototype.TryIndex<JobPrototype>(jobId, out var job))
        {
            return job.LocalizedName;
        }

        return Loc.GetString("wh40k-squad-role-unknown");
    }

    private bool IsDead(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mobState) &&
               mobState.CurrentState == MobState.Dead;
    }

    private Color ResolveTeamColor(string? teamId)
    {
        if (!string.IsNullOrWhiteSpace(teamId) &&
            _teamRule.TryGetTeamColor(teamId, out var teamColor))
        {
            return teamColor;
        }

        return string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase)
            ? DefaultHereticsColor
            : DefaultImperiumColor;
    }

    private static bool SameTeam(string? left, string? right)
    {
        return TryCanonicalizeTeamId(left, out var canonicalLeft) &&
               TryCanonicalizeTeamId(right, out var canonicalRight) &&
               string.Equals(canonicalLeft, canonicalRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCanonicalizeTeamId(string? teamId, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "Imperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            canonical = "Heretics";
            return true;
        }

        return false;
    }
}
