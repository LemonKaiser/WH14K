using System;

namespace Content.Shared._WH40K.Tiers;

public static class WH40KMaterialStorageUnits
{
    // SS14 material storage uses raw material volume, where one sheet/ore unit is usually 100.
    public const int MaterialUnitVolume = 100;

    public static int ToRawMaterialVolume(int units)
    {
        return Math.Max(1, units) * MaterialUnitVolume;
    }

    public static int? ToRawMaterialVolume(int? units)
    {
        return units is > 0
            ? units.Value * MaterialUnitVolume
            : null;
    }
}
