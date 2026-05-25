using System.Text;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceAISystem : EntitySystem
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";

    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.wave.ai");

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
        _npc.SetBlackboard(uid, VisionRadiusKey, Math.Max(6f, attacker.VisionRadius), htn);
        _npc.SetBlackboard(uid, AggroVisionRadiusKey, Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius), htn);

        attacker.DebugState = attacker.Objective is { } objective && Exists(objective)
            ? $"configured:{htn.RootTask.Task}:{ToPrettyString(objective)}"
            : $"configured:{htn.RootTask.Task}:no-objective";

        _npc.WakeNPC(uid, htn);
        _htn.Replan(htn);
    }

    private void ApplyRootOverride(EntityUid uid, HTNComponent htn, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return;

        if (!_prototype.HasIndex<HTNCompoundPrototype>(taskId))
        {
            _sawmill.Warning($"WaveDefence attacker {ToPrettyString(uid)} requested missing HTN root '{taskId}'.");
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
