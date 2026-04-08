using Content.Shared.Actions;
using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Imperium psyker starter action delivery.
/// </summary>
public sealed class WH40KPsykerStarterActionLoadoutSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    private const string PsykerUiActionPrototype = "ActionWH40KPsykerToggleProgressionUi";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerStarterActionLoadoutComponent, ComponentStartup>(OnPsykerLoadoutStartup);
        SubscribeLocalEvent<WH40KPsykerRoleComponent, ComponentShutdown>(OnPsykerRoleShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<
            WH40KPsykerRoleComponent,
            WH40KPsykerProgressionComponent,
            WH40KPsykerStarterActionLoadoutComponent>();

        while (query.MoveNext(out var uid, out _, out var progression, out var loadout))
        {
            if (_globalWarp.CatastropheTriggered)
            {
                SealPsykerActions(uid, loadout, progression.Level);
                continue;
            }

            if (!loadout.AppliedCatastropheLockdown && loadout.AppliedLevel == progression.Level)
                continue;

            ComposePsykerActions(uid, loadout, progression.Level);
        }
    }

    private void OnPsykerLoadoutStartup(Entity<WH40KPsykerStarterActionLoadoutComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner))
            return;

        var progression = EnsureComp<WH40KPsykerProgressionComponent>(ent.Owner);

        if (_globalWarp.CatastropheTriggered)
        {
            SealPsykerActions(ent.Owner, ent.Comp, progression.Level);
            return;
        }

        ComposePsykerActions(ent.Owner, ent.Comp, progression.Level);
    }

    private void OnPsykerRoleShutdown(Entity<WH40KPsykerRoleComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<WH40KPsykerStarterActionLoadoutComponent>(ent, out var loadout))
            return;

        ClearGrantedActions(ent, loadout.GrantedActions);
        loadout.AppliedLevel = 0;
        loadout.AppliedCatastropheLockdown = false;
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
        loadout.AppliedCatastropheLockdown = false;
    }

    private void SealPsykerActions(
        EntityUid uid,
        WH40KPsykerStarterActionLoadoutComponent loadout,
        int level)
    {
        if (loadout.AppliedCatastropheLockdown && loadout.GrantedActions.Count == 0)
            return;

        ClearGrantedActions(uid, loadout.GrantedActions);
        loadout.AppliedLevel = level;
        loadout.AppliedCatastropheLockdown = true;
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
