using System;
using System.Numerics;
using Content.Server.Popups;
using Content.Server.Sprite;
using Content.Shared.Buckle.Components;
using Content.Shared.Popups;
using Content.Shared._WH40K.Furniture;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.Furniture;

public sealed partial class WH40KSelfLockingChairSystem : EntitySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ScaleVisualsSystem _scaleVisuals = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KSelfLockingChairComponent, UnstrapAttemptEvent>(OnUnstrapAttempt);
        SubscribeLocalEvent<WH40KSelfLockingChairComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WH40KSelfLockingChairComponent, UnstrappedEvent>(OnUnstrapped);
    }

    private void OnUnstrapAttempt(Entity<WH40KSelfLockingChairComponent> ent, ref UnstrapAttemptEvent args)
    {
        if (args.Cancelled || args.User != args.Buckle.Owner)
            return;

        args.Cancelled = true;

        if (args.Popup)
        {
            _popup.PopupEntity(
                Loc.GetString("wh40k-self-locking-chair-stuck"),
                ent.Owner,
                args.Buckle.Owner,
                PopupType.MediumCaution);
        }
    }

    private void OnStrapped(Entity<WH40KSelfLockingChairComponent> ent, ref StrappedEvent args)
    {
        var scale = NormalizeScale(_scaleVisuals.GetSpriteScale(args.Buckle.Owner));
        _scaleVisuals.SetSpriteScale(args.Buckle.Owner, new Vector2(scale.X, -MathF.Abs(scale.Y)));
    }

    private void OnUnstrapped(Entity<WH40KSelfLockingChairComponent> ent, ref UnstrappedEvent args)
    {
        var scale = NormalizeScale(_scaleVisuals.GetSpriteScale(args.Buckle.Owner));
        _scaleVisuals.SetSpriteScale(args.Buckle.Owner, new Vector2(scale.X, MathF.Abs(scale.Y)));
    }

    private static Vector2 NormalizeScale(Vector2 scale)
    {
        if (scale.X == 0f)
            scale.X = 1f;

        if (scale.Y == 0f)
            scale.Y = 1f;

        return scale;
    }
}
