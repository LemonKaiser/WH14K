using System;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Tiers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Cargo;

/// <summary>
/// Synchronizes WH40K cargo logistics tier from current team base level.
/// </summary>
public sealed class WH40KCargoLogisticsTierSyncSystem : EntitySystem
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(1);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;

    private TimeSpan _nextSync = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CargoLogisticsTierComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, CargoLogisticsTierComponent component, MapInitEvent args)
    {
        ApplyTierLogisticsProfile(component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSync)
            return;

        _nextSync = _timing.CurTime + SyncInterval;

        var query = EntityQueryEnumerator<CargoLogisticsTierComponent>();
        while (query.MoveNext(out var stationUid, out var logistics))
        {
            ApplyTierLogisticsProfile(logistics);

            if (logistics.AccountTeams.Count == 0)
                continue;

            foreach (var (account, teamId) in logistics.AccountTeams)
            {
                if (string.IsNullOrWhiteSpace(teamId))
                    continue;

                if (!_teamRule.TryGetTeamProgress(teamId, out var level, out _, out _))
                    continue;

                var targetTier = logistics.GetTierForBaseLevel(level);
                var currentTier = logistics.GetTier(account);
                if (targetTier == currentTier)
                    continue;

                _cargo.SetCargoLogisticsTier(stationUid, account, targetTier);
            }
        }
    }

    private void ApplyTierLogisticsProfile(CargoLogisticsTierComponent logistics)
    {
        if (logistics.TierLogisticsProfile is { } profileId &&
            _proto.TryIndex(profileId, out WH40KTierLogisticsProfilePrototype? profile))
        {
            if (profile.ThresholdProfile is { } thresholdId &&
                _proto.TryIndex(thresholdId, out WH40KTierThresholdProfilePrototype? threshold))
            {
                logistics.Tier1MinBaseLevel = threshold.Tier1MinBaseLevel;
                logistics.Tier2MinBaseLevel = threshold.Tier2MinBaseLevel;
                logistics.Tier3MinBaseLevel = threshold.Tier3MinBaseLevel;
            }

            logistics.Tier1MaxItemsBonus = profile.Tier1MaxItemsBonus;
            logistics.Tier2MaxItemsBonus = profile.Tier2MaxItemsBonus;
            logistics.Tier3MaxItemsBonus = profile.Tier3MaxItemsBonus;
            logistics.Tier1DeliveryMinutesReduction = profile.Tier1DeliveryMinutesReduction;
            logistics.Tier2DeliveryMinutesReduction = profile.Tier2DeliveryMinutesReduction;
            logistics.Tier3DeliveryMinutesReduction = profile.Tier3DeliveryMinutesReduction;
        }

        var (tier1, tier2, tier3) = WH40KTierMath.NormalizeThresholds(
            logistics.Tier1MinBaseLevel,
            logistics.Tier2MinBaseLevel,
            logistics.Tier3MinBaseLevel);

        logistics.Tier1MinBaseLevel = tier1;
        logistics.Tier2MinBaseLevel = tier2;
        logistics.Tier3MinBaseLevel = tier3;

        logistics.Tier1MaxItemsBonus = Math.Max(0, logistics.Tier1MaxItemsBonus);
        logistics.Tier2MaxItemsBonus = Math.Max(0, logistics.Tier2MaxItemsBonus);
        logistics.Tier3MaxItemsBonus = Math.Max(0, logistics.Tier3MaxItemsBonus);
        logistics.Tier1DeliveryMinutesReduction = Math.Max(0, logistics.Tier1DeliveryMinutesReduction);
        logistics.Tier2DeliveryMinutesReduction = Math.Max(0, logistics.Tier2DeliveryMinutesReduction);
        logistics.Tier3DeliveryMinutesReduction = Math.Max(0, logistics.Tier3DeliveryMinutesReduction);
    }
}
