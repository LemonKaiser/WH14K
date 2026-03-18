using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Chat;
using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Role-aware wave NPC voice calls with zone/faction deduplication.
/// Prevents "all NPCs shout at once" while keeping tactical feedback alive.
/// </summary>
public sealed class NPCWaveCommunicationSystem : EntitySystem
{
    [Dependency] private readonly NPCBenchmarkSystem _bench = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<CommBucketKey, TimeSpan> _bucketCooldowns = new();
    private readonly Dictionary<SpeakerEventKey, TimeSpan> _speakerCooldowns = new();
    private readonly List<CommBucketKey> _bucketPrune = new();
    private readonly List<SpeakerEventKey> _speakerPrune = new();
    private TimeSpan _nextPruneTime = TimeSpan.Zero;

    private const float CellSize = 14f;

    public bool TryEnemySpotted(EntityUid speaker, EntityUid target)
    {
        return TrySpeak(speaker, WaveCommEvent.EnemySpotted);
    }

    public bool TryEngagingEnemy(EntityUid speaker, EntityUid target)
    {
        return TrySpeak(speaker, WaveCommEvent.EngagingEnemy);
    }

    public bool TryMineCleared(EntityUid speaker, EntityUid mine)
    {
        return TrySpeak(speaker, WaveCommEvent.MineCleared);
    }

    public bool TryTacticalOrder(EntityUid speaker, EntityUid target)
    {
        return TrySpeak(speaker, WaveCommEvent.TacticalOrder);
    }

    public bool TryServiceReport(EntityUid speaker, EntityUid machine)
    {
        return TrySpeak(speaker, WaveCommEvent.ServiceReport);
    }

    private bool TrySpeak(EntityUid speaker, WaveCommEvent evt)
    {
        if (TerminatingOrDeleted(speaker) ||
            !TryComp(speaker, out HTNComponent? htn) ||
            !IsWaveRole(htn))
        {
            return false;
        }

        var now = _timing.CurTime;
        if (now >= _nextPruneTime)
        {
            _nextPruneTime = now + TimeSpan.FromSeconds(5);
            Prune(now);
        }

        var eventToken = GetEventToken(evt);
        _bench.RecordCount($"npc.wave.comms.{eventToken}.attempt", 1);

        if (!TryBuildBucketKey(speaker, evt, out var bucket))
        {
            _bench.RecordCount($"npc.wave.comms.{eventToken}.suppressed", 1);
            return false;
        }

        if (_bucketCooldowns.TryGetValue(bucket, out var bucketNext) &&
            bucketNext > now)
        {
            _bench.RecordCount($"npc.wave.comms.{eventToken}.suppressed", 1);
            return false;
        }

        var speakerKey = new SpeakerEventKey(speaker, evt);
        if (_speakerCooldowns.TryGetValue(speakerKey, out var speakerNext) &&
            speakerNext > now)
        {
            _bench.RecordCount($"npc.wave.comms.{eventToken}.suppressed", 1);
            return false;
        }

        var role = ResolveRole(htn);
        var line = PickLine(role, evt);

        if (line.Length == 0)
        {
            _bench.RecordCount($"npc.wave.comms.{eventToken}.suppressed", 1);
            return false;
        }

        _chat.TrySendInGameICMessage(speaker, line, InGameICChatType.Speak, hideChat: false, hideLog: false);
        _bucketCooldowns[bucket] = now + TimeSpan.FromSeconds(GetBucketCooldownSeconds(evt));
        _speakerCooldowns[speakerKey] = now + TimeSpan.FromSeconds(GetSpeakerCooldownSeconds(evt));

        _bench.RecordCount($"npc.wave.comms.{eventToken}.sent", 1);
        _bench.RecordCount($"npc.wave.comms.role.{GetRoleToken(role)}.sent", 1);
        return true;
    }

