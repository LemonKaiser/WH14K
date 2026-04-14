using Robust.Shared.GameStates;

namespace Content.Shared.Mech.Components;

/// <summary>
/// Forces a mech to use its walking movement path even if the pilot holds the sprint modifier.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MechWalkOnlyComponent : Component;
