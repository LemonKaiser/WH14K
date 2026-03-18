using System;
using Robust.Shared.Serialization;

namespace Content.Shared.Movement.Components;

[Serializable, NetSerializable]
public enum ContactModifierAggregationMode : byte
{
    Average = 0,
    Strongest = 1,
    Multiply = 2,
    WeightedAverage = 3,
}
