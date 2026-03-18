using Content.Shared._WH40K.Psyker;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Server-side acknowledgment for client-facing psyker UI action.
/// Keeps action execution contract valid (handled event/cooldown flow).
/// </summary>
public sealed class WH40KPsykerUiActionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleComponent, WH40KPsykerToggleProgressionUiActionEvent>(OnTogglePsykerUi);
    }

    private void OnTogglePsykerUi(
        Entity<WH40KPsykerRoleComponent> ent,
        ref WH40KPsykerToggleProgressionUiActionEvent args)
    {
        args.Handled = true;
    }
}
