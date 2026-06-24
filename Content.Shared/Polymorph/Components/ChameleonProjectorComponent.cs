using Content.Shared.Polymorph.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared.Polymorph.Components;

/// <summary>
/// A chameleon projector polymorphs you into a clicked entity, then polymorphs back when clicked on or destroyed.
/// This creates a new dummy polymorph entity and copies the appearance over.
/// </summary>
[RegisterComponent, Access(typeof(SharedChameleonProjectorSystem))]
public sealed partial class ChameleonProjectorComponent : Component
{
    /// <summary>
    /// If non-null, whitelist for valid entities to disguise as.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// If non-null, blacklist that prevents entities from being used even if they are in the whitelist.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Disguise entity to spawn and use.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId DisguiseProto = string.Empty;

    /// <summary>
    /// Action for disabling your disguise's rotation.
    /// </summary>
    [DataField]
    public EntProtoId NoRotAction = "ActionDisguiseNoRot";
    [DataField]
    public EntityUid? NoRotActionEntity;

    /// <summary>
    /// Whether disguising should grant the rotation toggle action.
    /// </summary>
    [DataField]
    public bool ProvideNoRotAction = true;

    /// <summary>
    /// Action for anchoring your disguise in place.
    /// </summary>
    [DataField]
    public EntProtoId AnchorAction = "ActionDisguiseAnchor";
    [DataField]
    public EntityUid? AnchorActionEntity;

    /// <summary>
    /// Whether hand interaction with the disguise should reveal the user.
    /// </summary>
    [DataField]
    public bool RevealOnInteract = true;

    /// <summary>
    /// Whether trying to pick the disguise up should reveal the user.
    /// </summary>
    [DataField]
    public bool RevealOnPickup = true;

    /// <summary>
    /// Whether attempting to insert the disguise into storage should reveal the user.
    /// </summary>
    [DataField]
    public bool RevealOnStorageInsert = true;

    /// <summary>
    /// Whether shutting down the projector should reveal the disguised user.
    /// </summary>
    [DataField]
    public bool RevealOnProjectorShutdown = true;

    /// <summary>
    /// Minimum health to give the disguise.
    /// </summary>
    [DataField]
    public float MinHealth = 1f;

    /// <summary>
    /// Maximum health to give the disguise, health scales with mass.
    /// </summary>
    [DataField]
    public float MaxHealth = 100f;

    /// <summary>
    /// User currently disguised by this projector, if any
    /// </summary>
    [DataField]
    public EntityUid? Disguised;
}
