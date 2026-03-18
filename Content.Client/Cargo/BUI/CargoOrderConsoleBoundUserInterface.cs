using Content.Shared.Cargo;
using Content.Client.Cargo.UI;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.IdentityManagement;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;
using System;
using System.Linq;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.Cargo.BUI
{
    public sealed class CargoOrderConsoleBoundUserInterface : BoundUserInterface
    {
        private readonly SharedCargoSystem _cargoSystem;

        [ViewVariables]
        private CargoConsoleMenu? _menu;

        /// <summary>
        /// This is the separate popup window for individual orders.
        /// </summary>
        [ViewVariables]
        private CargoConsoleOrderMenu? _orderMenu;

        [ViewVariables]
        public string? AccountName { get; private set; }

        [ViewVariables]
        public int BankBalance { get; private set; }

        [ViewVariables]
        public int OrderCapacity { get; private set; }

        [ViewVariables]
        public int OrderCount { get; private set; }

        /// <summary>
        /// Currently selected product
        /// </summary>
        [ViewVariables]
        private CargoProductPrototype? _product;
        private List<CargoOrderData> _currentOrders = new();
        private EntityUid? _lastStation;
        private bool _accountActionsInitialized;

        public CargoOrderConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _cargoSystem = EntMan.System<SharedCargoSystem>();
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

            _menu.OnClose += Close;

            _menu.OnItemSelected += (row) =>
            {
                if (row == null)
                    return;

                description.Clear();
                description.PushColor(Color.White); // Rich text default color is grey
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
            _menu.OnApproveAllRequests += ApproveAllOrders;
            _menu.OnCancelAllRequests += RemoveAllOrders;
            _menu.OnRemoveRequest += RemoveOrderById;
            _orderMenu.SubmitButton.OnPressed += (_) =>
            {
                if (AddOrder())
                {
                    _orderMenu.Close();
                }
            };

            _menu.OnAccountAction += (account, amount) =>
            {
                SendMessage(new CargoConsoleWithdrawFundsMessage(account, amount));
            };

            _menu.OnToggleUnboundedLimit += _ =>
            {
                SendMessage(new CargoConsoleToggleLimitMessage());
            };

            _menu.OpenCentered();
        }

        private void Populate(List<CargoOrderData> orders)
        {
            if (_menu == null)
                return;

            _menu.PopulateOrders(orders);
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not CargoConsoleInterfaceState cState || !EntMan.TryGetComponent<CargoOrderConsoleComponent>(Owner, out var orderConsole))
                return;
            var station = EntMan.GetEntity(cState.Station);

            OrderCapacity = cState.Capacity;
            OrderCount = cState.Count;
            BankBalance = _cargoSystem.GetBalanceFromAccount(station, orderConsole.Account);

            AccountName = cState.Name;

            if (_menu == null)
                return;

            _menu.UpdateOrderCapacity(OrderCount, OrderCapacity);
            _menu.UpdateDeliveryState(cState.HasDeliveryEta, cState.DeliveryEtaEndTime, cState.DeliveryDuration);

            var productsChanged = !_menu.ProductCatalogue.SequenceEqual(cState.Products);
            if (productsChanged)
            {
                _menu.ProductCatalogue = cState.Products;
                _menu.PopulateCategories();
                _menu.PopulateProducts();
            }

            _menu.UpdateStation(station);
            if (!_accountActionsInitialized || _lastStation != station)
            {
                _menu.PopulateAccountActions();
                _accountActionsInitialized = true;
                _lastStation = station;
            }

            _currentOrders = cState.Orders.ToList();
            Populate(cState.Orders);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            _menu?.Dispose();
            _orderMenu?.Dispose();
        }

        private bool AddOrder()
        {
            var orderAmt = _orderMenu?.Amount.Value ?? 0;
            var remaining = Math.Max(0, OrderCapacity - OrderCount);
            if (orderAmt < 1 || orderAmt > remaining)
            {
                return false;
            }

            SendMessage(new CargoConsoleAddOrderMessage(
                _orderMenu?.Requester.Text ?? "",
                string.Empty,
                _product?.ID ?? "",
                orderAmt));

            return true;
        }

        private void ApproveAllOrders()
        {
            var pendingOrder = _currentOrders.FirstOrDefault(order => !order.Approved);
            if (pendingOrder != null)
                SendMessage(new CargoConsoleApproveOrderMessage(pendingOrder.OrderId));
        }

        private void RemoveAllOrders()
        {
            foreach (var order in _currentOrders.Where(order => !order.Approved))
            {
                SendMessage(new CargoConsoleRemoveOrderMessage(order.OrderId));
            }
        }

        private void RemoveOrderById(int orderId)
        {
            SendMessage(new CargoConsoleRemoveOrderMessage(orderId));
        }
    }
}
