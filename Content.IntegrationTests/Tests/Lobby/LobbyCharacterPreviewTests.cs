using System.Collections.Generic;
using System.Linq;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
public sealed class LobbyCharacterPreviewTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: PreviewSwapATracker

- type: playTimeTracker
  id: PreviewSwapBTracker

- type: loadout
  id: PreviewSwapKnifeLoadout
  inhand:
  - CombatKnife

- type: loadoutGroup
  id: PreviewSwapKnifeGroup
  name: generic-unknown
  loadouts:
  - PreviewSwapKnifeLoadout

- type: roleLoadout
  id: JobPreviewSwapA
  groups:
  - PreviewSwapKnifeGroup

- type: job
  id: PreviewSwapA
  playTimeTracker: PreviewSwapATracker

- type: loadout
  id: PreviewSwapCrowbarLoadout
  inhand:
  - Crowbar

- type: loadoutGroup
  id: PreviewSwapCrowbarGroup
  name: generic-unknown
  loadouts:
  - PreviewSwapCrowbarLoadout

- type: roleLoadout
  id: JobPreviewSwapB
  groups:
  - PreviewSwapCrowbarGroup

- type: job
  id: PreviewSwapB
  playTimeTracker: PreviewSwapBTracker
";

    public override PoolSettings PoolSettings => new() { InLobby = true };

    [Test]
    public async Task CarouselPreviewSwapClearsHandsAndMarkings()
    {
        var pair = Pair;

        await pair.Client.WaitAssertion(() =>
        {
            var prototypeManager = pair.Client.ResolveDependency<IPrototypeManager>();
            ProtoId<JobPrototype> firstJob = "PreviewSwapA";
            ProtoId<JobPrototype> secondJob = "PreviewSwapB";
            var head = new ProtoId<OrganCategoryPrototype>("Head");
            var hairLayer = HumanoidVisualLayers.Hair;
            var profileA = CreateProfile(
                "Preview A",
                Sex.Male,
                new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>()
                {
                    [head] = new()
                    {
                        [hairLayer] = new()
                        {
                            new("HumanHairLongBedhead2", new[] { Color.Red }),
                        },
                    },
                });
            var profileB = CreateProfile(
                "Preview B",
                Sex.Female,
                new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>());
            var preview = new ProfilePreviewSpriteView();

            try
            {
                preview.LoadPreview(profileA, prototypeManager.Index(firstJob), true);
                var firstDummy = preview.PreviewDummy;

                Assert.That(GetHeldPrototypeIds(pair.Client.EntMan, firstDummy), Is.EquivalentTo(new[] { "CombatKnife" }),
                    "The first preview should only hold the first profile's in-hand item.");
                Assert.That(GetAppliedMarkingIds(pair.Client.EntMan, firstDummy, head, hairLayer), Does.Contain("HumanHairLongBedhead2"),
                    "The first preview should apply the first profile's markings.");

                preview.LoadPreview(profileB, prototypeManager.Index(secondJob), true);

                Assert.That(preview.PreviewDummy, Is.EqualTo(firstDummy),
                    "The test must exercise the carousel fast-swap path rather than a full dummy respawn.");
                Assert.That(GetHeldPrototypeIds(pair.Client.EntMan, preview.PreviewDummy), Is.EquivalentTo(new[] { "Crowbar" }),
                    "After swapping, only the second profile's in-hand item should remain.");
                Assert.That(GetAppliedMarkingIds(pair.Client.EntMan, preview.PreviewDummy, head, hairLayer), Does.Not.Contain("HumanHairLongBedhead2"),
                    "After swapping, markings from the previous profile must not remain on the reused dummy.");
            }
            finally
            {
                preview.ClearPreview();
            }
        });
    }

    private static HumanoidCharacterProfile CreateProfile(
        string name,
        Sex sex,
        Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> markings)
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human", sex)
            .WithName(name);

        return profile.WithCharacterAppearance(profile.Appearance.WithMarkings(markings));
    }

    private static IReadOnlyCollection<string> GetHeldPrototypeIds(IEntityManager entMan, EntityUid uid)
    {
        if (!entMan.TryGetComponent(uid, out HandsComponent hands))
            return Array.Empty<string>();

        return entMan.System<SharedHandsSystem>()
            .EnumerateHeld((uid, hands))
            .Select(held => entMan.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID)
            .OfType<string>()
            .ToArray();
    }

    private static IReadOnlyCollection<string> GetAppliedMarkingIds(
        IEntityManager entMan,
        EntityUid uid,
        ProtoId<OrganCategoryPrototype> organ,
        HumanoidVisualLayers layer)
    {
        var visualBody = entMan.System<SharedVisualBodySystem>();
        if (!visualBody.TryGatherMarkingsData(uid, new HashSet<HumanoidVisualLayers> { layer }, out _, out _, out var applied) ||
            !applied.TryGetValue(organ, out var organMarkings) ||
            !organMarkings.TryGetValue(layer, out var markings))
        {
            return Array.Empty<string>();
        }

        return markings.Select(marking => marking.MarkingId.Id).ToArray();
    }
}
