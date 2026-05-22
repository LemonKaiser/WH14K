using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class CargoPalletConsoleInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// Estimated full untaxed value of all the entities on top of pallets on the same grid as the console.
    /// </summary>
    public int Appraisal;

    /// <summary>
    /// Actual payout after the sale tax has been applied.
    /// </summary>
    public int SaleValue;

    /// <summary>
    /// Number of entities on top of pallets on the same grid as the console.
    /// </summary>
    public int Count;

    /// <summary>
    /// are the buttons enabled
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// The percent of original value actually paid out by the console.
    /// </summary>
    public int SalePayoutPercent;

    public CargoPalletConsoleInterfaceState(
        int appraisal,
        int saleValue,
        int count,
        bool enabled,
        int salePayoutPercent = 50)
    {
        Appraisal = appraisal;
        SaleValue = saleValue;
        Count = count;
        Enabled = enabled;
        SalePayoutPercent = salePayoutPercent;
    }
}
