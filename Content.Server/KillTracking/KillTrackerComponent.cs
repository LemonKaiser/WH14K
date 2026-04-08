using System;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Robust.Shared.Network;

namespace Content.Server.KillTracking;

/// <summary>
/// This is used for entities that track player damage sources and killers.
/// </summary>
[RegisterComponent, Access(typeof(KillTrackingSystem))]
public sealed partial class KillTrackerComponent : Component
{
    /// <summary>
    /// The mobstate that registers as a compatibility "kill" event.
    /// </summary>
    [DataField("killState")]
    public MobState KillState = MobState.Critical;

    /// <summary>
    /// How long a contributor remains eligible for kill and assist attribution.
    /// </summary>
    [DataField("assistWindowSeconds")]
    public float AssistWindowSeconds = 30f;

    /// <summary>
    /// Minimum damage fraction relative to the killer required for an assist.
    /// </summary>
    [DataField("assistFractionThreshold")]
    public float AssistFractionThreshold = 0.1f;

    /// <summary>
    /// Minimum absolute damage required for an assist.
    /// </summary>
    [DataField("minimumAssistDamage")]
    public FixedPoint2 MinimumAssistDamage = FixedPoint2.New(1);

    /// <summary>
    /// Runtime damage ledger for the current life / recovery window.
    /// </summary>
    public Dictionary<KillSource, KillAttributionRecord> DamageLedger = new();
}

public sealed class KillAttributionRecord
{
    public FixedPoint2 TotalDamage = FixedPoint2.Zero;
    public TimeSpan LastDamageTime = TimeSpan.Zero;
}

public abstract record KillSource;

/// <summary>
/// A kill source for players
/// </summary>
[DataDefinition, Serializable]
public sealed partial record KillPlayerSource : KillSource
{
    [DataField("playerId")]
    public NetUserId PlayerId;

    public KillPlayerSource(NetUserId playerId)
    {
        PlayerId = playerId;
    }
}

/// <summary>
/// A kill source for non-player controlled entities
/// </summary>
[DataDefinition, Serializable]
public sealed partial record KillNpcSource : KillSource
{
    [DataField("npcEnt")]
    public EntityUid NpcEnt;

    public KillNpcSource(EntityUid npcEnt)
    {
        NpcEnt = npcEnt;
    }
}

/// <summary>
/// A kill source for kills with no damage origin
/// </summary>
[DataDefinition, Serializable]
public sealed partial record KillEnvironmentSource : KillSource;
