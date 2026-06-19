using System.Text;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Server.NPC;
using Content.Shared.Climbing.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Prying.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WaveDefence;

public sealed partial class WH40KWaveDefenceAISystem : EntitySystem
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";
    private const string MovementRangeKey = "MovementRange";
    private const string MeleeRangeKey = "MeleeRange";
    private const string CombatGroupPrefix = "WH40KWaveDefence";

    [Dependency] private  HTNSystem _htn = default!;
    [Dependency] private  NPCSystem _npc = default!;
    [Dependency] private  InventorySystem _inventory = default!;
    [Dependency] private  PathfindingSystem _pathfinding = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWaveDefenceAttackerComponent, ComponentStartup>(OnAttackerStartup);
    }

    private void OnAttackerStartup(EntityUid uid, WH40KWaveDefenceAttackerComponent component, ref ComponentStartup args)
    {
        ConfigureAttacker(uid, component);
    }

    public void ConfigureAttacker(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent? attacker = null,
        HTNComponent? htn = null)
    {
        if (!Resolve(uid, ref attacker, false) || !Resolve(uid, ref htn, false))
            return;

        ApplyRootOverride(uid, htn, attacker.RootTaskOverride);
        var navigationRadius = GetWaveNavigationRadius(uid);
        _npc.SetBlackboard(uid, VisionRadiusKey, Math.Max(6f, attacker.VisionRadius), htn);
        _npc.SetBlackboard(uid, AggroVisionRadiusKey, Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius), htn);
        _npc.SetBlackboard(uid, MovementRangeKey, Math.Max(0.75f, navigationRadius + 0.2f), htn);
        _npc.SetBlackboard(uid, MeleeRangeKey, Math.Max(1.0f, navigationRadius + 0.75f), htn);
        ConfigureCombatBrain(uid, attacker);

        attacker.DebugState = attacker.Objective is { } objective && Exists(objective)
            ? $"configured:{htn.RootTask.Task}:{ToPrettyString(objective)}"
            : $"configured:{htn.RootTask.Task}:no-objective";

        _npc.WakeNPC(uid, htn);
        _htn.Replan(htn);
    }

    private void ConfigureCombatBrain(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        if (TryComp<HTNComponent>(uid, out var htn))
            ConfigureNavigationBrain(uid, htn);

        var group = EnsureComp<NPCGroupComponent>(uid);
        group.GroupId = $"{CombatGroupPrefix}:{(attacker.Objective is { } objective ? objective.Id : 0)}";
        group.CollectiveMind = true;
        group.CoordinateObstacles = true;
        group.WaitForGroupObstacle = true;
        group.WorkGroupRadius = Math.Max(group.WorkGroupRadius, 3.0f);
        group.SeparationRadius = Math.Max(group.SeparationRadius, 1.1f);
        group.SeparationWeight = Math.Max(group.SeparationWeight, 0.75f);

        EnsureComp<NPCCombatMemoryComponent>(uid);

        var perception = EnsureComp<NPCCombatPerceptionComponent>(uid);
        perception.VisionRadius = Math.Max(6f, attacker.VisionRadius);
        perception.AggroVisionRadius = Math.Max(perception.VisionRadius, attacker.AggroVisionRadius);
        perception.VisionCheckInterval = 0.2f;
        perception.ShareContactInterval = 0.45f;
        perception.AssignmentInterval = 0.45f;
        perception.ShareContactRadius = Math.Max(8f, perception.VisionRadius * 0.6f);
        perception.ShareRequiresLineOfSight = true;
        perception.RequireSameGroupForReports = true;
        perception.MemoryDuration = Math.Max(2f, attacker.PlayerMemorySeconds);
        perception.SearchDuration = Math.Max(2f, attacker.PlayerMemorySeconds * 0.6f);
        perception.VisibleGrace = 0.35f;
        perception.ReportConfidenceMultiplier = 0.75f;
        perception.MinimumContactConfidence = 0.15f;
        perception.MeleeSlotsPerTarget = Math.Max(2, perception.MeleeSlotsPerTarget);
        perception.RangedSlotsPerTarget = Math.Max(2, perception.RangedSlotsPerTarget);
        perception.UseOpaqueForLOSChecks = true;
        perception.RecognizeStaticThreats = true;
    }

    private void ConfigureNavigationBrain(EntityUid uid, HTNComponent htn)
    {
        var steering = EnsureComp<NPCSteeringComponent>(uid);
        var oldFlags = steering.Flags;
        var oldRadius = steering.Radius;

        ConfigureObstacleNavigationKit(uid, steering);

        _npc.SetBlackboard(uid, NPCBlackboard.NavInteract, true, htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavPry, HasPryingCapability(uid), htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavClimb, HasClimbingCapability(uid), htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavSmash, HasSmashingCapability(uid), htn);

        steering.Flags = _pathfinding.GetFlags(uid);
        if (steering.Pathfind &&
            (oldFlags != steering.Flags || Math.Abs(oldRadius - steering.Radius) > 0.01f))
        {
            steering.PathfindToken?.Cancel();
            steering.PathfindToken = null;
            steering.CurrentPath.Clear();
        }
    }

    private void ConfigureObstacleNavigationKit(EntityUid uid, NPCSteeringComponent steering)
    {
        EnsureWavePrying(uid);
        EnsureWaveStructuralMelee(uid);
        EnsureComp<CombatModeComponent>(uid);
        ConfigureWaveSteering(uid, steering);
    }

    private void EnsureWavePrying(EntityUid uid)
    {
        var prying = EnsureComp<PryingComponent>(uid);
        prying.Enabled = true;
        prying.PryPowered = true;
        prying.Force = true;
        prying.SpeedModifier = Math.Max(prying.SpeedModifier, 1.5f);
        Dirty(uid, prying);
    }

    private void EnsureWaveStructuralMelee(EntityUid uid)
    {
        var melee = EnsureComp<MeleeWeaponComponent>(uid);
        melee.Damage ??= new DamageSpecifier();

        var structural = FixedPoint2.New(GetWaveStructuralDamage(uid));
        if (!melee.Damage.DamageDict.TryGetValue("Structural", out var existing) || existing < structural)
            melee.Damage.DamageDict["Structural"] = structural;

        melee.Hidden = true;
        melee.AltDisarm = false;
        melee.Range = Math.Max(melee.Range, GetWaveNavigationRadius(uid) + 0.85f);
        melee.AttackRate = Math.Max(melee.AttackRate, 1.3f);
        Dirty(uid, melee);
    }

    private void ConfigureWaveSteering(EntityUid uid, NPCSteeringComponent steering)
    {
        var navigationRadius = GetWaveNavigationRadius(uid);

        steering.PreserveOnUnregister = true;
        steering.Radius = Math.Max(steering.Radius, navigationRadius);
        steering.Range = Math.Max(steering.Range, 0.25f);
        steering.RepathRange = steering.RepathRange <= 0f
            ? 0.9f
            : Math.Min(steering.RepathRange, 0.9f);
        steering.FailedPathLimit = Math.Max(steering.FailedPathLimit, 8);
        steering.EnablePathShortcutting = true;
        steering.PathShortcutLookahead = Math.Max(steering.PathShortcutLookahead, 10);
        steering.ObstacleRepathInterval = steering.ObstacleRepathInterval <= 0f
            ? 1.0f
            : Math.Min(steering.ObstacleRepathInterval, 1.0f);
        steering.EnablePathOffsets = true;
        steering.PathOffsetMin = Math.Max(steering.PathOffsetMin, 0.1f);
        steering.PathOffsetMax = Math.Max(steering.PathOffsetMax, 0.32f);
        steering.PendingPathDirectMoveProbe = Math.Max(steering.PendingPathDirectMoveProbe, navigationRadius + 1.6f);
        Dirty(uid, steering);
    }

    private float GetWaveNavigationRadius(EntityUid uid)
    {
        var radius = 0.35f;

        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return radius;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Shape is PhysShapeCircle circle)
                radius = Math.Max(radius, circle.Radius);
        }

        return Math.Clamp(radius, 0.25f, 1.1f);
    }

    private int GetWaveStructuralDamage(EntityUid uid)
    {
        var radius = GetWaveNavigationRadius(uid);

        if (radius >= 0.7f)
            return 45;

        if (radius >= 0.55f)
            return 30;

        return 18;
    }

    private bool HasPryingCapability(EntityUid uid)
    {
        if (HasComp<PryingComponent>(uid))
            return true;

        var hands = CompOrNull<HandsComponent>(uid);
        var inventory = CompOrNull<InventoryComponent>(uid);
        foreach (var item in _inventory.GetHandOrInventoryEntities((uid, hands, inventory)))
        {
            if (HasComp<PryingComponent>(item))
                return true;
        }

        return false;
    }

    private bool HasClimbingCapability(EntityUid uid)
    {
        return TryComp<ClimbingComponent>(uid, out var climbing) && climbing.CanClimb;
    }

    private bool HasSmashingCapability(EntityUid uid)
    {
        if (!HasComp<CombatModeComponent>(uid))
            return false;

        if (HasComp<MeleeWeaponComponent>(uid))
            return true;

        var hands = CompOrNull<HandsComponent>(uid);
        var inventory = CompOrNull<InventoryComponent>(uid);
        foreach (var item in _inventory.GetHandOrInventoryEntities((uid, hands, inventory)))
        {
            if (HasComp<MeleeWeaponComponent>(item))
                return true;
        }

        return false;
    }

    private void ApplyRootOverride(EntityUid uid, HTNComponent htn, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return;

        if (!_prototype.HasIndex<HTNCompoundPrototype>(taskId))
        {
            return;
        }

        htn.PlanningToken?.Cancel();
        htn.PlanningToken = null;
        htn.PlanningJob = null;
        htn.Plan = null;
        htn.PlanAccumulator = 0f;
        htn.RootTask = new HTNCompoundTask
        {
            Task = taskId
        };
    }

    public string BuildAiStatusText(int maxEntries = 18)
    {
        var builder = new StringBuilder();
        var entries = new List<string>();
        var total = 0;
        var awake = 0;

        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var attacker, out var htn))
        {
            total++;

            var isAwake = HasComp<Content.Shared.NPC.ActiveNPCComponent>(uid);
            if (isAwake)
                awake++;

            if (entries.Count >= maxEntries)
                continue;

            var steeringStatus = CompOrNull<NPCSteeringComponent>(uid)?.Status.ToString() ?? "Idle";
            var objective = attacker.Objective is { } objectiveUid && Exists(objectiveUid)
                ? ToPrettyString(objectiveUid)
                : "none";
            entries.Add(
                $"{ToPrettyString(uid)} role={attacker.Role} profile={attacker.AiProfile} awake={isAwake} root={htn.RootTask.Task} steer={steeringStatus} objective={objective} state={attacker.DebugState}");
        }

        builder.AppendLine($"Wave attackers: {total}");
        builder.AppendLine($"Awake attackers: {awake}");
        if (entries.Count == 0)
        {
            builder.Append("No active WaveDefence attackers.");
            return builder.ToString();
        }

        builder.AppendLine("Entries:");
        foreach (var entry in entries)
        {
            builder.AppendLine(entry);
        }

        return builder.ToString().TrimEnd();
    }
}
