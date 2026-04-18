using System;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server._WH40K.Localizations;
using Content.Shared.Bed.Sleep;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Handles Imperium astral node ownership and discipline point spending.
/// The client only requests purchases; all unlock validation lives here.
/// </summary>
public sealed class WH40KPsykerAstralProgressionSystem : EntitySystem
{
    private const string RootNodeId = "PsykerAstralAnchor";
    private const int MaxCollectibleStars = 10;
    private const int CollectibleStarXpMin = 5;
    private const int CollectibleStarXpMax = 10;
    private static readonly TimeSpan CollectibleStarMinInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CollectibleStarMaxInterval = TimeSpan.FromSeconds(30);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly WH40KPsykerAstralRiskSystem _risks = default!;
    [Dependency] private readonly SharedWH40KPsykerProgressionSystem _progression = default!;

    private string _activeLayoutId = string.Empty;
    private int _activeLayoutRoundId = -1;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerAstralProgressionComponent, ComponentStartup>(OnAstralProgressionStartup);
        SubscribeLocalEvent<WH40KPsykerAstralProjectionComponent, ComponentStartup>(OnAstralProjectionStartup);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeNetworkEvent<WH40KPsykerAstralPurchaseNodeRequestEvent>(OnPurchaseNodeRequest);
        SubscribeNetworkEvent<WH40KPsykerAstralCollectStarRequestEvent>(OnCollectStarRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<
            WH40KPsykerAstralProgressionComponent,
            WH40KPsykerProgressionComponent,
            WH40KPsykerRoleComponent>();

        while (query.MoveNext(out var uid, out var astralProgression, out var levelProgression, out _))
        {
            SyncProgression(uid, astralProgression, levelProgression);
            UpdateCollectibleStars(uid, astralProgression);
        }
    }

    private void OnAstralProgressionStartup(Entity<WH40KPsykerAstralProgressionComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<WH40KPsykerProgressionComponent>(ent.Owner, out var levelProgression))
            return;

