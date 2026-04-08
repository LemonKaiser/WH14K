using System.Numerics;
using Content.Shared._WH40K.Tank;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.Tests.Shared._WH40K.Tank;

[TestFixture]
public sealed class WH40KTankDirectionalOffsetSetTests : RobustUnitTest
{
    [Test]
    public void ResolveReturnsConfiguredCardinalOffset()
    {
        var offsets = new WH40KTankDirectionalOffsetSet
        {
            North = new Vector2(1f, 2f),
            South = new Vector2(3f, 4f),
            East = new Vector2(5f, 6f),
            West = new Vector2(7f, 8f),
        };

        Assert.Multiple(() =>
        {
            Assert.That(offsets.Resolve(Direction.North, Vector2.Zero), Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(offsets.Resolve(Direction.South, Vector2.Zero), Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(offsets.Resolve(Direction.East, Vector2.Zero), Is.EqualTo(new Vector2(5f, 6f)));
            Assert.That(offsets.Resolve(Direction.West, Vector2.Zero), Is.EqualTo(new Vector2(7f, 8f)));
        });
    }

    [Test]
    public void ResolveFallsBackForNonCardinalDirections()
    {
        var fallback = new Vector2(9f, 10f);
        var offsets = new WH40KTankDirectionalOffsetSet
        {
            North = new Vector2(1f, 2f),
            South = new Vector2(3f, 4f),
            East = new Vector2(5f, 6f),
            West = new Vector2(7f, 8f),
        };

        Assert.Multiple(() =>
        {
            Assert.That(offsets.Resolve(Direction.NorthEast, fallback), Is.EqualTo(fallback));
            Assert.That(offsets.Resolve(Direction.SouthWest, fallback), Is.EqualTo(fallback));
            Assert.That(offsets.Resolve(Direction.Invalid, fallback), Is.EqualTo(fallback));
        });
    }
}
