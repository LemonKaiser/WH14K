using System.Collections.Generic;
using System.Linq;
using Content.Shared.Polymorph.Components;
using Content.Shared._WH40K.PropHunt;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.PropHunt;

public sealed partial class WH40KPropHuntInvisibilitySystem : EntitySystem
{
    private const float LocalAlpha = 0.38f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, SavedSpriteState> _savedStates = new();
    private readonly HashSet<EntityUid> _activeTargets = new();

    private EntityQuery<ChameleonDisguisedComponent> _disguisedQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;

    public WH40KPropHuntInvisibilitySystem()
    {
        _disguisedQuery = default!;
        _spriteQuery = default!;
    }

    public override void Initialize()
    {
        base.Initialize();
        _disguisedQuery = GetEntityQuery<ChameleonDisguisedComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _activeTargets.Clear();
        var local = _player.LocalEntity;

        var query = EntityQueryEnumerator<WH40KPropHuntInvisibleComponent>();
        while (query.MoveNext(out var uid, out var invisible))
        {
            if (!invisible.Active)
                continue;

            if (!_disguisedQuery.TryComp(uid, out var disguised) || !_spriteQuery.TryComp(disguised.Disguise, out _))
                continue;

            ApplyVisual(disguised.Disguise, uid == local);
        }

        RestoreInactiveVisuals();
    }

    private void ApplyVisual(EntityUid disguise, bool local)
    {
        if (!_spriteQuery.TryComp(disguise, out var sprite))
            return;

        if (!_savedStates.ContainsKey(disguise))
            _savedStates[disguise] = new SavedSpriteState(sprite.Visible, sprite.Color);

        _activeTargets.Add(disguise);

        if (local)
        {
            _sprite.SetVisible((disguise, sprite), true);
            _sprite.SetColor((disguise, sprite), _savedStates[disguise].Color.WithAlpha(LocalAlpha));
            return;
        }

        _sprite.SetVisible((disguise, sprite), false);
        _sprite.SetColor((disguise, sprite), _savedStates[disguise].Color);
    }

    private void RestoreInactiveVisuals()
    {
        foreach (var (uid, state) in _savedStates.ToArray())
        {
            if (_activeTargets.Contains(uid))
                continue;

            if (!_spriteQuery.TryComp(uid, out var sprite))
            {
                _savedStates.Remove(uid);
                continue;
            }

            _sprite.SetVisible((uid, sprite), state.Visible);
            _sprite.SetColor((uid, sprite), state.Color);
            _savedStates.Remove(uid);
        }
    }

    private readonly record struct SavedSpriteState(bool Visible, Color Color);
}
