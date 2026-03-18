using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.Interface;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Influence;

/// <summary>
/// Draws influence capture zones in world space with faction coloring.
/// </summary>
public sealed class WH40KInfluenceOverlay : Overlay
{
    private static readonly Color NeutralColor = Color.FromHex("#7f8790");
    private static readonly Color ImperiumColor = Color.FromHex("#FFD200");
    private static readonly Color HereticsColor = Color.FromHex("#D62828");
    private const float BaseTransparency = 0.70f;
    private const float InsidePointTransparency = 0.80f;

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly IPlayerManager _player;
    private readonly Dictionary<EntityUid, float> _displayedCaptureProgress = new();
    private readonly Dictionary<string, Color> _teamColors = new();
    private TimeSpan _lastDrawTime;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WH40KInfluenceOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = _entManager.System<SharedTransformSystem>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _player = IoCManager.Resolve<IPlayerManager>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var now = (float) _timing.CurTime.TotalSeconds;
        var dt = (float) Math.Clamp((_timing.CurTime - _lastDrawTime).TotalSeconds, 0, 0.25);
        _lastDrawTime = _timing.CurTime;
        var localEntity = _player.LocalEntity;
        var hasLocalPosition = false;
        var localPosition = Vector2.Zero;

        if (localEntity != null &&
            _entManager.TryGetComponent<TransformComponent>(localEntity, out var localXform) &&
            localXform.MapID == args.MapId)
        {
            localPosition = _transform.GetWorldPosition(localXform, xformQuery);
            hasLocalPosition = true;
        }

        var query = _entManager.AllEntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var radius = MathF.Max(0.5f, point.CaptureRadius);
            var worldPos = _transform.GetWorldPosition(xform, xformQuery);

            var aabb = new Box2(worldPos - new Vector2(radius, radius), worldPos + new Vector2(radius, radius));
            if (!aabb.Intersects(args.WorldAABB))
                continue;

            var ownerColor = GetTeamColor(point.OwnerTeamId);
            var captureColor = GetTeamColor(point.CapturingTeamId);
            var isCapturing = !string.IsNullOrWhiteSpace(point.CapturingTeamId);
            var isContestedCapture = isCapturing &&
                                     !string.IsNullOrWhiteSpace(point.OwnerTeamId) &&
                                     !string.Equals(point.OwnerTeamId, point.CapturingTeamId, StringComparison.OrdinalIgnoreCase);
            var isInsidePoint = hasLocalPosition && (localPosition - worldPos).LengthSquared() <= radius * radius;
            var alphaScale = GetAlphaScale(isInsidePoint ? InsidePointTransparency : BaseTransparency);

            var pulse = 0.5f + 0.5f * MathF.Sin(now * 2.7f);
            var ringColor = NeutralColor;
            var fillAlpha = 0.08f * alphaScale;
            var ringAlpha = 0.80f * alphaScale;
            var isNeutralIdlePoint = ownerColor == NeutralColor && !isCapturing;

            if (isNeutralIdlePoint)
            {
                // Neutral points need extra contrast against map tiles.
                handle.DrawCircle(worldPos, radius, Color.Black.WithAlpha(0.28f * alphaScale), true);
                fillAlpha = 0.20f * alphaScale;
                ringAlpha = 1.00f * alphaScale;
            }

            handle.DrawCircle(worldPos, radius, ringColor.WithAlpha(fillAlpha), true);
            handle.DrawCircle(worldPos, radius, ringColor.WithAlpha(ringAlpha), false);

            if (ownerColor != NeutralColor)
            {
                handle.DrawCircle(worldPos, radius, ownerColor.WithAlpha(0.20f * alphaScale), true);
                handle.DrawCircle(worldPos, radius, ownerColor.WithAlpha(0.65f * alphaScale), false);
            }

            // Radial fill shows capture progress from center to edge.
            if (isCapturing)
            {
                var captureTime = MathF.Max(1f, point.CaptureTimeSeconds);
                var targetProgress = Math.Clamp(point.CaptureProgressSeconds / captureTime, 0f, 1f);
                if (!_displayedCaptureProgress.TryGetValue(uid, out var shownProgress))
                    shownProgress = targetProgress;

                // Ease in updates from server snapshots for smoother radial growth.
                var progressLerp = Math.Clamp(dt * 8f, 0f, 1f);
                shownProgress += (targetProgress - shownProgress) * progressLerp;
                _displayedCaptureProgress[uid] = shownProgress;

                var easedProgress = shownProgress * shownProgress * (3f - 2f * shownProgress);
                var progressRadius = MathF.Max(0.15f, radius * easedProgress);
                var progressFill = captureColor.WithAlpha((0.16f + 0.18f * pulse) * alphaScale);

                handle.DrawCircle(worldPos, progressRadius, progressFill, true);
                handle.DrawCircle(worldPos, progressRadius, captureColor.WithAlpha(0.92f * alphaScale), false);

                // Keep pulse closer to outer half so it does not grow from center.
                var pulseRadius = radius * (0.55f + 0.45f * pulse);
                handle.DrawCircle(worldPos, pulseRadius, captureColor.WithAlpha(0.72f * alphaScale), false);
            }
            else if (_displayedCaptureProgress.ContainsKey(uid))
            {
                _displayedCaptureProgress.Remove(uid);
            }

            if (isContestedCapture)
                handle.DrawCircle(worldPos, radius * 0.78f, ownerColor.WithAlpha(0.65f * alphaScale), false);
        }
    }

    private static float GetAlphaScale(float transparencyPercent)
    {
        return Math.Clamp(1f - transparencyPercent, 0f, 1f);
    }

    public void ApplyTeamColors(IReadOnlyList<WH40KTeamColorDefinition> teamColors)
    {
        _teamColors.Clear();

        foreach (var entry in teamColors)
        {
            if (string.IsNullOrWhiteSpace(entry.TeamId) || string.IsNullOrWhiteSpace(entry.ColorHex))
                continue;

            var raw = entry.ColorHex.Trim();
            var normalizedHexBody = raw.TrimStart('#');
            if (string.IsNullOrWhiteSpace(normalizedHexBody))
                continue;

            var hex = $"#{normalizedHexBody}";
            var color = Color.FromHex(hex, NeutralColor);
            RegisterTeamColor(entry.TeamId, color);
        }
    }

    private Color GetTeamColor(string? teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return NeutralColor;

        var normalizedTeamId = NormalizeTeamId(teamId);

        if (_teamColors.TryGetValue(normalizedTeamId, out var color) &&
            color != NeutralColor)
        {
            return color;
        }

        if (normalizedTeamId is "imperium" or "wh40kimperium")
            return ImperiumColor;

        if (normalizedTeamId is "heretics" or "wh40kheretics")
            return HereticsColor;

        return NeutralColor;
    }

    private void RegisterTeamColor(string teamId, Color color)
    {
        var normalizedTeamId = NormalizeTeamId(teamId);
        if (string.IsNullOrWhiteSpace(normalizedTeamId))
            return;

        _teamColors[normalizedTeamId] = color;

        const string wh40kPrefix = "wh40k";
        if (normalizedTeamId.StartsWith(wh40kPrefix, StringComparison.Ordinal))
        {
            var withoutPrefix = normalizedTeamId.Substring(wh40kPrefix.Length);
            if (!string.IsNullOrWhiteSpace(withoutPrefix))
                _teamColors[withoutPrefix] = color;
        }
    }

    private static string NormalizeTeamId(string teamId)
    {
        return teamId
            .Trim()
            .ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
    }
}
