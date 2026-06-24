using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K;

/// <summary>
/// Centralised system that owns the single <see cref="MetaDataComponent"/> +
/// <see cref="BeforeDamageChangedEvent"/> subscription shared by all WH40K game modes.
/// Individual game-mode systems register their handlers here to avoid
/// "Duplicate Subscriptions" errors in <see cref="EntityEventBus"/>.
/// </summary>
public sealed class WH40KDamageProtectionSystem : EntitySystem
{
    private readonly List<BeforeDamageChangedHandler> _handlers = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetaDataComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    /// <summary>
    /// Register a handler that will be invoked when <see cref="BeforeDamageChangedEvent"/>
    /// is raised for any entity. Handlers are called in registration order;
    /// execution stops as soon as <see cref="BeforeDamageChangedEvent.Cancelled"/> is set.
    /// </summary>
    public void RegisterHandler(BeforeDamageChangedHandler handler)
    {
        _handlers.Add(handler);
    }

    private void OnBeforeDamageChanged(EntityUid uid, MetaDataComponent component, ref BeforeDamageChangedEvent args)
    {
        foreach (var handler in _handlers)
        {
            if (args.Cancelled)
                return;

            handler(uid, ref args);
        }
    }
}

/// <summary>
/// Delegate used by <see cref="WH40KDamageProtectionSystem"/> to dispatch
/// <see cref="BeforeDamageChangedEvent"/> to registered game-mode handlers.
/// </summary>
public delegate void BeforeDamageChangedHandler(EntityUid uid, ref BeforeDamageChangedEvent args);