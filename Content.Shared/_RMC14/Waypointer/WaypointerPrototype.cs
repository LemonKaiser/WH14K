using Content.Shared._RMC14.Waypointer.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._RMC14.Waypointer;

[Prototype("rmcWaypointer")]
public sealed partial class WaypointerPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<WaypointerPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField, NeverPushInheritance]
    public bool Abstract { get; private set; }

    [DataField(required: true)]
    public required string Name;

    [DataField(required: true)]
    public ComponentRegistry TrackedComponents = default!;

    [DataField(required: true)]
    public ResPath RsiPath;

    [DataField]
    public float WaypointerStates = 1f;

    [DataField]
    public Color? Color;

    [DataField]
    public bool WorkOnGrid;

    [DataField]
    public bool WorkInCombat;

    [DataField]
    public int MaxRange = 200;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField(required: true)]
    public ResPath RadialMenuIconPath;
}
