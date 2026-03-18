using Content.Server.Movement.Components;
using Content.Server.Projectiles;
using Content.Shared._WH40K.Weapons.Ranged.Prediction;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Projectiles;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._WH40K.Weapons.Ranged.Prediction;

public sealed class WH40KGunPredictionSystem : EntitySystem
{
    private const int DefaultMaxHitsPerReport = 8;

    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ProjectileSystem _projectiles = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly List<(WH40KPredictedProjectileHitEvent Event, ICommonSession Reporter)> _pendingHits = [];

    private bool _predictionEnabled;
    private bool _logRejectedHits;
    private float _coordinateDeviation;
    private float _lowestCoordinateDeviation;
    private float _aabbEnlargement;
    private float _maxReportAgeSeconds;
    private int _maxHitsPerReport = DefaultMaxHitsPerReport;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<WH40KGunPredictionComponent> _gunPredictionQuery;
    private EntityQuery<LagCompensationComponent> _lagCompensationQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _gunPredictionQuery = GetEntityQuery<WH40KGunPredictionComponent>();
        _lagCompensationQuery = GetEntityQuery<LagCompensationComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<ProjectileComponent, MapInitEvent>(OnProjectileMapInit);
        SubscribeNetworkEvent<WH40KPredictedProjectileHitEvent>(OnPredictedProjectileHit);

