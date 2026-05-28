using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server.Cargo.Systems;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._WH40K.Vehicle.Fabrication;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Content.Shared.Vehicle.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Vehicle.Fabrication;

public sealed partial class WH40KVehicleFabricationSystem : EntitySystem
{
    [Dependency] private  CargoSystem _cargo = default!;
    [Dependency] private  SharedContainerSystem _containers = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  SharedMaterialStorageSystem _materials = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  IPrototypeManager _proto = default!;
    [Dependency] private  SharedStackSystem _stack = default!;
    [Dependency] private  StationSystem _station = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<WH40KVehicleFabricationConsoleComponent>(WH40KVehicleFabricationUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KVehicleFabricationAddOrderMessage>(OnAddOrderMessage);
            subs.Event<WH40KVehicleFabricationRemoveOrderMessage>(OnRemoveOrderMessage);
        });

        SubscribeLocalEvent<WH40KVehicleFabricationConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<WH40KVehicleFabricationConsoleComponent, InteractUsingEvent>(OnInteractUsing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KVehicleFabricationConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            var dirty = false;

            if (TryCompleteActiveBuild(uid, console))
                dirty = true;

            if (TryStartNextBuild(uid, console))
                dirty = true;

            if (!_ui.IsUiOpen(uid, WH40KVehicleFabricationUiKey.Key))
                continue;

            if (!dirty && console.NextUiRefresh > now)
                continue;

            console.NextUiRefresh = now + console.UiRefreshInterval;
            UpdateUi(uid, console);
        }
    }

    private void OnConsoleInit(Entity<WH40KVehicleFabricationConsoleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.PartsContainer = _containers.EnsureContainer<Container>(ent.Owner, ent.Comp.PartContainerId);
    }

    private void OnUiOpened(Entity<WH40KVehicleFabricationConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateUi(ent.Owner, ent.Comp);
    }

    private void OnInteractUsing(Entity<WH40KVehicleFabricationConsoleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var partContainer = ent.Comp.PartsContainer ?? _containers.EnsureContainer<Container>(ent.Owner, ent.Comp.PartContainerId);
        ent.Comp.PartsContainer = partContainer;

        if (!TryGetAcceptedPartId(ent.Comp, args.Used, out _))
            return;

        if (!_containers.Insert(args.Used, partContainer))
            return;

        _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-part-stored"), ent.Owner, args.User);
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        args.Handled = true;
    }

    private void OnAddOrderMessage(Entity<WH40KVehicleFabricationConsoleComponent> ent, ref WH40KVehicleFabricationAddOrderMessage args)
    {
        if (args.Amount <= 0)
            return;

        if (!TryComp(ent.Owner, out CargoOrderConsoleComponent? cargoConsole) ||
            !TryComp(ent.Owner, out MaterialStorageComponent? materialStorage))
        {
            return;
        }

        if (!TryResolveRecipe(ent.Comp, args.CargoProductId, out var recipeId, out var recipe, out var product))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-invalid-recipe"), ent.Owner, args.Actor);
            return;
        }

        if (!TryFindAssemblyPad(ent.Owner, ent.Comp, requireFree: false, out _))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-no-pad"), ent.Owner, args.Actor);
            return;
        }

        var activeCount = ent.Comp.ActiveBuild != null ? 1 : 0;
        var currentCount = ent.Comp.Queue.Count + activeCount;
        var remainingCapacity = Math.Max(0, ent.Comp.QueueCapacity - currentCount);
        if (args.Amount > remainingCapacity)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-vehicle-fabrication-popup-queue-full", ("remaining", remainingCapacity)),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!TryGetFactionBank(ent.Owner, cargoConsole.Account, out var bank, out var balance))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-bank-missing"), ent.Owner, args.Actor);
            return;
        }

        var totalCost = Math.Max(0, product.Cost) * args.Amount;
        if (balance < totalCost)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-vehicle-fabrication-popup-insufficient-funds", ("cost", totalCost), ("balance", balance)),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!HasRequiredMaterials(ent.Owner, materialStorage, recipe, args.Amount, out var missingMaterials))
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-vehicle-fabrication-popup-missing-materials", ("details", missingMaterials)),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!HasRequiredParts(ent.Comp, recipe, args.Amount, out var missingParts))
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-vehicle-fabrication-popup-missing-parts", ("details", missingParts)),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!TryConsumeMaterials(ent.Owner, materialStorage, recipe, args.Amount))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-invalid-recipe"), ent.Owner, args.Actor);
            return;
        }

        if (!TryConsumeParts(ent.Owner, ent.Comp, recipe, args.Amount))
        {
            RefundMaterials(ent.Owner, materialStorage, recipe, args.Amount);
            _popup.PopupEntity(
                Loc.GetString("wh40k-vehicle-fabrication-popup-missing-parts", ("details", BuildMissingPartsString(ent.Comp, recipe, args.Amount))),
                ent.Owner,
                args.Actor);
            return;
        }

        if (!_cargo.TryAdjustBankAccount(bank, cargoConsole.Account, -totalCost))
        {
            RefundMaterials(ent.Owner, materialStorage, recipe, args.Amount);
            RefundParts(ent.Owner, ent.Comp, recipe, args.Amount);
            _popup.PopupEntity(Loc.GetString("wh40k-vehicle-fabrication-popup-bank-missing"), ent.Owner, args.Actor);
            return;
        }

        var requester = string.IsNullOrWhiteSpace(args.Requester)
            ? Identity.Name(args.Actor, EntityManager)
            : args.Requester;

        for (var i = 0; i < args.Amount; i++)
        {
            var orderId = ent.Comp.NextOrderId++;
            var orderData = new CargoOrderData(
                orderId,
                product,
                1,
                requester,
                product.Description,
                cargoConsole.Account,
                product.Cost);

            ent.Comp.Queue.Add(new WH40KVehicleQueuedOrder(orderId, recipeId, orderData));
        }

        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateUi(ent.Owner, ent.Comp);
        _popup.PopupEntity(
            Loc.GetString("wh40k-vehicle-fabrication-popup-order-queued", ("name", product.Name), ("amount", args.Amount)),
            ent.Owner,
            args.Actor);
    }

    private void OnRemoveOrderMessage(Entity<WH40KVehicleFabricationConsoleComponent> ent, ref WH40KVehicleFabricationRemoveOrderMessage args)
    {
        if (!TryComp(ent.Owner, out CargoOrderConsoleComponent? cargoConsole) ||
            !TryComp(ent.Owner, out MaterialStorageComponent? materialStorage))
        {
            return;
        }

        var orderId = args.OrderId;
        var index = ent.Comp.Queue.FindIndex(order => order.OrderId == orderId);
        if (index == -1)
            return;

        var removed = ent.Comp.Queue[index];
        ent.Comp.Queue.RemoveAt(index);

        if (_proto.TryIndex(removed.Recipe, out WH40KVehicleRecipePrototype? recipe))
        {
            RefundMaterials(ent.Owner, materialStorage, recipe, 1);
            RefundParts(ent.Owner, ent.Comp, recipe, 1);

            if (TryGetFactionBank(ent.Owner, cargoConsole.Account, out var bank, out _))
                _cargo.TryAdjustBankAccount(bank, cargoConsole.Account, removed.OrderData.Price);
        }

        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateUi(ent.Owner, ent.Comp);

        _popup.PopupEntity(
            Loc.GetString("wh40k-vehicle-fabrication-popup-order-cancelled", ("name", removed.OrderData.ProductName)),
            ent.Owner,
            args.Actor);
    }

    private bool TryStartNextBuild(EntityUid uid, WH40KVehicleFabricationConsoleComponent console)
    {
        if (console.ActiveBuild != null || console.Queue.Count == 0)
            return false;

        if (!TryFindAssemblyPad(uid, console, requireFree: true, out _))
            return false;

        var next = console.Queue[0];
        console.Queue.RemoveAt(0);

        if (!_proto.TryIndex(next.Recipe, out WH40KVehicleRecipePrototype? recipe))
            return true;

        var now = _timing.CurTime;
        console.ActiveBuild = new WH40KVehicleActiveBuild(
            next,
            now,
            now + TimeSpan.FromSeconds(Math.Max(1, recipe.BuildDurationSeconds)));
        console.NextUiRefresh = TimeSpan.Zero;
        return true;
    }

    private bool TryCompleteActiveBuild(EntityUid uid, WH40KVehicleFabricationConsoleComponent console)
    {
        var active = console.ActiveBuild;
        if (active == null || active.EndsAt > _timing.CurTime)
            return false;

        if (!_proto.TryIndex(active.QueueOrder.Recipe, out WH40KVehicleRecipePrototype? recipe))
        {
            console.ActiveBuild = null;
            console.NextUiRefresh = TimeSpan.Zero;
            return true;
        }

        if (!TryFindAssemblyPad(uid, console, requireFree: true, out var pad))
            return false;

        Spawn(recipe.Spawn, Transform(pad).Coordinates);
        console.ActiveBuild = null;
        console.NextUiRefresh = TimeSpan.Zero;
        return true;
    }

    private void UpdateUi(EntityUid uid, WH40KVehicleFabricationConsoleComponent console)
    {
        if (!TryComp(uid, out CargoOrderConsoleComponent? cargoConsole))
            return;

        var station = _station.GetOwningStation(uid);
        var stationName = station != null ? MetaData(station.Value).EntityName : MetaData(uid).EntityName;
        var stationNet = station != null ? GetNetEntity(station.Value) : NetEntity.Invalid;

        var products = new List<ProtoId<CargoProductPrototype>>();
        foreach (var recipeId in console.Recipes)
        {
            if (_proto.TryIndex(recipeId, out WH40KVehicleRecipePrototype? recipeProto))
                products.Add(recipeProto.Product);
        }

        var orders = new List<CargoOrderData>();
        if (console.ActiveBuild != null)
            orders.Add(BuildActiveOrderData(console.ActiveBuild, cargoConsole.Account));

        orders.AddRange(console.Queue.Select(entry => entry.OrderData));

        var hasDeliveryEta = console.ActiveBuild != null && console.ActiveBuild.EndsAt > _timing.CurTime;
        var deliveryEtaEndTime = hasDeliveryEta ? console.ActiveBuild!.EndsAt : TimeSpan.Zero;
        var deliveryDuration = TimeSpan.Zero;
        if (console.ActiveBuild != null &&
            _proto.TryIndex(console.ActiveBuild.QueueOrder.Recipe, out WH40KVehicleRecipePrototype? recipe))
        {
            deliveryDuration = TimeSpan.FromSeconds(Math.Max(1, recipe.BuildDurationSeconds));
        }

        _ui.SetUiState(uid, WH40KVehicleFabricationUiKey.Key, new WH40KVehicleFabricationBuiState(
            stationName,
            console.Queue.Count + (console.ActiveBuild != null ? 1 : 0),
            console.QueueCapacity,
            stationNet,
            hasDeliveryEta,
            deliveryEtaEndTime,
            deliveryDuration,
            orders,
            products));
    }

    private CargoOrderData BuildActiveOrderData(WH40KVehicleActiveBuild activeBuild, ProtoId<CargoAccountPrototype> account)
    {
        var source = activeBuild.QueueOrder.OrderData;
        var data = new CargoOrderData(
            source.OrderId,
            source.ProductId,
            source.ProductName,
            source.Price,
            source.OrderQuantity,
            source.Requester,
            source.Reason,
            account);
        data.Approved = true;
        data.SetApproverData(Loc.GetString("wh40k-vehicle-fabrication-order-active"));
        return data;
    }

    private bool TryResolveRecipe(
        WH40KVehicleFabricationConsoleComponent console,
        string cargoProductId,
        out ProtoId<WH40KVehicleRecipePrototype> recipeId,
        out WH40KVehicleRecipePrototype recipe,
        out CargoProductPrototype product)
    {
        foreach (var candidateId in console.Recipes)
        {
            if (!_proto.TryIndex(candidateId, out WH40KVehicleRecipePrototype? candidateRecipe) ||
                !_proto.TryIndex(candidateRecipe.Product, out CargoProductPrototype? candidateProduct))
            {
                continue;
            }

            if (!string.Equals(candidateProduct.ID, cargoProductId, StringComparison.Ordinal))
                continue;

            recipeId = candidateId;
            recipe = candidateRecipe;
            product = candidateProduct;
            return true;
        }

        recipeId = default;
        recipe = null!;
        product = null!;
        return false;
    }

    private bool TryGetFactionBank(
        EntityUid entity,
        ProtoId<CargoAccountPrototype> account,
        out Entity<StationBankAccountComponent?> bank,
        out int balance)
    {
        bank = default;
        balance = 0;

        if (_station.GetOwningStation(entity) is not { } stationUid ||
            !TryComp<StationBankAccountComponent>(stationUid, out var bankComponent) ||
            !_cargo.TryGetAccount((stationUid, bankComponent), account, out balance))
        {
            return false;
        }

        bank = (stationUid, bankComponent);
        return true;
    }

    private bool HasRequiredMaterials(
        EntityUid uid,
        MaterialStorageComponent storage,
        WH40KVehicleRecipePrototype recipe,
        int amount,
        out string missing)
    {
        var sb = new StringBuilder();

        foreach (var (materialId, baseAmount) in recipe.Materials)
        {
            var needed = Math.Max(0, baseAmount) * amount;
            var available = _materials.GetMaterialAmount(uid, materialId, storage);
            if (available >= needed)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");

            sb.Append($"{ResolveMaterialName(materialId)} x{needed - available}");
        }

        missing = sb.ToString();
        return sb.Length == 0;
    }

    private bool HasRequiredParts(
        WH40KVehicleFabricationConsoleComponent console,
        WH40KVehicleRecipePrototype recipe,
        int amount,
        out string missing)
    {
        missing = BuildMissingPartsString(console, recipe, amount);
        return string.IsNullOrEmpty(missing);
    }

    private string BuildMissingPartsString(
        WH40KVehicleFabricationConsoleComponent console,
        WH40KVehicleRecipePrototype recipe,
        int amount)
    {
        var availableParts = CountStoredParts(console);
        var sb = new StringBuilder();

        foreach (var (partId, baseAmount) in recipe.Parts)
        {
            var needed = Math.Max(0, baseAmount) * amount;
            var available = availableParts.GetValueOrDefault(partId);
            if (available >= needed)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");

            sb.Append($"{ResolvePartName(partId)} x{needed - available}");
        }

        return sb.ToString();
    }

    private bool TryConsumeMaterials(
        EntityUid uid,
        MaterialStorageComponent storage,
        WH40KVehicleRecipePrototype recipe,
        int amount)
    {
        var materials = recipe.Materials
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => -pair.Value * amount);

        return materials.Count == 0 || _materials.TryChangeMaterialAmount((uid, storage), materials);
    }

    private void RefundMaterials(
        EntityUid uid,
        MaterialStorageComponent storage,
        WH40KVehicleRecipePrototype recipe,
        int amount)
    {
        var materials = recipe.Materials
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value * amount);

        if (materials.Count > 0)
            _materials.TryChangeMaterialAmount((uid, storage), materials);
    }

    private bool TryConsumeParts(
        EntityUid uid,
        WH40KVehicleFabricationConsoleComponent console,
        WH40KVehicleRecipePrototype recipe,
        int amount)
    {
        var requirements = recipe.Parts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value * amount);

        if (requirements.Count == 0)
            return true;

        var container = console.PartsContainer ?? _containers.EnsureContainer<Container>(uid, console.PartContainerId);
        console.PartsContainer = container;

        foreach (var part in container.ContainedEntities.ToArray())
        {
            var prototypeId = MetaData(part).EntityPrototype?.ID;
            if (prototypeId == null ||
                !requirements.TryGetValue(prototypeId, out var remaining) ||
                remaining <= 0)
            {
                continue;
            }

            var available = TryComp<StackComponent>(part, out var stackComp)
                ? _stack.GetCount((part, stackComp))
                : 1;

            var toConsume = Math.Min(available, remaining);
            if (toConsume <= 0)
                continue;

            if (stackComp != null)
                _stack.TryUse((part, stackComp), toConsume);
            else
                QueueDel(part);

            requirements[prototypeId] -= toConsume;
            if (requirements.Values.All(value => value <= 0))
                return true;
        }

        return requirements.Values.All(value => value <= 0);
    }

    private void RefundParts(
        EntityUid uid,
        WH40KVehicleFabricationConsoleComponent console,
        WH40KVehicleRecipePrototype recipe,
        int amount)
    {
        var container = console.PartsContainer ?? _containers.EnsureContainer<Container>(uid, console.PartContainerId);
        console.PartsContainer = container;
        var coords = Transform(uid).Coordinates;

        foreach (var (partId, baseAmount) in recipe.Parts)
        {
            for (var i = 0; i < baseAmount * amount; i++)
            {
                var spawned = Spawn(partId, coords);
                if (!_containers.Insert(spawned, container))
                    _transform.SetCoordinates(spawned, coords);
            }
        }
    }

    private Dictionary<string, int> CountStoredParts(WH40KVehicleFabricationConsoleComponent console)
    {
        var result = new Dictionary<string, int>();
        if (console.PartsContainer == null)
            return result;

        foreach (var part in console.PartsContainer.ContainedEntities)
        {
            var prototypeId = MetaData(part).EntityPrototype?.ID;
            if (prototypeId == null)
                continue;

            var count = TryComp<StackComponent>(part, out var stackComp)
                ? _stack.GetCount((part, stackComp))
                : 1;

            result[prototypeId] = result.GetValueOrDefault(prototypeId) + count;
        }

        return result;
    }

    private bool TryGetAcceptedPartId(
        WH40KVehicleFabricationConsoleComponent console,
        EntityUid used,
        out string prototypeId)
    {
        prototypeId = string.Empty;
        var usedPrototype = MetaData(used).EntityPrototype?.ID;
        if (usedPrototype == null)
            return false;

        foreach (var recipeId in console.Recipes)
        {
            if (!_proto.TryIndex(recipeId, out WH40KVehicleRecipePrototype? recipe))
                continue;

            if (!recipe.Parts.ContainsKey(usedPrototype))
                continue;

            prototypeId = usedPrototype;
            return true;
        }

        return false;
    }

    private bool TryFindAssemblyPad(
        EntityUid uid,
        WH40KVehicleFabricationConsoleComponent console,
        bool requireFree,
        out EntityUid padUid)
    {
        padUid = EntityUid.Invalid;
        var consoleStation = _station.GetOwningStation(uid);
        var consoleWorld = _transform.GetWorldPosition(uid);

        var candidates = _lookup.GetEntitiesInRange(uid, console.AssemblyPadRange, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries);
        EntityUid? bestCandidate = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!HasComp<WH40KVehicleAssemblyPadComponent>(candidate))
                continue;

            var candidateStation = _station.GetOwningStation(candidate);
            if (consoleStation != null && candidateStation != consoleStation)
                continue;

            if (requireFree && IsAssemblyPadBlocked(candidate))
                continue;

            var distance = (_transform.GetWorldPosition(candidate) - consoleWorld).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestCandidate = candidate;
        }

        if (bestCandidate == null)
            return false;

        padUid = bestCandidate.Value;
        return true;
    }

    private bool IsAssemblyPadBlocked(EntityUid padUid)
    {
        foreach (var ent in _lookup.GetEntitiesInRange(padUid, 0.7f, LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries))
        {
            if (ent == padUid || HasComp<WH40KVehicleAssemblyPadComponent>(ent))
                continue;

            if (HasComp<VehicleComponent>(ent))
                return true;
        }

        return false;
    }

    private string ResolveMaterialName(string materialId)
    {
        return _proto.TryIndex<MaterialPrototype>(materialId, out var material)
            ? material.Name
            : materialId;
    }

    private string ResolvePartName(string prototypeId)
    {
        return _proto.TryIndex<EntityPrototype>(prototypeId, out var prototype)
            ? prototype.Name
            : prototypeId;
    }
}
