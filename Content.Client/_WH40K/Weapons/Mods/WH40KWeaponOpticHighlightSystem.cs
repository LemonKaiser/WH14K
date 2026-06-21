using System;
using System.Linq;
using System.Numerics;
using Content.Client.CombatMode;
using Content.Shared._WH40K.Aiming;
using Content.Shared._WH40K.Weapons.Mods;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponOpticHighlightSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> HighlightShader = "WH40KOpticHighlight";
    private const float HighlightHoldSeconds = 0.5f;
    private static readonly Vector2 LookupVector = new(0.35f, 0.35f);

    [Dependency] private CombatModeSystem _combat = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    private OpticHighlightState? _highlighted;
    private ShaderInstance? _highlightShader;

    public override void Initialize()
    {
        base.Initialize();
        EnsureHighlightShader();
        SubscribeLocalEvent<SpriteComponent, EntityTerminatingEvent>(OnSpriteEntityTerminating);
    }

    public override void Shutdown()
    {
        ClearHighlightImmediate();
        _highlightShader = null;
        base.Shutdown();
    }

    /// <summary>
    /// Lazily creates the unique highlight shader instance, or recreates it if the previous
    /// instance was disposed (e.g. after a prototype reload / GL context teardown). The shader
    /// returned by <see cref="ShaderPrototype.InstanceUnique"/> is a disposable duplicate, so
    /// reusing a disposed one makes Clyde throw <c>Unable to use disposed shader instance</c>
    /// on every frame, which produces a permanent black screen.
    /// </summary>
    private void EnsureHighlightShader()
    {
        if (_highlightShader is { Disposed: false })
            return;

        _highlightShader?.Dispose();
        _highlightShader = _prototype.Index<ShaderPrototype>(HighlightShader).InstanceUnique();
    }

    private void OnSpriteEntityTerminating(EntityUid uid, SpriteComponent component, ref EntityTerminatingEvent args)
    {
        if (_highlighted is { } highlighted && highlighted.Target == uid)
            ClearHighlightImmediate();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryGetHighlightSource(out var user, out var highlightColor))
        {
            ClearHighlightImmediate();
            return;
        }

        EnsureHighlightShader();
        if (_highlightShader is not { Disposed: false })
            return;

        ConfigureHighlightColor(highlightColor);

        var target = FindTarget(user);
        if (target != null)
        {
            ApplyOrRefreshHighlight(target.Value);
            return;
        }

        if (_highlighted == null)
            return;

        if (_timing.CurTime >= _highlighted.RemoveAt)
        {
            ClearHighlightImmediate();
            return;
        }

        MaintainHighlight();
    }

    private bool TryGetHighlightSource(out EntityUid user, out Color highlightColor)
    {
        user = default;
        highlightColor = Color.FromHex("#FF9438");

        if (_player.LocalSession?.AttachedEntity is not { Valid: true } localPlayer)
            return false;

        if (!_combat.IsInCombatMode(localPlayer))
            return false;

        if (!_hands.TryGetActiveItem(localPlayer, out var activeItem) ||
            !TryComp(activeItem, out WieldableComponent? wieldable) ||
            !wieldable.Wielded ||
            !TryComp(activeItem, out WH40KWeaponModHostComponent? host) ||
            !TryGetHighlightOptic(host, out _, out var optic))
        {
            return false;
        }

        if (!TryComp(localPlayer, out AimingUserComponent? aimingUser) || !aimingUser.Enabled)
            return false;

        user = localPlayer;
        highlightColor = optic.HighlightColor;
        return true;
    }

    private EntityUid? FindTarget(EntityUid user)
    {
        var mouseMap = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouseMap.MapId == MapId.Nullspace || mouseMap.MapId != _transform.GetMapId(user))
            return null;

        var bounds = new Box2(mouseMap.Position - LookupVector, mouseMap.Position + LookupVector);
        var entities = _lookup.GetEntitiesIntersecting(mouseMap.MapId, bounds, LookupFlags.All | LookupFlags.Approximate);

        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        foreach (var entity in entities)
        {
            if (entity == user ||
                !_mobStateQuery.TryComp(entity, out var mobState) ||
                mobState.CurrentState is not (MobState.Alive or MobState.Critical) ||
                !_spriteQuery.TryComp(entity, out var sprite) ||
                !sprite.Visible ||
                !_interaction.InRangeUnobstructed(user, entity, SharedInteractionSystem.MaxRaycastRange, CollisionGroup.Opaque))
            {
                continue;
            }

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(entity), mouseMap.Position);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = entity;
        }

        return best;
    }

    private void ApplyOrRefreshHighlight(EntityUid target)
    {
        if (_highlighted is { } highlighted &&
            highlighted.Target == target)
        {
            highlighted.RemoveAt = _timing.CurTime + TimeSpan.FromSeconds(HighlightHoldSeconds);
            EnsureHighlightApplied(target);
            return;
        }

        ClearHighlightImmediate();

        if (!_spriteQuery.TryComp(target, out var sprite))
            return;

        _highlighted = new OpticHighlightState(
            target,
            _timing.CurTime + TimeSpan.FromSeconds(HighlightHoldSeconds),
            sprite.PostShader,
            sprite.GetScreenTexture,
            sprite.RaiseShaderEvent);

        ApplyHighlight(sprite);
    }

    private void MaintainHighlight()
    {
        if (_highlighted is not { } highlighted)
            return;

        EnsureHighlightApplied(highlighted.Target);
    }

    private void EnsureHighlightApplied(EntityUid target)
    {
        if (!_spriteQuery.TryComp(target, out var sprite))
            return;

        ApplyHighlight(sprite);
    }

    private void ApplyHighlight(SpriteComponent sprite)
    {
        if (_highlightShader is not { Disposed: false })
            return;

        sprite.PostShader = _highlightShader;
        sprite.GetScreenTexture = false;
        sprite.RaiseShaderEvent = false;
    }

    private void ConfigureHighlightColor(Color color)
    {
        if (_highlightShader is not { Disposed: false })
            return;

        _highlightShader.SetParameter("Tint", new Vector3(color.R, color.G, color.B));
    }

    private void ClearHighlightImmediate()
    {
        if (_highlighted is not { } highlighted)
            return;

        if (_spriteQuery.TryComp(highlighted.Target, out var sprite))
        {
            // Restore the previous post-shader unconditionally, but never restore a shader
            // instance that has itself been disposed (otherwise we re-introduce the same
            // "Unable to use disposed shader instance" crash the next time Clyde draws).
            var previous = highlighted.PreviousShader;
            if (previous is { Disposed: true })
                previous = null;

            sprite.PostShader = previous;
            sprite.GetScreenTexture = highlighted.PreviousGetScreenTexture;
            sprite.RaiseShaderEvent = highlighted.PreviousRaiseShaderEvent;
        }

        _highlighted = null;
    }

    private bool TryGetHighlightOptic(WH40KWeaponModHostComponent host, out EntityUid opticUid, out WH40KWeaponModOpticComponent optic)
    {
        opticUid = default;
        optic = default!;

        foreach (var definition in host.SlotDefinitions.OrderByDescending(x => x.Priority))
        {
            if (definition.SlotType != WH40KWeaponModSlotType.OpticTop)
                continue;

            var slotId = WH40KWeaponModHelper.GetSlotId(definition.Id);
            if (!host.ModSlots.TryGetValue(slotId, out var slot) ||
                slot.Item is not { } installed ||
                !TryComp(installed, out WH40KWeaponModOpticComponent? opticComp) ||
                !opticComp.HighlightTargets)
            {
                continue;
            }

            opticUid = installed;
            optic = opticComp;
            return true;
        }

        return false;
    }

    private sealed class OpticHighlightState
    {
        public EntityUid Target { get; }
        public TimeSpan RemoveAt { get; set; }
        public ShaderInstance? PreviousShader { get; }
        public bool PreviousGetScreenTexture { get; }
        public bool PreviousRaiseShaderEvent { get; }

        public OpticHighlightState(
            EntityUid target,
            TimeSpan removeAt,
            ShaderInstance? previousShader,
            bool previousGetScreenTexture,
            bool previousRaiseShaderEvent)
        {
            Target = target;
            RemoveAt = removeAt;
            PreviousShader = previousShader;
            PreviousGetScreenTexture = previousGetScreenTexture;
            PreviousRaiseShaderEvent = previousRaiseShaderEvent;
        }
    }
}
