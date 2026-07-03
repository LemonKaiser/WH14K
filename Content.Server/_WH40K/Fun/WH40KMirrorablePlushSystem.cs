using System.Numerics;
using Content.Server.Sprite;
using Content.Shared.Verbs;
using Content.Shared._WH40K.Fun;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.Fun;

public sealed partial class WH40KMirrorablePlushSystem : EntitySystem
{
    [Dependency] private ScaleVisualsSystem _scaleVisuals = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KMirrorablePlushComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WH40KMirrorablePlushComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Hands == null)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("wh40k-plush-mirror-verb"),
            Act = () => ToggleMirror(ent.Owner)
        });
    }

    private void ToggleMirror(EntityUid uid)
    {
        var scale = _scaleVisuals.GetSpriteScale(uid);
        var x = scale.X == 0f ? 1f : scale.X;
        var y = scale.Y == 0f ? 1f : scale.Y;

        _scaleVisuals.SetSpriteScale(uid, new Vector2(-x, y));
    }
}
