using System;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.MetaProgress;

public sealed class WH40KGhostDecorationVisualizerSystem : EntitySystem
{
    private const string DefaultGhostRsiPath = "/Textures/Mobs/Ghosts/ghost_human.rsi";
    private const string DefaultGhostState = "animated";

    [Dependency] private readonly SpriteSystem _sprite = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.meta.ghostvisual");

        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, ComponentStartup>(OnVisualStartup);
        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, AfterAutoHandleStateEvent>(OnVisualState);
        SubscribeLocalEvent<WH40KGhostDecorationVisualComponent, ComponentShutdown>(OnVisualShutdown);
    }

    private void OnVisualStartup(Entity<WH40KGhostDecorationVisualComponent> ent, ref ComponentStartup args)
    {
        ApplyVisual(ent);
    }

    private void OnVisualState(Entity<WH40KGhostDecorationVisualComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyVisual(ent);
    }

    private void OnVisualShutdown(Entity<WH40KGhostDecorationVisualComponent> ent, ref ComponentShutdown args)
    {
        ApplyDefault(ent.Owner);
    }

    private void ApplyVisual(Entity<WH40KGhostDecorationVisualComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var path = string.IsNullOrWhiteSpace(ent.Comp.GhostRsiPath) ? DefaultGhostRsiPath : ent.Comp.GhostRsiPath;
        var state = string.IsNullOrWhiteSpace(ent.Comp.GhostState) ? DefaultGhostState : ent.Comp.GhostState;
        if (TryApplySprite(ent.Owner, sprite, path, state))
            return;

        ApplyDefault(ent.Owner);
    }

    private void ApplyDefault(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        TryApplySprite(uid, sprite, DefaultGhostRsiPath, DefaultGhostState);
    }

    private bool TryApplySprite(EntityUid uid, SpriteComponent sprite, string rsiPath, string state)
    {
        try
        {
            _sprite.LayerSetSprite(
                (uid, sprite),
                0,
                new SpriteSpecifier.Rsi(new ResPath(rsiPath), state));
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Failed to apply WH40K ghost skin '{rsiPath}:{state}' for {ToPrettyString(uid)}: {e.Message}");
            return false;
        }
    }
}