        Subs.CVar(_config, CCVars.WH40KGunPrediction, value => _predictionEnabled = value, true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionLogRejectedHits, value => _logRejectedHits = value, true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionCoordinateDeviation, value => _coordinateDeviation = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionLowestCoordinateDeviation, value => _lowestCoordinateDeviation = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionAabbEnlargement, value => _aabbEnlargement = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionMaxReportAgeSeconds, value => _maxReportAgeSeconds = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.WH40KGunPredictionMaxHitsPerReport, value => _maxHitsPerReport = Math.Max(1, value), true);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _pendingHits.Clear();
    }

    private void OnProjectileMapInit(Entity<ProjectileComponent> projectile, ref MapInitEvent args)
    {
        if (!TryGetPrediction(projectile.Comp.Weapon, out var prediction) || prediction is null || !prediction.Enabled)
            return;

        var predicted = EnsureComp<WH40KPredictedProjectileServerComponent>(projectile);
        if (predicted.SpawnTime == default)
            predicted.SpawnTime = _timing.CurTime;
    }

    private void OnPredictedProjectileHit(WH40KPredictedProjectileHitEvent ev, EntitySessionEventArgs args)
    {
        if (!_predictionEnabled || ev.Hit.Count == 0)
            return;

        if (ev.Hit.Count > _maxHitsPerReport)
        {
            if (_logRejectedHits)
            {
                Log.Debug($"WH40K prediction rejected hit report: projectileNet={ev.Projectile} reason=payload-too-large size={ev.Hit.Count} max={_maxHitsPerReport}");
            }

            return;
        }

        _pendingHits.Add((ev, args.SenderSession));
    }

    public override void Update(float frameTime)
    {
        if (_pendingHits.Count == 0)
            return;

        try
        {
            if (!_predictionEnabled)
                return;

            foreach (var (ev, reporter) in _pendingHits)
            {
                ProcessPredictedHit(ev, reporter);
            }
        }
        finally
        {
            _pendingHits.Clear();
        }
    }

    private void ProcessPredictedHit(WH40KPredictedProjectileHitEvent ev, ICommonSession reporter)
    {
        var projectileUid = GetEntity(ev.Projectile);
        if (TerminatingOrDeleted(projectileUid) ||
            !_projectileQuery.TryComp(projectileUid, out var projectileComp) ||
            !_physicsQuery.TryComp(projectileUid, out var projectilePhysics) ||
            !_transformQuery.TryComp(projectileUid, out var projectileTransform))
        {
            return;
        }

        if (projectileComp.ProjectileSpent ||
            !TryGetPrediction(projectileComp.Weapon, out var prediction) ||
            prediction is null ||
            !prediction.Enabled ||
            !IsShooterSessionMatch(projectileComp.Shooter, reporter))
        {
            if (_logRejectedHits)
                Log.Debug($"WH40K prediction rejected hit report: projectile={ToPrettyString(projectileUid)} reason=projectile-validation-failed");

            return;
        }

        var predictedServer = EnsureComp<WH40KPredictedProjectileServerComponent>(projectileUid);
        if (predictedServer.SpawnTime == default)
            predictedServer.SpawnTime = _timing.CurTime;

        if (predictedServer.Consumed)
        {
            LogRejected(projectileUid, null, "already-consumed");
            return;
        }

        var reportAgeSeconds = (float) (_timing.CurTime - predictedServer.SpawnTime).TotalSeconds;
        if (_maxReportAgeSeconds > 0f && reportAgeSeconds > _maxReportAgeSeconds)
        {
            predictedServer.Consumed = true;
            LogRejected(projectileUid, null, $"stale-report age={reportAgeSeconds:F2}s");
            return;
        }

        // Consume first valid-shooter report for this projectile to block duplicate/stale replay spam.
        predictedServer.Consumed = true;

        foreach (var (netTarget, clientCoordinates) in ev.Hit)
        {
            var targetUid = GetEntity(netTarget);
            if (TerminatingOrDeleted(targetUid) ||
                targetUid == projectileUid ||
                targetUid == projectileComp.Weapon ||
                targetUid == projectileComp.Shooter ||
                HasComp<WH40KIgnorePredictionHitComponent>(targetUid))
            {
                LogRejected(projectileUid, targetUid, "target-filtered");
                continue;
            }

            if (predictedServer.AcceptedTargets.Contains(targetUid))
            {
                LogRejected(projectileUid, targetUid, "duplicate-target");
                continue;
            }

            if (!_lagCompensationQuery.TryComp(targetUid, out var targetLagComp) ||
                !_fixturesQuery.TryComp(targetUid, out var targetFixtures) ||
                !_physicsQuery.TryComp(targetUid, out var targetPhysics) ||
                !_transformQuery.TryComp(targetUid, out var targetTransform))
            {
                LogRejected(projectileUid, targetUid, "target-missing-required-components");
                continue;
            }

            if (!Collides(
                    (projectileUid, projectileComp, projectilePhysics, projectileTransform),
                    (targetUid, targetLagComp, targetFixtures, targetPhysics, targetTransform),
                    clientCoordinates,
                    reporter))
            {
                LogRejected(projectileUid, targetUid, "collision-validation-failed");
                continue;
            }

            if (_projectiles.TryProcessPredictedHit(projectileUid, targetUid))
            {
                predictedServer.AcceptedTargets.Add(targetUid);
                return;
            }

            LogRejected(projectileUid, targetUid, "projectile-reconcile-failed");
        }
    }

    private bool TryGetPrediction(EntityUid? weaponUid, out WH40KGunPredictionComponent? prediction)
    {
        prediction = null;
        return weaponUid is { Valid: true } weapon && _gunPredictionQuery.TryComp(weapon, out prediction);
    }

    private bool IsShooterSessionMatch(EntityUid? shooterUid, ICommonSession reporter)
    {
        if (shooterUid is not { Valid: true } shooter || TerminatingOrDeleted(shooter))
            return false;

        if (_players.TryGetSessionByEntity(shooter, out var shooterSession))
            return shooterSession.UserId == reporter.UserId;

        return reporter.AttachedEntity == shooter;
    }

    private bool Collides(
        Entity<ProjectileComponent, PhysicsComponent, TransformComponent> projectile,
        Entity<LagCompensationComponent, FixturesComponent, PhysicsComponent, TransformComponent> target,
        MapCoordinates clientCoordinates,
        ICommonSession reporter)
    {
        var projectileCoordinates = _transform.GetMapCoordinates(projectile);
        if (projectileCoordinates.MapId == MapId.Nullspace)
            return false;

        var projectilePosition = projectileCoordinates.Position;
        var ping = reporter.Ping;
        var sentTime = _timing.CurTime - TimeSpan.FromMilliseconds(ping * 1.5);
        var pingTime = TimeSpan.FromMilliseconds(ping);

        MapCoordinates lowestCoordinates = default;
        MapCoordinates targetCoordinates = default;

        foreach (var entry in target.Comp1.Positions)
        {
            targetCoordinates = _transform.ToMapCoordinates(entry.Item2);

            if (lowestCoordinates == default && entry.Item1 >= sentTime - pingTime)
                lowestCoordinates = targetCoordinates;

            if (entry.Item1 >= sentTime)
                break;
        }

        if (targetCoordinates == default)
            targetCoordinates = _transform.GetMapCoordinates(target);

        if (lowestCoordinates == default)
            lowestCoordinates = targetCoordinates;

        if (clientCoordinates.MapId == targetCoordinates.MapId &&
            (clientCoordinates.InRange(targetCoordinates, _coordinateDeviation) ||
             clientCoordinates.InRange(lowestCoordinates, _lowestCoordinateDeviation)))
        {
            targetCoordinates = clientCoordinates;
        }

        if (targetCoordinates.MapId != projectileCoordinates.MapId)
            return false;

        var broadphaseTransform = new PhysicsTransform(targetCoordinates.Position, 0);
        var hasBounds = false;
        Box2 bounds = default;

        foreach (var fixture in target.Comp2.Fixtures.Values)
        {
            if (!fixture.Hard ||
                (fixture.CollisionLayer & projectile.Comp2.CollisionMask) == 0)
            {
                continue;
            }

            for (var child = 0; child < fixture.Shape.ChildCount; child++)
            {
                var fixtureBounds = fixture.Shape.ComputeAABB(broadphaseTransform, child);
                bounds = hasBounds ? bounds.Union(fixtureBounds) : fixtureBounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
            return false;

        bounds = bounds.Enlarged(_aabbEnlargement);
        if (bounds.Contains(projectilePosition))
            return true;

        var velocity = _physics.GetLinearVelocity(projectile, projectile.Comp2.LocalCenter);
        projectilePosition += velocity / _timing.TickRate / 1.5f;
        return bounds.Contains(projectilePosition);
    }

    private void LogRejected(EntityUid projectileUid, EntityUid? targetUid, string reason)
    {
        if (!_logRejectedHits)
            return;

        var targetText = targetUid is { } target ? ToPrettyString(target) : "<none>";
        Log.Debug($"WH40K prediction rejected hit report: projectile={ToPrettyString(projectileUid)} target={targetText} reason={reason}");
    }
}
