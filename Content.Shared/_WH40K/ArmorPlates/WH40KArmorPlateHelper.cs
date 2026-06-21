using Content.Shared.Damage;

namespace Content.Shared._WH40K.ArmorPlates;

public static class WH40KArmorPlateHelper
{
    public const float MaxProtectionPercent = 80f;

    private static readonly string[] LaserDamageTypes = ["Heat"];
    private static readonly string[] BulletDamageTypes = ["Piercing"];
    private static readonly string[] MeleeDamageTypes = ["Blunt", "Slash"];

    public static string GetSlotId(int slotIndex)
    {
        return $"{WH40KArmorPlateHolderComponent.SlotIdPrefix}{slotIndex}";
    }

    public static bool TryGetSlotIndex(string slotId, out int slotIndex)
    {
        slotIndex = 0;

        if (!slotId.StartsWith(WH40KArmorPlateHolderComponent.SlotIdPrefix))
            return false;

        return int.TryParse(
            slotId[WH40KArmorPlateHolderComponent.SlotIdPrefix.Length..],
            out slotIndex);
    }

    public static IReadOnlyList<string> GetProtectedDamageTypes(WH40KArmorPlateType type)
    {
        return type switch
        {
            WH40KArmorPlateType.Laser => LaserDamageTypes,
            WH40KArmorPlateType.Bullet => BulletDamageTypes,
            WH40KArmorPlateType.Melee => MeleeDamageTypes,
            _ => Array.Empty<string>(),
        };
    }

    public static WH40KArmorPlateDamageMask GetDamageMask(WH40KArmorPlateType type)
    {
        return type switch
        {
            WH40KArmorPlateType.Laser => WH40KArmorPlateDamageMask.Laser,
            WH40KArmorPlateType.Bullet => WH40KArmorPlateDamageMask.Bullet,
            WH40KArmorPlateType.Melee => WH40KArmorPlateDamageMask.Melee,
            _ => WH40KArmorPlateDamageMask.None,
        };
    }

    public static bool MatchesDamage(WH40KArmorPlateType type, WH40KArmorPlateDamageMask damageMask)
    {
        return (GetDamageMask(type) & damageMask) != 0;
    }

    public static WH40KArmorPlateDamageMask GetDamageMask(DamageSpecifier damage)
    {
        var mask = WH40KArmorPlateDamageMask.None;

        foreach (var (damageType, value) in damage.DamageDict)
        {
            if (value <= 0)
                continue;

            switch (damageType)
            {
                case "Heat":
                    mask |= WH40KArmorPlateDamageMask.Laser;
                    break;
                case "Piercing":
                    mask |= WH40KArmorPlateDamageMask.Bullet;
                    break;
                case "Blunt":
                case "Slash":
                    mask |= WH40KArmorPlateDamageMask.Melee;
                    break;
            }
        }

        return mask;
    }

    public static float GetProtectionPercent(float coefficient)
    {
        return Math.Clamp((1f - coefficient) * 100f, 0f, 100f);
    }

    public static float GetEffectiveBonusPercent(float baseCoefficient, float bonusPercent)
    {
        var protection = GetProtectionPercent(baseCoefficient);
        if (protection >= MaxProtectionPercent)
            return 0f;

        return Math.Max(0f, Math.Min(bonusPercent, MaxProtectionPercent - protection));
    }

    public static float ApplyBonusToCoefficient(float baseCoefficient, float bonusPercent)
    {
        var protection = GetProtectionPercent(baseCoefficient);
        var effectiveBonus = GetEffectiveBonusPercent(baseCoefficient, bonusPercent);
        var finalProtection = Math.Clamp(protection + effectiveBonus, 0f, 100f);
        return 1f - finalProtection / 100f;
    }

    public static DamageModifierSet CloneModifierSet(DamageModifierSet source)
    {
        return new DamageModifierSet
        {
            Coefficients = new Dictionary<string, float>(source.Coefficients),
            FlatReduction = new Dictionary<string, float>(source.FlatReduction),
        };
    }

    public static string GetPlateTexturePath(WH40KArmorPlateType type)
    {
        return type switch
        {
            WH40KArmorPlateType.Laser => "/Textures/_WH40K/Objects/ArmorPlates/laser.rsi",
            WH40KArmorPlateType.Bullet => "/Textures/_WH40K/Objects/ArmorPlates/bullet.rsi",
            WH40KArmorPlateType.Melee => "/Textures/_WH40K/Objects/ArmorPlates/melee.rsi",
            _ => "/Textures/_WH40K/Objects/ArmorPlates/laser.rsi",
        };
    }

    public static string GetTierOverlayState(int tier)
    {
        return $"t{tier}";
    }
}
