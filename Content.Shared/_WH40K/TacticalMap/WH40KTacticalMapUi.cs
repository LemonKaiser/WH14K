using System;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.TacticalMap;

[Serializable, NetSerializable]
public enum WH40KTacticalMapAnnotationTool : byte
{
    Pan,
    Brush,
    Eraser,
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapAnnotationStroke
{
    public Vector2[] Points { get; }
    public Color Color { get; }
    public float Thickness { get; }

    public WH40KTacticalMapAnnotationStroke(Vector2[] points, Color color, float thickness)
    {
        Points = points ?? Array.Empty<Vector2>();
        Color = color;
        Thickness = thickness;
    }
}

[Serializable, NetSerializable]
public enum WH40KTacticalMapStrategicMarkerKind : byte
{
    CapturePoint = 0,
    CommandNode = 1,
}

[Serializable, NetSerializable]
public enum WH40KTacticalMapStrategicRelation : byte
{
    Neutral = 0,
    Allied = 1,
    Hostile = 2,
    Contested = 3,
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapAllyMarker
{
    public NetEntity Entity { get; }
    public string Label { get; }
    public Vector2 Position { get; }
    public Color Color { get; }

    public WH40KTacticalMapAllyMarker(NetEntity entity, string label, Vector2 position, Color color)
    {
        Entity = entity;
        Label = label;
        Position = position;
        Color = color;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapCapturePointMarker
{
    public NetEntity Entity { get; }
    public WH40KTacticalMapStrategicMarkerKind Kind { get; }
    public WH40KTacticalMapStrategicRelation Relation { get; }
    public string Label { get; }
    public string Callsign { get; }
    public Vector2 Position { get; }
    public string OwnerTeamId { get; }
    public string OwnerDisplayName { get; }
    public Color OwnerColor { get; }
    public string CapturingTeamId { get; }
    public string CapturingDisplayName { get; }
    public Color CapturingColor { get; }
    public float CaptureProgress { get; }
    public int FrontReward { get; }
    public bool Contested { get; }

    public WH40KTacticalMapCapturePointMarker(
        NetEntity entity,
        WH40KTacticalMapStrategicMarkerKind kind,
        WH40KTacticalMapStrategicRelation relation,
        string label,
        string callsign,
        Vector2 position,
        string ownerTeamId,
        string ownerDisplayName,
        Color ownerColor,
        string capturingTeamId,
        string capturingDisplayName,
        Color capturingColor,
        float captureProgress,
        int frontReward,
        bool contested)
    {
        Entity = entity;
        Kind = kind;
        Relation = relation;
        Label = label;
        Callsign = callsign;
        Position = position;
        OwnerTeamId = ownerTeamId;
        OwnerDisplayName = ownerDisplayName;
        OwnerColor = ownerColor;
        CapturingTeamId = capturingTeamId;
        CapturingDisplayName = capturingDisplayName;
        CapturingColor = capturingColor;
        CaptureProgress = captureProgress;
        FrontReward = frontReward;
        Contested = contested;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapBuiState : BoundUserInterfaceState
{
    public NetEntity TargetGrid { get; }
    public string GridName { get; }
    public string SnapshotTexturePath { get; }
    public NetEntity TrackedEntity { get; }
    public bool CanAnnotate { get; }
    public bool LiveRefreshEnabled { get; }
    public string TeamId { get; }
    public bool FogEnabled { get; }
    public int FogChunkSize { get; }
    public int RevealRevision { get; }
    public Vector2i[] RevealedChunks { get; }
    public int AnnotationRevision { get; }
    public WH40KTacticalMapAnnotationStroke[] AnnotationStrokes { get; }
    public int OverlayRevision { get; }
    public WH40KTacticalMapAllyMarker[] AlliedMarkers { get; }
    public WH40KTacticalMapCapturePointMarker[] CapturePoints { get; }

    public WH40KTacticalMapBuiState(
        NetEntity targetGrid,
        string gridName,
        string snapshotTexturePath,
        NetEntity trackedEntity,
        bool canAnnotate,
        bool liveRefreshEnabled,
        string teamId,
        bool fogEnabled,
        int fogChunkSize,
        int revealRevision,
        Vector2i[] revealedChunks,
        int annotationRevision,
        WH40KTacticalMapAnnotationStroke[] annotationStrokes,
        int overlayRevision,
        WH40KTacticalMapAllyMarker[] alliedMarkers,
        WH40KTacticalMapCapturePointMarker[] capturePoints)
    {
        TargetGrid = targetGrid;
        GridName = gridName;
        SnapshotTexturePath = snapshotTexturePath;
        TrackedEntity = trackedEntity;
        CanAnnotate = canAnnotate;
        LiveRefreshEnabled = liveRefreshEnabled;
        TeamId = teamId;
        FogEnabled = fogEnabled;
        FogChunkSize = fogChunkSize;
        RevealRevision = revealRevision;
        RevealedChunks = revealedChunks;
        AnnotationRevision = annotationRevision;
        AnnotationStrokes = annotationStrokes;
        OverlayRevision = overlayRevision;
        AlliedMarkers = alliedMarkers ?? Array.Empty<WH40KTacticalMapAllyMarker>();
        CapturePoints = capturePoints ?? Array.Empty<WH40KTacticalMapCapturePointMarker>();
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapOverlayState(
    int overlayRevision,
    WH40KTacticalMapAllyMarker[] alliedMarkers,
    WH40KTacticalMapCapturePointMarker[] capturePoints)
{
    public int OverlayRevision { get; } = overlayRevision;
    public WH40KTacticalMapAllyMarker[] AlliedMarkers { get; } = alliedMarkers ?? Array.Empty<WH40KTacticalMapAllyMarker>();
    public WH40KTacticalMapCapturePointMarker[] CapturePoints { get; } = capturePoints ?? Array.Empty<WH40KTacticalMapCapturePointMarker>();
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapSaveAnnotationsMessage(WH40KTacticalMapAnnotationStroke[] strokes) : BoundUserInterfaceMessage
{
    public WH40KTacticalMapAnnotationStroke[] Strokes { get; } = strokes ?? Array.Empty<WH40KTacticalMapAnnotationStroke>();
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapStateEvent(NetEntity tacticalMap, WH40KTacticalMapBuiState state) : EntityEventArgs
{
    public NetEntity TacticalMap { get; } = tacticalMap;
    public WH40KTacticalMapBuiState State { get; } = state;
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapOverlayEvent(NetEntity tacticalMap, WH40KTacticalMapOverlayState state) : EntityEventArgs
{
    public NetEntity TacticalMap { get; } = tacticalMap;
    public WH40KTacticalMapOverlayState State { get; } = state;
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapLiveRefreshState(
    bool active,
    int revision,
    NetEntity eye,
    Vector2i tileOrigin,
    Vector2i tileSize)
{
    public bool Active { get; } = active;
    public int Revision { get; } = revision;
    public NetEntity Eye { get; } = eye;
    public Vector2i TileOrigin { get; } = tileOrigin;
    public Vector2i TileSize { get; } = tileSize;
}

[Serializable, NetSerializable]
public sealed class WH40KTacticalMapLiveRefreshEvent(NetEntity tacticalMap, WH40KTacticalMapLiveRefreshState state) : EntityEventArgs
{
    public NetEntity TacticalMap { get; } = tacticalMap;
    public WH40KTacticalMapLiveRefreshState State { get; } = state;
}