    private void Prune(TimeSpan now)
    {
        _bucketPrune.Clear();
        foreach (var (key, expiresAt) in _bucketCooldowns)
        {
            if (expiresAt <= now)
                _bucketPrune.Add(key);
        }

        foreach (var key in _bucketPrune)
        {
            _bucketCooldowns.Remove(key);
        }

        _speakerPrune.Clear();
        foreach (var (key, expiresAt) in _speakerCooldowns)
        {
            if (expiresAt <= now || TerminatingOrDeleted(key.Speaker))
                _speakerPrune.Add(key);
        }

        foreach (var key in _speakerPrune)
        {
            _speakerCooldowns.Remove(key);
        }
    }

    private bool TryBuildBucketKey(EntityUid speaker, WaveCommEvent evt, out CommBucketKey key)
    {
        key = default;

        if (!TryComp(speaker, out TransformComponent? xform))
            return false;

        var mapCoords = _transform.GetMapCoordinates(speaker, xform);
        if (mapCoords.MapId == MapId.Nullspace)
            return false;

        var cellX = (int) MathF.Floor(mapCoords.Position.X / CellSize);
        var cellY = (int) MathF.Floor(mapCoords.Position.Y / CellSize);

        key = new CommBucketKey(
            evt,
            mapCoords.MapId,
            cellX,
            cellY,
            GetFactionSignature(speaker));
        return true;
    }

    private int GetFactionSignature(EntityUid speaker)
    {
        if (!TryComp(speaker, out NpcFactionMemberComponent? faction) ||
            faction.Factions.Count == 0)
        {
            return 0;
        }

        var hash = 17;
        foreach (var group in faction.Factions.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            hash = HashCode.Combine(hash, group.Id.GetHashCode(StringComparison.Ordinal));
        }

        return hash;
    }

