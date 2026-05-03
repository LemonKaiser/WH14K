#nullable enable
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.TacticalMap;

public static class WH40KTacticalMapSnapshotCatalog
{
    public static readonly ResPath BattlefieldSnapshotTexturePath = new("/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png");
    public static readonly ResPath WinterAssaultSnapshotTexturePath = new("/Textures/_WH40K/Interface/TacticalMap/winterassault_snapshot.png");
    public static readonly ResPath TinyBattleSnapshotTexturePath = new("/Textures/_WH40K/Interface/TacticalMap/tinybattle_snapshot.png");

    public static ResPath ResolveSnapshotTexture(string? mapId, ResPath? mapPath, ResPath fallback)
    {
        return TryResolveSnapshotTexture(mapId, mapPath, out var resolved)
            ? resolved
            : fallback;
    }

    public static bool TryResolveSnapshotTexture(string? mapId, ResPath? mapPath, out ResPath resolved)
    {
        switch (mapId)
        {
            case "Battlefield40k":
                resolved = BattlefieldSnapshotTexturePath;
                return true;
            case "WinterAssault":
                resolved = WinterAssaultSnapshotTexturePath;
                return true;
            case "TinyBattle":
                resolved = TinyBattleSnapshotTexturePath;
                return true;
        }

        if (mapPath != null)
        {
            switch (mapPath.Value.ToString().ToLowerInvariant())
            {
                case "/maps/_wh40k/battlefield40k.yml":
                    resolved = BattlefieldSnapshotTexturePath;
                    return true;
                case "/maps/_wh40k/winterassault.yml":
                    resolved = WinterAssaultSnapshotTexturePath;
                    return true;
                case "/maps/_wh40k/tinybattle.yml":
                    resolved = TinyBattleSnapshotTexturePath;
                    return true;
            }
        }

        resolved = default;
        return false;
    }
}
