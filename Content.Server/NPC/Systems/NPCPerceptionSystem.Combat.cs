using System.Numerics;
using System.Linq;
using Content.Server.Interaction;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Shared._WH40K.Combat;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Turrets;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCPerceptionSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [Dependency] private EntityQuery<NPCCombatMemoryComponent> _combatMemoryQuery = default!;
    [Dependency] private EntityQuery<NPCCombatPerceptionComponent> _combatPerceptionQuery = default!;
    [Dependency] private EntityQuery<NPCGroupComponent> _combatGroupQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<DeployableTurretComponent> _deployableTurretQuery = default!;
    [Dependency] private EntityQuery<DestructibleComponent> _destructibleQuery = default!;
    [Dependency] private EntityQuery<WH40KTurretProfileComponent> _turretProfileQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    private void UpdateCombat(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<NPCCombatPerceptionComponent, NPCCombatMemoryComponent, ActiveNPCComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var perception, out var memory, out _, out var xform))
        {
            CleanupCombatMemory(uid, perception, memory, now);

            if (now >= perception.NextVisionCheck)
            {
                perception.NextVisionCheck = now + TimeSpan.FromSeconds(Math.Max(0.05f, perception.VisionCheckInterval));
                UpdateVisibleCombatContacts(uid, perception, memory, xform, now);
            }

            if (now >= perception.NextAssignment)
            {
                perception.NextAssignment = now + TimeSpan.FromSeconds(Math.Max(0.1f, perception.AssignmentInterval));
                UpdateCombatAssignment(uid, perception, memory, xform, now);
            }

            if (now >= perception.NextShare)
            {
                perception.NextShare = now + TimeSpan.FromSeconds(Math.Max(0.1f, perception.ShareContactInterval));
                ShareCombatContacts(uid, perception, memory, xform, now);
            }
        }
    }

    private void UpdateVisibleCombatContacts(
        EntityUid uid,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TransformComponent xform,
        TimeSpan now)
    {
        var radius = HasActiveCombatContact(perception, memory, now)
            ? perception.AggroVisionRadius
            : perception.VisionRadius;

        foreach (var target in _npcFaction.GetNearbyHostiles(uid, radius))
        {
            if (target == uid ||
                !IsCombatTargetActive(target, perception) ||
                !_xformQuery.TryGetComponent(target, out var targetXform))
            {
                continue;
            }

            if (!CanSeeTarget(uid, target, perception, xform, targetXform, radius))
                continue;

            RememberVisibleContact(target, targetXform.Coordinates, perception, memory, now);
        }
    }

    private bool CanSeeTarget(
        EntityUid uid,
        EntityUid target,
        NPCCombatPerceptionComponent perception,
        TransformComponent xform,
        TransformComponent targetXform,
        float radius)
    {
        if (xform.MapID != targetXform.MapID)
            return false;

        if (!xform.Coordinates.TryDistance(EntityManager, _transform, targetXform.Coordinates, out var distance) ||
            distance > radius)
        {
            return false;
        }

        var collisionGroup = perception.UseOpaqueForLOSChecks
            ? CollisionGroup.Opaque
            : CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

        return _interaction.InRangeUnobstructed(uid, target, radius + 0.1f, collisionGroup);
    }

    private void RememberVisibleContact(
        EntityUid target,
        EntityCoordinates coordinates,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TimeSpan now)
    {
        var contact = GetOrCreateContact(memory, target);
        contact.LastKnownCoordinates = coordinates;
        contact.LastSeen = now;
        contact.LastUpdated = now;
        contact.VisibleUntil = now + TimeSpan.FromSeconds(Math.Max(0.05f, perception.VisibleGrace));
        contact.InitialConfidence = 1f;
        contact.PersonallySeen = true;
        contact.Reported = false;
        contact.ReportedBy = EntityUid.Invalid;
        contact.State = NPCCombatContactState.Visible;
    }

    private void ShareCombatContacts(
        EntityUid uid,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TransformComponent xform,
        TimeSpan now)
    {
        if (!_combatGroupQuery.HasComponent(uid) && perception.RequireSameGroupForReports)
            return;

        var origin = _transform.GetMapCoordinates(uid, xform: xform);

        foreach (var recipient in _lookup.GetEntitiesInRange<NPCCombatMemoryComponent>(origin, perception.ShareContactRadius))
        {
            var other = recipient.Owner;
            if (other == uid ||
                !_combatPerceptionQuery.TryGetComponent(other, out var otherPerception) ||
                !CanShareCombatKnowledge(uid, other, perception))
            {
                continue;
            }

            foreach (var contact in memory.Contacts.Values)
            {
                if (!contact.PersonallySeen ||
                    !IsContactVisible(contact, now))
                {
                    continue;
                }

                var confidence = GetContactConfidence(contact, perception, now) * otherPerception.ReportConfidenceMultiplier;
                if (confidence < otherPerception.MinimumContactConfidence)
                    continue;

                RememberReportedContact(other, uid, contact, otherPerception, recipient.Comp, confidence, now);
            }
        }
    }

    private bool CanShareCombatKnowledge(
        EntityUid sender,
        EntityUid recipient,
        NPCCombatPerceptionComponent perception)
    {
        if (perception.RequireSameGroupForReports &&
            !IsSameCombatGroup(sender, recipient))
        {
            return false;
        }

        if (!_npcFaction.IsEntityFriendly(sender, recipient))
            return false;

        if (!perception.ShareRequiresLineOfSight)
            return true;

        return _interaction.InRangeUnobstructed(sender, recipient, perception.ShareContactRadius + 0.1f, CollisionGroup.Opaque);
    }

    private bool IsSameCombatGroup(EntityUid first, EntityUid second)
    {
        return _combatGroupQuery.TryGetComponent(first, out var firstGroup) &&
               _combatGroupQuery.TryGetComponent(second, out var secondGroup) &&
               firstGroup.CollectiveMind &&
               secondGroup.CollectiveMind &&
               firstGroup.GroupId == secondGroup.GroupId;
    }

    private void RememberReportedContact(
        EntityUid recipient,
        EntityUid sender,
        NPCCombatContact report,
        NPCCombatPerceptionComponent recipientPerception,
        NPCCombatMemoryComponent recipientMemory,
        float confidence,
        TimeSpan now)
    {
        if (report.Target == recipient ||
            !IsCombatTargetActive(report.Target, recipientPerception))
        {
            return;
        }

        var contact = GetOrCreateContact(recipientMemory, report.Target);
        var existingConfidence = GetContactConfidence(contact, recipientPerception, now);

        if (contact.PersonallySeen && existingConfidence >= confidence)
            return;

        if (contact.LastUpdated > report.LastUpdated && existingConfidence >= confidence)
            return;

        contact.LastKnownCoordinates = report.LastKnownCoordinates;
        contact.LastSeen = report.LastSeen;
        contact.LastUpdated = now;
        contact.VisibleUntil = TimeSpan.Zero;
        contact.InitialConfidence = Math.Clamp(confidence, 0f, 1f);
        contact.PersonallySeen = false;
        contact.Reported = true;
        contact.ReportedBy = sender;
        contact.State = NPCCombatContactState.Reported;
    }

    private void UpdateCombatAssignment(
        EntityUid uid,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TransformComponent xform,
        TimeSpan now)
    {
        memory.AssignedTarget = EntityUid.Invalid;
        memory.AssignedBy = EntityUid.Invalid;

        var bestTarget = EntityUid.Invalid;
        var bestScore = 0f;
        var origin = _transform.GetMapCoordinates(uid, xform: xform);

        foreach (var contact in memory.Contacts.Values)
        {
            if (!IsUsableCombatContact(contact, perception, now))
                continue;

            if (!_xformQuery.TryGetComponent(contact.Target, out var targetXform) ||
                !targetXform.Coordinates.TryDistance(EntityManager, _transform, xform.Coordinates, out var distance))
            {
                continue;
            }

            var confidence = GetContactConfidence(contact, perception, now);
            var visibleBonus = IsContactVisible(contact, now) ? 1.5f : 1f;
            var personalBonus = contact.PersonallySeen ? 1.15f : 1f;
            var distanceScore = 1f / MathF.Max(1f, distance);
            var loadPenalty = GetTargetAssignmentPenalty(uid, contact.Target, perception, origin);
            var score = confidence * visibleBonus * personalBonus * distanceScore * loadPenalty;

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestTarget = contact.Target;
        }

        memory.AssignedTarget = bestTarget;
        memory.AssignedBy = bestTarget.IsValid() ? uid : EntityUid.Invalid;
    }

    private float GetTargetAssignmentPenalty(
        EntityUid uid,
        EntityUid target,
        NPCCombatPerceptionComponent perception,
        MapCoordinates origin)
    {
        var slots = HasComp<GunComponent>(uid)
            ? Math.Max(1, perception.RangedSlotsPerTarget)
            : Math.Max(1, perception.MeleeSlotsPerTarget);

        var assigned = 0;

        foreach (var otherMemory in _lookup.GetEntitiesInRange<NPCCombatMemoryComponent>(origin, perception.ShareContactRadius))
        {
            var other = otherMemory.Owner;
            if (other == uid ||
                otherMemory.Comp.AssignedTarget != target ||
                !IsSameCombatGroup(uid, other))
            {
                continue;
            }

            assigned++;
        }

        return slots / (float) (slots + assigned);
    }

    private bool HasActiveCombatContact(
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TimeSpan now)
    {
        foreach (var contact in memory.Contacts.Values)
        {
            if (IsUsableCombatContact(contact, perception, now))
                return true;
        }

        return false;
    }

    private void CleanupCombatMemory(
        EntityUid uid,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TimeSpan now)
    {
        foreach (var (target, contact) in memory.Contacts.ToArray())
        {
            if (TerminatingOrDeleted(target) ||
                !IsCombatTargetActive(target, perception))
            {
                memory.Contacts.Remove(target);
                continue;
            }

            if (IsContactVisible(contact, now))
            {
                contact.State = NPCCombatContactState.Visible;
                continue;
            }

            var age = (float) (now - contact.LastUpdated).TotalSeconds;
            var seenAge = (float) (now - contact.LastSeen).TotalSeconds;
            var confidence = GetContactConfidence(contact, perception, now);

            if (confidence < perception.MinimumContactConfidence ||
                age > perception.MemoryDuration + perception.SearchDuration)
            {
                contact.State = NPCCombatContactState.Lost;
                memory.Contacts.Remove(target);
                continue;
            }

            contact.State = seenAge <= perception.MemoryDuration
                ? contact.Reported ? NPCCombatContactState.Reported : NPCCombatContactState.Investigating
                : NPCCombatContactState.Searching;
        }

        if (!memory.AssignedTarget.IsValid() ||
            memory.Contacts.ContainsKey(memory.AssignedTarget))
        {
            return;
        }

        memory.AssignedTarget = EntityUid.Invalid;
        memory.AssignedBy = EntityUid.Invalid;
    }

    public bool TryGetCombatTarget(
        EntityUid uid,
        bool requireVisible,
        out EntityUid target,
        out EntityCoordinates targetCoordinates)
    {
        target = EntityUid.Invalid;
        targetCoordinates = default;

        if (!_combatMemoryQuery.TryGetComponent(uid, out var memory) ||
            !_combatPerceptionQuery.TryGetComponent(uid, out var perception))
        {
            return false;
        }

        var now = _timing.CurTime;

        if (TryUseCombatTarget(memory.AssignedTarget, requireVisible, perception, memory, now, out target, out targetCoordinates))
            return true;

        foreach (var contact in memory.Contacts.Values
                     .OrderByDescending(contact => GetCombatTargetScore(uid, contact, perception, now)))
        {
            if (TryUseCombatTarget(contact.Target, requireVisible, perception, memory, now, out target, out targetCoordinates))
                return true;
        }

        return false;
    }

    private bool TryUseCombatTarget(
        EntityUid candidate,
        bool requireVisible,
        NPCCombatPerceptionComponent perception,
        NPCCombatMemoryComponent memory,
        TimeSpan now,
        out EntityUid target,
        out EntityCoordinates targetCoordinates)
    {
        target = EntityUid.Invalid;
        targetCoordinates = default;

        if (!candidate.IsValid() ||
            !memory.Contacts.TryGetValue(candidate, out var contact) ||
            !IsUsableCombatContact(contact, perception, now))
        {
            return false;
        }

        if (requireVisible && !IsContactVisible(contact, now))
            return false;

        target = candidate;
        targetCoordinates = requireVisible
            ? new EntityCoordinates(candidate, Vector2.Zero)
            : contact.LastKnownCoordinates;
        return true;
    }

    public bool TryGetInvestigationPoint(
        EntityUid uid,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        target = EntityUid.Invalid;
        coordinates = default;

        if (!_combatMemoryQuery.TryGetComponent(uid, out var memory) ||
            !_combatPerceptionQuery.TryGetComponent(uid, out var perception))
        {
            return false;
        }

        var now = _timing.CurTime;
        NPCCombatContact? best = null;
        var bestScore = 0f;

        foreach (var contact in memory.Contacts.Values)
        {
            if (!IsUsableCombatContact(contact, perception, now) ||
                IsContactVisible(contact, now))
            {
                continue;
            }

            var score = GetContactConfidence(contact, perception, now);
            if (score <= bestScore)
                continue;

            best = contact;
            bestScore = score;
        }

        if (best == null)
            return false;

        target = best.Target;
        coordinates = best.LastKnownCoordinates;
        return true;
    }

    public bool IsTargetVisibleTo(EntityUid uid, EntityUid target, NPCCombatMemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false) ||
            !memory.Contacts.TryGetValue(target, out var contact))
        {
            return false;
        }

        return IsContactVisible(contact, _timing.CurTime);
    }

    private float GetCombatTargetScore(
        EntityUid uid,
        NPCCombatContact contact,
        NPCCombatPerceptionComponent perception,
        TimeSpan now)
    {
        if (!IsUsableCombatContact(contact, perception, now))
            return 0f;

        return GetContactConfidence(contact, perception, now) * (IsContactVisible(contact, now) ? 2f : 1f);
    }

    private bool IsUsableCombatContact(
        NPCCombatContact contact,
        NPCCombatPerceptionComponent perception,
        TimeSpan now)
    {
        return contact.Target.IsValid() &&
               IsCombatTargetActive(contact.Target, perception) &&
               GetContactConfidence(contact, perception, now) >= perception.MinimumContactConfidence;
    }

    private bool IsCombatTargetActive(EntityUid target, NPCCombatPerceptionComponent perception)
    {
        if (TerminatingOrDeleted(target))
            return false;

        if (_mobState.IsAlive(target))
            return true;

        if (!perception.RecognizeStaticThreats ||
            !IsStaticCombatThreat(target) ||
            !_damageableQuery.HasComponent(target))
        {
            return false;
        }

        return !_destructibleQuery.TryGetComponent(target, out var destructible) ||
               !destructible.IsBroken;
    }

    private bool IsStaticCombatThreat(EntityUid target)
    {
        return _turretProfileQuery.HasComponent(target) ||
               _deployableTurretQuery.HasComponent(target);
    }

    private float GetContactConfidence(
        NPCCombatContact contact,
        NPCCombatPerceptionComponent perception,
        TimeSpan now)
    {
        if (IsContactVisible(contact, now))
            return 1f;

        var age = Math.Max(0f, (float) (now - contact.LastUpdated).TotalSeconds);
        var lifetime = Math.Max(0.1f, perception.MemoryDuration + perception.SearchDuration);
        return Math.Clamp(contact.InitialConfidence * (1f - age / lifetime), 0f, 1f);
    }

    private bool IsContactVisible(NPCCombatContact contact, TimeSpan now)
    {
        return contact.VisibleUntil > now;
    }

    private NPCCombatContact GetOrCreateContact(NPCCombatMemoryComponent memory, EntityUid target)
    {
        if (memory.Contacts.TryGetValue(target, out var contact))
            return contact;

        contact = new NPCCombatContact
        {
            Target = target,
        };
        memory.Contacts[target] = contact;
        return contact;
    }
}
