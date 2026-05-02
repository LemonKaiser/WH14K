using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.NPC.Prototypes;
using Content.Shared._WH40K.Notifications;
using Content.Shared.Roles;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Cinematic;

[DataDefinition]
public sealed partial class WH40KCinematicActionDefinition
{
    [DataField("id")]
    public string? Id;

    [DataField("targetActionId")]
    public string? TargetActionId;

    [DataField("targetActionIds")]
    public List<string>? TargetActionIds;

    [DataField("type", required: true)]
    public WH40KCinematicActionType Type;

    [DataField("blocking")]
    public bool Blocking;

    [DataField("persistAfterCinematic")]
    public bool PersistAfterCinematic;

    [DataField("optionalAnchor")]
    public bool OptionalAnchor = true;

    [DataField("anchorId")]
    public string? AnchorId;

    [DataField("contextId")]
    public string? ContextId;

    [DataField("prototype")]
    public EntProtoId? Prototype;

    [DataField("startingGearId")]
    public ProtoId<StartingGearPrototype>? StartingGearId;

    [DataField("npcFactionId")]
    public ProtoId<NpcFactionPrototype>? NpcFactionId;

    [DataField("flowId")]
    public string? FlowId;

    [DataField("width")]
    public int? Width;

    [DataField("widthShape")]
    public WH40KCinematicLavaWidthShape? WidthShape;

    [DataField("obstacleMode")]
    public WH40KCinematicLavaObstacleMode? ObstacleMode;

    [DataField("preserveExistingFloor")]
    public bool? PreserveExistingFloor;

    [DataField("floorTile")]
    public ProtoId<ContentTileDefinition>? FloorTile;

    [DataField("lavaPrototype")]
    public EntProtoId? LavaPrototype;

    [DataField("advanceInterval")]
    public float? AdvanceIntervalSeconds;

    [DataField("tilesPerAdvance")]
    public int? TilesPerAdvance;

    [DataField("sound")]
    public SoundSpecifier? Sound;

    [DataField("audio")]
    public AudioParams? Audio;

    [DataField("deliveryScope")]
    public WH40KCinematicSoundDeliveryScope DeliveryScope = WH40KCinematicSoundDeliveryScope.Audience;

    [DataField("radius")]
    public float? Radius;

    [DataField("damage")]
    public DamageSpecifier? Damage;

    [DataField("entitySetId")]
    public string? EntitySetId;

    [DataField("npcId")]
    public string? NpcId;

    [DataField("targetNpcId")]
    public string? TargetNpcId;

    [DataField("trackId")]
    public ProtoId<WH40KCinematicActorTrackPrototype>? TrackId;

    [DataField("trackSegmentId")]
    public string? TrackSegmentId;

    [DataField("anchorIds")]
    public List<string> AnchorIds = new();

    [DataField("slot")]
    public string? Slot;

    [DataField("offset")]
    public Vector2? Offset;

    [DataField("facingDirection")]
    public Vector2? FacingDirection;

    [DataField("searchRadius")]
    public float SearchRadius = 1.25f;

    [DataField("htnEnabled")]
    public bool? HtnEnabled;

    [DataField("restoreActorState")]
    public bool RestoreActorState = true;

    [DataField("ignoreStateMismatch")]
    public bool IgnoreStateMismatch;

    [DataField("allowRecoveryTeleport")]
    public bool AllowRecoveryTeleport = true;

    [DataField("reuseExistingEntity")]
    public bool ReuseExistingEntity;

    [DataField("signal")]
    public string? Signal;

    [DataField("sceneMapPath")]
    public ResPath? SceneMapPath;

    [DataField("sceneGridPath")]
    public ResPath? SceneGridPath;

    [DataField("sceneTransferMode")]
    public WH40KCinematicSceneTransferMode SceneTransferMode = WH40KCinematicSceneTransferMode.CameraOnly;

    [DataField("sceneCleanupPolicy")]
    public WH40KCinematicSceneCleanupPolicy SceneCleanupPolicy = WH40KCinematicSceneCleanupPolicy.DestroyOnFinish;

