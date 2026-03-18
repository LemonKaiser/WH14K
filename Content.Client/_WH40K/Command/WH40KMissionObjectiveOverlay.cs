using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Interface;
using Content.Shared.Ghost;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Enums;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Command;

/// <summary>
/// Draws mission objective circles and labels in world space.
/// Faction objectives are shown only to same-team players, while ghosts can see all markers.
/// </summary>
public sealed class WH40KMissionObjectiveOverlay : Overlay
{
    private static readonly Color DefaultColor = Color.FromHex("#FFD250");
    private readonly IEntityManager _entManager;
    private readonly IPlayerManager _playerManager;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly Font _labelFont;
    private readonly Dictionary<string, Color> _teamColors = new();
    private string _localTeamId = string.Empty;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WH40KMissionObjectiveOverlay(IEntityManager entManager, IResourceCache cache, IPlayerManager playerManager)
    {
        _entManager = entManager;
        _playerManager = playerManager;
        _transform = _entManager.System<SharedTransformSystem>();
        _timing = IoCManager.Resolve<IGameTiming>();
        _labelFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 12);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null)
            return;

        var now = (float) _timing.CurTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(now * 2.4f);
        var matrix = args.ViewportControl.GetWorldToScreenMatrix();
        var localGhost = IsLocalGhostObserver();
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        var query = _entManager.AllEntityQueryEnumerator<WH40KMissionObjectiveVisualComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            if (!CanSeeMarker(marker.TeamId, localGhost))
                continue;

            var radius = MathF.Max(0.45f, marker.Radius);
            var position = _transform.GetWorldPosition(xform, xformQuery);
            var aabb = new Box2(position - new Vector2(radius, radius), position + new Vector2(radius, radius));
            if (!aabb.Intersects(args.WorldAABB))
                continue;

            var color = ResolveMarkerColor(marker);
            var fillAlpha = marker.Pulse ? 0.10f + 0.06f * pulse : 0.14f;
            var ringAlpha = marker.Pulse ? 0.75f + 0.17f * pulse : 0.85f;
            var innerAlpha = marker.Pulse ? 0.28f + 0.10f * pulse : 0.34f;

            var handle = args.WorldHandle;
            handle.DrawCircle(position, radius, color.WithAlpha(fillAlpha), true);
            handle.DrawCircle(position, radius, color.WithAlpha(ringAlpha), false);
            handle.DrawCircle(position, radius * 0.55f, color.WithAlpha(innerAlpha), false);

            var label = ResolveLocalizedOrRaw(marker.Label);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var screenPos = Vector2.Transform(position, matrix);
            var dimensions = args.ScreenHandle.GetDimensions(_labelFont, label, 1f);
            var labelPos = screenPos - new Vector2(dimensions.X / 2f, 36f + radius * 6f + dimensions.Y);
            args.ScreenHandle.DrawString(_labelFont, labelPos, label, color.WithAlpha(0.95f));
        }
    }

    public void SetLocalTeamId(string? teamId)
    {
        _localTeamId = teamId?.Trim() ?? string.Empty;
    }

    public void ApplyTeamColors(IReadOnlyList<WH40KTeamColorDefinition> teamColors)
    {
        _teamColors.Clear();
        foreach (var definition in teamColors)
        {
            if (string.IsNullOrWhiteSpace(definition.TeamId) || string.IsNullOrWhiteSpace(definition.ColorHex))
                continue;

            var color = Color.FromHex($"#{definition.ColorHex.Trim().TrimStart('#')}", DefaultColor);
            RegisterTeamColor(definition.TeamId, color);
        }
    }

    private bool CanSeeMarker(string markerTeamId, bool localGhost)
    {
        if (localGhost)
            return true;

        if (string.IsNullOrWhiteSpace(markerTeamId))
            return true;

        return string.Equals(markerTeamId, _localTeamId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLocalGhostObserver()
    {
        return _playerManager.LocalEntity is { } local &&
               _entManager.HasComponent<GhostComponent>(local);
    }

    private Color ResolveMarkerColor(WH40KMissionObjectiveVisualComponent marker)
    {
        if (!string.IsNullOrWhiteSpace(marker.TeamId))
        {
            var normalized = NormalizeTeamId(marker.TeamId);
            if (_teamColors.TryGetValue(normalized, out var teamColor))
                return teamColor;
        }

        return marker.Color == default ? DefaultColor : marker.Color;
    }

    private void RegisterTeamColor(string teamId, Color color)
    {
        var normalized = NormalizeTeamId(teamId);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        _teamColors[normalized] = color;
        if (normalized.StartsWith("wh40k", StringComparison.Ordinal))
            _teamColors[normalized.Substring(5)] = color;
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

    private static string ResolveLocalizedOrRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (Loc.TryGetString(value, out var localized) && !string.IsNullOrWhiteSpace(localized))
            return localized!;

        return value;
    }
}
