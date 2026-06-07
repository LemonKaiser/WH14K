using System.Linq;
using Content.Client.Humanoid;
using Content.Client.Station;
using Content.Shared.Body;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI.ProfileEditorControls;

public sealed partial class ProfilePreviewSpriteView
{
    private EntProtoId? _loadedHumanoidBasePrototype;
    private bool _loadedHumanoidUsedSpecialPreview;

    /// <summary>
    /// A slim reload that only updates the entity itself and not any of the job entities, etc.
    /// </summary>
    private void ReloadHumanoidEntity(HumanoidCharacterProfile humanoid)
    {
        if (!EntMan.EntityExists(PreviewDummy) ||
            !EntMan.HasComponent<VisualBodyComponent>(PreviewDummy))
            return;

        ApplyHumanoidAppearanceToPreview(humanoid);
    }

    private bool TryRefreshHumanoidPreview(HumanoidCharacterProfile humanoid, JobPrototype? job, bool jobClothes)
    {
        if (!TryResolveHumanoidPreviewBasePrototype(humanoid, job, jobClothes, out var basePrototype, out var resolvedJob, out var usesSpecialPreview))
            return false;

        if (!EntMan.EntityExists(PreviewDummy))
            return false;

        if (!EntMan.HasComponent<VisualBodyComponent>(PreviewDummy))
            return false;

        if (_loadedHumanoidBasePrototype != basePrototype)
            return false;

        if (_loadedHumanoidUsedSpecialPreview)
            return false;

        if (usesSpecialPreview)
            return false;

        ClearDummyEquipment();
        ApplyHumanoidAppearanceToPreview(humanoid);

        if (jobClothes && resolvedJob != null)
        {
            RoleLoadout? loadout = null;
            if (_prototypeManager.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(resolvedJob.ID)))
            {
                loadout = humanoid.GetLoadoutOrDefault(
                    LoadoutSystem.GetJobPrototype(resolvedJob.ID),
                    _playerManager.LocalSession,
                    humanoid.Species,
                    EntMan,
                    _prototypeManager);
            }

            GiveDummyLoadout(loadout);
            GiveDummyJobClothes(resolvedJob);
        }

