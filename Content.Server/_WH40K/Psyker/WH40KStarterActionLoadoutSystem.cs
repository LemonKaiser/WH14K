using Content.Shared.Actions;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Delivers starter ability packs for P4:
/// - Imperium psyker discipline starter actions;
/// - Chaos gifts patron branch actions gated by R5 unlock economy.
/// </summary>
public sealed class WH40KStarterActionLoadoutSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    private const string PsykerUiActionPrototype = "ActionWH40KPsykerToggleProgressionUi";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerStarterActionLoadoutComponent, ComponentStartup>(OnPsykerLoadoutStartup);
        SubscribeLocalEvent<WH40KPsykerRoleComponent, ComponentShutdown>(OnPsykerRoleShutdown);

        SubscribeLocalEvent<WH40KChaosGiftStarterActionLoadoutComponent, ComponentStartup>(OnChaosLoadoutStartup);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ComponentShutdown>(OnChaosRoleShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var psykerQuery = EntityQueryEnumerator<
            WH40KPsykerRoleComponent,
            WH40KPsykerProgressionComponent,
            WH40KPsykerStarterActionLoadoutComponent>();

        while (psykerQuery.MoveNext(out var uid, out _, out var progression, out var loadout))
        {
            if (loadout.AppliedLevel == progression.Level)
                continue;

            ComposePsykerActions(uid, loadout, progression.Level);
        }

        var query = EntityQueryEnumerator<
            WH40KChaosGiftRoleComponent,
            WH40KChaosGiftProgressionComponent,
            WH40KChaosGiftStarterActionLoadoutComponent>();

        while (query.MoveNext(out var uid, out _, out var progression, out var loadout))
        {
            var unlockMask = GetUnlockMask(progression);
            if (loadout.AppliedPatron == progression.AttunedPatron &&
                loadout.AppliedLevel == progression.Level &&
                loadout.AppliedPrimaryGiftSlot == progression.PrimaryGiftSlot &&
                loadout.AppliedUnlockMask == unlockMask &&
                loadout.AppliedKhornePassiveEx == progression.KhornePassiveExUnlocked)
            {
                continue;
            }

            ComposeChaosActions(uid, loadout, progression);
        }
    }

    private void OnPsykerLoadoutStartup(Entity<WH40KPsykerStarterActionLoadoutComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner))
            return;

        var progression = EnsureComp<WH40KPsykerProgressionComponent>(ent.Owner);
        ComposePsykerActions(ent.Owner, ent.Comp, progression.Level);
    }

    private void OnPsykerRoleShutdown(Entity<WH40KPsykerRoleComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<WH40KPsykerStarterActionLoadoutComponent>(ent, out var loadout))
            return;

        ClearGrantedActions(ent, loadout.GrantedActions);
        loadout.AppliedLevel = 0;
    }

    private void OnChaosLoadoutStartup(Entity<WH40KChaosGiftStarterActionLoadoutComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WH40KChaosGiftRoleComponent>(ent.Owner))
            return;

        var progression = EnsureComp<WH40KChaosGiftProgressionComponent>(ent.Owner);
        ComposeChaosActions(ent.Owner, ent.Comp, progression);
    }

    private void OnChaosRoleShutdown(Entity<WH40KChaosGiftRoleComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<WH40KChaosGiftStarterActionLoadoutComponent>(ent, out var loadout))
            return;

        ClearGrantedActions(ent, loadout.GrantedActions);
        loadout.AppliedPatron = WH40KChaosPatron.None;
        loadout.AppliedLevel = 0;
        loadout.AppliedPrimaryGiftSlot = 0;
        loadout.AppliedUnlockMask = 0;
        loadout.AppliedKhornePassiveEx = false;
    }

    private void ComposePsykerActions(
        EntityUid uid,
        WH40KPsykerStarterActionLoadoutComponent loadout,
        int level)
    {
        ClearGrantedActions(uid, loadout.GrantedActions);
        var actions = new List<string> { PsykerUiActionPrototype };
        actions.AddRange(loadout.StarterActions);
        AddUnlockedActions(actions, loadout.ScaledActions, level);
        GrantActions(uid, loadout.GrantedActions, actions);
        loadout.AppliedLevel = level;
    }

    private void ComposeChaosActions(
        EntityUid uid,
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosGiftProgressionComponent progression)
    {
        ClearGrantedActions(uid, loadout.GrantedActions);

        var actions = new List<string>();
        var patron = progression.AttunedPatron;
        if (patron is WH40KChaosPatron.Khorne or
            WH40KChaosPatron.Nurgle or
            WH40KChaosPatron.Slaanesh or
            WH40KChaosPatron.Tzeentch)
        {
            for (var slot = 1; slot <= 3; slot++)
            {
                if (!IsGiftSlotUnlocked(progression, slot))
                    continue;

                if (!TryGetBranchAction(loadout, patron, slot, out var action))
                    continue;

                actions.Add(action);
            }

            if (patron == WH40KChaosPatron.Khorne && progression.KhornePassiveExUnlocked)
                actions.AddRange(loadout.KhornePassiveExActions);
        }

        GrantActions(uid, loadout.GrantedActions, actions);
        loadout.AppliedPatron = patron;
        loadout.AppliedLevel = progression.Level;
        loadout.AppliedPrimaryGiftSlot = progression.PrimaryGiftSlot;
        loadout.AppliedUnlockMask = GetUnlockMask(progression);
        loadout.AppliedKhornePassiveEx = progression.KhornePassiveExUnlocked;
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

    private static bool IsGiftSlotUnlocked(WH40KChaosGiftProgressionComponent progression, int slot)
    {
        return slot switch
        {
            1 => progression.GiftSlotOneUnlocked,
            2 => progression.GiftSlotTwoUnlocked,
            3 => progression.GiftSlotThreeUnlocked,
            _ => false,
        };
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

    private static void AddUnlockedActions(List<string> output, List<WH40KLevelLockedAction> entries, int level)
    {
        if (entries.Count == 0 || level <= 0)
            return;

        foreach (var entry in entries)
        {
            if (entry.RequiredLevel > level || string.IsNullOrWhiteSpace(entry.ActionPrototype))
                continue;

            output.Add(entry.ActionPrototype);
        }
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
