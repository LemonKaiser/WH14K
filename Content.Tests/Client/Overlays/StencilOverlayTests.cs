using Content.Client.Overlays;
using NUnit.Framework;

namespace Content.Tests.Client.Overlays;

[TestFixture]
public sealed class StencilOverlayTests
{
    [Test]
    public void WeatherUsesEqualStencilMask()
    {
        Assert.That(StencilOverlay.WeatherStencilDraw.Id, Is.EqualTo("StencilEqualDraw"));
    }
}
