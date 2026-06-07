using System.Collections.Generic;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Humanoid;

[TestFixture]
[TestOf(typeof(HumanoidCharacterAppearance))]
public sealed class HumanoidCharacterAppearanceCloneTests
{
    [Test]
    public void CloneDoesNotShareNestedMarkings()
    {
        var head = new ProtoId<OrganCategoryPrototype>("Head");
        var original = new HumanoidCharacterAppearance(
            Color.Black,
            Color.White,
            new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>()
            {
                [head] = new()
                {
                    [HumanoidVisualLayers.Hair] = new()
                    {
                        new("HumanHairLongBedhead2", new[] { Color.Red }),
                    },
                },
            });

        var clone = original.Clone();
        clone.Markings[head][HumanoidVisualLayers.Hair][0] = clone.Markings[head][HumanoidVisualLayers.Hair][0].WithColor(Color.Green);
        clone.Markings[head][HumanoidVisualLayers.Hair].Add(new("HumanHairAfro", new[] { Color.Blue }));

        Assert.That(original.Markings[head][HumanoidVisualLayers.Hair], Has.Count.EqualTo(1));
        Assert.That(original.Markings[head][HumanoidVisualLayers.Hair][0].MarkingColors[0], Is.EqualTo(Color.Red));
        Assert.That(clone.Markings[head][HumanoidVisualLayers.Hair], Has.Count.EqualTo(2));
        Assert.That(clone.Markings[head][HumanoidVisualLayers.Hair][0].MarkingColors[0], Is.EqualTo(Color.Green));
    }
}
