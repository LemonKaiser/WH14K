using Content.Shared.Construction;
using Content.Shared.Mech.Components;
using Content.Shared.Power.Components;
using JetBrains.Annotations;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Construction.Completions;

/// <summary>
/// Creates the mech entity and transfers the inserted construction power cell into its battery slot.
/// </summary>
[UsedImplicitly, DataDefinition]
public sealed partial class BuildMech : IGraphAction
{
    [DataField("mechPrototype", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string MechPrototype = string.Empty;

    [DataField("container")]
    public string Container = "battery-container";

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent(uid, out ContainerManagerComponent? containerManager))
        {
            Logger.Warning($"Mech construct entity {uid} did not have a container manager! Aborting build mech action.");
            return;
        }

        var containerSystem = entityManager.EntitySysManager.GetEntitySystem<ContainerSystem>();

        if (!containerSystem.TryGetContainer(uid, Container, out var container, containerManager))
        {
            Logger.Warning($"Mech construct entity {uid} did not have the specified '{Container}' container! Aborting build mech action.");
            return;
        }

        if (container.ContainedEntities.Count != 1)
        {
            Logger.Warning($"Mech construct entity {uid} did not have exactly one item in the specified '{Container}' container! Aborting build mech action.");
            return;
        }

        var cell = container.ContainedEntities[0];

        if (!entityManager.TryGetComponent<BatteryComponent>(cell, out _))
        {
            Logger.Warning($"Mech construct entity {uid} had an invalid entity in container \"{Container}\"! Aborting build mech action.");
            return;
        }

        var transform = entityManager.GetComponent<TransformComponent>(uid);
        var mech = entityManager.SpawnEntity(MechPrototype, transform.Coordinates);
        if (!entityManager.TryGetComponent(mech, out MechComponent? mechComponent))
        {
            Logger.Warning($"Spawned mech {mech} from prototype '{MechPrototype}' without a mech component! Aborting build mech action.");
            entityManager.QueueDeleteEntity(mech);
            return;
        }

        mechComponent.BatterySlot = containerSystem.EnsureContainer<ContainerSlot>(mech, mechComponent.BatterySlotId);

        containerSystem.Remove(cell, container);
        if (!containerSystem.Insert(cell, mechComponent.BatterySlot))
        {
            Logger.Warning($"Failed to insert power cell {cell} into mech {mech} battery slot during mech construction.");
        }

        var entChangeEv = new ConstructionChangeEntityEvent(mech, uid);
        entityManager.EventBus.RaiseLocalEvent(uid, entChangeEv);
        entityManager.EventBus.RaiseLocalEvent(mech, entChangeEv, broadcast: true);
        entityManager.QueueDeleteEntity(uid);
    }
}

