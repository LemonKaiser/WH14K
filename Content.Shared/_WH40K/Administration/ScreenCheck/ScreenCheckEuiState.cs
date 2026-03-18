using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.ScreenCheck;

[Serializable, NetSerializable]
public enum ScreenCheckUiStatus : byte
{
    Pending,
    Success,
    TimedOut,
    TargetDisconnected,
    CaptureFailed,
    InvalidData,
    Cancelled,
}

[Serializable, NetSerializable]
public sealed partial class ScreenCheckEuiState(
    string targetName,
    ScreenCheckUiStatus status,
    byte[] imageData) : EuiStateBase
{
    public readonly string TargetName = targetName;
    public readonly ScreenCheckUiStatus Status = status;
    public readonly byte[] ImageData = imageData;
}
