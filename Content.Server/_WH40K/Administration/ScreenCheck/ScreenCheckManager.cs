using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Shared._WH40K.Administration.ScreenCheck;
using Content.Shared.Database;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Administration.ScreenCheck;

public enum ScreenCheckStartResult : byte
{
    Success,
    AdminAlreadyHasPending,
    TargetAlreadyHasPending,
    TooManyPending
}

public readonly record struct ScreenCheckTargetSnapshot(
    bool HasActiveRequest,
    string? ActiveAdminName,
    DateTime ActiveSinceUtc,
    bool HasLastResult,
    string? LastAdminName,
    DateTime LastUpdatedUtc,
    ScreenCheckUiStatus LastStatus);

public sealed class ScreenCheckManager
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const int MaxConcurrentChecks = 16;

    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly EuiManager _euis = default!;
    [Dependency] private readonly ILogManager _logs = default!;
    [Dependency] private readonly IServerNetManager _net = default!;

    private readonly Dictionary<uint, PendingScreenCheck> _pendingChecks = new();
    private readonly Dictionary<NetUserId, uint> _pendingByAdmin = new();
    private readonly Dictionary<NetUserId, uint> _pendingByTarget = new();
    private readonly Dictionary<NetUserId, ScreenCheckTargetSnapshot> _targetSnapshots = new();
    private ISawmill _sawmill = default!;
    private uint _nextRequestId;

    private sealed record PendingScreenCheck(
        NetUserId AdminUserId,
        string AdminName,
        NetUserId TargetUserId,
        string TargetName,
        ScreenCheckEui Ui);

    public event Action<NetUserId>? TargetStateChanged;

    public void Initialize()
    {
        _sawmill = _logs.GetSawmill("screencheck");

        _net.RegisterNetMessage<MsgScreenCheckRequest>();
        _net.RegisterNetMessage<MsgScreenCheckResponse>(OnScreenCheckResponse);
        _net.Disconnect += OnDisconnect;
    }

    public ScreenCheckTargetSnapshot GetTargetSnapshot(NetUserId targetUserId)
    {
        return _targetSnapshots.GetValueOrDefault(targetUserId);
    }

    public ScreenCheckStartResult StartScreenCheck(ICommonSession admin, ICommonSession target)
    {
        if (_pendingChecks.Count >= MaxConcurrentChecks)
            return ScreenCheckStartResult.TooManyPending;

        if (_pendingByAdmin.ContainsKey(admin.UserId))
            return ScreenCheckStartResult.AdminAlreadyHasPending;

        if (_pendingByTarget.ContainsKey(target.UserId))
            return ScreenCheckStartResult.TargetAlreadyHasPending;

        var requestId = NextRequestId();
        var ui = new ScreenCheckEui(requestId, target.Name, this);

        _euis.OpenEui(ui, admin);
        ui.StateDirty();

        var pending = new PendingScreenCheck(admin.UserId, admin.Name, target.UserId, target.Name, ui);
        _pendingChecks[requestId] = pending;
        _pendingByAdmin[pending.AdminUserId] = requestId;
        _pendingByTarget[pending.TargetUserId] = requestId;
        MarkRequestStarted(pending);

        _net.ServerSendMessage(new MsgScreenCheckRequest { RequestId = requestId }, target.Channel);
        Timer.Spawn(RequestTimeout, () => OnTimeout(requestId));

        _adminLog.Add(
            LogType.Action,
            LogImpact.Medium,
            $"Screencheck requested by {admin:actor} for {target:target}.");
        _chat.SendAdminAnnouncement(
            Loc.GetString(
                "screen-check-admin-announcement-start",
                ("admin", admin.Name),
                ("player", target.Name)));

        return ScreenCheckStartResult.Success;
    }

    public void OnEuiClosed(uint requestId)
    {
        FinalizeRequest(requestId, ScreenCheckUiStatus.Cancelled);
    }

    private uint NextRequestId()
    {
        for (uint i = 0; i < uint.MaxValue; i++)
        {
            var next = unchecked(++_nextRequestId);
            if (next == 0)
                continue;

            if (!_pendingChecks.ContainsKey(next))
                return next;
        }

        throw new InvalidOperationException("No free screencheck request id is available.");
    }

    private bool TryFinalizePending(uint requestId, out PendingScreenCheck pending)
    {
        if (!_pendingChecks.Remove(requestId, out pending!))
            return false;

        _pendingByAdmin.Remove(pending.AdminUserId);
        _pendingByTarget.Remove(pending.TargetUserId);
        return true;
    }

    private void MarkRequestStarted(PendingScreenCheck pending)
    {
        var oldSnapshot = _targetSnapshots.GetValueOrDefault(pending.TargetUserId);
        var updated = new ScreenCheckTargetSnapshot(
            true,
            pending.AdminName,
            DateTime.UtcNow,
            oldSnapshot.HasLastResult,
            oldSnapshot.LastAdminName,
            oldSnapshot.LastUpdatedUtc,
            oldSnapshot.LastStatus);

        _targetSnapshots[pending.TargetUserId] = updated;
        TargetStateChanged?.Invoke(pending.TargetUserId);
    }

    private void MarkRequestFinished(PendingScreenCheck pending, ScreenCheckUiStatus status)
    {
        _targetSnapshots[pending.TargetUserId] = new ScreenCheckTargetSnapshot(
            false,
            null,
            default,
            true,
            pending.AdminName,
            DateTime.UtcNow,
            status);
        TargetStateChanged?.Invoke(pending.TargetUserId);
    }

    private void FinalizeRequest(uint requestId, ScreenCheckUiStatus status, byte[]? imageData = null)
    {
        if (!TryFinalizePending(requestId, out var pending))
            return;

        MarkRequestFinished(pending, status);

        var impact = status switch
        {
            ScreenCheckUiStatus.Success => LogImpact.Medium,
            ScreenCheckUiStatus.InvalidData => LogImpact.High,
            ScreenCheckUiStatus.CaptureFailed => LogImpact.Medium,
            ScreenCheckUiStatus.TimedOut => LogImpact.Low,
            ScreenCheckUiStatus.TargetDisconnected => LogImpact.Low,
            ScreenCheckUiStatus.Cancelled => LogImpact.Low,
            _ => LogImpact.Low
        };

        _adminLog.Add(
            LogType.Action,
            impact,
            $"Screencheck result {status} for {pending.TargetName} ({pending.TargetUserId}) by {pending.AdminName}.");

        _chat.SendAdminAnnouncement(
            Loc.GetString(
                "screen-check-admin-announcement-finish",
                ("admin", pending.AdminName),
                ("player", pending.TargetName),
                ("status", Loc.GetString(GetStatusLocKey(status)))));

        if (pending.Ui.IsShutDown)
            return;

        pending.Ui.SetState(status, imageData);
    }

    private static string GetStatusLocKey(ScreenCheckUiStatus status)
    {
        return status switch
        {
            ScreenCheckUiStatus.Pending => "screen-check-status-pending",
            ScreenCheckUiStatus.Success => "screen-check-status-success",
            ScreenCheckUiStatus.TimedOut => "screen-check-status-timeout",
            ScreenCheckUiStatus.TargetDisconnected => "screen-check-status-disconnected",
            ScreenCheckUiStatus.CaptureFailed => "screen-check-status-capture-failed",
            ScreenCheckUiStatus.InvalidData => "screen-check-status-invalid-data",
            ScreenCheckUiStatus.Cancelled => "screen-check-status-cancelled",
            _ => "screen-check-status-invalid-data"
        };
    }

    private void OnScreenCheckResponse(MsgScreenCheckResponse message)
    {
        if (!_pendingChecks.TryGetValue(message.RequestId, out var pending))
            return;

        if (pending.TargetUserId != message.MsgChannel.UserId)
        {
            _sawmill.Warning(
                "Received screencheck response for request {0} from unexpected user {1}.",
                message.RequestId,
                message.MsgChannel.UserId);
            return;
        }

        if (!message.Success)
        {
            FinalizeRequest(message.RequestId, ScreenCheckUiStatus.CaptureFailed);
            return;
        }

        if (!ScreenCheckImageValidator.IsValidEncodedJpeg(message.ImageData))
        {
            FinalizeRequest(message.RequestId, ScreenCheckUiStatus.InvalidData);
            return;
        }

        FinalizeRequest(message.RequestId, ScreenCheckUiStatus.Success, message.ImageData!);
    }

    private void OnTimeout(uint requestId)
    {
        FinalizeRequest(requestId, ScreenCheckUiStatus.TimedOut);
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        List<(uint RequestId, ScreenCheckUiStatus Status)>? completed = null;

        foreach (var (requestId, pending) in _pendingChecks)
        {
            if (pending.AdminUserId == args.Channel.UserId)
            {
                completed ??= new();
                completed.Add((requestId, ScreenCheckUiStatus.Cancelled));
                continue;
            }

            if (pending.TargetUserId == args.Channel.UserId)
            {
                completed ??= new();
                completed.Add((requestId, ScreenCheckUiStatus.TargetDisconnected));
            }
        }

        if (completed == null)
            return;

        foreach (var (requestId, status) in completed)
        {
            FinalizeRequest(requestId, status);
        }
    }
}
