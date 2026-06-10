using System;
using System.Numerics;
using Content.Shared._WH40K.StrategicPoints;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.StrategicPoints;

public sealed partial class WH40KStrategicPointVisualizerSystem : EntitySystem
{
    [Dependency] private  SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KStrategicPointVisualsComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<WH40KStrategicPointVisualsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var layer = _sprite.LayerMapReserve((ent.Owner, args.Sprite), WH40KStrategicPointVisualLayers.Base);

        if (args.AppearanceData.TryGetValue(WH40KStrategicPointVisuals.AnchorHidden, out var hiddenObj) &&
            hiddenObj is bool hidden)
        {
            _sprite.LayerSetVisible((ent.Owner, args.Sprite), layer, !hidden);
        }

        if (!TryResolvePointVisualState(args, out var pointType, out var tier, out var rsiPath))
            return;

        _sprite.LayerSetVisible((ent.Owner, args.Sprite), layer, true);
        _sprite.SetOffset((ent.Owner, args.Sprite), ResolveSpriteOffset(pointType, tier));
        _sprite.LayerSetRsi((ent.Owner, args.Sprite), layer, new ResPath(rsiPath), new RSI.StateId("base"));
    }

    private static bool TryResolvePointVisualState(
        AppearanceChangeEvent args,
        out WH40KStrategicPointType pointType,
        out WH40KStrategicPointTier tier,
        out string rsiPath)
    {
        pointType = default;
        tier = default;
        rsiPath = string.Empty;

        if (!args.AppearanceData.TryGetValue(WH40KStrategicPointVisuals.PointType, out var typeObj) ||
            typeObj is not WH40KStrategicPointType resolvedType ||
            !args.AppearanceData.TryGetValue(WH40KStrategicPointVisuals.Tier, out var tierObj) ||
            tierObj is not WH40KStrategicPointTier resolvedTier ||
            resolvedTier <= WH40KStrategicPointTier.T0)
        {
            return false;
        }

        pointType = resolvedType;
        tier = resolvedTier;

        var team = "imperium";
        if (args.AppearanceData.TryGetValue(WH40KStrategicPointVisuals.OwnerTeamId, out var teamObj) &&
            teamObj is string teamId &&
            (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase)))
        {
            team = "chaos";
        }

        var tierValue = (int) tier;
        rsiPath = pointType switch
        {
            WH40KStrategicPointType.Resource => $"/Textures/_WH40K/StrategicPoints/resource/{team}/t{tierValue}.rsi",
            WH40KStrategicPointType.Research => $"/Textures/_WH40K/StrategicPoints/research/t{tierValue}/{team}.rsi",
            WH40KStrategicPointType.Influence => $"/Textures/_WH40K/StrategicPoints/flag/{team}/t{tierValue}.rsi",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(rsiPath);
    }

    private static Vector2 ResolveSpriteOffset(WH40KStrategicPointType pointType, WH40KStrategicPointTier tier)
    {
        return pointType switch
        {
            WH40KStrategicPointType.Resource => new Vector2(0f, 0.5f),
            WH40KStrategicPointType.Research when tier == WH40KStrategicPointTier.T1 => new Vector2(0f, 0.5f),
            WH40KStrategicPointType.Research when tier == WH40KStrategicPointTier.T2 => new Vector2(0f, 0.75f),
            WH40KStrategicPointType.Research when tier >= WH40KStrategicPointTier.T3 => new Vector2(0f, 1.25f),
            WH40KStrategicPointType.Influence when tier == WH40KStrategicPointTier.T1 => new Vector2(0f, 0.5f),
            WH40KStrategicPointType.Influence when tier >= WH40KStrategicPointTier.T2 => new Vector2(0f, 1.0f),
            _ => Vector2.Zero
        };
    }
}
