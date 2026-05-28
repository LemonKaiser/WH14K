using System.Text;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Server._WH40K.WaveDefence.HTN;
using Content.Shared._WH40K.WaveDefence;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WaveDefence;

public sealed partial class WH40KWaveDefenceAiDebugOverlaySystem : SharedWH40KWaveDefenceAiDebugOverlaySystem
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";
    private const string GenericCombatTargetKey = "Target";
    private const string GenericMoveTargetKey = "TargetCoordinates";

    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private readonly HashSet<ICommonSession> _observers = [];
    private TimeSpan? _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;

        foreach (var observer in new List<ICommonSession>(_observers))
        {
            RemoveObserver(observer);
        }
    }

    public bool ToggleObserver(ICommonSession observer)
    {
        if (HasObserver(observer))
        {
            RemoveObserver(observer);
            return false;
        }

        AddObserver(observer);
        return true;
    }

    public bool HasObserver(ICommonSession observer)
    {
        return _observers.Contains(observer);
    }

    public void AddObserver(ICommonSession observer)
    {
        if (_observers.Add(observer))
            _nextTick = _timing.CurTime;
    }

    public void RemoveObserver(ICommonSession observer)
    {
        if (!_observers.Remove(observer))
            return;

        RaiseNetworkEvent(new WH40KWaveDefenceAiDebugOverlayDisableMessage(), observer.Channel);
    }

    public string BuildStatusText(int maxEntries = 24)
    {
        var builder = new StringBuilder();
        var total = 0;
        var waveAttackers = 0;
        var moving = 0;
        var noPath = 0;
        var shown = 0;

        builder.AppendLine("NPC AI debug status:");

        var query = EntityQueryEnumerator<HTNComponent, Content.Shared.NPC.ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var htn, out _, out var xform))
        {
            total++;

            var steering = CompOrNull<NPCSteeringComponent>(uid);
            if (steering != null)
            {
                if (steering.Status == SteeringStatus.Moving)
                    moving++;
                else if (steering.Status == SteeringStatus.NoPath)
                    noPath++;
            }

            var attacker = CompOrNull<WH40KWaveDefenceAttackerComponent>(uid);
            if (attacker != null)
                waveAttackers++;

            if (shown >= maxEntries)
                continue;

            var entry = BuildEntry(uid, htn, xform, steering, attacker);
            builder.AppendLine(
                $"{entry.Label} root={entry.RootTask} task={entry.CurrentTask} steer={entry.SteeringStatus} focus={entry.FocusLabel} engaged={entry.Engaged} wave={entry.IsWaveAttacker} state={entry.DebugState}");
            shown++;
        }

        builder.Insert(0,
            $"Total active NPCs: {total}\nWave attackers: {waveAttackers}\nMoving: {moving}\nNo-path: {noPath}\n");
        return builder.ToString().TrimEnd();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            RemoveObserver(args.Session);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_observers.Count == 0)
            return;

        if (_nextTick is { } nextTick && _timing.CurTime < nextTick)
            return;

        foreach (var observer in _observers)
        {
            if (observer.AttachedEntity is not { Valid: true } viewer ||
                Deleted(viewer) ||
                !TryComp(viewer, out TransformComponent? viewerXform) ||
                viewerXform.MapID == MapId.Nullspace)
            {
                continue;
            }

            var viewerMap = _transform.GetMapCoordinates(viewer, xform: viewerXform);
            var worldBounds = Box2.CenteredAround(viewerMap.Position, new Vector2(LocalViewRange, LocalViewRange));
            var entries = BuildEntries(worldBounds, viewerXform.MapID);
            RaiseNetworkEvent(new WH40KWaveDefenceAiDebugOverlayMessage(entries), observer.Channel);
        }

        _nextTick = _timing.CurTime + Cooldown;
    }

    private WH40KWaveDefenceAiDebugEntry[] BuildEntries(Box2 worldBounds, MapId mapId)
    {
        var entries = new List<WH40KWaveDefenceAiDebugEntry>();
        var query = EntityQueryEnumerator<HTNComponent, Content.Shared.NPC.ActiveNPCComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var htn, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var npcPosition = _transform.GetMapCoordinates(uid, xform: xform);
            if (!worldBounds.Contains(npcPosition.Position))
                continue;

            var steering = CompOrNull<NPCSteeringComponent>(uid);
            var attacker = CompOrNull<WH40KWaveDefenceAttackerComponent>(uid);
            entries.Add(BuildEntry(uid, htn, xform, steering, attacker));
        }

        return entries.ToArray();
    }

    private WH40KWaveDefenceAiDebugEntry BuildEntry(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        WH40KWaveDefenceAttackerComponent? attacker)
    {
        var focusPosition = MapCoordinates.Nullspace;
        var focusKind = WH40KWaveDefenceAiDebugTargetKind.None;
        var focusLabel = "idle";
        var hasFocusPosition = TryResolveFocus(uid, htn, steering, attacker, out focusPosition, out focusKind, out focusLabel);
        var currentTask = ResolveCurrentTask(htn);
        var steeringStatus = steering?.Status.ToString() ?? "Idle";
        var engaged = HasComp<Content.Server.NPC.Components.NPCMeleeCombatComponent>(uid) ||
                      HasComp<Content.Server.NPC.Components.NPCRangedCombatComponent>(uid) ||
                      focusKind is WH40KWaveDefenceAiDebugTargetKind.CombatTarget or WH40KWaveDefenceAiDebugTargetKind.ObjectiveTarget;

        return new WH40KWaveDefenceAiDebugEntry(
            Label: $"{MetaData(uid).EntityName}#{uid.Id}",
            NpcPosition: _transform.GetMapCoordinates(uid, xform: xform),
            VisionRadius: Math.Max(0f, htn.Blackboard.GetValueOrDefault<float>(VisionRadiusKey, EntityManager)),
            AggroVisionRadius: Math.Max(0f, htn.Blackboard.GetValueOrDefault<float>(AggroVisionRadiusKey, EntityManager)),
            FocusPosition: focusPosition,
            HasFocusPosition: hasFocusPosition,
            FocusKind: focusKind,
            RootTask: htn.RootTask.Task,
            CurrentTask: currentTask,
            SteeringStatus: steeringStatus,
            FocusLabel: focusLabel,
            DebugState: attacker?.DebugState ?? currentTask,
            NoPath: steering?.Status == SteeringStatus.NoPath,
            Engaged: engaged,
            IsWaveAttacker: attacker != null);
    }

    private bool TryResolveFocus(
        EntityUid uid,
        HTNComponent htn,
        NPCSteeringComponent? steering,
        WH40KWaveDefenceAttackerComponent? attacker,
        out MapCoordinates position,
        out WH40KWaveDefenceAiDebugTargetKind kind,
        out string label)
    {
        position = MapCoordinates.Nullspace;
        kind = WH40KWaveDefenceAiDebugTargetKind.None;
        label = "idle";

        if (TryGetBlackboardEntity(htn, WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget, out var objectiveTarget))
        {
            position = _transform.GetMapCoordinates(objectiveTarget);
            kind = WH40KWaveDefenceAiDebugTargetKind.ObjectiveTarget;
            label = $"objective:{ToPrettyString(objectiveTarget)}";
            return true;
        }

        if (TryGetBlackboardEntity(htn, WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget, out var wavePlayerTarget) ||
            TryGetBlackboardEntity(htn, GenericCombatTargetKey, out wavePlayerTarget))
        {
            position = _transform.GetMapCoordinates(wavePlayerTarget);
            kind = WH40KWaveDefenceAiDebugTargetKind.CombatTarget;
            label = $"combat:{ToPrettyString(wavePlayerTarget)}";
            return true;
        }

        if (TryGetBlackboardCoordinates(htn, WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates, out var objectiveCoordinates))
        {
            position = _transform.ToMapCoordinates(objectiveCoordinates);
            kind = WH40KWaveDefenceAiDebugTargetKind.ObjectiveTarget;
            label = "objective:approach";
            return true;
        }

        if (TryGetBlackboardCoordinates(htn, WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates, out var playerCoordinates) ||
            TryGetBlackboardCoordinates(htn, WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates, out playerCoordinates) ||
            TryGetBlackboardCoordinates(htn, GenericMoveTargetKey, out playerCoordinates))
        {
            position = _transform.ToMapCoordinates(playerCoordinates);
            kind = WH40KWaveDefenceAiDebugTargetKind.MoveTarget;
            label = "move:blackboard";
            return true;
        }

        if (steering != null && steering.Coordinates.IsValid(EntityManager))
        {
            position = _transform.ToMapCoordinates(steering.Coordinates);
            kind = WH40KWaveDefenceAiDebugTargetKind.MoveTarget;
            label = "move:steering";
            return true;
        }

        if (attacker?.Objective is { } fallbackObjective && Exists(fallbackObjective))
        {
            position = _transform.GetMapCoordinates(fallbackObjective);
            kind = WH40KWaveDefenceAiDebugTargetKind.ObjectiveTarget;
            label = $"objective:{ToPrettyString(fallbackObjective)}";
            return true;
        }

        return false;
    }

    private bool TryGetBlackboardEntity(HTNComponent htn, string key, out EntityUid entity)
    {
        entity = EntityUid.Invalid;
        if (!htn.Blackboard.TryGetValue<EntityUid>(key, out var resolved, EntityManager) ||
            resolved == EntityUid.Invalid ||
            Deleted(resolved))
        {
            return false;
        }

        entity = resolved;
        return true;
    }

    private bool TryGetBlackboardCoordinates(HTNComponent htn, string key, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        return htn.Blackboard.TryGetValue<EntityCoordinates>(key, out coordinates, EntityManager) &&
               coordinates.IsValid(EntityManager);
    }

    private static string ResolveCurrentTask(HTNComponent htn)
    {
        if (htn.Planning)
            return "Planning";

        if (htn.Plan?.Tasks.Count > 0)
            return FormatOperatorName(htn.Plan.CurrentOperator.GetType().Name);

        return "NoPlan";
    }

    private static string FormatOperatorName(string value)
    {
        return value.EndsWith("Operator", StringComparison.Ordinal)
            ? value[..^"Operator".Length]
            : value;
    }
}
