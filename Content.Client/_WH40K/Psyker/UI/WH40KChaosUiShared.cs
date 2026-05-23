using System;
using Content.Shared._WH40K.Psyker;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Psyker.UI;

internal readonly record struct WH40KChaosGiftPathKeys(
    string PowerShort,
    string CooldownShort,
    string UtilityShort,
    string PowerLong,
    string CooldownLong,
    string UtilityLong);

internal static class WH40KChaosUiShared
{
    public const int UpgradeTierCost = 1;
    public const int UpgradeExCost = 3;

    private static readonly ResPath SkrizhalRsi = new("_WH40K/Interface/Abilities/skrizhali_runes.rsi");
    private static readonly ResPath CultistActionsRsi = new("_WH40K/Interface/Actions/cultist_abilities.rsi");
    private static readonly ResPath MagicActionsRsi = new("Objects/Magic/magicactions.rsi");
    private static readonly ResPath XenoToxicRsi = new("Objects/Weapons/Guns/Projectiles/xeno_toxic.rsi");

    public static SpriteSpecifier.Rsi GetGiftIconSpecifier(WH40KChaosPatron patron, int slot)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => slot switch
            {
                1 => new SpriteSpecifier.Rsi(CultistActionsRsi, "blade"),
                2 => new SpriteSpecifier.Rsi(CultistActionsRsi, "shield"),
                3 => new SpriteSpecifier.Rsi(CultistActionsRsi, "dash"),
                _ => new SpriteSpecifier.Rsi(MagicActionsRsi, "fireball"),
            },
            WH40KChaosPatron.Nurgle => slot switch
            {
                1 => new SpriteSpecifier.Rsi(CultistActionsRsi, "smoke"),
                2 => new SpriteSpecifier.Rsi(XenoToxicRsi, "xeno_toxic"),
                3 => new SpriteSpecifier.Rsi(CultistActionsRsi, "zombie"),
                _ => new SpriteSpecifier.Rsi(CultistActionsRsi, "smoke"),
            },
            WH40KChaosPatron.Slaanesh => slot switch
            {
                1 => new SpriteSpecifier.Rsi(CultistActionsRsi, "portal"),
                2 => new SpriteSpecifier.Rsi(CultistActionsRsi, "stim"),
                3 => new SpriteSpecifier.Rsi(CultistActionsRsi, "shield"),
                _ => new SpriteSpecifier.Rsi(CultistActionsRsi, "portal"),
            },
            WH40KChaosPatron.Tzeentch => slot switch
            {
                1 => new SpriteSpecifier.Rsi(MagicActionsRsi, "fireball"),
                2 => new SpriteSpecifier.Rsi(CultistActionsRsi, "shield"),
                3 => new SpriteSpecifier.Rsi(CultistActionsRsi, "portal"),
                _ => new SpriteSpecifier.Rsi(MagicActionsRsi, "fireball"),
            },
            _ => slot switch
            {
                1 => new SpriteSpecifier.Rsi(CultistActionsRsi, "portal"),
                2 => new SpriteSpecifier.Rsi(CultistActionsRsi, "shield"),
                3 => new SpriteSpecifier.Rsi(MagicActionsRsi, "fireball"),
                _ => new SpriteSpecifier.Rsi(MagicActionsRsi, "fireball"),
            },
        };
    }

    public static Color ResolvePatronAccent(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => Color.FromHex("#C14B53"),
            WH40KChaosPatron.Nurgle => Color.FromHex("#4FA95E"),
            WH40KChaosPatron.Slaanesh => Color.FromHex("#9A63C8"),
            WH40KChaosPatron.Tzeentch => Color.FromHex("#5DA8D8"),
            _ => Color.FromHex("#7B88A6"),
        };
    }

    public static Color MixColor(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            a.R + (b.R - a.R) * amount,
            a.G + (b.G - a.G) * amount,
            a.B + (b.B - a.B) * amount,
            1f);
    }

    public static Color LightenColor(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            color.R + (1f - color.R) * amount,
            color.G + (1f - color.G) * amount,
            color.B + (1f - color.B) * amount,
            color.A);
    }

    public static string FormatNumber(float value, int decimals = 1)
    {
        var format = decimals <= 0
            ? "0"
            : $"0.{new string('#', decimals)}";
        return MathF.Round(value, decimals).ToString(format);
    }

    public static string GetPatronLocKey(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => "wh40k-chaos-patron-khorne",
            WH40KChaosPatron.Nurgle => "wh40k-chaos-patron-nurgle",
            WH40KChaosPatron.Slaanesh => "wh40k-chaos-patron-slaanesh",
            WH40KChaosPatron.Tzeentch => "wh40k-chaos-patron-tzeentch",
            WH40KChaosPatron.Undivided => "wh40k-chaos-patron-undivided",
            _ => "wh40k-chaos-window-patron-none",
        };
    }

    public static string GetBranchTitleKey(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => "w40k-ch-khorne-title",
            WH40KChaosPatron.Nurgle => "w40k-ch-nurgle-title",
            WH40KChaosPatron.Slaanesh => "w40k-ch-slaanesh-title",
            WH40KChaosPatron.Tzeentch => "w40k-ch-tzeentch-title",
            _ => "w40k-ch-undivided-title",
        };
    }

    public static string GetGiftTitleKey(WH40KChaosPatron patron, int index)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => $"w40k-ch-khorne-gift-{index}-title",
            WH40KChaosPatron.Nurgle => $"w40k-ch-nurgle-gift-{index}-title",
            WH40KChaosPatron.Slaanesh => $"w40k-ch-slaanesh-gift-{index}-title",
            WH40KChaosPatron.Tzeentch => $"w40k-ch-tzeentch-gift-{index}-title",
            _ => $"w40k-ch-undivided-gift-{index}-title",
        };
    }

    public static string GetGiftDescriptionKey(WH40KChaosPatron patron, int index)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => $"w40k-ch-khorne-gift-{index}-desc",
            WH40KChaosPatron.Nurgle => $"w40k-ch-nurgle-gift-{index}-desc",
            WH40KChaosPatron.Slaanesh => $"w40k-ch-slaanesh-gift-{index}-desc",
            WH40KChaosPatron.Tzeentch => $"w40k-ch-tzeentch-gift-{index}-desc",
            _ => $"w40k-ch-undivided-gift-{index}-desc",
        };
    }

    public static string GetPatronIconState(WH40KChaosPatron patron)
    {
        return patron switch
        {
            WH40KChaosPatron.Khorne => "skrizhal_khorn",
            WH40KChaosPatron.Nurgle => "skrizhal_nurgk",
            WH40KChaosPatron.Slaanesh => "skrizhal_slaanesh",
            WH40KChaosPatron.Tzeentch => "skrizhal_tzinch",
            _ => "skrizhal_chaos",
        };
    }

    public static SpriteSpecifier.Rsi GetPatronIconSpecifier(WH40KChaosPatron patron)
    {
        return new SpriteSpecifier.Rsi(SkrizhalRsi, GetPatronIconState(patron));
    }

    public static WH40KChaosGiftPathKeys ResolvePathKeys(WH40KChaosPatron patron, int slot)
    {
        if (patron == WH40KChaosPatron.Khorne)
        {
            return slot switch
            {
                1 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-khorne-blade-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-khorne-blade-duration-short",
                    "w40k-ch-upgrade-path-khorne-blade-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-khorne-blade-duration"),
                2 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-khorne-bloodheal-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-khorne-bloodheal-cost-short",
                    "w40k-ch-upgrade-path-khorne-bloodheal-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-khorne-bloodheal-cost"),
                3 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-khorne-dash-damage-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-khorne-dash-range-short",
                    "w40k-ch-upgrade-path-khorne-dash-damage",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-khorne-dash-range"),
                _ => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-khorne-passive-speed-short",
                    "w40k-ch-upgrade-path-khorne-passive-health-short",
                    "w40k-ch-upgrade-path-khorne-passive-melee-short",
                    "w40k-ch-upgrade-path-khorne-passive-speed",
                    "w40k-ch-upgrade-path-khorne-passive-health",
                    "w40k-ch-upgrade-path-khorne-passive-melee"),
            };
        }

        if (patron == WH40KChaosPatron.Nurgle)
        {
            return slot switch
            {
                1 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-nurgle-miasma-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-nurgle-miasma-radius-short",
                    "w40k-ch-upgrade-path-nurgle-miasma-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-nurgle-miasma-radius"),
                2 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-nurgle-acid-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-nurgle-acid-utility-short",
                    "w40k-ch-upgrade-path-nurgle-acid-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-nurgle-acid-utility"),
                3 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-nurgle-bloom-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-nurgle-bloom-radius-short",
                    "w40k-ch-upgrade-path-nurgle-bloom-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-nurgle-bloom-radius"),
                _ => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-nurgle-passive-kills-short",
                    "w40k-ch-upgrade-path-nurgle-passive-health-short",
                    "w40k-ch-upgrade-path-nurgle-passive-regen-short",
                    "w40k-ch-upgrade-path-nurgle-passive-kills",
                    "w40k-ch-upgrade-path-nurgle-passive-health",
                    "w40k-ch-upgrade-path-nurgle-passive-regen"),
            };
        }

        if (patron == WH40KChaosPatron.Slaanesh)
        {
            return slot switch
            {
                1 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-slaanesh-masochism-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-slaanesh-masochism-cost-short",
                    "w40k-ch-upgrade-path-slaanesh-masochism-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-slaanesh-masochism-cost"),
                2 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-slaanesh-choir-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-slaanesh-choir-radius-short",
                    "w40k-ch-upgrade-path-slaanesh-choir-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-slaanesh-choir-radius"),
                3 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-slaanesh-tempo-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-slaanesh-tempo-duration-short",
                    "w40k-ch-upgrade-path-slaanesh-tempo-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-slaanesh-tempo-duration"),
                _ => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-slaanesh-passive-slow-short",
                    "w40k-ch-upgrade-path-slaanesh-passive-stun-short",
                    "w40k-ch-upgrade-path-slaanesh-passive-speed-short",
                    "w40k-ch-upgrade-path-slaanesh-passive-slow",
                    "w40k-ch-upgrade-path-slaanesh-passive-stun",
                    "w40k-ch-upgrade-path-slaanesh-passive-speed"),
            };
        }

        if (patron == WH40KChaosPatron.Tzeentch)
        {
            return slot switch
            {
                1 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-tzeentch-barrier-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-tzeentch-barrier-duration-short",
                    "w40k-ch-upgrade-path-tzeentch-barrier-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-tzeentch-barrier-duration"),
                2 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-tzeentch-speed-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-tzeentch-speed-radius-short",
                    "w40k-ch-upgrade-path-tzeentch-speed-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-tzeentch-speed-radius"),
                3 => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-tzeentch-vision-power-short",
                    "w40k-ch-upgrade-path-khorne-cooldown-short",
                    "w40k-ch-upgrade-path-tzeentch-vision-radius-short",
                    "w40k-ch-upgrade-path-tzeentch-vision-power",
                    "w40k-ch-upgrade-path-khorne-cooldown",
                    "w40k-ch-upgrade-path-tzeentch-vision-radius"),
                _ => new WH40KChaosGiftPathKeys(
                    "w40k-ch-upgrade-path-power-short",
                    "w40k-ch-upgrade-path-cooldown-short",
                    "w40k-ch-upgrade-path-cast-time-short",
                    "w40k-ch-upgrade-path-power",
                    "w40k-ch-upgrade-path-cooldown",
                    "w40k-ch-upgrade-path-cast-time"),
            };
        }

        return new WH40KChaosGiftPathKeys(
            "w40k-ch-upgrade-path-power-short",
            "w40k-ch-upgrade-path-cooldown-short",
            "w40k-ch-upgrade-path-cast-time-short",
            "w40k-ch-upgrade-path-power",
            "w40k-ch-upgrade-path-cooldown",
            "w40k-ch-upgrade-path-cast-time");
    }
}
