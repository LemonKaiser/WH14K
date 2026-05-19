using System;
using System.Collections.Generic;
using Content.Server.Pinpointer;
using Content.Server._WH40K.Diagnostics;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.Notifications;
using Content.Shared.GameTicking;
using Content.Shared.Pinpointer;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Physics;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Influence;

/// <summary>
/// Ownership and control logic for WH40K influence points.
/// </summary>
public sealed class WH40KInfluencePointSystem : EntitySystem
{
    private static readonly string[] TacticalCallsigns =
    {
        "Альфа",
        "Браво",
        "Чарли",
        "Дельта",
        "Эхо",
        "Фокстрот",
        "Гольф",
        "Хотел",
        "Индия",
        "Джульетт",
        "Кило",
        "Лима",
        "Майк",
        "Ноябрь",
        "Оскар",
        "Папа",
        "Квебек",
        "Ромео",
        "Сьерра",
        "Танго",
        "Юниформ",
        "Виктор",
        "Виски",
        "Иксрей",
        "Янки",
        "Зулу",
    };

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NavMapSystem _navMap = default!;
    [Dependency] private readonly WH40KNetDiagAttributionSystem _attribution = default!;
    [Dependency] private readonly WH40KTeamRuleFacadeSystem _teamRule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<string, int> _presentTeamCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<EntityUid> _nearby = new();
    private int _nextAutoCallsignIndex;

    private static readonly string[] TacticalCallsignTokens =
    {
        "Alpha",
        "Bravo",
        "Charlie",
        "Delta",
        "Echo",
        "Foxtrot",
        "Golf",
        "Hotel",
        "India",
        "Juliett",
        "Kilo",
        "Lima",
        "Mike",
        "November",
        "Oscar",
        "Papa",
        "Quebec",
        "Romeo",
        "Sierra",
        "Tango",
        "Uniform",
        "Victor",
        "Whiskey",
        "Xray",
        "Yankee",
        "Zulu",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KInfluencePointComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnMapInit(EntityUid uid, WH40KInfluencePointComponent component, MapInitEvent args)
    {
        AssignCallsign(uid, component);
        NormalizeBeaconLabel(uid, component);
        component.NextRewardTick = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, component.RewardIntervalSeconds));
        if (string.IsNullOrWhiteSpace(component.OwnerTeamId))
            component.OwnerTeamId = null;
        component.CapturingTeamId = null;
        component.CaptureProgressSeconds = 0f;
        component.LastSyncedCaptureProgressSeconds = 0f;
        component.NextCaptureProgressSyncAt = _timing.CurTime;
        _attribution.RecordDirty("influence.map_init", uid);
        Dirty(uid, component);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _nextAutoCallsignIndex = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var phase = _teamRule.GetCurrentPhase();
        var query = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            var dirty = UpdateCaptureState(uid, point, xform, frameTime, phase);
            UpdateRewardState(uid, point, now);
            var progressSynced = false;
            if (!dirty && ShouldSyncProgress(point, now))
            {
                dirty = true;
                progressSynced = true;
            }

            if (!dirty)
                continue;

            point.LastSyncedCaptureProgressSeconds = point.CaptureProgressSeconds;
            if (progressSynced)
            {
                var syncIntervalSeconds = Math.Max(0.05f, point.CaptureProgressSyncIntervalSeconds);
                point.NextCaptureProgressSyncAt = now + TimeSpan.FromSeconds(syncIntervalSeconds);
            }

