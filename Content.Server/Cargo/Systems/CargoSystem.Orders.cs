using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Server.Cargo.Components;
using Content.Server._WH40K.Cargo.Components;
using Content.Server._WH40K.Research.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Database;
using Content.Shared.Emag.Systems;
using Content.Shared.Interaction;
using Content.Shared.Labels.Components;
using Content.Shared.Paper;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Station.Components;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Cargo.Systems
{
    public sealed partial class CargoSystem
    {
        [Dependency] private SharedTransformSystem _transformSystem = default!;
        [Dependency] private EmagSystem _emag = default!;
        [Dependency] private IGameTiming _timing = default!;

        private void InitializeConsole()
        {
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleAddOrderMessage>(OnAddOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleRemoveOrderMessage>(OnRemoveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleApproveOrderMessage>(OnApproveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, BoundUIOpenedEvent>(OnOrderUIOpened);
            SubscribeLocalEvent<CargoOrderConsoleComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<CargoOrderConsoleComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<CargoOrderConsoleComponent, GotEmaggedEvent>(OnEmagged);
        }

        private void OnInteractUsingCash(EntityUid uid, CargoOrderConsoleComponent component, ref InteractUsingEvent args)
        {
            var price = _pricing.GetPrice(args.Used);

            if (price == 0)
                return;

            var stationUid = _station.GetOwningStation(args.Used);

            if (!TryComp(stationUid, out StationBankAccountComponent? bank))
                return;

            _audio.PlayPvs(ApproveSound, uid);
            UpdateBankAccount((stationUid.Value, bank), (int) price, component.Account);
            QueueDel(args.Used);
            args.Handled = true;
        }

        private void OnInteractUsingSlip(Entity<CargoOrderConsoleComponent> ent, ref InteractUsingEvent args, CargoSlipComponent slip)
        {
            if (slip.OrderQuantity <= 0)
                return;

            var stationUid = _station.GetOwningStation(ent);

            if (!TryGetOrderDatabase(stationUid, out var orderDatabase))
                return;

            if (!_protoMan.TryIndex(slip.Product, out var product))
            {
                Log.Error($"Tried to add invalid cargo product {slip.Product} as order!");
                return;
            }

            if (!ent.Comp.AllowedGroups.Contains(product.Group))
                return;

            var orderId = GenerateOrderId(orderDatabase);
            var unitPrice = GetEffectiveOrderUnitPrice(stationUid.Value, ent.Comp.Account, product.Cost);
            var data = new CargoOrderData(
                orderId,
                product,
                slip.OrderQuantity,
                slip.Requester,
                slip.Reason,
                slip.Account,
                unitPrice);

            if (!CanQueueOrderQuantity((stationUid.Value, orderDatabase), ent.Comp.Account, slip.OrderQuantity, out _))
            {
                ConsolePopup(args.User, Loc.GetString("cargo-console-too-many"));
                PlayDenySound(ent, ent.Comp);
                return;
            }

            if (!TryAddOrder(stationUid.Value, ent.Comp.Account, data, orderDatabase))
            {
                PlayDenySound(ent, ent.Comp);
                return;
            }

            // Log order addition
            _audio.PlayPvs(ent.Comp.ScanSound, ent);
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(args.User):user} inserted order slip [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.Product}, requester:{data.Requester}, reason:{data.Reason}]");
            QueueDel(args.Used);
            args.Handled = true;
        }

        private void OnInteractUsing(EntityUid uid, CargoOrderConsoleComponent component, ref InteractUsingEvent args)
        {
            if (HasComp<CashComponent>(args.Used))
            {
                OnInteractUsingCash(uid, component, ref args);
            }
            else if (TryComp<CargoSlipComponent>(args.Used, out var slip) && component.Mode == CargoOrderConsoleMode.DirectOrder)
            {
                OnInteractUsingSlip((uid, component), ref args, slip);
            }
        }

        private void OnInit(EntityUid uid, CargoOrderConsoleComponent orderConsole, ComponentInit args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, station);
        }

        private void OnEmagged(Entity<CargoOrderConsoleComponent> ent, ref GotEmaggedEvent args)
        {
            if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
                return;

            if (_emag.CheckFlag(ent, EmagType.Interaction))
                return;

            args.Handled = true;
        }

        private void UpdateConsole()
        {
            var stationQuery = EntityQueryEnumerator<StationBankAccountComponent>();
            while (stationQuery.MoveNext(out var uid, out var bank))
            {
                if (Timing.CurTime < bank.NextIncomeTime)
                    continue;
                bank.NextIncomeTime += bank.IncomeDelay;

                var balanceToAdd = (int) Math.Round(bank.IncreasePerSecond * bank.IncomeDelay.TotalSeconds);
                UpdateBankAccount((uid, bank), balanceToAdd, bank.RevenueDistribution);
            }

            UpdateDelayedBatches();
        }

        private void UpdateDelayedBatches()
        {
            var query = EntityQueryEnumerator<StationCargoOrderBatchComponent, StationCargoOrderDatabaseComponent>();
            while (query.MoveNext(out var stationUid, out var batchState, out var orderDatabase))
            {
                if (batchState.ActiveBatches.Count == 0)
                    continue;

                var deliveredAccounts = new List<ProtoId<CargoAccountPrototype>>();
                var shouldRefreshOrders = false;
                foreach (var (account, batch) in batchState.ActiveBatches)
                {
                    if (Timing.CurTime < batch.DeliverAt)
                    {
                        continue;
                    }

                    if (!TryDeliverBatch(stationUid, account, batch, orderDatabase))
                        continue;

                    deliveredAccounts.Add(account);
                    shouldRefreshOrders = true;
                }

                if (deliveredAccounts.Count != 0)
                {
                    foreach (var account in deliveredAccounts)
                    {
                        batchState.ActiveBatches.Remove(account);
                    }

                    Dirty(stationUid, batchState);
                }

                if (shouldRefreshOrders)
                {
                    UpdateOrders(stationUid);
                }
            }
        }

        private bool TryDeliverBatch(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account,
            CargoOrderBatchTransitData batch,
            StationCargoOrderDatabaseComponent orderDatabase)
        {
            var destinations = GetBatchPalletDestinations(stationUid, account);
            if (destinations.Count == 0)
                return false;

            var productsToDeliver = new List<CargoOrderBatchItemData>();
            foreach (var item in batch.Items)
            {
                if (item.Quantity <= 0)
                {
                    continue;
                }

                for (var i = 0; i < item.Quantity; i++)
                {
                    productsToDeliver.Add(item);
                }
            }

            if (productsToDeliver.Count > 0)
            {
                _random.Shuffle(destinations);
                _random.Shuffle(productsToDeliver);

                // Limit crate count by both pallet capacity and batch size.
                // For WH40K batches we cap crates to floor(itemCount / 2) to avoid single-item crates.
                var maxCratesByItems = Math.Max(1, productsToDeliver.Count / 2);
                var maxCrates = Math.Max(1, Math.Min(destinations.Count, maxCratesByItems));
                var cratesToSpawn = _random.Next(1, maxCrates + 1);

                var cratePrototype = batch.CratePrototype;
                if (!_protoMan.HasIndex<EntityPrototype>(cratePrototype))
                    cratePrototype = "CrateGenericSteel";

                var crates = new List<EntityUid>(cratesToSpawn);
                for (var i = 0; i < cratesToSpawn; i++)
                {
                    crates.Add(Spawn(cratePrototype, destinations[i]));
                }

                // Seed items across crates first, then spread remaining items randomly.
                // With crates <= floor(itemCount / 2), this keeps crate contents denser.
                var minItemsPerCrate = productsToDeliver.Count >= crates.Count * 2 ? 2 : 1;
                var productIndex = 0;

                for (var crateIndex = 0; crateIndex < crates.Count; crateIndex++)
                {
                    for (var j = 0; j < minItemsPerCrate && productIndex < productsToDeliver.Count; j++)
                    {
                        SpawnBatchItemInCrate(crates[crateIndex], productsToDeliver[productIndex]);
                        productIndex++;
                    }
                }

                for (; productIndex < productsToDeliver.Count; productIndex++)
                {
                    var crateIndex = _random.Next(crates.Count);
                    SpawnBatchItemInCrate(crates[crateIndex], productsToDeliver[productIndex]);
                }
            }

            if (orderDatabase.Orders.TryGetValue(account, out var orders))
                orders.RemoveAll(order => order.OrderId == batch.SummaryOrderId);

            return true;
        }

        private void SpawnBatchItemInCrate(EntityUid crate, CargoOrderBatchItemData product)
        {
            if (!_container.TryGetContainer(crate, SharedEntityStorageSystem.ContainerName, out var crateContainer))
                return;

            var spawn = Transform(crate).Coordinates;
            if (!TrySpawnCargoProductEntity(product.Product, product.ProductId, spawn, out _, out var item))
                return;

            if (!_container.Insert(item.Value, crateContainer))
                QueueDel(item.Value);
        }

        private List<EntityCoordinates> GetBatchPalletDestinations(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account)
        {
            var destinations = new List<EntityCoordinates>();
            var query = EntityQueryEnumerator<CargoPalletComponent, CargoOrderBatchPalletComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var pallet, out var accountPallet, out var xform))
            {
                if ((pallet.PalletType & BuySellType.Buy) == 0)
                    continue;

                if (accountPallet.Account != account)
                    continue;

                if (!xform.Anchored)
                    continue;

                if (_station.GetOwningStation(uid, xform) != stationUid)
                    continue;

                destinations.Add(new EntityCoordinates(xform.ParentUid, xform.LocalPosition));
            }

            if (destinations.Count > 0)
                return destinations;

            // Support older maps that still use generic buy pallets for delayed cargo delivery.
            var fallbackQuery = EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
            while (fallbackQuery.MoveNext(out var uid, out var pallet, out var xform))
            {
                if ((pallet.PalletType & BuySellType.Buy) == 0)
                    continue;

                if (HasComp<CargoOrderBatchPalletComponent>(uid))
                    continue;

                if (!xform.Anchored)
                    continue;

                if (_station.GetOwningStation(uid, xform) != stationUid)
                    continue;

                destinations.Add(new EntityCoordinates(xform.ParentUid, xform.LocalPosition));
            }

            return destinations;
        }

        private bool IsBatchInTransit(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account,
            [NotNullWhen(true)] out CargoOrderBatchTransitData? batch)
        {
            batch = null;

            if (!TryComp<StationCargoOrderBatchComponent>(stationUid, out var batchState))
                return false;

            if (!batchState.ActiveBatches.TryGetValue(account, out batch))
                return false;

            return true;
        }

        private string BuildBatchManifest(IReadOnlyList<CargoOrderData> orders)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (i > 0)
                    builder.Append('\n');

                var name = order.ProductName;
                if (string.IsNullOrWhiteSpace(name) &&
                    !string.IsNullOrWhiteSpace(order.Product) &&
                    _protoMan.Resolve(order.Product, out var cargoProduct))
                {
                    name = cargoProduct.Name;
                }

                if (string.IsNullOrWhiteSpace(name) &&
                    _protoMan.TryIndex<EntityPrototype>(order.ProductId, out var productProto))
                {
                    name = productProto.Name;
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = !string.IsNullOrWhiteSpace(order.ProductId) ? order.ProductId : Loc.GetString("cargo-console-invalid-product");

                builder.Append("- ");
                builder.Append(order.OrderQuantity);
                builder.Append("x ");
                builder.Append(name);
            }

            return builder.ToString();
        }

        private void TryApproveDelayedBatch(
            EntityUid uid,
            EntityUid player,
            CargoOrderConsoleComponent component,
            CargoBatchOrderConsoleComponent batchConsole,
            EntityUid stationUid,
            StationBankAccountComponent bank,
            StationCargoOrderDatabaseComponent orderDatabase)
        {
            if (IsBatchInTransit(stationUid, component.Account, out _))
            {
                ConsolePopup(player, Loc.GetString("wh40k-cargo-batch-train-in-transit"));
                PlayDenySound(uid, component);
                return;
            }

            var accountOrders = EnsureOrderList(orderDatabase, component.Account);

            var pendingOrders = accountOrders
                .Where(order => !order.Approved)
                .ToList();

            if (pendingOrders.Count == 0)
                return;

            var totalCost = pendingOrders.Sum(order => order.Price * order.OrderQuantity);
            var totalQuantity = pendingOrders.Sum(order => order.OrderQuantity);
            var accountBalance = GetBalanceFromAccount((stationUid, bank), component.Account);

            if (totalCost > accountBalance)
            {
                ConsolePopup(player, Loc.GetString("cargo-console-insufficient-funds", ("cost", totalCost)));
                PlayDenySound(uid, component);
                return;
            }

            var batchState = EnsureComp<StationCargoOrderBatchComponent>(stationUid);
            var batchId = batchState.NextBatchId++;
            var summaryOrderId = GenerateOrderId(orderDatabase);
            var summaryOrder = new CargoOrderData(
                summaryOrderId,
                batchConsole.SummaryProduct,
                Loc.GetString("wh40k-cargo-batch-summary-name", ("batchId", batchId)),
                totalCost,
                totalQuantity,
                Loc.GetString("wh40k-cargo-batch-requester"),
                BuildBatchManifest(pendingOrders),
                component.Account)
            {
                Approved = true,
                IsBatchSummary = true
            };

            var effectiveDelaySeconds = GetEffectiveBatchDelaySeconds(stationUid, component.Account, batchConsole);
            var etaMinutes = Math.Max(1, (int) Math.Ceiling(effectiveDelaySeconds / 60f));
            summaryOrder.SetApproverData(Loc.GetString("wh40k-cargo-batch-summary-approver-minutes", ("minutes", etaMinutes)));

            accountOrders.RemoveAll(order => !order.Approved);
            accountOrders.Add(summaryOrder);

            var batchData = new CargoOrderBatchTransitData
            {
                BatchId = batchId,
                SummaryOrderId = summaryOrderId,
                DeliverAt = Timing.CurTime + TimeSpan.FromSeconds(effectiveDelaySeconds),
                Account = component.Account,
                CratePrototype = batchConsole.SummaryProduct
            };

            foreach (var order in pendingOrders)
            {
                batchData.Items.Add(new CargoOrderBatchItemData
                {
                    Product = order.Product,
                    ProductId = order.ProductId,
                    ProductName = order.ProductName,
                    Price = order.Price,
                    Quantity = order.OrderQuantity
                });
            }

            batchState.ActiveBatches[component.Account] = batchData;

            UpdateBankAccount((stationUid, bank), -totalCost, component.Account);
            Dirty(stationUid, batchState);

            _audio.PlayPvs(ApproveSound, uid);
            ConsolePopup(player, Loc.GetString("wh40k-cargo-batch-approved", ("minutes", etaMinutes)));
            UpdateOrders(stationUid);
        }

        #region Interface

        private void OnApproveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleApproveOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (component.Mode != CargoOrderConsoleMode.DirectOrder)
                return;

            if (!_accessReaderSystem.IsAllowed(player, uid))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-order-not-allowed"));
                PlayDenySound(uid, component);
                return;
            }

            var station = _station.GetOwningStation(uid);

            // No station to deduct from.
            if (!TryComp(station, out StationBankAccountComponent? bank) ||
                !TryComp(station, out StationDataComponent? stationData) ||
                !TryGetOrderDatabase(station, out var orderDatabase))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-station-not-found"));
                PlayDenySound(uid, component);
                return;
            }

            if (TryComp<CargoBatchOrderConsoleComponent>(uid, out var batchConsole))
            {
                TryApproveDelayedBatch(uid, player, component, batchConsole, station.Value, bank, orderDatabase);
                return;
            }

            // Find our order again. It might have been dispatched or approved already
            var accountOrders = EnsureOrderList(orderDatabase, component.Account);
            var order = accountOrders.Find(order => args.OrderId == order.OrderId && !order.Approved);
            if (order == null || !_protoMan.Resolve(order.Account, out var account))
            {
                return;
            }

            // Invalid order
            if (string.IsNullOrWhiteSpace(order.Product) ||
                !_protoMan.Resolve(order.Product, out var product))
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-invalid-product"));
                PlayDenySound(uid, component);
                return;
            }

            var amount = GetOutstandingOrderCount((station.Value, orderDatabase), order.Account);
            var capacity = GetEffectiveOrderCapacity((station.Value, orderDatabase), order.Account);

            // Too many orders, avoid them getting spammed in the UI.
            if (amount >= capacity)
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-too-many"));
                PlayDenySound(uid, component);
                return;
            }

            // Cap orders so someone can't spam thousands.
            var cappedAmount = Math.Min(capacity - amount, order.OrderQuantity);

            if (cappedAmount != order.OrderQuantity)
            {
                order.OrderQuantity = cappedAmount;
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-snip-snip"));
                PlayDenySound(uid, component);
            }

            var unitPrice = order.Price > 0 ? order.Price : product.Cost;
            var cost = unitPrice * order.OrderQuantity;
            var accountBalance = GetBalanceFromAccount((station.Value, bank), order.Account);

            // Not enough balance
            if (cost > accountBalance)
            {
                ConsolePopup(args.Actor, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                PlayDenySound(uid, component);
                return;
            }

            var ev = new FulfillCargoOrderEvent((station.Value, stationData), order, (uid, component));
            RaiseLocalEvent(ref ev);
            ev.FulfillmentEntity ??= station.Value;

            if (!ev.Handled)
            {
                ev.FulfillmentEntity = TryFulfillOrder((station.Value, stationData), order.Account, order, orderDatabase);

                if (ev.FulfillmentEntity == null)
                {
                    ConsolePopup(args.Actor, Loc.GetString("cargo-console-unfulfilled"));
                    PlayDenySound(uid, component);
                    return;
                }
            }

            order.Approved = true;
            _audio.PlayPvs(ApproveSound, uid);

            if (!_emag.CheckFlag(uid, EmagType.Interaction))
            {
                order.SetApproverData(_identity.GetIdentityShortInfo(player, uid));

                var message = Loc.GetString("cargo-console-unlock-approved-order-broadcast",
                    ("productName", product.Name),
                    ("orderAmount", order.OrderQuantity),
                    ("approver", order.Approver ?? string.Empty),
                    ("cost", cost));
                _radio.SendRadioMessage(uid, message, account.RadioChannel, uid, escapeMarkup: false);
                if (CargoOrderConsoleComponent.BaseAnnouncementChannel != account.RadioChannel)
                    _radio.SendRadioMessage(uid, message, CargoOrderConsoleComponent.BaseAnnouncementChannel, uid, escapeMarkup: false);
            }

            ConsolePopup(args.Actor, Loc.GetString("cargo-console-trade-station", ("destination", MetaData(ev.FulfillmentEntity.Value).EntityName)));

            // Log order approval
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(player):user} approved order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.Product}, requester:{order.Requester}, reason:{order.Reason}] on account {order.Account} with balance at {accountBalance}");

            accountOrders.Remove(order);
            UpdateBankAccount((station.Value, bank), -cost, order.Account);
            UpdateOrders(station.Value);
        }

        private EntityUid? TryFulfillOrder(Entity<StationDataComponent> stationData, ProtoId<CargoAccountPrototype> account, CargoOrderData order, StationCargoOrderDatabaseComponent orderDatabase)
        {
            // No slots at the trade station
            _listEnts.Clear();
            GetTradeStations(stationData, ref _listEnts);
            EntityUid? tradeDestination = null;

            // Try to fulfill from any station where possible, if the pad is not occupied.
            foreach (var trade in _listEnts)
            {
                var tradePads = GetCargoPallets(trade, BuySellType.Buy);
                _random.Shuffle(tradePads);

                var freePads = GetFreeCargoPallets(trade, tradePads);
                if (freePads.Count >= order.OrderQuantity) //check if the station has enough free pallets
                {
                    foreach (var pad in freePads)
                    {
                        var coordinates = new EntityCoordinates(trade, pad.Transform.LocalPosition);

                        if (FulfillOrder(order, account, coordinates, orderDatabase.PrinterOutput))
                        {
                            tradeDestination = trade;
                            order.NumDispatched++;
                            if (order.OrderQuantity <= order.NumDispatched) //Spawn a crate on free pellets until the order is fulfilled.
                                break;
                        }
                    }
                }

                if (tradeDestination != null)
                    break;
            }

            return tradeDestination;
        }

        private void GetTradeStations(StationDataComponent data, ref List<EntityUid> ents)
        {
            foreach (var gridUid in data.Grids)
            {
                if (!_tradeStationQuery.HasComponent(gridUid))
                    continue;

                ents.Add(gridUid);
            }
        }

        private void OnRemoveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleRemoveOrderMessage args)
        {
            var station = _station.GetOwningStation(uid);

            if (component.Mode != CargoOrderConsoleMode.DirectOrder)
                return;

            if (!TryGetOrderDatabase(station, out var orderDatabase))
                return;

            RemoveOrder(station.Value, component.Account, args.OrderId, orderDatabase);
        }

        private void OnAddOrderMessageSlipPrinter(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args, CargoProductPrototype product)
        {
            if (!_protoMan.Resolve(component.Account, out var account))
                return;

            if (Timing.CurTime < component.NextPrintTime)
                return;

            var label = Spawn(account.AcquisitionSlip, Transform(uid).Coordinates);
            component.NextPrintTime = Timing.CurTime + component.PrintDelay;
            _audio.PlayPvs(component.PrintSound, uid);

            var paper = EnsureComp<PaperComponent>(label);
            var msg = new FormattedMessage();
            var stationUid = _station.GetOwningStation(uid);
            var unitPrice = stationUid is { } stationValue
                ? GetEffectiveOrderUnitPrice(stationValue, component.Account, product.Cost)
                : product.Cost;
            var totalPrice = Math.Max(0, unitPrice * args.Amount);

            msg.AddMarkupPermissive(Loc.GetString("cargo-acquisition-slip-body",
                ("product", product.Name),
                ("description", product.Description),
                ("unit", unitPrice),
                ("amount", args.Amount),
                ("cost", totalPrice),
                ("orderer", args.Requester),
                ("reason", args.Reason)));
            _paperSystem.SetContent((label, paper), msg.ToMarkup());

            var slip = EnsureComp<CargoSlipComponent>(label);
            slip.Product = product.ID;
            slip.Requester = args.Requester;
            slip.Reason = args.Reason;
            slip.OrderQuantity = args.Amount;
            slip.Account = component.Account;
        }

        private void OnAddOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args)
        {
            if (args.Actor is not { Valid: true } player)
                return;

            if (args.Amount <= 0)
                return;

            var stationUid = _station.GetOwningStation(uid);

            if (!TryGetOrderDatabase(stationUid, out var orderDatabase))
                return;

            if (!TryComp<StationBankAccountComponent>(stationUid, out var bank))
                return;

            if (!_protoMan.TryIndex<CargoProductPrototype>(args.CargoProductId, out var product))
            {
                Log.Error($"Tried to add invalid cargo product {args.CargoProductId} as order!");
                return;
            }

            if (!GetAvailableProducts((uid, component)).Contains(args.CargoProductId))
                return;

            if (component.Mode == CargoOrderConsoleMode.PrintSlip)
            {
                OnAddOrderMessageSlipPrinter(uid, component, args, product);
                return;
            }

            var targetAccount = component.Mode == CargoOrderConsoleMode.SendToPrimary ? bank.PrimaryAccount : component.Account;

            if (!CanQueueOrderQuantity((stationUid.Value, orderDatabase), component.Account, args.Amount, out _))
            {
                ConsolePopup(player, Loc.GetString("cargo-console-too-many"));
                PlayDenySound(uid, component);
                return;
            }

            var unitPrice = GetEffectiveOrderUnitPrice(stationUid.Value, component.Account, product.Cost);
            var data = GetOrderData(args, product, GenerateOrderId(orderDatabase), component.Account, unitPrice);

            if (!TryAddOrder(stationUid.Value, targetAccount, data, orderDatabase))
            {
                PlayDenySound(uid, component);
                return;
            }

            // Log order addition
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"{ToPrettyString(player):user} added order [orderId:{data.OrderId}, quantity:{data.OrderQuantity}, product:{data.Product}, requester:{data.Requester}, reason:{data.Reason}]");

        }

        private void OnOrderUIOpened(EntityUid uid, CargoOrderConsoleComponent component, BoundUIOpenedEvent args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(uid, station);
        }

        #endregion

        private void UpdateOrderState(EntityUid consoleUid, EntityUid? station)
        {
            if (!TryComp<CargoOrderConsoleComponent>(consoleUid, out var console))
                return;

            if (!TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDatabase))
                return;

            var hasDeliveryEta = false;
            var deliveryEtaEndTime = TimeSpan.Zero;
            var deliveryDuration = TimeSpan.Zero;

            if (TryComp<CargoBatchOrderConsoleComponent>(consoleUid, out var batchConsole))
            {
                var effectiveDelaySeconds = GetEffectiveBatchDelaySeconds(station.Value, console.Account, batchConsole);
                deliveryDuration = TimeSpan.FromSeconds(effectiveDelaySeconds);
            }

            if (TryComp<StationCargoOrderBatchComponent>(station, out var batchState) &&
                batchState.ActiveBatches.TryGetValue(console.Account, out var activeBatch) &&
                activeBatch.DeliverAt > _timing.CurTime)
            {
                hasDeliveryEta = true;
                deliveryEtaEndTime = activeBatch.DeliverAt;
            }

            if (_uiSystem.HasUi(consoleUid, CargoConsoleUiKey.Orders))
            {
                _uiSystem.SetUiState(consoleUid,
                    CargoConsoleUiKey.Orders,
                    new CargoConsoleInterfaceState(
                    MetaData(station.Value).EntityName,
                    GetPendingOrderItemCount((station!.Value, orderDatabase), console.Account),
                    GetEffectiveOrderCapacity((station.Value, orderDatabase), console.Account),
                    GetNetEntity(station.Value),
                    hasDeliveryEta,
                    deliveryEtaEndTime,
                    deliveryDuration,
                    RelevantOrders((station!.Value, orderDatabase), (consoleUid, console)),
                    GetAvailableProducts((consoleUid, console))
                ));
            }
        }

        /// <summary>
        /// Gets orders relevant to this account, i.e. orders on the account directly or orders on behalf of the account in the primary account.
        /// </summary>
        private List<CargoOrderData> RelevantOrders(Entity<StationCargoOrderDatabaseComponent> station, Entity<CargoOrderConsoleComponent> console)
        {
            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                return [];

            station.Comp.Orders.TryGetValue(console.Comp.Account, out var ourOrders);

            if (console.Comp.Account == bank.PrimaryAccount)
                return ourOrders?.ToList() ?? [];

            if (!station.Comp.Orders.TryGetValue(bank.PrimaryAccount, out var primaryOrders))
                return ourOrders?.ToList() ?? [];

            if (ourOrders == null || ourOrders.Count == 0)
                return primaryOrders.Where(order => order.Account == console.Comp.Account).ToList();

            var otherOrders = primaryOrders.Where(order => order.Account == console.Comp.Account);

            return ourOrders.Concat(otherOrders).ToList();
        }

        private void ConsolePopup(EntityUid actor, string text)
        {
            _popup.PopupCursor(text, actor);
        }

        private void PlayDenySound(EntityUid uid, CargoOrderConsoleComponent component)
        {
            if (_timing.CurTime >= component.NextDenySoundTime)
            {
                component.NextDenySoundTime = _timing.CurTime + component.DenySoundDelay;
                _audio.PlayPvs(_audio.ResolveSound(component.ErrorSound), uid);
            }
        }

        private static CargoOrderData GetOrderData(
            CargoConsoleAddOrderMessage args,
            CargoProductPrototype cargoProduct,
            int id,
            ProtoId<CargoAccountPrototype> account,
            int unitPrice)
        {
            return new CargoOrderData(id, cargoProduct, args.Amount, args.Requester, args.Reason, account, unitPrice);
        }

        public int GetOutstandingOrderCount(Entity<StationCargoOrderDatabaseComponent> station, ProtoId<CargoAccountPrototype> account)
        {
            var amount = 0;

            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                return amount;

            if (station.Comp.Orders.TryGetValue(account, out var accountOrders))
            {
                foreach (var order in accountOrders)
                {
                    if (!order.Approved)
                        continue;

                    amount += Math.Max(0, order.OrderQuantity - order.NumDispatched);
                }
            }

            if (account == bank.PrimaryAccount)
                return amount;

            if (!station.Comp.Orders.TryGetValue(bank.PrimaryAccount, out var primaryOrders))
                return amount;

            foreach (var order in primaryOrders)
            {
                if (order.Account != account)
                    continue;

                if (!order.Approved)
                    continue;

                amount += Math.Max(0, order.OrderQuantity - order.NumDispatched);
            }

            return amount;
        }

        public int GetPendingOrderItemCount(Entity<StationCargoOrderDatabaseComponent> station, ProtoId<CargoAccountPrototype> account)
        {
            var amount = 0;

            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                return amount;

            if (station.Comp.Orders.TryGetValue(account, out var accountOrders))
            {
                foreach (var order in accountOrders)
                {
                    if (order.Approved)
                        continue;

                    amount += Math.Max(0, order.OrderQuantity);
                }
            }

            if (account == bank.PrimaryAccount)
                return amount;

            if (!station.Comp.Orders.TryGetValue(bank.PrimaryAccount, out var primaryOrders))
                return amount;

            foreach (var order in primaryOrders)
            {
                if (order.Account != account)
                    continue;

                if (order.Approved)
                    continue;

                amount += Math.Max(0, order.OrderQuantity);
            }

            return amount;
        }

        private bool CanQueueOrderQuantity(
            Entity<StationCargoOrderDatabaseComponent> station,
            ProtoId<CargoAccountPrototype> account,
            int requestedAmount,
            out int remainingCapacity)
        {
            var pending = GetPendingOrderItemCount(station, account);
            var capacity = GetEffectiveOrderCapacity(station, account);
            remainingCapacity = Math.Max(0, capacity - pending);
            return requestedAmount > 0 && requestedAmount <= remainingCapacity;
        }

        /// <summary>
        /// Sets logistics tier for a station account. Tier is clamped to [0..3].
        /// </summary>
        public void SetCargoLogisticsTier(EntityUid stationUid, ProtoId<CargoAccountPrototype> account, int tier)
        {
            var logistics = EnsureComp<CargoLogisticsTierComponent>(stationUid);
            logistics.AccountTiers[account] = Math.Clamp(tier, 0, 3);
            UpdateOrders(stationUid);
        }

        /// <summary>
        /// Sets external percentage logistics bonuses for a station account.
        /// Positive delivery speed bonus means lower ETA.
        /// Positive max-items bonus increases pending-capacity.
        /// Positive price discount lowers order unit price.
        /// </summary>
        public void SetCargoLogisticsExternalBonuses(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account,
            float deliverySpeedBonusPercent,
            float maxItemsBonusPercent,
            float priceDiscountPercent)
        {
            var logistics = EnsureComp<CargoLogisticsTierComponent>(stationUid);
            logistics.ExternalDeliverySpeedBonusPercent[account] = deliverySpeedBonusPercent;
            logistics.ExternalMaxItemsBonusPercent[account] = maxItemsBonusPercent;
            logistics.ExternalPriceDiscountPercent[account] = priceDiscountPercent;
            UpdateOrders(stationUid);
        }

        private int GetEffectiveOrderCapacity(
            Entity<StationCargoOrderDatabaseComponent> station,
            ProtoId<CargoAccountPrototype> account)
        {
            var baseCapacity = Math.Max(1, station.Comp.Capacity);
            if (!TryComp<CargoLogisticsTierComponent>(station, out var logistics))
                return baseCapacity;

            var tier = logistics.GetTier(account);
            var tierBonus = logistics.GetTierMaxItemsBonus(tier);
            var withTier = Math.Max(1, baseCapacity + tierBonus);
            var externalBonusPercent = logistics.GetExternalMaxItemsBonusPercent(account);
            var externalDelta = RoundToInt(withTier * externalBonusPercent / 100f);

            return Math.Max(1, withTier + externalDelta);
        }

        private int GetEffectiveBatchDelaySeconds(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account,
            CargoBatchOrderConsoleComponent batchConsole)
        {
            const int MinimumBatchDelaySeconds = 60;

            var baseDelay = Math.Max(MinimumBatchDelaySeconds, batchConsole.BatchDelaySeconds);
            if (!TryComp<CargoLogisticsTierComponent>(stationUid, out var logistics))
                return baseDelay;

            var tier = logistics.GetTier(account);
            var tierReduction = logistics.GetTierDeliveryReductionSeconds(tier);
            var tierAdjusted = Math.Max(MinimumBatchDelaySeconds, baseDelay - tierReduction);
            var speedBonusPercent = logistics.GetExternalDeliverySpeedBonusPercent(account);
            var speedMultiplier = Math.Clamp(1f - speedBonusPercent / 100f, 0.05f, 10f);
            var adjusted = RoundToInt(tierAdjusted * speedMultiplier);

            return Math.Max(MinimumBatchDelaySeconds, adjusted);
        }

        private int GetEffectiveOrderUnitPrice(
            EntityUid stationUid,
            ProtoId<CargoAccountPrototype> account,
            int basePrice)
        {
            var safeBasePrice = Math.Max(1, basePrice);
            if (!TryComp<CargoLogisticsTierComponent>(stationUid, out var logistics))
                return safeBasePrice;

            var discountPercent = logistics.GetExternalPriceDiscountPercent(account);
            var discountMultiplier = Math.Clamp(1f - discountPercent / 100f, 0.01f, 10f);
            var adjusted = RoundToInt(safeBasePrice * discountMultiplier);

            return Math.Max(1, adjusted);
        }

        private static int RoundToInt(float value)
        {
            return (int) Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Updates all of the cargo-related consoles for a particular station.
        /// This should be called whenever orders change.
        /// </summary>
        private void UpdateOrders(EntityUid dbUid)
        {
            // Order added so all consoles need updating.
            var orderQuery = AllEntityQuery<CargoOrderConsoleComponent>();

            while (orderQuery.MoveNext(out var uid, out var _))
            {
                var station = _station.GetOwningStation(uid);
                if (station != dbUid)
                    continue;

                UpdateOrderState(uid, station);
            }
        }

        public void RefreshOrderStateForStation(EntityUid stationUid)
        {
            UpdateOrders(stationUid);
        }

        public bool AddAndApproveOrder(
            EntityUid dbUid,
            CargoProductPrototype product,
            int qty,
            string sender,
            string description,
            string dest,
            StationCargoOrderDatabaseComponent component,
            ProtoId<CargoAccountPrototype> account,
            Entity<StationDataComponent> stationData
        )
        {
            // Make an order
            var id = GenerateOrderId(component);
            var order = new CargoOrderData(id, product, qty, sender, description, account);

            // Approve it now
            order.SetApproverData(dest, sender);
            order.Approved = true;

            // Log order addition
            _adminLogger.Add(LogType.Action,
                LogImpact.Low,
                $"AddAndApproveOrder {description} added order [orderId:{order.OrderId}, quantity:{order.OrderQuantity}, product:{order.Product}, requester:{order.Requester}, reason:{order.Reason}]");

            // Add it to the list
            return TryAddOrder(dbUid, account, order, component) && TryFulfillOrder(stationData, account, order, component).HasValue;
        }

        private static List<CargoOrderData> EnsureOrderList(StationCargoOrderDatabaseComponent orderDatabase, ProtoId<CargoAccountPrototype> account)
        {
            if (orderDatabase.Orders.TryGetValue(account, out var orders))
                return orders;

            orders = new List<CargoOrderData>();
            orderDatabase.Orders[account] = orders;
            return orders;
        }

        private bool TryAddOrder(EntityUid dbUid, ProtoId<CargoAccountPrototype> account, CargoOrderData data, StationCargoOrderDatabaseComponent component)
        {
            EnsureOrderList(component, account).Add(data);
            UpdateOrders(dbUid);
            return true;
        }

        private static int GenerateOrderId(StationCargoOrderDatabaseComponent orderDB)
        {
            // We need an arbitrary unique ID to identify orders, since they may
            // want to be cancelled later.
            return ++orderDB.NumOrdersCreated;
        }

        public void RemoveOrder(EntityUid dbUid, ProtoId<CargoAccountPrototype> account, int index, StationCargoOrderDatabaseComponent orderDB)
        {
            if (!orderDB.Orders.TryGetValue(account, out var orders))
            {
                UpdateOrders(dbUid);
                return;
            }

            var sequenceIdx = orders.FindIndex(order => order.OrderId == index);
            if (sequenceIdx != -1)
            {
                orders.RemoveAt(sequenceIdx);
            }
            UpdateOrders(dbUid);
        }

        public void ClearOrders(StationCargoOrderDatabaseComponent component)
        {
            if (component.Orders.Count == 0)
                return;

            component.Orders.Clear();
        }

        private static bool PopFrontOrder(StationCargoOrderDatabaseComponent orderDB, ProtoId<CargoAccountPrototype> account, [NotNullWhen(true)] out CargoOrderData? orderOut)
        {
            if (!orderDB.Orders.TryGetValue(account, out var orders))
            {
                orderOut = null;
                return false;
            }

            var orderIdx = orders.FindIndex(order => order.Approved);
            if (orderIdx == -1)
            {
                orderOut = null;
                return false;
            }

            orderOut = orders[orderIdx];
            orderOut.NumDispatched++;

            if (orderOut.NumDispatched >= orderOut.OrderQuantity)
            {
                // Order is complete. Remove from the queue.
                orders.RemoveAt(orderIdx);
            }
            return true;
        }

        /// <summary>
        /// Tries to fulfill the next outstanding order.
        /// </summary>
        [PublicAPI]
        private bool FulfillNextOrder(StationCargoOrderDatabaseComponent orderDB, ProtoId<CargoAccountPrototype> account, EntityCoordinates spawn, string? paperProto)
        {
            if (!PopFrontOrder(orderDB, account, out var order))
                return false;

            return FulfillOrder(order, account, spawn, paperProto);
        }

        /// <summary>
        /// Fulfills the specified cargo order and spawns paper attached to it.
        /// </summary>
        private bool FulfillOrder(CargoOrderData order, ProtoId<CargoAccountPrototype> account, EntityCoordinates spawn, string? paperProto)
        {
            if (!TrySpawnCargoProductEntity(order.Product, order.ProductId, spawn, out var product, out var item))
                return false;

            // Create a sheet of paper to write the order details on
            var printed = Spawn(paperProto, spawn);
            if (TryComp<PaperComponent>(printed, out var paper))
            {
                // fill in the order data
                var val = Loc.GetString("cargo-console-paper-print-name", ("orderNumber", order.OrderId));
                _metaSystem.SetEntityName(printed, val);

                var accountProto = _protoMan.Index(account);
                _paperSystem.SetContent((printed, paper),
                    Loc.GetString(
                        "cargo-console-paper-print-text",
                        ("orderNumber", order.OrderId),
                        ("itemName", product?.Name ?? order.ProductName),
                        ("orderQuantity", order.OrderQuantity),
                        ("requester", order.Requester),
                        ("reason", string.IsNullOrWhiteSpace(order.Reason) ? Loc.GetString("cargo-console-paper-reason-default") : order.Reason),
                        ("account", Loc.GetString(accountProto.Name)),
                        ("accountcode", Loc.GetString(accountProto.Code)),
                        ("approver", string.IsNullOrWhiteSpace(order.Approver) ? Loc.GetString("cargo-console-paper-approver-default") : order.Approver)));

                // attempt to attach the label to the item
                if (TryComp<PaperLabelComponent>(item.Value, out var label))
                {
                    _slots.TryInsert(item.Value, label.LabelSlot, printed, null);
                }
            }

            return true;

        }

        private bool TrySpawnCargoProductEntity(
            ProtoId<CargoProductPrototype> productId,
            string? fallbackProductId,
            EntityCoordinates spawn,
            out CargoProductPrototype? product,
            [NotNullWhen(true)] out EntityUid? entity)
        {
            product = null;
            entity = null;

            if (_protoMan.Resolve(productId, out product))
            {
                var item = Spawn(product.Product, spawn);
                var itemXform = Transform(item);
                _transformSystem.Unanchor(item, itemXform);

                if (product.Container is not { } productContainer)
                {
                    entity = item;
                    return true;
                }

                var containerEntity = Spawn(productContainer.Entity, itemXform.Coordinates);
                _transformSystem.SetLocalRotation(containerEntity, itemXform.LocalRotation);

                if (!_container.TryGetContainer(containerEntity, productContainer.ContainerId, out var productContainerStorage) ||
                    !_container.Insert(item, productContainerStorage, force: true))
                {
                    DebugTools.Assert(
                        $"Failed to insert cargo product into its specified container. This indicates an error in the cargo product definition's YAML as the product should be insertable into its container. {nameof(CargoProductPrototype)}: {(ProtoId<CargoProductPrototype>)productId.Id}");
                    QueueDel(item);
                    QueueDel(containerEntity);
                    return false;
                }

                entity = containerEntity;
                return true;
            }

            if (string.IsNullOrWhiteSpace(fallbackProductId) || !_protoMan.HasIndex<EntityPrototype>(fallbackProductId))
                return false;

            entity = Spawn(fallbackProductId, spawn);
            _transformSystem.Unanchor(entity.Value, Transform(entity.Value));
            return true;
        }

        public List<ProtoId<CargoProductPrototype>> GetAvailableProducts(Entity<CargoOrderConsoleComponent> ent)
        {
            if (_station.GetOwningStation(ent) is not { } station ||
                !TryComp<StationCargoOrderDatabaseComponent>(station, out var db))
            {
                return new List<ProtoId<CargoProductPrototype>>();
            }

            var products = new List<ProtoId<CargoProductPrototype>>();

            // Note that a market must be both on the station and on the console to be available.
            var markets = ent.Comp.AllowedGroups.Intersect(db.Markets).ToList();
            foreach (var product in _protoMan.EnumeratePrototypes<CargoProductPrototype>())
            {
                if (!markets.Contains(product.Group))
                    continue;

                products.Add(product.ID);
            }

            if (TryComp<WH40KCargoProductUnlocksComponent>(station, out var unlocks))
                products = FilterWh40KUnlockedProducts(products, station, ent.Comp.Account, unlocks);

            return products;
        }

        private List<ProtoId<CargoProductPrototype>> FilterWh40KUnlockedProducts(
            List<ProtoId<CargoProductPrototype>> products,
            EntityUid station,
            ProtoId<CargoAccountPrototype> account,
            WH40KCargoProductUnlocksComponent unlocks)
        {
            if (!unlocks.UnlockedProductsByAccount.TryGetValue(account, out var unlocked) || unlocked.Count == 0)
                return new List<ProtoId<CargoProductPrototype>>();

            var unlockedSet = unlocked.ToHashSet();
            var unlockedTechnologies = GetTeamUnlockedTechnologiesForAccount(station, account);

            return products
                .Where(product =>
                    unlockedSet.Contains(product) &&
                    MeetsWh40KResearchRequirement(account, product, unlocks, unlockedTechnologies))
                .ToList();
        }

        private static bool MeetsWh40KResearchRequirement(
            ProtoId<CargoAccountPrototype> account,
            ProtoId<CargoProductPrototype> product,
            WH40KCargoProductUnlocksComponent unlocks,
            HashSet<ProtoId<TechnologyPrototype>> unlockedTechnologies)
        {
            if (!unlocks.ResearchRequirementsByAccount.TryGetValue(account, out var requirementsByProduct) ||
                !requirementsByProduct.TryGetValue(product, out var requiredTechs) ||
                requiredTechs.Count == 0)
            {
                return true;
            }

            foreach (var tech in requiredTechs)
            {
                if (!unlockedTechnologies.Contains(tech))
                    return false;
            }

            return true;
        }

        private HashSet<ProtoId<TechnologyPrototype>> GetTeamUnlockedTechnologiesForAccount(
            EntityUid station,
            ProtoId<CargoAccountPrototype> account)
        {
            var result = new HashSet<ProtoId<TechnologyPrototype>>();
            if (!TryResolveWh40KTeamIdForAccount(account, out var teamId))
                return result;

            var query = EntityQueryEnumerator<TechnologyDatabaseComponent, WH40KResearchTeamComponent>();
            while (query.MoveNext(out _, out var database, out var researchTeam))
            {
                if (!string.Equals(researchTeam.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var technology in database.UnlockedTechnologies)
                {
                    result.Add(technology);
                }
            }

            return result;
        }

        private static bool TryResolveWh40KTeamIdForAccount(
            ProtoId<CargoAccountPrototype> account,
            [NotNullWhen(true)] out string? teamId)
        {
            teamId = null;

            if (string.Equals(account, "WH40KImperium", StringComparison.OrdinalIgnoreCase))
            {
                teamId = "Imperium";
                return true;
            }

            if (string.Equals(account, "WH40KHeretics", StringComparison.OrdinalIgnoreCase))
            {
                teamId = "Heretics";
                return true;
            }

            return false;
        }

        #region Station

        private bool TryGetOrderDatabase([NotNullWhen(true)] EntityUid? stationUid, [MaybeNullWhen(false)] out StationCargoOrderDatabaseComponent dbComp)
        {
            return TryComp(stationUid, out dbComp);
        }

        #endregion
    }
}