    private bool IsWaveRole(HTNComponent htn)
    {
        return htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveRoleEnabled, out var enabled, EntityManager) &&
               enabled;
    }

    private static WaveNpcRole ResolveRole(HTNComponent htn)
    {
        return htn.RootTask.Task switch
        {
            "WH40KWaveAssaultRoot" => WaveNpcRole.Assault,
            "WH40KWaveBreacherRoot" => WaveNpcRole.Breacher,
            "WH40KWaveSapperRoot" => WaveNpcRole.Sapper,
            "WH40KWaveSupportRoot" => WaveNpcRole.Support,
            "WH40KWaveLogisticsRoot" => WaveNpcRole.Logistics,
            "WH40KWaveCoordinatorRoot" => WaveNpcRole.Coordinator,
            _ => WaveNpcRole.Unknown,
        };
    }

    private string PickLine(WaveNpcRole role, WaveCommEvent evt)
    {
        var pool = GetLinePool(role, evt);
        if (pool.Length == 0)
            return string.Empty;

        return pool[_random.Next(pool.Length)];
    }

    private static string[] GetLinePool(WaveNpcRole role, WaveCommEvent evt)
    {
        return (role, evt) switch
        {
            (WaveNpcRole.Assault, WaveCommEvent.EnemySpotted) => AssaultEnemySpotted,
            (WaveNpcRole.Assault, WaveCommEvent.EngagingEnemy) => AssaultEngaging,
            (WaveNpcRole.Assault, WaveCommEvent.MineCleared) => AssaultMineCleared,
            (WaveNpcRole.Assault, WaveCommEvent.TacticalOrder) => AssaultTactical,
            (WaveNpcRole.Assault, WaveCommEvent.ServiceReport) => AssaultService,

            (WaveNpcRole.Breacher, WaveCommEvent.EnemySpotted) => BreacherEnemySpotted,
            (WaveNpcRole.Breacher, WaveCommEvent.EngagingEnemy) => BreacherEngaging,
            (WaveNpcRole.Breacher, WaveCommEvent.MineCleared) => BreacherMineCleared,
            (WaveNpcRole.Breacher, WaveCommEvent.TacticalOrder) => BreacherTactical,
            (WaveNpcRole.Breacher, WaveCommEvent.ServiceReport) => BreacherService,

            (WaveNpcRole.Sapper, WaveCommEvent.EnemySpotted) => SapperEnemySpotted,
            (WaveNpcRole.Sapper, WaveCommEvent.EngagingEnemy) => SapperEngaging,
            (WaveNpcRole.Sapper, WaveCommEvent.MineCleared) => SapperMineCleared,
            (WaveNpcRole.Sapper, WaveCommEvent.TacticalOrder) => SapperTactical,
            (WaveNpcRole.Sapper, WaveCommEvent.ServiceReport) => SapperService,

            (WaveNpcRole.Support, WaveCommEvent.EnemySpotted) => SupportEnemySpotted,
            (WaveNpcRole.Support, WaveCommEvent.EngagingEnemy) => SupportEngaging,
            (WaveNpcRole.Support, WaveCommEvent.MineCleared) => SupportMineCleared,
            (WaveNpcRole.Support, WaveCommEvent.TacticalOrder) => SupportTactical,
            (WaveNpcRole.Support, WaveCommEvent.ServiceReport) => SupportService,

            (WaveNpcRole.Logistics, WaveCommEvent.EnemySpotted) => LogisticsEnemySpotted,
            (WaveNpcRole.Logistics, WaveCommEvent.EngagingEnemy) => LogisticsEngaging,
            (WaveNpcRole.Logistics, WaveCommEvent.MineCleared) => LogisticsMineCleared,
            (WaveNpcRole.Logistics, WaveCommEvent.TacticalOrder) => LogisticsTactical,
            (WaveNpcRole.Logistics, WaveCommEvent.ServiceReport) => LogisticsService,

            (WaveNpcRole.Coordinator, WaveCommEvent.EnemySpotted) => CoordinatorEnemySpotted,
            (WaveNpcRole.Coordinator, WaveCommEvent.EngagingEnemy) => CoordinatorEngaging,
            (WaveNpcRole.Coordinator, WaveCommEvent.MineCleared) => CoordinatorMineCleared,
            (WaveNpcRole.Coordinator, WaveCommEvent.TacticalOrder) => CoordinatorTactical,
            (WaveNpcRole.Coordinator, WaveCommEvent.ServiceReport) => CoordinatorService,

            _ => GenericFallback,
        };
    }

    private static float GetBucketCooldownSeconds(WaveCommEvent evt)
    {
        return evt switch
        {
            WaveCommEvent.EnemySpotted => 2.8f,
            WaveCommEvent.EngagingEnemy => 1.8f,
            WaveCommEvent.MineCleared => 5.5f,
            WaveCommEvent.TacticalOrder => 6.0f,
            WaveCommEvent.ServiceReport => 4.5f,
            _ => 3f,
        };
    }

    private static float GetSpeakerCooldownSeconds(WaveCommEvent evt)
    {
        return evt switch
        {
            WaveCommEvent.EnemySpotted => 3.4f,
            WaveCommEvent.EngagingEnemy => 2.2f,
            WaveCommEvent.MineCleared => 7.0f,
            WaveCommEvent.TacticalOrder => 7.5f,
            WaveCommEvent.ServiceReport => 5.0f,
            _ => 3.5f,
        };
    }

    private static string GetEventToken(WaveCommEvent evt)
    {
        return evt switch
        {
            WaveCommEvent.EnemySpotted => "enemy_spotted",
            WaveCommEvent.EngagingEnemy => "engaging_enemy",
            WaveCommEvent.MineCleared => "mine_cleared",
            WaveCommEvent.TacticalOrder => "tactical_order",
            WaveCommEvent.ServiceReport => "service_report",
            _ => "unknown",
        };
    }

    private static string GetRoleToken(WaveNpcRole role)
    {
        return role switch
        {
            WaveNpcRole.Assault => "assault",
            WaveNpcRole.Breacher => "breacher",
            WaveNpcRole.Sapper => "sapper",
            WaveNpcRole.Support => "support",
            WaveNpcRole.Logistics => "logistics",
            WaveNpcRole.Coordinator => "coordinator",
            _ => "unknown",
        };
    }

    private static readonly string[] GenericFallback =
    {
        "Contact confirmed.",
    };

    private static readonly string[] AssaultEnemySpotted =
    {
        "Assault: contact ahead!",
        "Assault: target spotted.",
    };

    private static readonly string[] AssaultEngaging =
    {
        "Assault: engaging now!",
        "Assault: suppressing target!",
    };

    private static readonly string[] AssaultMineCleared =
    {
        "Assault: lane is clear.",
    };

    private static readonly string[] AssaultTactical =
    {
        "Assault: push the marked flank.",
    };

    private static readonly string[] AssaultService =
    {
        "Assault: resupply confirmed.",
    };

    private static readonly string[] BreacherEnemySpotted =
    {
        "Breacher: contact on breach lane.",
        "Breacher: target by obstruction.",
    };

    private static readonly string[] BreacherEngaging =
    {
        "Breacher: firing through lane.",
        "Breacher: breach team engaging!",
    };

    private static readonly string[] BreacherMineCleared =
    {
        "Breacher: hazard removed.",
    };

    private static readonly string[] BreacherTactical =
    {
        "Breacher: stack on breach point.",
    };

    private static readonly string[] BreacherService =
    {
        "Breacher: supply node updated.",
    };

    private static readonly string[] SapperEnemySpotted =
    {
        "Sapper: hostile near hazard zone.",
        "Sapper: visual contact.",
    };

    private static readonly string[] SapperEngaging =
    {
        "Sapper: covering fire!",
        "Sapper: engaging while clearing.",
    };

    private static readonly string[] SapperMineCleared =
    {
        "Sapper: mine disarmed, path safe.",
        "Sapper: hazard neutralized.",
    };

    private static readonly string[] SapperTactical =
    {
        "Sapper: move through safe lane.",
    };

    private static readonly string[] SapperService =
    {
        "Sapper: support flow green.",
    };

    private static readonly string[] SupportEnemySpotted =
    {
        "Support: contact acquired.",
        "Support: hostile in sight.",
    };

    private static readonly string[] SupportEngaging =
    {
        "Support: opening fire.",
        "Support: target under pressure.",
    };

    private static readonly string[] SupportMineCleared =
    {
        "Support: route clear.",
    };

    private static readonly string[] SupportTactical =
    {
        "Support: hold angles and rotate.",
        "Support: focus the marked target.",
    };

    private static readonly string[] SupportService =
    {
        "Support: supply cycle complete.",
    };

    private static readonly string[] LogisticsEnemySpotted =
    {
        "Logistics: contact reported.",
        "Logistics: enemy visual confirmed.",
    };

    private static readonly string[] LogisticsEngaging =
    {
        "Logistics: returning fire.",
    };

    private static readonly string[] LogisticsMineCleared =
    {
        "Logistics: hazard notice acknowledged.",
    };

    private static readonly string[] LogisticsTactical =
    {
        "Logistics: maintain supply lane.",
    };

    private static readonly string[] LogisticsService =
    {
        "Logistics: vending restock complete.",
        "Logistics: supply machine serviced.",
    };

    private static readonly string[] CoordinatorEnemySpotted =
    {
        "Coordinator: enemy contact confirmed.",
        "Coordinator: target picture updated.",
    };

    private static readonly string[] CoordinatorEngaging =
    {
        "Coordinator: fire discipline, engage.",
    };

    private static readonly string[] CoordinatorMineCleared =
    {
        "Coordinator: sapper lane verified.",
    };

    private static readonly string[] CoordinatorTactical =
    {
        "Coordinator: all squads, push on mark.",
        "Coordinator: regroup and press forward.",
    };

    private static readonly string[] CoordinatorService =
    {
        "Coordinator: logistics cycle confirmed.",
    };

    private enum WaveCommEvent
    {
        EnemySpotted,
        EngagingEnemy,
        MineCleared,
        TacticalOrder,
        ServiceReport,
    }

    private enum WaveNpcRole
    {
        Unknown,
        Assault,
        Breacher,
        Sapper,
        Support,
        Logistics,
        Coordinator,
    }

    private readonly record struct CommBucketKey(
        WaveCommEvent Event,
        MapId MapId,
        int CellX,
        int CellY,
        int FactionSignature);

    private readonly record struct SpeakerEventKey(
        EntityUid Speaker,
        WaveCommEvent Event);
}
