using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K;

/// <summary>
/// Centralised system that owns the single <see cref="MetaDataComponent"/> +
/// <see cref="MeleeHitEvent"/> subscription shared by all WH40K game modes.
/// Individual game-mode systems register their handlers here to avoid
/// "Duplicate Subscriptions" errors in <see cref="EntityEventBus"/>.
/// </summary>
public sealed class WH40KMeleeProtectionSystem : EntitySystem
{
    private readonly List<MeleeHitHandler> _handlers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetaDataComponent, MeleeHitEvent>(OnMeleeHit);
    }

    /// <summary>
    /// Register a handler that will be invoked when <see cref="MeleeHitEvent"/>
    /// is raised for any entity. Handlers are called in registration order;
    /// execution stops as soon as <see cref="HandledEntityEventArgs.Handled"/> is set.
    /// </summary>
    public void RegisterHandler(MeleeHitHandler handler)
    {
        _handlers.Add(handler);
    }

    private void OnMeleeHit(EntityUid uid, MetaDataComponent component, ref MeleeHitEvent args)
    {
        foreach (var handler in _handlers)
        {
            if (args.Handled)
                return;

            handler(uid, ref args);
        }
    }
}

/// <summary>
/// Delegate used by <see cref="WH40KMeleeProtectionSystem"/> to dispatch
/// <see cref="MeleeHitEvent"/> to registered game-mode handlers.
/// </summary>
public delegate void MeleeHitHandler(EntityUid uid, ref MeleeHitEvent args);