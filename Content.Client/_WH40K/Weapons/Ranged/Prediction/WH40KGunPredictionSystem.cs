using Content.Shared._WH40K.Weapons.Ranged.Prediction;
using Content.Shared.CCVar;
using Content.Shared.Projectiles;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Weapons.Ranged.Prediction;

public sealed partial class WH40KGunPredictionSystem : EntitySystem
{
    [Dependency] private  IConfigurationManager _config = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private bool _predictionEnabled;

    public override void Initialize()
    {
        SubscribeLocalEvent<ProjectileComponent, StartCollideEvent>(OnProjectileStartCollide);
        Subs.CVar(_config, CCVars.WH40KGunPrediction, value => _predictionEnabled = value, true);
    }

    private void OnProjectileStartCollide(Entity<ProjectileComponent> projectile, ref StartCollideEvent args)
    {
        if (!_predictionEnabled ||
            !_timing.IsFirstTimePredicted ||
            args.OurFixtureId != SharedProjectileSystem.ProjectileFixture ||
            !args.OtherFixture.Hard ||
            projectile.Comp.ProjectileSpent)
        {
            return;
        }

        if (!CanReportPrediction(projectile) ||
            args.OtherEntity == projectile.Comp.Shooter ||
            args.OtherEntity == projectile.Comp.Weapon ||
            HasComp<WH40KIgnorePredictionHitComponent>(args.OtherEntity))
        {
            return;
        }

        var predicted = EnsureComp<WH40KPredictedProjectileClientComponent>(projectile);
        if (predicted.HitReported)
            return;

        var projectileNet = GetNetEntity(projectile.Owner);
        var targetNet = GetNetEntity(args.OtherEntity);
        var targetCoordinates = _transform.GetMapCoordinates(args.OtherEntity);

        if (projectileNet == NetEntity.Invalid ||
            targetNet == NetEntity.Invalid ||
            targetCoordinates.MapId == MapId.Nullspace)
        {
            return;
        }

        var hit = new HashSet<(NetEntity Id, MapCoordinates Coordinates)>
        {
            (targetNet, targetCoordinates),
        };

        RaiseNetworkEvent(new WH40KPredictedProjectileHitEvent(projectileNet, hit));
        predicted.HitReported = true;
    }

    private bool CanReportPrediction(Entity<ProjectileComponent> projectile)
    {
        if (_players.LocalEntity is not { Valid: true } localEntity ||
            projectile.Comp.Shooter != localEntity)
        {
            return false;
        }

        if (projectile.Comp.Weapon is not { Valid: true } weaponUid ||
            !TryComp<WH40KGunPredictionComponent>(weaponUid, out var gunPrediction) ||
            !gunPrediction.Enabled)
        {
            return false;
        }

        return true;
    }
}
