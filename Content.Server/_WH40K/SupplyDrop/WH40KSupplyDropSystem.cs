using System;
using System.Collections.Generic;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Store.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Popups;
using Content.Server.Storage.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._WH40K.SupplyDrop;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Store;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Vector2 = System.Numerics.Vector2;

namespace Content.Server._WH40K.SupplyDrop;

public sealed class WH40KSupplyDropSystem : SharedWH40KSupplyDropSystem
{
    private const float DropVisualStartOffsetY = 18f;
    private const int MaxListingDropAmount = 50;

    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private readonly List<PendingDrop> _pendingDrops = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<WH40KSupplyDropPadComponent>(WH40KSupplyDropUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WH40KSupplyDropLaunchMessage>(OnLaunchPressed);
        });

        Subs.BuiEvents<WH40KVoxSupplyDropStoreComponent>(StoreUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnVoxUiOpened);
            subs.Event<StoreRequestUpdateInterfaceMessage>(OnVoxRequestUpdate);
            subs.Event<StoreBuyListingMessage>(OnVoxBuyPressed);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        for (var i = _pendingDrops.Count - 1; i >= 0; i--)
        {
            var drop = _pendingDrops[i];
            if (drop.Visual is { } visual && !TerminatingOrDeleted(visual))
                UpdateDropVisual(visual, drop, now);

            if (drop.DropAt > now)
                continue;

            if (drop.Visual is { } dropVisual && !TerminatingOrDeleted(dropVisual))
                QueueDel(dropVisual);

            ResolveDropAtTouchdown(drop);
            _pendingDrops.RemoveAt(i);
        }

        var query = EntityQueryEnumerator<WH40KSupplyDropPadComponent>();
        while (query.MoveNext(out var uid, out var pad))
        {
            if (!_ui.IsUiOpen(uid, WH40KSupplyDropUiKey.Key))
                continue;

            if (pad.NextUiRefresh > now)
                continue;

            pad.NextUiRefresh = now + TimeSpan.FromSeconds(1);
            UpdateUi((uid, pad));
        }

        var voxQuery = EntityQueryEnumerator<WH40KVoxSupplyDropStoreComponent>();
        while (voxQuery.MoveNext(out var uid, out var store))
        {
            if (!_ui.IsUiOpen(uid, StoreUiKey.Key))
                continue;

            if (store.NextUiRefresh > now)
                continue;

            store.NextUiRefresh = now + TimeSpan.FromSeconds(1);
            UpdateVoxUi((uid, store));
        }
    }

    private void OnUiOpened(Entity<WH40KSupplyDropPadComponent> ent, ref BoundUIOpenedEvent args)
    {
        var teamId = ResolvePadTeamId(ent);
        if (!IsUserAllowedForTeam(args.Actor, teamId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            _ui.CloseUi(ent.Owner, WH40KSupplyDropUiKey.Key, args.Actor);
            return;
        }

        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateUi(ent);
    }

    private void OnLaunchPressed(Entity<WH40KSupplyDropPadComponent> ent, ref WH40KSupplyDropLaunchMessage args)
    {
        var teamId = ResolvePadTeamId(ent);
        if (!IsUserAllowedForTeam(args.Actor, teamId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        var now = _timing.CurTime;
        if (now < ent.Comp.NextLaunchAt)
        {
            var remaining = (int) Math.Ceiling((ent.Comp.NextLaunchAt - now).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-cooldown", ("seconds", remaining)), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        if (!TryGetFactionBank(ent.Owner, ent.Comp.Account, out var bank, out var balance))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-bank-missing"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        var cost = Math.Max(0, ent.Comp.Cost);
        if (balance < cost)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-supplydrop-popup-insufficient-funds", ("cost", cost), ("balance", balance)),
                ent.Owner,
                args.Actor);
            UpdateUi(ent);
            return;
        }

        var target = _transform.GetMapCoordinates(args.Actor);
        if (target.MapId == MapId.Nullspace)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-invalid-target"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        if (!_cargo.TryAdjustBankAccount(bank, ent.Comp.Account, -cost))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-bank-missing"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        var delaySeconds = Math.Max(0.1f, ent.Comp.DropDelaySeconds);
        if (!TrySchedulePendingDrop(now, args.Actor, target, ent.Comp.CratePrototype, ent.Comp.MarkerPrototype, delaySeconds))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vox-supplydrop-popup-listing-unavailable"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        ent.Comp.LastLaunchAt = now;
        ent.Comp.NextLaunchAt = now + TimeSpan.FromSeconds(Math.Max(1f, ent.Comp.CooldownSeconds));
        ent.Comp.NextUiRefresh = TimeSpan.Zero;

        _popup.PopupEntity(
            Loc.GetString("wh40k-supplydrop-popup-launched", ("seconds", (int) Math.Ceiling(delaySeconds))),
            ent.Owner,
            args.Actor);

        UpdateUi(ent);
    }

    private void OnVoxUiOpened(Entity<WH40KVoxSupplyDropStoreComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            _ui.CloseUi(ent.Owner, StoreUiKey.Key, args.Actor);
            return;
        }

        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateVoxUi(ent);
    }

    private void OnVoxRequestUpdate(Entity<WH40KVoxSupplyDropStoreComponent> ent, ref StoreRequestUpdateInterfaceMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
            return;

        UpdateVoxUi(ent);
    }

    private void OnVoxBuyPressed(Entity<WH40KVoxSupplyDropStoreComponent> ent, ref StoreBuyListingMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        var now = _timing.CurTime;
        if (now < ent.Comp.NextLaunchAt)
        {
            var remaining = (int) Math.Ceiling((ent.Comp.NextLaunchAt - now).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-cooldown", ("seconds", remaining)), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (!TryResolveVoxListing(ent, args.Listing, out var listing))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vox-supplydrop-popup-listing-unavailable"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (!TryGetVoxListingCost(listing, ent.Comp.FundsCurrency, out var cost))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vox-supplydrop-popup-unsupported-currency"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (!TryGetFactionBank(ent.Owner, ent.Comp.Account, out var bank, out var balance))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-bank-missing"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (balance < cost)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-supplydrop-popup-insufficient-funds", ("cost", cost), ("balance", balance)),
                ent.Owner,
                args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        var target = _transform.GetMapCoordinates(args.Actor);
        if (target.MapId == MapId.Nullspace)
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-invalid-target"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (!IsOpenSkyTile(target))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vox-supplydrop-popup-open-sky-required"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        if (!_cargo.TryAdjustBankAccount(bank, ent.Comp.Account, -cost))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-supplydrop-popup-bank-missing"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        var delaySeconds = Math.Max(0.1f, ent.Comp.DropDelaySeconds);
        var dropAmount = ResolveListingDropAmount(ent.Comp, args.Listing);
        if (!TrySchedulePendingDrop(
                now,
                args.Actor,
                target,
                listing.ProductEntity!.Value,
                ent.Comp.MarkerPrototype,
                delaySeconds,
                ent.Comp.DeliveryCratePrototype,
                dropAmount))
        {
            _popup.PopupEntity(Loc.GetString("wh40k-vox-supplydrop-popup-listing-unavailable"), ent.Owner, args.Actor);
            UpdateVoxUi(ent);
            return;
        }

        ent.Comp.NextLaunchAt = now + TimeSpan.FromSeconds(Math.Max(1f, ent.Comp.CooldownSeconds));
        ent.Comp.NextUiRefresh = TimeSpan.Zero;

        _popup.PopupEntity(
            Loc.GetString("wh40k-supplydrop-popup-launched", ("seconds", (int) Math.Ceiling(delaySeconds))),
            ent.Owner,
            args.Actor);

        UpdateVoxUi(ent);
    }

    private void UpdateUi(Entity<WH40KSupplyDropPadComponent> ent)
    {
        var teamId = ResolvePadTeamId(ent);
        var teamName = teamId;
        if (!string.IsNullOrWhiteSpace(teamId) &&
            _teamRule.TryGetTeamDisplayName(teamId, out var displayName))
        {
            teamName = displayName;
        }

        var balance = 0;
        if (TryGetFactionBank(ent.Owner, ent.Comp.Account, out _, out var bankBalance))
            balance = bankBalance;

        var state = new WH40KSupplyDropBuiState(
            teamName,
            ent.Comp.Account.ToString(),
            balance,
            Math.Max(0, ent.Comp.Cost),
            (int) Math.Ceiling(Math.Max(0f, ent.Comp.DropDelaySeconds)),
            ent.Comp.LastLaunchAt,
            ent.Comp.NextLaunchAt);

        _ui.SetUiState(ent.Owner, WH40KSupplyDropUiKey.Key, state);
    }

    private void UpdateVoxUi(Entity<WH40KVoxSupplyDropStoreComponent> ent)
    {
        var listings = new HashSet<ListingDataWithCostModifiers>();
        var listingDropAmounts = new Dictionary<ProtoId<ListingPrototype>, int>();
        foreach (var listingId in ent.Comp.Listings)
        {
            if (!_proto.TryIndex<ListingPrototype>(listingId, out var listingProto))
                continue;

            var listing = new ListingDataWithCostModifiers(listingProto);
            if (listing.ProductEntity == null || !_proto.HasIndex(listing.ProductEntity.Value))
                continue;

            listings.Add(listing);
            listingDropAmounts[listingId] = ResolveListingDropAmount(ent.Comp, listingId);
        }

        var balance = 0;
        if (TryGetFactionBank(ent.Owner, ent.Comp.Account, out _, out var bankBalance))
            balance = bankBalance;

        var allCurrency = new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>();
        foreach (var listing in listings)
        {
            foreach (var currency in listing.Cost.Keys)
            {
                allCurrency.TryAdd(currency, FixedPoint2.Zero);
            }
        }

        allCurrency[ent.Comp.FundsCurrency] = Math.Max(0, balance);

        var state = new WH40KVoxStoreUpdateState(listings, allCurrency, ent.Comp.NextLaunchAt, listingDropAmounts);
        _ui.SetUiState(ent.Owner, StoreUiKey.Key, state);
    }

    private bool TryResolveVoxListing(
        Entity<WH40KVoxSupplyDropStoreComponent> ent,
        ProtoId<ListingPrototype> listingId,
        out ListingDataWithCostModifiers listing)
    {
        listing = null!;

        if (!ent.Comp.Listings.Contains(listingId))
            return false;

        if (!_proto.TryIndex<ListingPrototype>(listingId, out var listingProto))
            return false;

        var listingData = new ListingDataWithCostModifiers(listingProto);
        if (listingData.ProductEntity == null || !_proto.HasIndex(listingData.ProductEntity.Value))
            return false;

        listing = listingData;
        return true;
    }

    private static bool TryGetVoxListingCost(
        ListingDataWithCostModifiers listing,
        ProtoId<CurrencyPrototype> fundsCurrency,
        out int cost)
    {
        cost = 0;

        if (!listing.Cost.TryGetValue(fundsCurrency, out var fundsValue))
            return false;

        // Bank accounts operate in integer units; round up any fractional listing values.
        cost = Math.Max(0, (int) Math.Ceiling(fundsValue.Double()));
        return true;
    }

    private static int ResolveListingDropAmount(WH40KVoxSupplyDropStoreComponent store, ProtoId<ListingPrototype> listingId)
    {
        if (!store.ListingDropAmounts.TryGetValue(listingId, out var amount))
            return 1;

        return Math.Clamp(amount, 1, MaxListingDropAmount);
    }

    private bool TrySchedulePendingDrop(
        TimeSpan now,
        EntityUid requester,
        MapCoordinates target,
        EntProtoId payloadPrototype,
        EntProtoId? markerPrototype,
        float delaySeconds,
        EntProtoId? deliveryCratePrototype = null,
        int payloadAmount = 1)
    {
        if (!_proto.HasIndex(payloadPrototype))
            return false;

        if (deliveryCratePrototype is { } cratePrototype && !_proto.HasIndex(cratePrototype))
            return false;

        var delay = TimeSpan.FromSeconds(Math.Max(0.1f, delaySeconds));
        var dropAt = now + delay;
        var clampedPayloadAmount = Math.Clamp(payloadAmount, 1, MaxListingDropAmount);
        var visualStart = ResolveVisualStart(target, requester);

        EntityUid? visual = null;
        if (markerPrototype is { } markerProto && _proto.HasIndex(markerProto))
            visual = Spawn(markerProto, visualStart);

        _pendingDrops.Add(
            new PendingDrop(
                target,
                visualStart,
                payloadPrototype,
                deliveryCratePrototype,
                clampedPayloadAmount,
                now,
                dropAt,
                visual));

        return true;
    }

    /// <summary>
    /// Builds the visual start position in requester's parent-local space to keep
    /// trajectory consistent on rotated grids, then converts back to map coordinates.
    /// </summary>
    private MapCoordinates ResolveVisualStart(MapCoordinates target, EntityUid requester)
    {
        var requesterXform = Transform(requester);
        var parent = requesterXform.ParentUid;
        if (!parent.IsValid())
            return new MapCoordinates(target.Position + new Vector2(0f, DropVisualStartOffsetY), target.MapId);

        var targetRelative = _transform.ToCoordinates(parent, target);
        var visualStartRelative = targetRelative.Offset(new Vector2(0f, DropVisualStartOffsetY));
        return _transform.ToMapCoordinates(visualStartRelative);
    }

    private void ResolveDropAtTouchdown(PendingDrop drop)
    {
        var payloadAmount = Math.Max(1, drop.PayloadAmount);

        if (drop.DeliveryCratePrototype is not { } cratePrototype)
        {
            for (var i = 0; i < payloadAmount; i++)
            {
                Spawn(drop.PayloadPrototype, drop.Target);
            }

            return;
        }

        var crate = Spawn(cratePrototype, drop.Target);
        for (var i = 0; i < payloadAmount; i++)
        {
            var payload = Spawn(drop.PayloadPrototype, drop.Target);
            if (!_entityStorage.Insert(payload, crate))
                _transform.SetMapCoordinates(payload, drop.Target);
        }
    }

    private void UpdateDropVisual(EntityUid visual, PendingDrop drop, TimeSpan now)
    {
        var totalSeconds = (drop.DropAt - drop.StartAt).TotalSeconds;
        if (totalSeconds <= 0)
        {
            _transform.SetMapCoordinates(visual, drop.Target);
            return;
        }

        var elapsedSeconds = (now - drop.StartAt).TotalSeconds;
        var progress = Math.Clamp((float) (elapsedSeconds / totalSeconds), 0f, 1f);
        var position = drop.VisualStart.Position + (drop.Target.Position - drop.VisualStart.Position) * progress;

        _transform.SetMapCoordinates(visual, new MapCoordinates(position, drop.Target.MapId));
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
            !TryComp<StationBankAccountComponent>(stationUid, out StationBankAccountComponent? bankComponent) ||
            bankComponent == null)
        {
            return false;
        }

        if (!_cargo.TryGetAccount((stationUid, bankComponent), account, out balance))
            return false;

        bank = (stationUid, bankComponent);
        return true;
    }

    private string ResolvePadTeamId(Entity<WH40KSupplyDropPadComponent> ent)
    {
        if (!string.IsNullOrWhiteSpace(ent.Comp.TeamId))
            return ent.Comp.TeamId;

        if (TryComp<WH40KStoreTeamComponent>(ent.Owner, out var storeTeam) &&
            !string.IsNullOrWhiteSpace(storeTeam.TeamId))
        {
            return storeTeam.TeamId;
        }

        return string.Empty;
    }

    private bool IsUserAllowedForTeam(EntityUid user, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return true;

        if (TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract)
            return true;

        if (_teamRule.TryGetTeamIdFromEntity(user, out var directTeamId) &&
            string.Equals(directTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryComp<MindComponent>(user, out var mind))
            return false;

        if (mind.CurrentEntity is { } currentEntity)
        {
            if (TryComp<GhostComponent>(currentEntity, out var currentGhost) && currentGhost.CanGhostInteract)
                return true;

            if (_teamRule.TryGetTeamIdFromEntity(currentEntity, out var currentTeamId) &&
                string.Equals(currentTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (mind.UserId is not { } userId)
            return false;

        return _teamRule.TryGetRememberedTeam(userId, out var rememberedTeamId) &&
               string.Equals(rememberedTeamId, teamId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsOpenSkyTile(MapCoordinates target)
    {
        if (!_mapManager.TryFindGridAt(target, out var gridUid, out var grid))
            return true;

        var tileIndices = _map.WorldToTile(gridUid, grid, target.Position);
        return !IsRoovedTile(gridUid, grid, tileIndices);
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
        if (HasComp<ImplicitRoofComponent>(gridUid))
            return true;

        if (!TryComp<RoofComponent>(gridUid, out var roofComp))
            return false;

        return _roof.IsRooved((gridUid, grid, roofComp), tileIndices);
    }

    private sealed record PendingDrop(
        MapCoordinates Target,
        MapCoordinates VisualStart,
        EntProtoId PayloadPrototype,
        EntProtoId? DeliveryCratePrototype,
        int PayloadAmount,
        TimeSpan StartAt,
        TimeSpan DropAt,
        EntityUid? Visual);
}