        _loadedHumanoidBasePrototype = basePrototype;
        _loadedHumanoidUsedSpecialPreview = false;
        return true;
    }

    /// <summary>
    /// Loads the profile onto a dummy entity.
    /// </summary>
    private void LoadHumanoidEntity(HumanoidCharacterProfile? humanoid, JobPrototype? job, bool jobClothes)
    {
        var previewEntity = ResolveHumanoidPreviewEntity(humanoid, job, jobClothes, out job, out var usesSpecialPreview);

        if (previewEntity != null)
        {
            // Special type like borg or AI, do not spawn a human just spawn the entity.
            PreviewDummy = EntMan.SpawnEntity(previewEntity, MapCoordinates.Nullspace);
        }
        else if (humanoid is not null)
        {
            var dummy = _prototypeManager.Index(humanoid.Species).DollPrototype;
            PreviewDummy = EntMan.SpawnEntity(dummy, MapCoordinates.Nullspace);
        }
        else
        {
            PreviewDummy = EntMan.SpawnEntity(_prototypeManager.Index(HumanoidCharacterProfile.DefaultSpecies).DollPrototype, MapCoordinates.Nullspace);
        }

        if (humanoid != null && EntMan.HasComponent<VisualBodyComponent>(PreviewDummy))
        {
            ApplyHumanoidAppearanceToPreview(humanoid);
        }

        _loadedHumanoidBasePrototype = previewEntity;
        _loadedHumanoidUsedSpecialPreview = usesSpecialPreview;

        if (humanoid != null && jobClothes)
        {
            DebugTools.Assert(job != null);

            RoleLoadout? loadout = null;
            if (_prototypeManager.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID)))
            {
                loadout = humanoid.GetLoadoutOrDefault(LoadoutSystem.GetJobPrototype(job.ID), _playerManager.LocalSession, humanoid.Species, EntMan, _prototypeManager);
            }

            // Mirror real spawn order: role loadout claims its slots first, then job gear fills the remaining gaps.
            GiveDummyLoadout(loadout);
            GiveDummyJobClothes(job);
        }
    }

    private EntProtoId? ResolveHumanoidPreviewEntity(HumanoidCharacterProfile? humanoid, JobPrototype? job, bool jobClothes, out JobPrototype? resolvedJob, out bool usesSpecialPreview)
    {
        resolvedJob = job;
        usesSpecialPreview = false;

        EntProtoId? previewEntity = null;
        if (humanoid != null && jobClothes)
        {
            resolvedJob ??= GetPreferredJob(humanoid);
            previewEntity = resolvedJob.JobPreviewEntity ?? (EntProtoId?) resolvedJob.JobEntity;
            usesSpecialPreview = previewEntity != null;
        }

        if (previewEntity != null)
            return previewEntity;

        if (humanoid != null)
            return _prototypeManager.Index(humanoid.Species).DollPrototype;

        return _prototypeManager.Index(HumanoidCharacterProfile.DefaultSpecies).DollPrototype;
    }

    private bool TryResolveHumanoidPreviewBasePrototype(HumanoidCharacterProfile humanoid, JobPrototype? job, bool jobClothes, out EntProtoId basePrototype, out JobPrototype? resolvedJob, out bool usesSpecialPreview)
    {
        var previewEntity = ResolveHumanoidPreviewEntity(humanoid, job, jobClothes, out resolvedJob, out usesSpecialPreview);
        if (previewEntity == null)
        {
            basePrototype = default;
            return false;
        }

        basePrototype = previewEntity.Value;
        return true;
    }

    /// <summary>
    /// Gets the highest priority job for the profile.
    /// </summary>
    private JobPrototype GetPreferredJob(HumanoidCharacterProfile profile)
    {
        var highPriorityJob = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract (what is resharper smoking?)
        return _prototypeManager.Index<JobPrototype>(highPriorityJob.Id ?? SharedGameTicker.FallbackOverflowJob);
    }

    private void GiveDummyLoadout(RoleLoadout? roleLoadout)
    {
        if (roleLoadout == null)
            return;

        var spawnSys = EntMan.System<StationSpawningSystem>();

        foreach (var group in roleLoadout.SelectedLoadouts.Values)
        {
            foreach (var loadout in group)
            {
                if (!_prototypeManager.Resolve(loadout.Prototype, out var loadoutProto))
                    continue;

                spawnSys.EquipStartingGear(PreviewDummy, loadoutProto);
            }
        }
    }

    /// <summary>
    /// Applies the specified job's clothes to the dummy.
    /// </summary>
    private void GiveDummyJobClothes(JobPrototype job)
    {
        var inventorySys = EntMan.System<InventorySystem>();
        if (!inventorySys.TryGetSlots(PreviewDummy, out var slots))
            return;

        if (!_prototypeManager.Resolve(job.StartingGear, out var gear))
            return;

        foreach (var slot in slots)
        {
            if (inventorySys.TryGetSlotEntity(PreviewDummy, slot.Name, out _))
                continue;

            var itemType = ((IEquipmentLoadout) gear).GetGear(slot.Name);

            if (itemType != string.Empty)
            {
                var item = EntMan.SpawnEntity(itemType, MapCoordinates.Nullspace);

                if (!inventorySys.TryEquip(PreviewDummy, item, slot.Name, true, true))
                    EntMan.DeleteEntity(item);
            }
        }
    }

    private void ClearDummyEquipment()
    {
        var handsSys = EntMan.System<SharedHandsSystem>();
        if (EntMan.TryGetComponent<HandsComponent>(PreviewDummy, out var hands))
        {
            foreach (var held in handsSys.EnumerateHeld((PreviewDummy, hands)).ToList())
            {
                handsSys.TryDrop((PreviewDummy, hands), held, checkActionBlocker: false, doDropInteraction: false);

                if (EntMan.EntityExists(held))
                    EntMan.DeleteEntity(held);
            }
        }

        var inventorySys = EntMan.System<InventorySystem>();
        if (!inventorySys.TryGetSlots(PreviewDummy, out var slots))
            return;

        foreach (var slot in slots)
        {
            if (!inventorySys.TryUnequip(PreviewDummy, slot.Name, out var removedItem, silent: true, force: true, reparent: false))
                continue;

            if (removedItem != null)
                EntMan.DeleteEntity(removedItem.Value);
        }
    }

    private void ResetHumanoidPreviewState()
    {
        _loadedHumanoidBasePrototype = null;
        _loadedHumanoidUsedSpecialPreview = false;
    }

    private void ApplyHumanoidAppearanceToPreview(HumanoidCharacterProfile humanoid)
    {
        var visualBody = EntMan.System<SharedVisualBodySystem>();
        visualBody.ApplyProfile(PreviewDummy, new OrganProfileData
        {
            Sex = humanoid.Sex,
            SkinColor = humanoid.Appearance.SkinColor,
            EyeColor = humanoid.Appearance.EyeColor,
        });
        visualBody.ApplyMarkings(PreviewDummy, ExpandPreviewMarkings(humanoid));
    }

    private Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> ExpandPreviewMarkings(HumanoidCharacterProfile humanoid)
    {
        var expanded = new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>();

        foreach (var (organ, organData) in _markingManager.GetMarkingData(humanoid.Species))
        {
            var organMarkings = new Dictionary<HumanoidVisualLayers, List<Marking>>();
            humanoid.Appearance.Markings.TryGetValue(organ, out var sourceMarkings);

            foreach (var layer in organData.Layers)
            {
                organMarkings[layer] = sourceMarkings?.TryGetValue(layer, out var markings) == true
                    ? markings.ToList()
                    : [];
            }

            expanded[organ] = organMarkings;
        }

        return expanded;
    }
}
