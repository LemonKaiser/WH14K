using Content.Shared.Actions;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Chaos gift action delivery isolated from the Imperium psyker path.
/// </summary>
public sealed class WH40KChaosStarterActionLoadoutSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    [Dependency] private readonly WH40KChaosCultSystem _cult = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftStarterActionLoadoutComponent, ComponentStartup>(OnChaosLoadoutStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<
            WH40KChaosGiftRoleComponent,
            WH40KChaosGiftProgressionComponent,
            WH40KChaosGiftStarterActionLoadoutComponent>();

        while (query.MoveNext(out var uid, out _, out var progression, out var loadout))
        {
            var isLeader = _cult.IsEffectiveLeader(uid, progression);
            var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
            var unlockMask = GetUnlockMask(progression);
            if (_globalWarp.CatastropheTriggered)
            {
                SealChaosActions(uid, loadout, progression, isLeader, unlockMask, passiveExUnlocked);
                continue;
            }

            if (loadout.AppliedPatron == progression.AttunedPatron &&
                loadout.AppliedLevel == progression.Level &&
                loadout.AppliedPrimaryGiftSlot == progression.PrimaryGiftSlot &&
                loadout.AppliedUnlockMask == unlockMask &&
                loadout.AppliedKhornePassiveEx == passiveExUnlocked &&
                loadout.AppliedLeaderState == isLeader &&
                !loadout.AppliedCatastropheLockdown)
            {
                continue;
            }

            ComposeChaosActions(uid, loadout, progression);
        }

        var cleanupQuery = EntityQueryEnumerator<WH40KChaosGiftStarterActionLoadoutComponent>();
        while (cleanupQuery.MoveNext(out var uid, out var loadout))
        {
            if (HasComp<WH40KChaosGiftRoleComponent>(uid) && HasComp<WH40KChaosGiftProgressionComponent>(uid))
                continue;

            if (loadout.GrantedActions.Count == 0 &&
                loadout.AppliedPatron == WH40KChaosPatron.None &&
                loadout.AppliedLevel == 0 &&
                loadout.AppliedPrimaryGiftSlot == 0 &&
                loadout.AppliedUnlockMask == 0 &&
                !loadout.AppliedKhornePassiveEx &&
                !loadout.AppliedLeaderState &&
                !loadout.AppliedCatastropheLockdown)
            {
                continue;
            }

            ClearGrantedActions(uid, loadout.GrantedActions);
            ResetAppliedState(loadout);
        }
    }

    private void OnChaosLoadoutStartup(Entity<WH40KChaosGiftStarterActionLoadoutComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WH40KChaosGiftRoleComponent>(ent.Owner))
            return;

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(ent.Owner);

        if (_globalWarp.CatastropheTriggered)
        {
            var isLeader = _cult.IsEffectiveLeader(ent.Owner, progression);
            var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
            SealChaosActions(ent.Owner, ent.Comp, progression, isLeader, GetUnlockMask(progression), passiveExUnlocked);
            return;
        }

        ComposeChaosActions(ent.Owner, ent.Comp, progression);
    }

    private void ComposeChaosActions(
        EntityUid uid,
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosGiftProgressionComponent progression)
    {
        ClearGrantedActions(uid, loadout.GrantedActions);

        var actions = new List<string>();
        var patron = progression.AttunedPatron;
        var isLeader = _cult.IsEffectiveLeader(uid, progression);
        if (patron is (WH40KChaosPatron.Khorne or
            WH40KChaosPatron.Nurgle or
            WH40KChaosPatron.Slaanesh or
            WH40KChaosPatron.Tzeentch))
        {
            for (var slot = 1; slot <= 3; slot++)
            {
                if (!WH40KChaosLeaderRuntimeRules.ShouldGrantGiftSlot(progression, slot, isLeader))
                    continue;

                if (!TryGetBranchAction(loadout, patron, slot, out var action))
                    continue;

                actions.Add(action);
            }

            if (isLeader)
            {
                actions.AddRange(GetBonusActions(loadout, patron));
                actions.AddRange(loadout.LeaderActions);

                if (patron == WH40KChaosPatron.Khorne && WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression))
                    actions.AddRange(loadout.KhornePassiveExActions);
            }
        }

        GrantActions(uid, loadout.GrantedActions, actions);
        loadout.AppliedPatron = patron;
        loadout.AppliedLevel = progression.Level;
        loadout.AppliedPrimaryGiftSlot = progression.PrimaryGiftSlot;
        loadout.AppliedUnlockMask = GetUnlockMask(progression);
        loadout.AppliedKhornePassiveEx = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
        loadout.AppliedLeaderState = isLeader;
        loadout.AppliedCatastropheLockdown = false;
    }

    private void SealChaosActions(
        EntityUid uid,
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosGiftProgressionComponent progression,
        bool isLeader,
        int unlockMask,
        bool passiveExUnlocked)
    {
        if (loadout.AppliedCatastropheLockdown && loadout.GrantedActions.Count == 0)
            return;

        ClearGrantedActions(uid, loadout.GrantedActions);
        loadout.AppliedPatron = progression.AttunedPatron;
        loadout.AppliedLevel = progression.Level;
        loadout.AppliedPrimaryGiftSlot = progression.PrimaryGiftSlot;
        loadout.AppliedUnlockMask = unlockMask;
        loadout.AppliedKhornePassiveEx = passiveExUnlocked;
        loadout.AppliedLeaderState = isLeader;
        loadout.AppliedCatastropheLockdown = true;
    }

    private static List<string> GetBonusActions(
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => loadout.KhorneBonusActions,
            WH40KChaosPatron.Nurgle => loadout.NurgleBonusActions,
            WH40KChaosPatron.Slaanesh => loadout.SlaaneshBonusActions,
            WH40KChaosPatron.Tzeentch => loadout.TzeentchBonusActions,
            _ => [],
        };
    }

    private static bool TryGetBranchAction(
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosPatron patron,
        int slot,
        out string action)
    {
        action = string.Empty;
        if (slot < 1 || slot > 3)
            return false;

        List<string>? source = patron switch
        {
            WH40KChaosPatron.Khorne => loadout.KhorneBranchActions,
            WH40KChaosPatron.Nurgle => loadout.NurgleBranchActions,
            WH40KChaosPatron.Slaanesh => loadout.SlaaneshBranchActions,
            WH40KChaosPatron.Tzeentch => loadout.TzeentchBranchActions,
            _ => null,
        };

        if (source == null || source.Count < slot)
            return false;

        action = source[slot - 1];
        return !string.IsNullOrWhiteSpace(action);
    }

    private static int GetUnlockMask(WH40KChaosGiftProgressionComponent progression)
    {
        var mask = 0;
        if (progression.GiftSlotOneUnlocked)
            mask |= 1 << 0;
        if (progression.GiftSlotTwoUnlocked)
            mask |= 1 << 1;
        if (progression.GiftSlotThreeUnlocked)
            mask |= 1 << 2;
        return mask;
    }

    private static void ResetAppliedState(WH40KChaosGiftStarterActionLoadoutComponent loadout)
    {
        loadout.AppliedPatron = WH40KChaosPatron.None;
        loadout.AppliedLevel = 0;
        loadout.AppliedPrimaryGiftSlot = 0;
        loadout.AppliedUnlockMask = 0;
        loadout.AppliedKhornePassiveEx = false;
        loadout.AppliedLeaderState = false;
        loadout.AppliedCatastropheLockdown = false;
    }

    private void GrantActions(EntityUid user, List<EntityUid> granted, List<string> prototypes)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prototype in prototypes)
        {
            if (string.IsNullOrWhiteSpace(prototype) || !unique.Add(prototype))
                continue;

            EntityUid? action = null;
            if (!_actions.AddAction(user, ref action, prototype, user) || action == null)
                continue;

            granted.Add(action.Value);
        }
    }

    private void ClearGrantedActions(EntityUid user, List<EntityUid> granted)
    {
        foreach (var action in granted)
        {
            _actions.RemoveAction(user, action);
        }

        granted.Clear();
    }
}
