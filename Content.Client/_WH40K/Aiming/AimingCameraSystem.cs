using System.Numerics;
using Content.Client.Viewport;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Ghost;
using Content.Shared.Construction;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Content.Shared._WH40K.Aiming;
using Content.Shared.Wieldable.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Timing;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Aiming;

public sealed partial class AimingCameraSystem : EntitySystem
{
    [Dependency] private  IEyeManager _eyeManager = default!;
    [Dependency] private  IInputManager _inputManager = default!;
    [Dependency] private  IClientGameTiming _timing = default!;
    [Dependency] private  SharedTransformSystem _xform = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  TagSystem _tags = default!;

    // Prevents needing to move mouse fully to the screen edge to reach full offset.
    private const float EdgeOffset = 0.8f;
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly ProtoId<TagPrototype> BarricadeTag = "barricade";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AimingUserComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
    }

    private void OnGetEyeOffset(EntityUid uid, AimingUserComponent userComp, ref GetEyeOffsetEvent args)
    {
        if (!TryComp(uid, out HandsComponent? hands))
        {
            Reset(userComp);
            userComp.WasValid = false;
            return;
        }

        var activeItem = _hands.GetActiveItem((uid, hands));
        if (activeItem == null || !TryComp(activeItem.Value, out AimingCameraComponent? aimingComp))
        {
            if (userComp.LastAimingItem != null)
            {
                userComp.LastAimingItem = null;
                userComp.TargetPosition = userComp.CurrentPosition;
            }

            userComp.WasValid = false;
            args.Offset += SmoothToTarget(userComp, Vector2.Zero, wallClamped: false, returning: true);
            return;
        }

        if (userComp.LastAimingItem != activeItem)
        {
            userComp.LastAimingItem = activeItem;
            userComp.TargetPosition = userComp.CurrentPosition;
        }

        UpdateConfig(userComp, aimingComp);

        if (!userComp.Enabled)
        {
            userComp.WasValid = false;
            args.Offset += SmoothToTarget(userComp, Vector2.Zero, wallClamped: false, returning: true);
            return;
        }

        if (aimingComp.RequireWield && !IsWieldedOrMultiHanded(activeItem.Value))
        {
            userComp.WasValid = false;
            args.Offset += SmoothToTarget(userComp, Vector2.Zero, wallClamped: false, returning: true);
            return;
        }

        var ignoreWallClamp = HasComp<GhostComponent>(uid);
        var offset = OffsetAfterMouse(uid, userComp, aimingComp, ignoreWallClamp);
        if (offset == null)
            return;

        args.Offset += offset.Value;
        userComp.WasValid = true;
    }

    private Vector2? OffsetAfterMouse(EntityUid user, AimingUserComponent userComp, AimingCameraComponent aimingComp, bool ignoreWallClamp)
    {
        if (_eyeManager.MainViewport is not ScalingViewport vp)
            return null;

        var mousePos = _inputManager.MouseScreenPosition.Position;
        var viewportSize = vp.PixelSize;
        var scalingViewportSize = vp.ViewportSize * vp.CurrentRenderScale;
        var visibleViewportSize = Vector2.Min(viewportSize, scalingViewportSize);

        if (visibleViewportSize.LengthSquared() <= 0f)
            return null;

        Matrix3x2.Invert(_eyeManager.MainViewport.GetLocalToScreenMatrix(), out var matrix);
        var mouseCoords = Vector2.Transform(mousePos, matrix);

        var mouseInside =
            mouseCoords.X >= 0f && mouseCoords.X <= visibleViewportSize.X &&
            mouseCoords.Y >= 0f && mouseCoords.Y <= visibleViewportSize.Y;

        // If mouse is outside the visible viewport or the game window, keep the last target,
        // but still clamp it to walls while the player moves.
        if (!mouseInside || _inputManager.MouseScreenPosition.Window == WindowId.Invalid)
        {
            var clampedTargetHold = ignoreWallClamp
                ? userComp.TargetPosition
                : ClampToWalls(user, userComp.TargetPosition, userComp.LastWallBuffer);
            var wallClampedHold = !ignoreWallClamp &&
                                  clampedTargetHold.LengthSquared() + 0.0001f < userComp.TargetPosition.LengthSquared();
            var releasedHold = ignoreWallClamp
                ? clampedTargetHold
                : ApplyWallRelease(userComp, clampedTargetHold, userComp.TargetPosition, wallClampedHold);
            if (ignoreWallClamp)
                userComp.LastWallClamped = false;
            return SmoothToTarget(userComp, releasedHold, wallClampedHold, returning: false);
        }

        var boundedMousePos = Vector2.Clamp(mouseCoords, Vector2.Zero, visibleViewportSize);
        var offsetRadius = MathF.Min(visibleViewportSize.X / 2f, visibleViewportSize.Y / 2f) * EdgeOffset;
        if (offsetRadius <= 0f)
            return null;

        var mouseNormalizedPos = new Vector2(
            -(boundedMousePos.X - visibleViewportSize.X / 2f) / offsetRadius,
            (boundedMousePos.Y - visibleViewportSize.Y / 2f) / offsetRadius
        );

        // Account for eye rotation.
        var eyeRotation = _eyeManager.CurrentEye.Rotation;
        var mouseActualRelativePos = Vector2.Transform(
            mouseNormalizedPos,
            Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, (float) eyeRotation.Opposite().Theta)
        );

        // Cap the offset into a circle around the player.
        mouseActualRelativePos *= aimingComp.MaxOffset;
        if (mouseActualRelativePos.Length() > aimingComp.MaxOffset)
            mouseActualRelativePos = mouseActualRelativePos.Normalized() * aimingComp.MaxOffset;

        // Clamp target to avoid peeking through walls, then smooth.
        var clampedTarget = ignoreWallClamp
            ? mouseActualRelativePos
            : ClampToWalls(user, mouseActualRelativePos, aimingComp.WallBuffer);
        var wallClamped = !ignoreWallClamp &&
                          clampedTarget.LengthSquared() + 0.0001f < mouseActualRelativePos.LengthSquared();
        var releasedTarget = ignoreWallClamp
            ? clampedTarget
            : ApplyWallRelease(userComp, clampedTarget, mouseActualRelativePos, wallClamped);
        if (ignoreWallClamp)
            userComp.LastWallClamped = false;
        return SmoothToTarget(userComp, releasedTarget, wallClamped, returning: false);
    }

    private Vector2 ClampToWalls(EntityUid user, Vector2 desiredOffset, float wallBuffer)
    {
        var distance = desiredOffset.Length();
        if (distance <= 0f)
            return desiredOffset;

        var origin = _xform.GetWorldPosition(user);
        var direction = desiredOffset / distance;
        var mapId = _xform.GetMapId(user);

        var ray = new CollisionRay(origin, direction, (int) CollisionGroup.Impassable);
        var hit = _physics.IntersectRayWithPredicate(mapId, ray, distance,
            uid => uid == user || IsTransparentForAiming(uid),
            returnOnFirstHit: true).FirstOrNull();
        if (hit == null)
            return desiredOffset;

        var allowed = MathF.Max(0f, hit.Value.Distance - wallBuffer);
        return direction * MathF.Min(allowed, distance);
    }

    private bool IsTransparentForAiming(EntityUid uid)
    {
        // Windows (including glass airlocks) and grilles should not block aiming camera.
        if (_tags.HasTag(uid, WindowTag))
            return true;

        if (_tags.HasTag(uid, BarricadeTag))
            return true;

        if (HasComp<SharedCanBuildWindowOnTopComponent>(uid))
            return true;

        return false;
    }

    private void Reset(AimingUserComponent userComp)
    {
        userComp.TargetPosition = Vector2.Zero;
        userComp.CurrentPosition = Vector2.Zero;
        userComp.LastWallClamped = false;
        userComp.LastWallClampDistance = 0f;
        userComp.LastWallClampDir = Vector2.Zero;
        userComp.LastWallClampTime = TimeSpan.Zero;
    }

    private Vector2 SmoothToTarget(AimingUserComponent userComp, Vector2 target, bool wallClamped, bool returning)
    {
        userComp.TargetPosition = target;

        if (userComp.CurrentPosition != userComp.TargetPosition)
        {
            var vectorOffset = userComp.TargetPosition - userComp.CurrentPosition;
            var maxStep = userComp.LastOffsetSpeed * (float) _timing.FrameTime.TotalSeconds * 60f;
            if (returning)
                maxStep *= userComp.ReturnMultiplier;
            var pullingBack = wallClamped &&
                              userComp.TargetPosition.LengthSquared() + 0.0001f <
                              userComp.CurrentPosition.LengthSquared();
            if (pullingBack)
                maxStep *= userComp.LastWallPullMultiplier;
            if (vectorOffset.Length() > maxStep)
                vectorOffset = vectorOffset.Normalized() * maxStep;

            userComp.CurrentPosition += vectorOffset;
        }

        return userComp.CurrentPosition;
    }

    private Vector2 ApplyWallRelease(AimingUserComponent userComp, Vector2 clampedTarget, Vector2 unclampedTarget, bool wallClamped)
    {
        var clampedLength = clampedTarget.Length();
        var unclampedLength = unclampedTarget.Length();
        var maxIncrease = userComp.LastOffsetSpeed * (float) _timing.FrameTime.TotalSeconds * 60f *
                          userComp.WallReleaseMultiplier;
        var now = _timing.CurTime;

        if (wallClamped)
        {
            userComp.LastWallClamped = true;
            userComp.LastWallClampTime = now;
            if (userComp.LastWallClampDistance <= 0f)
            {
                userComp.LastWallClampDistance = clampedLength;
            }
            else
            {
                var delta = clampedLength - userComp.LastWallClampDistance;
                if (MathF.Abs(delta) > maxIncrease)
                    clampedLength = userComp.LastWallClampDistance + MathF.Sign(delta) * maxIncrease;
                userComp.LastWallClampDistance = clampedLength;
            }

            var clampedDir = clampedTarget.LengthSquared() > 0.000001f
                ? clampedTarget.Normalized()
                : userComp.LastWallClampDir;
            userComp.LastWallClampDir = clampedDir;
            return clampedDir * clampedLength;
        }

        if (!userComp.LastWallClamped)
            return clampedTarget;

        var timeSinceClamp = now - userComp.LastWallClampTime;
        if (timeSinceClamp <= TimeSpan.FromSeconds(userComp.WallStickSeconds) &&
            unclampedLength >= userComp.LastWallClampDistance - 0.01f)
        {
            return userComp.LastWallClampDir * userComp.LastWallClampDistance;
        }

        var allowedLength = MathF.Min(unclampedLength, userComp.LastWallClampDistance + maxIncrease);
        userComp.LastWallClampDistance = allowedLength;

        if (allowedLength >= unclampedLength - 0.01f)
            userComp.LastWallClamped = false;

        if (allowedLength <= 0f)
            return Vector2.Zero;

        Vector2 dir;
        if (unclampedLength <= 0.001f)
        {
            dir = userComp.LastWallClampDir;
        }
        else
        {
            var unclampedDir = unclampedTarget / unclampedLength;
            var dot = Vector2.Dot(userComp.LastWallClampDir, unclampedDir);
            dir = dot < 0.95f ? userComp.LastWallClampDir : unclampedDir;
            if (dir.LengthSquared() <= 0f)
                dir = unclampedDir;
        }
        return dir * allowedLength;
    }

    private void UpdateConfig(AimingUserComponent userComp, AimingCameraComponent aimingComp)
    {
        userComp.LastOffsetSpeed = aimingComp.OffsetSpeed;
        userComp.LastWallBuffer = aimingComp.WallBuffer;
        userComp.LastWallPullMultiplier = aimingComp.WallPullMultiplier;
    }

    private bool IsWieldedOrMultiHanded(EntityUid uid)
    {
        if (TryComp(uid, out WieldableComponent? wieldable) && wieldable.Wielded)
            return true;

        return HasComp<MultiHandedItemComponent>(uid);
    }
}