            _attribution.RecordDirty(
                progressSynced ? "influence.progress_sync" : "influence.state_change",
                uid);
            Dirty(uid, point);
        }
    }

    private bool UpdateCaptureState(
        EntityUid uid,
        WH40KInfluencePointComponent point,
        TransformComponent xform,
        float frameTime,
        WH40KBattlePhase phase)
    {
        var dirty = false;
        _presentTeamCounts.Clear();
        _nearby.Clear();

        var radius = Math.Max(0.5f, point.CaptureRadius);
        _lookup.GetEntitiesInRange(xform.Coordinates, radius, _nearby,
            LookupFlags.Dynamic | LookupFlags.Approximate);
        var radiusSquared = radius * radius;
        var pointWorldPos = _transform.GetWorldPosition(xform);

        foreach (var ent in _nearby)
        {
            if (!TryComp(ent, out TransformComponent? entXform) || entXform.MapID != xform.MapID)
                continue;

            // Enforce exact distance check: capture should only work strictly inside radius.
            var entWorldPos = _transform.GetWorldPosition(entXform);
            if ((entWorldPos - pointWorldPos).LengthSquared() > radiusSquared)
                continue;

            if (!_mobState.IsAlive(ent) && !_mobState.IsCritical(ent))
                continue;

            var hasTeamFromComponent = TryComp<WH40KTeamMemberComponent>(ent, out var teamMember) &&
                                       !string.IsNullOrWhiteSpace(teamMember.TeamId);

            if (hasTeamFromComponent)
            {
                AddPresentTeam(teamMember!.TeamId);
                continue;
            }

            if (_teamRule.TryGetTeamIdFromEntity(ent, out var resolvedTeamId) &&
                !string.IsNullOrWhiteSpace(resolvedTeamId))
            {
                AddPresentTeam(resolvedTeamId);
            }
        }

        if (phase < point.CaptureEnabledFromPhase)
        {
            if (point.CapturingTeamId != null)
            {
                point.CapturingTeamId = null;
                dirty = true;
            }

            if (point.CaptureProgressSeconds > 0f)
            {
                point.CaptureProgressSeconds = 0f;
                dirty = true;
            }

            return dirty;
        }

        if (!TryGetControlState(out var controllingTeamId, out var dominance, out var contested))
        {
            if (contested)
                // Multiple teams with equal top presence: freeze current progress.
                return dirty;

            return DecayProgressWhenEmpty(point, frameTime, dirty);
        }

        var captureTime = Math.Max(1f, point.CaptureTimeSeconds);
        var captureSpeedPerSecond = Math.Max(0.01f, point.CaptureSpeedPerSecond);
        var maxMultiplier = Math.Max(1f, point.MaxCaptureSpeedMultiplier);
        var multiplier = Math.Clamp(dominance, 1f, maxMultiplier);
        var captureDelta = frameTime * captureSpeedPerSecond * multiplier;

        var controllingIsOwner = string.Equals(point.OwnerTeamId, controllingTeamId, StringComparison.OrdinalIgnoreCase);
        if (point.CapturingTeamId == null)
        {
            if (controllingIsOwner)
            {
                if (point.CaptureProgressSeconds > 0f)
                {
                    point.CaptureProgressSeconds = 0f;
                    dirty = true;
                }

                return dirty;
            }

            point.CapturingTeamId = controllingTeamId;
            point.CaptureProgressSeconds = 0f;
            dirty = true;
        }

        if (string.Equals(point.CapturingTeamId, controllingTeamId, StringComparison.OrdinalIgnoreCase))
        {
            point.CaptureProgressSeconds += captureDelta;
        }
        else
        {
            point.CaptureProgressSeconds = MathF.Max(0f, point.CaptureProgressSeconds - captureDelta);
            if (point.CaptureProgressSeconds <= 0f)
            {
                point.CaptureProgressSeconds = 0f;

                if (controllingIsOwner)
                {
                    if (point.CapturingTeamId != null)
                    {
                        point.CapturingTeamId = null;
                        dirty = true;
                    }

                    return dirty;
                }

                point.CapturingTeamId = controllingTeamId;
                dirty = true;
            }
        }

        if (point.CaptureProgressSeconds < captureTime || point.CapturingTeamId == null)
            return dirty;

        var capturedByTeamId = point.CapturingTeamId;
        if (!string.Equals(point.OwnerTeamId, capturedByTeamId, StringComparison.OrdinalIgnoreCase))
        {
            point.OwnerTeamId = capturedByTeamId;
            dirty = true;
        }

        if (point.CapturingTeamId != null)
        {
            point.CapturingTeamId = null;
            dirty = true;
        }

        point.CaptureProgressSeconds = 0f;
        point.NextRewardTick = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, point.RewardIntervalSeconds));

        if (!string.IsNullOrWhiteSpace(capturedByTeamId) &&
            _teamRule.TryGetTeamDisplayName(capturedByTeamId, out var teamName))
        {
            RaiseNetworkEvent(new WH40KLocalizedNotificationEvent
            {
                LocKey = "wh40k-influence-captured",
                LocArgs = new Dictionary<string, string> { ["team"] = teamName },
                ResolveArgValues = true,
                AccentColor = _teamRule.TryGetTeamColor(capturedByTeamId, out var teamColor)
                    ? teamColor
                    : WH40KNotificationColors.ForTeam(capturedByTeamId),
            });
        }

        if (!string.IsNullOrWhiteSpace(capturedByTeamId))
            RaiseLocalEvent(new WH40KInfluencePointCapturedEvent(capturedByTeamId, uid));

        return dirty;
    }

    private bool DecayProgressWhenEmpty(WH40KInfluencePointComponent point, float frameTime, bool dirty)
    {
        if (point.CaptureProgressSeconds <= 0f)
            return dirty;

        var decayPerSecond = Math.Max(0.01f, point.CaptureDecayPerSecond);
        point.CaptureProgressSeconds = MathF.Max(0f, point.CaptureProgressSeconds - frameTime * decayPerSecond);
        if (point.CaptureProgressSeconds == 0f && point.CapturingTeamId != null)
        {
            point.CapturingTeamId = null;
            dirty = true;
        }

        return dirty;
    }

    private void AddPresentTeam(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        if (_presentTeamCounts.TryGetValue(teamId, out var count))
        {
            _presentTeamCounts[teamId] = count + 1;
            return;
        }

        _presentTeamCounts[teamId] = 1;
    }

    private bool TryGetControlState(out string controllingTeamId, out float dominance, out bool contested)
    {
        controllingTeamId = string.Empty;
        dominance = 0f;
        contested = false;

        if (_presentTeamCounts.Count == 0)
            return false;

        var maxCount = 0;
        var secondCount = 0;
        var maxTied = false;

        foreach (var (teamId, count) in _presentTeamCounts)
        {
            if (count > maxCount)
            {
                secondCount = maxCount;
                maxCount = count;
                controllingTeamId = teamId;
                maxTied = false;
                continue;
            }

            if (count == maxCount)
            {
                maxTied = true;
                continue;
            }

            if (count > secondCount)
                secondCount = count;
        }

        if (maxCount <= 0 || string.IsNullOrWhiteSpace(controllingTeamId))
            return false;

        if (maxTied)
        {
            contested = true;
            return false;
        }

        // Speed scales with net local advantage: 1v0 => x1, 2v1 => x1, 3v1 => x2, etc.
        dominance = MathF.Max(1f, maxCount - secondCount);
        return true;
    }

    private static bool ShouldSyncProgress(WH40KInfluencePointComponent point, TimeSpan now)
    {
        if (now < point.NextCaptureProgressSyncAt)
            return false;

        var current = point.CaptureProgressSeconds;
        var synced = point.LastSyncedCaptureProgressSeconds;

        if (MathF.Abs(current - synced) >= MathF.Max(0.02f, point.CaptureProgressSyncStep))
            return true;

        if (current <= 0f && synced > 0f)
            return true;

        return false;
    }

    private void UpdateRewardState(EntityUid uid, WH40KInfluencePointComponent point, TimeSpan now)
    {
        // Legacy capture points are visual/capture-only during the strategic point rework.
        // Economy is now produced by WH40KStrategicPointComponent nodes.
    }

    private void AssignCallsign(EntityUid uid, WH40KInfluencePointComponent point)
    {
        if (string.IsNullOrWhiteSpace(point.Callsign))
            point.Callsign = FormatCallsign(_nextAutoCallsignIndex++);

        if (!TryComp<NavMapBeaconComponent>(uid, out var beacon))
            return;

        if (!ShouldReplaceBeaconText(beacon.Text))
            return;

        _navMap.SetBeaconLabel(uid, $"Точка {point.Callsign}", beacon);
    }

    private void NormalizeBeaconLabel(EntityUid uid, WH40KInfluencePointComponent point)
    {
        if (string.IsNullOrWhiteSpace(point.Callsign) ||
            !TryComp<NavMapBeaconComponent>(uid, out var beacon) ||
            !ShouldNormalizeBeaconText(beacon.Text, point.Callsign))
        {
            return;
        }

        _navMap.SetBeaconLabel(uid, point.Callsign, beacon);
    }

    private static bool ShouldReplaceBeaconText(string? currentText)
    {
        if (string.IsNullOrWhiteSpace(currentText))
            return true;

        return currentText.Equals("Capture point", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldNormalizeBeaconText(string? currentText, string callsign)
    {
        if (string.IsNullOrWhiteSpace(currentText))
            return true;

        if (string.IsNullOrWhiteSpace(callsign))
            return false;

        var trimmed = currentText.Trim();
        return trimmed.Equals("Capture point", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Point ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(callsign, StringComparison.OrdinalIgnoreCase) ||
               (trimmed.Length <= callsign.Length + 8 &&
                trimmed.EndsWith(callsign, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatCallsign(int index)
    {
        var safeIndex = Math.Max(0, index);
        var baseName = TacticalCallsignTokens[safeIndex % TacticalCallsignTokens.Length];
        var tier = safeIndex / TacticalCallsignTokens.Length;
        return tier == 0 ? baseName : $"{baseName}-{tier + 1}";
    }
}
