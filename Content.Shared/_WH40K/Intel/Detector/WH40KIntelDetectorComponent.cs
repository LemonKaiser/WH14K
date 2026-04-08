using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.ViewVariables;

namespace Content.Shared._WH40K.Intel.Detector;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KIntelDetectorComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextScanAt;

    [DataField, AutoNetworkedField]
    public bool CanToggleRange = true;

    [DataField, AutoNetworkedField]
    public bool Short = true;

    [DataField, AutoNetworkedField]
    public int ShortRange = 7;

    [DataField, AutoNetworkedField]
    public int LongRange = 14;

    [DataField, AutoNetworkedField]
    public TimeSpan ShortRefresh = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public TimeSpan LongRefresh = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public bool DeactivateOnDrop = true;

    [DataField, AutoNetworkedField]
    public bool RequireTeam = true;

    [DataField, AutoNetworkedField]
    public List<string> AllowedTeamIds = new();

    [DataField, AutoNetworkedField]
    public List<WH40KIntelDetectorBlip> Blips { get; set; } = new();

    [DataField, AutoNetworkedField]
    public TimeSpan LastScan { get; set; }

    [DataField, AutoNetworkedField]
    public TimeSpan ScanDuration { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum interval between replicated detector state snapshots while scan result is stable.
    /// </summary>
    [DataField]
    public TimeSpan StateSyncInterval = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ScanSound =
        new SoundPathSpecifier("/Audio/_WH40K/Machines/scans.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ScanEmptySound =
        new SoundPathSpecifier("/Audio/_WH40K/Machines/scanning.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? ToggleSound =
        new SoundPathSpecifier("/Audio/Weapons/click.ogg");

    [DataField]
    public EntityUid? LastUser;

    /// <summary>
    /// Server-only next allowed network sync timestamp for detector state.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextStateSyncAt;
}

[Serializable, NetSerializable]
public enum WH40KIntelDetectorLayer : byte
{
    State = 0
}