    [DataField("sceneReturnPolicy")]
    public WH40KCinematicSceneReturnPolicy SceneReturnPolicy = WH40KCinematicSceneReturnPolicy.OriginalPosition;

    [DataField("entryAnchorId")]
    public string? EntryAnchorId;

    [DataField("returnAnchorId")]
    public string? ReturnAnchorId;

    [DataField("pauseSourceMap")]
    public bool PauseSourceMap;

    [DataField("switchToContext")]
    public bool SwitchToContext = true;

    [DataField("recordReplay")]
    public bool RecordReplay = true;

    [DataField("playSound")]
    public bool PlaySound = true;

    [DataField("teamId")]
    public string? TeamId;

    [DataField("title")]
    public string? Title;

    [DataField("titleLoc")]
    public string? TitleLoc;

    [DataField("text")]
    public string? Text;

    [DataField("textLoc")]
    public string? TextLoc;

    [DataField("message")]
    public string? Message;

    [DataField("messageLoc")]
    public string? MessageLoc;

    [DataField("sender")]
    public string? Sender;

    [DataField("senderLoc")]
    public string? SenderLoc;

    [DataField("locArgs")]
    public Dictionary<string, string>? LocArgs;

    [DataField("resolveLocArgValues")]
    public bool ResolveLocArgValues;

    [DataField("accentColor")]
    public Color? AccentColor;

    [DataField("duration")]
    public float? DurationSeconds;

    [DataField("shakeIntensity")]
    public float? ShakeIntensity;

    [DataField("shakeRampDuration")]
    public float? ShakeRampDurationSeconds;

    [DataField("shakePulseInterval")]
    public float? ShakePulseIntervalSeconds;

    [DataField("marquee")]
    public bool Marquee = true;

    [DataField("size")]
    public WH40KNotificationSize Size = WH40KNotificationSize.Standard;

    [DataField("category")]
    public WH40KNotificationCategory Category = WH40KNotificationCategory.Auto;

    [DataField("priority")]
    public WH40KNotificationPriority Priority = WH40KNotificationPriority.Auto;

    [DataField("icon")]
    public WH40KNotificationIcon Icon = WH40KNotificationIcon.Auto;

    [DataField("stackKey")]
    public string StackKey = string.Empty;

    [DataField("ignoreUserPreferences")]
    public bool IgnoreUserPreferences;

    public WH40KCinematicActionDefinition Clone()
    {
        var clone = (WH40KCinematicActionDefinition) MemberwiseClone();
        clone.AnchorIds = new List<string>(AnchorIds);
        clone.TargetActionIds = TargetActionIds == null ? null : new List<string>(TargetActionIds);
        clone.LocArgs = LocArgs == null ? null : new Dictionary<string, string>(LocArgs);
        return clone;
    }
}

public enum WH40KCinematicActionType : byte
{
    Notify,
    Announcement,
    StartAudienceShake,
    PlayGlobalSound,
    PlayAnchorSound,
    ApplyLocalDamageToAudience,
    StopActions,
    SpawnAtAnchor,
    RunLavaFlow,
    LoadSceneMap,
    UnloadSceneMap,
    SwitchContext,
    EmitSignal,
    ClearEntitySet,
    SpawnNpc,
    BindExistingEntityAsNpc,
    DespawnNpc,
    NpcSpeak,
    NpcEmote,
    NpcFaceDirection,
    NpcMoveByOffset,
    NpcMoveToAnchor,
    NpcPathToAnchor,
    NpcPathThroughAnchors,
    NpcAttackDirection,
    NpcUseEntity,
    NpcEquipPrototype,
    NpcUnequipSlot,
    NpcSetHTNEnabled,
    NpcReleaseScriptControl,
    NpcWait,
    PlayActorTrack
}

public enum WH40KCinematicLavaMarkerRole : byte
{
    Start,
    Guide,
    End
}

public enum WH40KCinematicLavaObstacleMode : byte
{
    Ignore,
    StopOnWallOrEmpty
}

public enum WH40KCinematicLavaWidthShape : byte
{
    Diamond,
    Square
}
