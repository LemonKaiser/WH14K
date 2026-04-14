using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Actions.Components;

/// <summary>
/// Lets the player controlling this entity use actions.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedActionsSystem))]
public sealed partial class ActionsComponent : Component
{
    /// <summary>
    /// List of actions currently granted to this entity.
    /// On the client, this may contain a mixture of client-side and networked entities.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Actions = new();
}

/// <summary>
/// When present on a controlled entity, indicates that its HUD should also display actions of another source entity,
/// and clicks may optionally be proxied back to that source.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActionsDisplayRelayComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public EntityUid? Source;

    [DataField, AutoNetworkedField]
    public bool InteractAsSource = false;
}

[Serializable, NetSerializable]
public sealed class ActionsComponentState : ComponentState
{
    public readonly HashSet<NetEntity> Actions;

    public ActionsComponentState(HashSet<NetEntity> actions)
    {
        Actions = actions;
    }
}

/// <summary>
///     Determines how the action icon appears in the hotbar for item actions.
/// </summary>
public enum ItemActionIconStyle : byte
{
    /// <summary>
    /// The default - The item icon will be big with a small action icon in the corner
    /// </summary>
    BigItem,

    /// <summary>
    /// The action icon will be big with a small item icon in the corner
    /// </summary>
    BigAction,

    /// <summary>
    /// BigAction but no item icon will be shown in the corner.
    /// </summary>
    NoItem
}
