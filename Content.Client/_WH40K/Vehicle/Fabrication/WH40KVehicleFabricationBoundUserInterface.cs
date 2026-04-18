using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Cargo.UI;
using Content.Shared._WH40K.Vehicle.Fabrication;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Vehicle.Fabrication;

[UsedImplicitly]
public sealed class WH40KVehicleFabricationBoundUserInterface : BoundUserInterface
{
    private CargoConsoleMenu? _menu;
    private CargoConsoleOrderMenu? _orderMenu;
    private CargoProductPrototype? _product;
    private List<CargoOrderData> _currentOrders = new();
    private EntityUid? _lastStation;
    private bool _accountActionsInitialized;

    public int OrderCapacity { get; private set; }
    public int OrderCount { get; private set; }

    public WH40KVehicleFabricationBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var spriteSystem = EntMan.System<SpriteSystem>();
        var dependencies = IoCManager.Instance!;
        _menu = new CargoConsoleMenu(Owner, EntMan, dependencies.Resolve<IPrototypeManager>(), spriteSystem);
        var localPlayer = dependencies.Resolve<IPlayerManager>().LocalEntity;
        var description = new FormattedMessage();

        string orderRequester;

        if (EntMan.EntityExists(localPlayer))
            orderRequester = Identity.Name(localPlayer.Value, EntMan);
        else
            orderRequester = string.Empty;

        _orderMenu = new CargoConsoleOrderMenu();
        if (EntMan.TryGetComponent<CargoOrderConsoleComponent>(Owner, out var orderConsole))
        {
            var theme = WH40KCargoConsoleStyles.ResolveTheme(orderConsole.Account);
            if (theme.Enabled)
                _orderMenu.ApplyWh40KTheme(theme);
        }

        _menu.OnClose += Close;

        _menu.OnItemSelected += row =>
        {
            if (row == null)
                return;

            description.Clear();
            description.PushColor(Color.White);
            if (row.MainButton.ToolTip != null)
                description.AddText(row.MainButton.ToolTip);

            _orderMenu.Description.SetMessage(description);
            _product = row.Product;
            _orderMenu.ProductName.Text = row.ProductName.Text;
            _orderMenu.PointCost.Text = row.PointCost.Text;
            _orderMenu.Requester.Text = orderRequester;
            _orderMenu.Amount.Value = 1;
            _orderMenu.OpenCentered();
        };

        _menu.OnCancelAllRequests += RemoveAllOrders;
        _menu.OnRemoveRequest += RemoveOrderById;
        _orderMenu.SubmitButton.OnPressed += _ =>
        {
            if (AddOrder())
                _orderMenu.Close();
        };

        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WH40KVehicleFabricationBuiState cast ||
            !EntMan.TryGetComponent<CargoOrderConsoleComponent>(Owner, out var orderConsole))
        {
            return;
        }

        var station = cast.Station != NetEntity.Invalid
            ? EntMan.GetEntity(cast.Station)
            : (EntityUid?) null;

        OrderCapacity = cast.Capacity;
        OrderCount = cast.Count;

        if (_menu == null)
            return;

        _menu.UpdateOrderCapacity(OrderCount, OrderCapacity);
        _menu.UpdateDeliveryState(cast.HasDeliveryEta, cast.DeliveryEtaEndTime, cast.DeliveryDuration);

        var productsChanged = !_menu.ProductCatalogue.SequenceEqual(cast.Products);
        if (productsChanged)
        {
            _menu.ProductCatalogue = cast.Products;
            _menu.PopulateCategories();
            _menu.PopulateProducts();
        }

        if (station != null)
        {
            _menu.UpdateStation(station.Value);
            if (!_accountActionsInitialized || _lastStation != station)
            {
                _menu.PopulateAccountActions();
                _accountActionsInitialized = true;
                _lastStation = station;
            }
        }

        _currentOrders = cast.Orders.ToList();
        _menu.PopulateOrders(cast.Orders);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _menu?.Orphan();
        _orderMenu?.Orphan();
    }

    private bool AddOrder()
    {
        var orderAmt = _orderMenu?.Amount.Value ?? 0;
        var remaining = Math.Max(0, OrderCapacity - OrderCount);
        if (orderAmt < 1 || orderAmt > remaining)
            return false;

        SendMessage(new WH40KVehicleFabricationAddOrderMessage(
            _orderMenu?.Requester.Text ?? string.Empty,
            _product?.ID ?? string.Empty,
            orderAmt));

        return true;
    }

    private void RemoveAllOrders()
    {
        foreach (var order in _currentOrders.Where(order => !order.Approved))
        {
            SendMessage(new WH40KVehicleFabricationRemoveOrderMessage(order.OrderId));
        }
    }

    private void RemoveOrderById(int orderId)
    {
        SendMessage(new WH40KVehicleFabricationRemoveOrderMessage(orderId));
    }
}
