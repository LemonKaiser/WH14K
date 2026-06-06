using System;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Serializable, NetSerializable]
public sealed class WH40KMutePanelEuiState : EuiStateBase
{
    public string PlayerName { get; }
    public bool CanMute { get; }

    public WH40KMutePanelEuiState(string playerName, bool canMute)
    {
        PlayerName = playerName;
        CanMute = canMute;
    }
}

public static class WH40KMutePanelEuiStateMsg
{
    [Serializable, NetSerializable]
    public sealed class CreateMuteRequest : EuiMessageBase
    {
        public WH40KCreateMuteRequest Request { get; }

        public CreateMuteRequest(WH40KCreateMuteRequest request)
        {
            Request = request;
        }
    }

    [Serializable, NetSerializable]
    public sealed class GetPlayerInfoRequest : EuiMessageBase
    {
        public string PlayerUsername { get; }

        public GetPlayerInfoRequest(string playerUsername)
        {
            PlayerUsername = playerUsername;
        }
    }
}

[Serializable, NetSerializable]
public sealed record WH40KCreateMuteRequest(
    string? Target,
    WH40KMuteType Type,
    uint DurationMinutes,
    string Reason,
    bool Erase);
