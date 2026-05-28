using System;
using System.Linq;
using Content.Shared.Actions;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Imperium psyker starter action delivery.
/// </summary>
public sealed partial class WH40KPsykerStarterActionLoadoutSystem : EntitySystem
{
    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  WH40KGlobalWarpInstabilitySystem _globalWarp = default!;
    [Dependency] private  WH40KPsykerDisciplineModifierSystem _modifiers = default!;
    [Dependency] private  IPrototypeManager _prototypeManager = default!;
    private const string PsykerUiActionPrototype = "ActionWH40KPsykerAstralProjection";

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerStarterActionLoadoutComponent, ComponentStartup>(OnPsykerLoadoutStartup);
        SubscribeLocalEvent<WH40KPsykerRoleShutdownEvent>(OnPsykerRoleShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<
            WH40KPsykerRoleComponent,
            WH40KPsykerProgressionComponent,
            WH40KPsykerAstralProgressionComponent,
            WH40KPsykerStarterActionLoadoutComponent>();

        while (query.MoveNext(out var uid, out _, out var progression, out var astralProgression, out var loadout))
        {
            if (_globalWarp.CatastropheTriggered)
            {
                SealPsykerActions(uid, loadout, progression.Level);
                continue;
            }

            var astralSignature = BuildAstralSignature(astralProgression);
            if (!loadout.AppliedCatastropheLockdown &&
                loadout.AppliedLevel == progression.Level &&
                string.Equals(loadout.AppliedAstralSignature, astralSignature, StringComparison.Ordinal))
            {
                continue;
            }

            ComposePsykerActions(uid, loadout, progression.Level, astralProgression, astralSignature);
        }
    }

    private void OnPsykerLoadoutStartup(Entity<WH40KPsykerStarterActionLoadoutComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<WH40KPsykerRoleComponent>(ent.Owner))
            return;

        var progression = EnsureComp<WH40KPsykerProgressionComponent>(ent.Owner);
        var astralProgression = EnsureComp<WH40KPsykerAstralProgressionComponent>(ent.Owner);
        var astralSignature = BuildAstralSignature(astralProgression);

        if (_globalWarp.CatastropheTriggered)
        {
            SealPsykerActions(ent.Owner, ent.Comp, progression.Level);
            return;
        }

        ComposePsykerActions(ent.Owner, ent.Comp, progression.Level, astralProgression, astralSignature);
    }

    private void OnPsykerRoleShutdown(WH40KPsykerRoleShutdownEvent args)
    {
        if (!TryComp<WH40KPsykerStarterActionLoadoutComponent>(args.User, out var loadout))
            return;

        ClearGrantedActions(args.User, loadout.GrantedActions);
        loadout.AppliedLevel = 0;
        loadout.AppliedAstralSignature = string.Empty;
        loadout.AppliedCatastropheLockdown = false;
        _modifiers.ResetDisciplineState(args.User);
    }

    private void ComposePsykerActions(
        EntityUid uid,
        WH40KPsykerStarterActionLoadoutComponent loadout,
        int level,
        WH40KPsykerAstralProgressionComponent astralProgression,
        string astralSignature)
    {
        ClearGrantedActions(uid, loadout.GrantedActions);
        var actions = new List<string> { PsykerUiActionPrototype };
        actions.AddRange(loadout.StarterActions);
        AddUnlockedActions(actions, loadout.ScaledActions, level);
        AddAstralNodeActions(actions, astralProgression);
        GrantActions(uid, loadout.GrantedActions, actions);
        loadout.AppliedLevel = level;
        loadout.AppliedAstralSignature = astralSignature;
        loadout.AppliedCatastropheLockdown = false;
        _modifiers.RefreshDisciplineState(uid, loadout, astralProgression);
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
        loadout.AppliedAstralSignature = string.Empty;
        loadout.AppliedCatastropheLockdown = true;
        _modifiers.ResetDisciplineState(uid);
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

    private void AddAstralNodeActions(List<string> output, WH40KPsykerAstralProgressionComponent progression)
    {
        foreach (var nodeId in progression.UnlockedNodes)
        {
            if (string.IsNullOrWhiteSpace(nodeId) ||
                !_prototypeManager.TryIndex<WH40KPsykerDisciplineNodePrototype>(nodeId, out var node) ||
                string.IsNullOrWhiteSpace(node.PlannedAction))
            {
                continue;
            }

            output.Add(node.PlannedAction);
        }
    }

    private static string BuildAstralSignature(WH40KPsykerAstralProgressionComponent progression)
    {
        if (progression.UnlockedNodes.Count == 0)
            return string.Empty;

        return string.Join("|", progression.UnlockedNodes.OrderBy(id => id, StringComparer.Ordinal));
    }
}
