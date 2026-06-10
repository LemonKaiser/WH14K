using Content.Shared._WH40K.ArmorPlates;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.UnitTesting;

namespace Content.Tests.Shared._WH40K.ArmorPlates;

[TestFixture]
public sealed class WH40KArmorPlateHelperTests : RobustUnitTest
{
    [Test]
    public void DamageMaskFollowsApprovedTypeMapping()
    {
        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                ["Heat"] = FixedPoint2.New(8),
                ["Slash"] = FixedPoint2.New(4),
                ["Piercing"] = FixedPoint2.Zero,
            },
        };

        var mask = WH40KArmorPlateHelper.GetDamageMask(damage);

        Assert.That(mask, Is.EqualTo(
            WH40KArmorPlateDamageMask.Laser |
            WH40KArmorPlateDamageMask.Melee));
    }

    [Test]
    public void EffectiveBonusStopsAtEightyPercentProtection()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KArmorPlateHelper.GetEffectiveBonusPercent(0.3f, 30f), Is.EqualTo(10f).Within(0.001f));
            Assert.That(WH40KArmorPlateHelper.ApplyBonusToCoefficient(0.3f, 30f), Is.EqualTo(0.2f).Within(0.001f));

            Assert.That(WH40KArmorPlateHelper.GetEffectiveBonusPercent(0.25f, 5f), Is.EqualTo(5f).Within(0.001f));
            Assert.That(WH40KArmorPlateHelper.ApplyBonusToCoefficient(0.25f, 5f), Is.EqualTo(0.2f).Within(0.001f));

            Assert.That(WH40KArmorPlateHelper.GetEffectiveBonusPercent(0.2f, 30f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(WH40KArmorPlateHelper.ApplyBonusToCoefficient(0.2f, 30f), Is.EqualTo(0.2f).Within(0.001f));
        });
    }

    [Test]
    public void SlotIdsRoundTrip()
    {
        var slotId = WH40KArmorPlateHelper.GetSlotId(4);

        Assert.Multiple(() =>
        {
            Assert.That(slotId, Is.EqualTo("wh40k-plate-slot-4"));
            Assert.That(WH40KArmorPlateHelper.TryGetSlotIndex(slotId, out var index), Is.True);
            Assert.That(index, Is.EqualTo(4));
            Assert.That(WH40KArmorPlateHelper.TryGetSlotIndex("not-a-plate-slot", out _), Is.False);
        });
    }
}
