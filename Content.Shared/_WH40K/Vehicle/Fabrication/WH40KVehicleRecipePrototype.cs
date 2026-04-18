using System.Collections.Generic;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Vehicle.Fabrication;

[Prototype("wh40kVehicleRecipe")]
public sealed partial class WH40KVehicleRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<CargoProductPrototype> Product = default!;

    [DataField(required: true)]
    public EntProtoId Spawn = default!;

    [DataField]
    public int BuildDurationSeconds = 45;

    [DataField]
    public Dictionary<string, int> Materials = new();

    [DataField]
    public Dictionary<string, int> Parts = new();
}
