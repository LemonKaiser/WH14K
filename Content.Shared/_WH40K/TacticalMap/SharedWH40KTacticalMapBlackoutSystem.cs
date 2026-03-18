using System.Diagnostics.Contracts;
using Robust.Shared.Analyzers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.TacticalMap;

/// <summary>
/// Handles mapper-authored blackout tiles for tactical-map rendering.
/// </summary>
[Virtual]
public abstract class SharedWH40KTacticalMapBlackoutSystem : EntitySystem
{
    /// <summary>
    /// Returns whether the specified tile is blacked out on the tactical map.
    /// </summary>
    [Pure]
    public bool IsBlackedOut(Entity<MapGridComponent, WH40KTacticalMapBlackoutComponent> grid, Vector2i index)
    {
        var blackout = grid.Comp2;
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, WH40KTacticalMapBlackoutComponent.ChunkSize);

        if (!blackout.Data.TryGetValue(chunkOrigin, out var bitMask))
            return false;

        var chunkRelative = SharedMapSystem.GetChunkRelative(index, WH40KTacticalMapBlackoutComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * WH40KTacticalMapBlackoutComponent.ChunkSize);
        return (bitMask & bitFlag) == bitFlag;
    }

    public void SetBlackout(
        Entity<MapGridComponent?, WH40KTacticalMapBlackoutComponent?> grid,
        Vector2i index,
        bool value)
    {
        if (!Resolve(grid, ref grid.Comp1))
            return;

        grid.Comp2 ??= EnsureComp<WH40KTacticalMapBlackoutComponent>(grid.Owner);

        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, WH40KTacticalMapBlackoutComponent.ChunkSize);
        var blackout = grid.Comp2;

        if (!blackout.Data.TryGetValue(chunkOrigin, out var chunkData))
        {
            if (!value)
                return;

            chunkData = 0;
        }

        var chunkRelative = SharedMapSystem.GetChunkRelative(index, WH40KTacticalMapBlackoutComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * WH40KTacticalMapBlackoutComponent.ChunkSize);

        if (value)
        {
            if ((chunkData & bitFlag) == bitFlag)
                return;

            chunkData |= bitFlag;
        }
        else
        {
            if ((chunkData & bitFlag) == 0x0)
                return;

            chunkData &= ~bitFlag;
        }

        if (chunkData == 0)
            blackout.Data.Remove(chunkOrigin);
        else
            blackout.Data[chunkOrigin] = chunkData;

        Dirty(grid.Owner, blackout);
    }
}
