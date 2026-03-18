using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System.Text;
namespace Content.Shared.Cargo
{
    [DataDefinition, NetSerializable, Serializable]
    public sealed partial class CargoOrderData
    {
        /// <summary>
        /// Unit price captured when the order was created.
        /// </summary>
        [DataField]
        public int Price;

        /// <summary>
        /// A unique (arbitrary) ID which identifies this order.
        /// </summary>
        [DataField]
        public int OrderId { get; private set; }

        /// <summary>
        /// The cargo product ordered.
        /// </summary>
        [DataField]
        public ProtoId<CargoProductPrototype> Product;

        /// <summary>
        /// The spawned entity prototype for the order item, captured for batch delivery
        /// and for display fallback when the cargo product cannot be resolved.
        /// </summary>
        [DataField]
        public string ProductId { get; private set; } = string.Empty;

        /// <summary>
        /// Snapshot of the localized product name for display-only fallback rows.
        /// </summary>
        [DataField]
        public string ProductName { get; private set; } = string.Empty;

        /// <summary>
        /// The number of items in the order. Not readonly, as it might change
        /// due to caps on the amount of orders that can be placed.
        /// </summary>
        [DataField]
        public int OrderQuantity;

        /// <summary>
        /// How many instances of this order that we've already dispatched
        /// </summary>
        [DataField]
        public int NumDispatched = 0;

        /// <summary>
        /// Marks synthetic WH40K batch summary rows so clients do not have to
        /// infer them from localized text.
        /// </summary>
        [DataField]
        public bool IsBatchSummary;

        [DataField]
        public string Requester { get; private set; }
        // public String RequesterRank; // TODO Figure out how to get Character ID card data
        // public int RequesterId;
        [DataField]
        public string Reason { get; private set; }
        public  bool Approved;
        [DataField]
        public string? Approver;

        /// <summary>
        /// Which account to deduct funds from when ordering
        /// </summary>
        [DataField]
        public ProtoId<CargoAccountPrototype> Account;

        public CargoOrderData(int orderId, ProtoId<CargoProductPrototype> product, int amount, string requester, string reason, ProtoId<CargoAccountPrototype> account)
        {
            OrderId = orderId;
            Product = product;
            OrderQuantity = amount;
            Requester = requester;
            Reason = reason;
            Account = account;
        }

        public CargoOrderData(
            int orderId,
            CargoProductPrototype product,
            int amount,
            string requester,
            string reason,
            ProtoId<CargoAccountPrototype> account,
            int? price = null)
            : this(orderId, product.ID, amount, requester, reason, account)
        {
            ProductId = product.Product;
            ProductName = product.Name;
            Price = price ?? product.Cost;
        }

        public CargoOrderData(
            int orderId,
            string productId,
            string productName,
            int price,
            int amount,
            string requester,
            string reason,
            ProtoId<CargoAccountPrototype> account)
            : this(orderId, string.Empty, amount, requester, reason, account)
        {
            ProductId = productId;
            ProductName = productName;
            Price = price;
        }

        public void SetApproverData(string? approver)
        {
            Approver = approver;
        }

        public void SetApproverData(string? fullName, string? jobTitle)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                sb.Append($"{fullName} ");
            }
            if (!string.IsNullOrWhiteSpace(jobTitle))
            {
                sb.Append($"({jobTitle})");
            }
            Approver = sb.ToString();
        }
    }
}