        SyncProgression(ent.Owner, ent.Comp, levelProgression);
    }

    private void OnAstralProjectionStartup(Entity<WH40KPsykerAstralProjectionComponent> ent, ref ComponentStartup args)
    {
        _risks.HandleAstralEntry(ent.Owner, ent.Comp);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _activeLayoutId = string.Empty;
        _activeLayoutRoundId = -1;
    }

    private void OnPurchaseNodeRequest(WH40KPsykerAstralPurchaseNodeRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !HasComp<WH40KPsykerRoleComponent>(user) ||
            HasComp<WH40KChaosGiftRoleComponent>(user))
        {
            return;
        }

        var astralProgression = EnsureComp<WH40KPsykerAstralProgressionComponent>(user);
        var levelProgression = EnsureComp<WH40KPsykerProgressionComponent>(user);
        SyncProgression(user, astralProgression, levelProgression);

        if (!TryPurchaseNode(user, ev.NodeId, astralProgression, levelProgression, out var popupKey, out var popupArgs))
        {
            PopupCaution(user, popupKey, popupArgs);
            return;
        }

        PopupSuccess(user, popupKey, popupArgs);
    }

    private void OnCollectStarRequest(WH40KPsykerAstralCollectStarRequestEvent ev, EntitySessionEventArgs args)
    {
        if (ev.StarId <= 0 ||
            args.SenderSession.AttachedEntity is not { } user ||
            !HasComp<WH40KPsykerRoleComponent>(user) ||
            HasComp<WH40KChaosGiftRoleComponent>(user) ||
            !TryComp<WH40KPsykerAstralProgressionComponent>(user, out var astralProgression) ||
            !TryComp<WH40KPsykerProgressionComponent>(user, out var levelProgression))
        {
            return;
        }

        SyncProgression(user, astralProgression, levelProgression);
        if (!TryCollectStar(user, ev.StarId, astralProgression, levelProgression, out var xpReward))
            return;

        PopupSuccess(user, "wh40k-psyker-astral-popup-star-harvested", ("xp", xpReward));
    }

    private bool TryPurchaseNode(
        EntityUid user,
        string nodeId,
        WH40KPsykerAstralProgressionComponent astralProgression,
        WH40KPsykerProgressionComponent levelProgression,
        out string popupKey,
        out (string, object)[] popupArgs)
    {
        popupKey = "wh40k-psyker-astral-popup-invalid-node";
        popupArgs = Array.Empty<(string, object)>();

        if (string.IsNullOrWhiteSpace(nodeId) ||
            !_prototype.TryIndex<WH40KPsykerDisciplineNodePrototype>(nodeId, out var node))
        {
            return false;
        }

        if (astralProgression.UnlockedNodes.Contains(nodeId, StringComparer.Ordinal))
        {
            popupKey = "wh40k-psyker-astral-popup-already-unlocked";
            popupArgs = new[] { ("node", (object) Loc.GetString(node.Name)) };
            return false;
        }

        if (!TryComp<WH40KPsykerAstralProjectionComponent>(user, out var astralProjection) ||
            !HasComp<SleepingComponent>(user))
        {
            popupKey = "wh40k-psyker-astral-popup-astral-only";
            return false;
        }

        if (_timing.CurTime < astralProjection.FadeEndsAt)
        {
            popupKey = "wh40k-psyker-astral-popup-fade-pending";
            return false;
        }

        if (node.RequiredLevel > levelProgression.Level)
        {
            popupKey = "wh40k-psyker-astral-popup-level-locked";
            popupArgs = new[] { ("level", (object) node.RequiredLevel) };
            return false;
        }

        if (TryGetMissingPrerequisite(node, astralProgression, out var prerequisiteName))
        {
            popupKey = "wh40k-psyker-astral-popup-prerequisite-locked";
            popupArgs = new[] { ("node", (object) prerequisiteName) };
            return false;
        }

        if (IsCapstone(node) && astralProgression.UnlockedCapstoneCount >= 1)
        {
            popupKey = "wh40k-psyker-astral-popup-capstone-locked";
            return false;
        }

        if (astralProgression.DisciplinePoints < node.Cost)
        {
            popupKey = "wh40k-psyker-astral-popup-not-enough-points";
            popupArgs = new[] { ("points", (object) Math.Max(0, node.Cost - astralProgression.DisciplinePoints)) };
            return false;
        }

        if (node.InstabilityRisk > 0f && IsWarpSealed(user))
        {
            popupKey = "wh40k-psyker-astral-popup-warp-sealed";
            return false;
        }

        astralProgression.UnlockedNodes.Add(nodeId);

        if (!string.IsNullOrWhiteSpace(node.PlannedAction) &&
            !astralProgression.PendingUnlockEffects.Contains(node.PlannedAction, StringComparer.Ordinal))
        {
            astralProgression.PendingUnlockEffects.Add(node.PlannedAction);
        }

        _risks.HandleRiskyNodePurchase(user, node);
        SyncProgression(user, astralProgression, levelProgression);

        popupKey = "wh40k-psyker-astral-popup-purchase-success";
        popupArgs = new[] { ("node", (object) Loc.GetString(node.Name)) };
        return true;
    }

    private bool SyncProgression(
        EntityUid uid,
        WH40KPsykerAstralProgressionComponent astralProgression,
        WH40KPsykerProgressionComponent levelProgression)
    {
        var changed = false;

        changed |= EnsureRootUnlocked(astralProgression);
        changed |= EnsureUniqueList(astralProgression.UnlockedNodes);
        changed |= EnsureUniqueList(astralProgression.PendingUnlockEffects);

        var totalEarned = GetTotalDisciplinePoints(levelProgression.Level);
        if (astralProgression.TotalDisciplinePointsEarned != totalEarned)
        {
            astralProgression.TotalDisciplinePointsEarned = totalEarned;
            changed = true;
        }

        var spentPoints = GetSpentPoints(astralProgression.UnlockedNodes);
        var availablePoints = Math.Max(0, totalEarned - spentPoints);
        if (astralProgression.DisciplinePoints != availablePoints)
        {
            astralProgression.DisciplinePoints = availablePoints;
            changed = true;
        }

        var depth = ResolveAstralDepth(levelProgression.Level);
        if (astralProgression.AstralDepth != depth)
        {
            astralProgression.AstralDepth = depth;
            changed = true;
        }

        var capstoneCount = CountUnlockedCapstones(astralProgression.UnlockedNodes);
        if (astralProgression.UnlockedCapstoneCount != capstoneCount)
        {
            astralProgression.UnlockedCapstoneCount = capstoneCount;
            changed = true;
        }

        var clampedStrain = WH40KPsykerAstralMath.ClampAstralStrain(astralProgression.AstralStrain);
        if (MathF.Abs(astralProgression.AstralStrain - clampedStrain) > 0.001f)
        {
            astralProgression.AstralStrain = clampedStrain;
            changed = true;
        }

        var layoutId = ResolveRoundConstellationLayoutId();
        if (!string.Equals(astralProgression.ConstellationLayoutId, layoutId, StringComparison.Ordinal))
        {
            astralProgression.ConstellationLayoutId = layoutId;
            changed = true;
        }

        if (changed)
            Dirty(uid, astralProgression);

        return changed;
    }

    private void UpdateCollectibleStars(EntityUid uid, WH40KPsykerAstralProgressionComponent progression)
    {
        if (!IsAstralActive(uid))
        {
            ResetCollectibleStars(uid, progression);
            return;
        }

        if (progression.NextCollectibleStarAt == TimeSpan.Zero)
            ScheduleNextCollectibleStar(progression);

        if (_timing.CurTime < progression.NextCollectibleStarAt)
            return;

        if (progression.CollectibleStars.Count >= MaxCollectibleStars)
        {
            ScheduleNextCollectibleStar(progression);
            return;
        }

        if (!TrySpawnCollectibleStar(progression, out var star))
        {
            ScheduleNextCollectibleStar(progression);
            return;
        }

        progression.CollectibleStars.Add(star);
        ScheduleNextCollectibleStar(progression);
        Dirty(uid, progression);
    }

    private bool TryGetMissingPrerequisite(
        WH40KPsykerDisciplineNodePrototype node,
        WH40KPsykerAstralProgressionComponent astralProgression,
        out string prerequisiteName)
    {
        foreach (var prerequisiteId in node.Requires)
        {
            if (astralProgression.UnlockedNodes.Contains(prerequisiteId, StringComparer.Ordinal))
                continue;

            if (_prototype.TryIndex<WH40KPsykerDisciplineNodePrototype>(prerequisiteId, out var prerequisite))
            {
                prerequisiteName = Loc.GetString(prerequisite.Name);
                return true;
            }

            prerequisiteName = prerequisiteId;
            return true;
        }

        prerequisiteName = string.Empty;
        return false;
    }

    private bool EnsureRootUnlocked(WH40KPsykerAstralProgressionComponent progression)
    {
        if (progression.UnlockedNodes.Contains(RootNodeId, StringComparer.Ordinal))
            return false;

        progression.UnlockedNodes.Insert(0, RootNodeId);
        return true;
    }

    private static bool EnsureUniqueList(List<string> values)
    {
        if (values.Count <= 1)
            return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(values[i]))
            {
                values.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    private void ResetCollectibleStars(EntityUid uid, WH40KPsykerAstralProgressionComponent progression)
    {
        var changed = false;

        if (progression.CollectibleStars.Count > 0)
        {
            progression.CollectibleStars.Clear();
            changed = true;
        }

        if (progression.NextCollectibleStarId != 1)
            progression.NextCollectibleStarId = 1;

        progression.NextCollectibleStarAt = TimeSpan.Zero;

        if (changed)
            Dirty(uid, progression);
    }

    private int GetSpentPoints(IEnumerable<string> unlockedNodes)
    {
        var total = 0;

        foreach (var nodeId in unlockedNodes)
        {
            if (!_prototype.TryIndex<WH40KPsykerDisciplineNodePrototype>(nodeId, out var node))
                continue;

            total += Math.Max(0, node.Cost);
        }

        return total;
    }

    private int CountUnlockedCapstones(IEnumerable<string> unlockedNodes)
    {
        var count = 0;

        foreach (var nodeId in unlockedNodes)
        {
            if (!_prototype.TryIndex<WH40KPsykerDisciplineNodePrototype>(nodeId, out var node))
                continue;

            if (IsCapstone(node))
                count++;
        }

        return count;
    }

    private static int GetTotalDisciplinePoints(int level)
    {
        var normalizedLevel = Math.Max(1, level);
        var bonusPoints = 0;

        if (normalizedLevel >= 5)
            bonusPoints++;

        if (normalizedLevel >= 10)
            bonusPoints++;

        return normalizedLevel + bonusPoints;
    }

    private static int ResolveAstralDepth(int level)
    {
        var depth = 1;

        if (level >= 3)
            depth++;

        if (level >= 5)
            depth++;

        if (level >= 8)
            depth++;

        if (level >= 10)
            depth++;

        return depth;
    }

    private static bool IsCapstone(WH40KPsykerDisciplineNodePrototype node)
    {
        return node.RequiredLevel >= 10 || node.Tier >= 4;
    }

    private bool IsAstralActive(EntityUid uid)
    {
        return TryComp<WH40KPsykerAstralProjectionComponent>(uid, out _) &&
               HasComp<SleepingComponent>(uid);
    }

    private void ScheduleNextCollectibleStar(WH40KPsykerAstralProgressionComponent progression)
    {
        var delaySeconds = _random.NextFloat(
            (float) CollectibleStarMinInterval.TotalSeconds,
            (float) CollectibleStarMaxInterval.TotalSeconds);
        progression.NextCollectibleStarAt = _timing.CurTime + TimeSpan.FromSeconds(delaySeconds);
    }

    private bool TrySpawnCollectibleStar(
        WH40KPsykerAstralProgressionComponent progression,
        out WH40KPsykerAstralCollectibleStar star)
    {
        star = default;

        if (string.IsNullOrWhiteSpace(progression.ConstellationLayoutId) ||
            !_prototype.TryIndex<WH40KPsykerAstralLayoutPrototype>(progression.ConstellationLayoutId, out var layout) ||
            layout.Positions.Count == 0)
        {
            return false;
        }

        const int attempts = 10;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var anchor = _random.Pick(layout.Positions);
            var angle = _random.NextFloat() * MathF.Tau;
            var distance = _random.NextFloat(0.055f, 0.13f);
            var candidate = new Vector2(
                Math.Clamp(anchor.X + MathF.Cos(angle) * distance * 1.1f, 0.08f, 0.92f),
                Math.Clamp(anchor.Y + MathF.Sin(angle) * distance * 0.9f, 0.10f, 0.90f));

            if (!IsCollectibleStarPositionValid(candidate, progression.CollectibleStars, layout))
                continue;

            star = new WH40KPsykerAstralCollectibleStar(
                progression.NextCollectibleStarId++,
                candidate.X,
                candidate.Y,
                _random.Next(CollectibleStarXpMin, CollectibleStarXpMax + 1),
                _random.NextFloat(0.84f, 1.22f),
                (byte) _random.Next(0, 4));
            return true;
        }

        return false;
    }

    private static bool IsCollectibleStarPositionValid(
        Vector2 candidate,
        IEnumerable<WH40KPsykerAstralCollectibleStar> activeStars,
        WH40KPsykerAstralLayoutPrototype layout)
    {
        foreach (var activeStar in activeStars)
        {
            var delta = candidate - new Vector2(activeStar.X, activeStar.Y);
            if (delta.LengthSquared() < 0.0064f)
                return false;
        }

        foreach (var node in layout.Positions)
        {
            var delta = candidate - new Vector2(node.X, node.Y);
            if (delta.LengthSquared() < 0.0021f)
                return false;
        }

        return true;
    }

    private bool TryCollectStar(
        EntityUid user,
        int starId,
        WH40KPsykerAstralProgressionComponent astralProgression,
        WH40KPsykerProgressionComponent levelProgression,
        out int xpReward)
    {
        xpReward = 0;

        if (!IsAstralActive(user))
            return false;

        var index = astralProgression.CollectibleStars.FindIndex(star => star.Id == starId);
        if (index < 0)
            return false;

        var star = astralProgression.CollectibleStars[index];
        astralProgression.CollectibleStars.RemoveAt(index);
        Dirty(user, astralProgression);

        xpReward = Math.Clamp(star.XpReward, CollectibleStarXpMin, CollectibleStarXpMax);
        _progression.GrantProgressionXp(user, xpReward, levelProgression);
        SyncProgression(user, astralProgression, levelProgression);

        if (astralProgression.NextCollectibleStarAt == TimeSpan.Zero)
            ScheduleNextCollectibleStar(astralProgression);

        return true;
    }

    private string ResolveRoundConstellationLayoutId()
    {
        if (!string.IsNullOrWhiteSpace(_activeLayoutId) &&
            _activeLayoutRoundId == _gameTicker.RoundId &&
            _prototype.HasIndex<WH40KPsykerAstralLayoutPrototype>(_activeLayoutId))
        {
            return _activeLayoutId;
        }

        var layouts = _prototype.EnumeratePrototypes<WH40KPsykerAstralLayoutPrototype>()
            .OrderBy(layout => layout.ID)
            .ToList();

        if (layouts.Count == 0)
        {
            _activeLayoutId = string.Empty;
            _activeLayoutRoundId = _gameTicker.RoundId;
            return _activeLayoutId;
        }

        _activeLayoutId = _random.Pick(layouts).ID;
        _activeLayoutRoundId = _gameTicker.RoundId;
        return _activeLayoutId;
    }

    private bool IsWarpSealed(EntityUid uid)
    {
        return TryComp<WH40KWarpInstabilityComponent>(uid, out var instability) &&
               instability.DecayPerSecond <= 0f &&
               instability.CurrentInstability + 0.001f >= instability.MaxInstability;
    }

    private void PopupCaution(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.SmallCaution);
    }

    private void PopupSuccess(EntityUid user, string key, params (string, object)[] args)
    {
        _popup.PopupEntity(_culture.GetPlayerString(user, key, args), user, user, PopupType.Small);
    }
}
