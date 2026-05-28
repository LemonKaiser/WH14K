using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared.ActionBlocker;
using Content.Shared.Construction;
using Content.Shared.Database;
using JetBrains.Annotations;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Content.Shared.Whitelist;
using Content.Shared.Interaction.Components;

namespace Content.Server.Materials;

/// <summary>
/// This handles <see cref="SharedMaterialStorageSystem"/>
/// </summary>
public sealed partial class MaterialStorageSystem : SharedMaterialStorageSystem
{
    [Dependency] private  IAdminLogManager _adminLogger = default!;
    [Dependency] private  IPrototypeManager _prototypeManager = default!;
    [Dependency] private  ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private  SharedAudioSystem _audio = default!;
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  StackSystem _stackSystem = default!;
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MaterialStorageComponent, MachineDeconstructedEvent>(OnDeconstructed);

        SubscribeAllEvent<EjectMaterialMessage>(OnEjectMessage);
    }

    private void OnDeconstructed(EntityUid uid, MaterialStorageComponent component, MachineDeconstructedEvent args)
    {
        if (!component.DropOnDeconstruct)
            return;

        foreach (var (material, amount) in component.Storage)
        {
            SpawnMultipleFromMaterial(amount, material, Transform(uid).Coordinates);
        }
    }

    private void OnEjectMessage(EjectMaterialMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } player)
            return;

        var uid = GetEntity(msg.Entity);

        if (!TryComp<MaterialStorageComponent>(uid, out var component))
            return;

        if (!Exists(uid))
            return;

        if (!_actionBlocker.CanInteract(player, uid))
            return;

        if (!component.CanEjectStoredMaterials || !_prototypeManager.TryIndex<MaterialPrototype>(msg.Material, out var material))
            return;

        var volume = 0;

        if (material.StackEntity != null)
        {
            if (!_prototypeManager.Index<EntityPrototype>(material.StackEntity).TryGetComponent<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory))
                return;

            var volumePerSheet = composition.MaterialComposition.FirstOrDefault(kvp => kvp.Key == msg.Material).Value;
            var sheetsToExtract = Math.Min(msg.SheetsToExtract, _stackSystem.GetMaxCount(material.StackEntity.Value));

            volume = sheetsToExtract * volumePerSheet;
        }

        if (volume <= 0 || !TryChangeMaterialAmount(uid, msg.Material, -volume))
            return;

        var mats = SpawnMultipleFromMaterial(volume, material, Transform(uid).Coordinates, out _);
        foreach (var mat in mats.Where(mat => !TerminatingOrDeleted(mat)))
        {
            _stackSystem.TryMergeToContacts(mat);
        }
    }

    public override bool TryInsertMaterialEntity(EntityUid user,
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent? storage = null,
        MaterialComponent? material = null,
        PhysicalCompositionComponent? composition = null)
    {
        if (!Resolve(receiver, ref storage) || !Resolve(toInsert, ref material, ref composition, false))
            return false;

        if (CanInsertMaterialEntity(toInsert, receiver, storage, material, composition))
        {
            if (!base.TryInsertMaterialEntity(user, toInsert, receiver, storage, material, composition))
                return false;

            HandleSuccessfulInsertion(user, toInsert, receiver, storage);
            return true;
        }

        if (TryInsertPartialStack(user, toInsert, receiver, storage, composition))
            return true;

        if (IsStorageOverflow(receiver, toInsert, storage, composition))
            _popup.PopupEntity(Loc.GetString("lathe-popup-material-storage-full"), receiver, user);

        return false;
    }

    public override bool CanInsertMaterialEntity(
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent? storage = null,
        MaterialComponent? material = null,
        PhysicalCompositionComponent? composition = null)
    {
        if (!Resolve(receiver, ref storage) || !Resolve(toInsert, ref material, ref composition, false))
            return false;

        if (TryComp<ApcPowerReceiverComponent>(receiver, out var power) && !power.Powered)
            return false;

        if (IsStorageOverflow(receiver, toInsert, storage, composition))
            return false;

        return base.CanInsertMaterialEntity(toInsert, receiver, storage, material, composition);
    }

    /// <summary>
    /// Server-side insertion variant for automation systems.
    /// No popup/sound/admin log side effects.
    /// </summary>
    public bool TryInsertMaterialEntityNoFeedback(
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent? storage = null,
        MaterialComponent? material = null,
        PhysicalCompositionComponent? composition = null)
    {
        if (!Resolve(receiver, ref storage) || !Resolve(toInsert, ref material, ref composition, false))
            return false;

        if (!CanInsertMaterialEntity(toInsert, receiver, storage, material, composition))
            return false;

        if (!base.TryInsertMaterialEntity(receiver, toInsert, receiver, storage, material, composition))
            return false;

        QueueDel(toInsert);
        return true;
    }

    private bool IsStorageOverflow(
        EntityUid receiver,
        EntityUid toInsert,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition)
    {
        if (storage.StorageLimit is not { } limit)
            return false;

        var currentVolume = GetTotalMaterialAmount(receiver, storage, localOnly: true);
        var incomingVolume = GetIncomingVolume(toInsert, composition);
        return currentVolume + incomingVolume > limit;
    }

    private bool TryInsertPartialStack(
        EntityUid user,
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition)
    {
        if (!IsStorageOverflow(receiver, toInsert, storage, composition))
            return false;

        if (!TryComp<StackComponent>(toInsert, out var stackComp) || stackComp.Count <= 1)
            return false;

        var maxSplit = GetMaximumInsertableStackCount(receiver, toInsert, stackComp, storage, composition);
        if (maxSplit <= 0)
            return false;

        for (var count = maxSplit; count >= 1; count--)
        {
            if (!CanInsertMaterialCount(toInsert, receiver, storage, composition, count))
                continue;

            var split = _stackSystem.Split((toInsert, stackComp), count, Transform(toInsert).Coordinates);
            if (split is not { } splitUid)
                continue;

            if (!TryComp<MaterialComponent>(splitUid, out var splitMaterial) ||
                !TryComp<PhysicalCompositionComponent>(splitUid, out var splitComposition))
            {
                if (TryComp<StackComponent>(splitUid, out var splitStack))
                    _stackSystem.TryMergeStacks((splitUid, splitStack), (toInsert, stackComp), out _);
                continue;
            }

            if (!base.TryInsertMaterialEntity(user, splitUid, receiver, storage, splitMaterial, splitComposition))
            {
                if (TryComp<StackComponent>(splitUid, out var splitStack))
                    _stackSystem.TryMergeStacks((splitUid, splitStack), (toInsert, stackComp), out _);
                continue;
            }

            HandleSuccessfulInsertion(user, splitUid, receiver, storage);
            return true;
        }

        return false;
    }

    private bool CanInsertMaterialCount(
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition,
        int count)
    {
        if (count <= 0)
            return false;

        if (_whitelist.IsWhitelistFail(storage.Whitelist, toInsert))
            return false;

        if (HasComp<UnremoveableComponent>(toInsert))
            return false;

        if (TryComp<ApcPowerReceiverComponent>(receiver, out var power) && !power.Powered)
            return false;

        var materials = composition.MaterialComposition
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value * count);

        return CanChangeMaterialAmount((receiver, storage), materials);
    }

    private int GetMaximumInsertableStackCount(
        EntityUid receiver,
        EntityUid toInsert,
        StackComponent stackComp,
        MaterialStorageComponent storage,
        PhysicalCompositionComponent composition)
    {
        if (stackComp.Count <= 1)
            return 0;

        var maxCount = stackComp.Count - 1;

        var perUnitVolume = GetIncomingVolume(toInsert, composition);
        if (perUnitVolume <= 0)
            return 0;

        perUnitVolume /= Math.Max(1, stackComp.Count);
        if (perUnitVolume <= 0)
            return 0;

        if (storage.StorageLimit is { } limit)
        {
            var current = GetTotalMaterialAmount(receiver, storage, localOnly: true);
            var remaining = Math.Max(0, limit - current);
            maxCount = Math.Min(maxCount, remaining / perUnitVolume);
        }

        return Math.Max(0, maxCount);
    }

    private void HandleSuccessfulInsertion(
        EntityUid user,
        EntityUid insertedItem,
        EntityUid receiver,
        MaterialStorageComponent storage)
    {
        _audio.PlayPvs(storage.InsertingSound, receiver);
        _popup.PopupEntity(Loc.GetString("machine-insert-item",
                ("user", user),
                ("machine", receiver),
                ("item", insertedItem)),
            receiver);

        QueueDel(insertedItem);

        TryComp<StackComponent>(insertedItem, out var stack);
        var count = stack?.Count ?? 1;
        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(user):player} inserted {count} {ToPrettyString(insertedItem):inserted} into {ToPrettyString(receiver):receiver}");
    }

    private int GetIncomingVolume(EntityUid toInsert, PhysicalCompositionComponent composition)
    {
        var multiplier = TryComp<StackComponent>(toInsert, out var stackComp) ? Math.Max(1, stackComp.Count) : 1;
        var incomingVolume = 0;
        foreach (var (_, volume) in composition.MaterialComposition)
        {
            incomingVolume += volume * multiplier;
        }

        return incomingVolume;
    }

    /// <summary>
    ///     Spawn an amount of a material in stack entities.
    ///     Note the 'amount' is material dependent.
    ///     1 biomass = 1 biomass in its stack,
    ///     but 100 plasma = 1 sheet of plasma, etc.
    /// </summary>
    public List<EntityUid> SpawnMultipleFromMaterial(int amount, string material, EntityCoordinates coordinates)
    {
        return SpawnMultipleFromMaterial(amount, material, coordinates, out _);
    }

    /// <summary>
    ///     Spawn an amount of a material in stack entities.
    ///     Note the 'amount' is material dependent.
    ///     1 biomass = 1 biomass in its stack,
    ///     but 100 plasma = 1 sheet of plasma, etc.
    /// </summary>
    public List<EntityUid> SpawnMultipleFromMaterial(int amount, string material, EntityCoordinates coordinates, out int overflowMaterial)
    {
        overflowMaterial = 0;
        if (!_prototypeManager.TryIndex<MaterialPrototype>(material, out var stackType))
        {
            Log.Error("Failed to index material prototype " + material);
            return new List<EntityUid>();
        }

        return SpawnMultipleFromMaterial(amount, stackType, coordinates, out overflowMaterial);
    }

    /// <summary>
    ///     Spawn an amount of a material in stack entities.
    ///     Note the 'amount' is material dependent.
    ///     1 biomass = 1 biomass in its stack,
    ///     but 100 plasma = 1 sheet of plasma, etc.
    /// </summary>
    [PublicAPI]
    public List<EntityUid> SpawnMultipleFromMaterial(int amount, MaterialPrototype materialProto, EntityCoordinates coordinates)
    {
        return SpawnMultipleFromMaterial(amount, materialProto, coordinates, out _);
    }

    /// <summary>
    ///     Spawn an amount of a material in stack entities.
    ///     Note the 'amount' is material dependent.
    ///     1 biomass = 1 biomass in its stack,
    ///     but 100 plasma = 1 sheet of plasma, etc.
    /// </summary>
    public List<EntityUid> SpawnMultipleFromMaterial(int amount, MaterialPrototype materialProto, EntityCoordinates coordinates, out int overflowMaterial)
    {
        overflowMaterial = 0;

        if (amount <= 0 || materialProto.StackEntity == null)
            return new List<EntityUid>();

        var entProto = _prototypeManager.Index<EntityPrototype>(materialProto.StackEntity);
        if (!entProto.TryGetComponent<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory))
            return new List<EntityUid>();

        var materialPerStack = composition.MaterialComposition[materialProto.ID];
        var amountToSpawn = amount / materialPerStack;
        overflowMaterial = amount - amountToSpawn * materialPerStack;

        if (amountToSpawn == 0)
            return new List<EntityUid>();

        return _stackSystem.SpawnMultipleAtPosition(materialProto.StackEntity.Value, amountToSpawn, coordinates);
    }

    /// <summary>
    /// Eject a material out of this storage. The internal counts are updated.
    /// Material that cannot be ejected stays in storage. (e.g. only have 50 but a sheet needs 100).
    /// </summary>
    /// <param name="entity">The entity with storage to eject from.</param>
    /// <param name="material">The material prototype to eject.</param>
    /// <param name="maxAmount">The maximum amount to eject. If not given, as much as possible is ejected.</param>
    /// <param name="coordinates">The position where to spawn the created sheets. If not given, they're spawned next to the entity.</param>
    /// <param name="component">The storage component on <paramref name="entity"/>. Resolved automatically if not given.</param>
    /// <returns>The stack entities that were spawned.</returns>
    public List<EntityUid> EjectMaterial(
        EntityUid entity,
        string material,
        int? maxAmount = null,
        EntityCoordinates? coordinates = null,
        MaterialStorageComponent? component = null)
    {
        if (!Resolve(entity, ref component))
            return new List<EntityUid>();

        coordinates ??= Transform(entity).Coordinates;

        var amount = GetMaterialAmount(entity, material, component);
        if (maxAmount != null)
            amount = Math.Min(maxAmount.Value, amount);

        var spawned = SpawnMultipleFromMaterial(amount, material, coordinates.Value, out var overflow);

        TryChangeMaterialAmount(entity, material, -(amount - overflow), component);
        return spawned;
    }

    /// <summary>
    /// Eject all material stored in an entity, with the same mechanics as <see cref="EjectMaterial"/>.
    /// </summary>
    /// <param name="entity">The entity with storage to eject from.</param>
    /// <param name="coordinates">The position where to spawn the created sheets. If not given, they're spawned next to the entity.</param>
    /// <param name="component">The storage component on <paramref name="entity"/>. Resolved automatically if not given.</param>
    /// <returns>The stack entities that were spawned.</returns>
    public List<EntityUid> EjectAllMaterial(
        EntityUid entity,
        EntityCoordinates? coordinates = null,
        MaterialStorageComponent? component = null)
    {
        if (!Resolve(entity, ref component))
            return new List<EntityUid>();

        coordinates ??= Transform(entity).Coordinates;

        var allSpawned = new List<EntityUid>();
        foreach (var material in component.Storage.Keys.ToArray())
        {
            var spawned = EjectMaterial(entity, material, null, coordinates, component);
            allSpawned.AddRange(spawned);
        }

        return allSpawned;
    }
}
