using System;
using System.Collections.Generic;
using Content.Client.Effects;
using Content.Shared.Polymorph.Components;
using Content.Shared.Polymorph.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Client.Polymorph.Systems;

public sealed partial class ChameleonProjectorSystem : SharedChameleonProjectorSystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    [Dependency] private EntityQuery<AppearanceComponent> _appearanceQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChameleonDisguiseComponent, AfterAutoHandleStateEvent>(OnHandleState);

        SubscribeLocalEvent<ChameleonDisguisedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ChameleonDisguisedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ChameleonDisguisedComponent, GetFlashEffectTargetEvent>(OnGetFlashEffectTargetEvent);
    }

    private void OnHandleState(Entity<ChameleonDisguiseComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        CopyComp<SpriteComponent>(ent);

        var visualRegistry = BuildVisualRegistry(ent);
        if (visualRegistry.Count > 0)
            EntityManager.AddComponents(ent, visualRegistry, removeExisting: true);

        // Re-copy live appearance data after adding visual components so startup/default logic
        // on copied client visual components does not leave the disguise stuck in fallback states.
        if (_appearanceQuery.TryComp(ent.Comp.SourceEntity, out var sourceAppearance) &&
            _appearanceQuery.TryComp(ent, out var disguiseAppearance))
        {
            _appearance.CopyData((ent.Comp.SourceEntity, sourceAppearance), (ent.Owner, disguiseAppearance));
        }
        // reload appearance to hopefully prevent any invisible layers
        else if (_appearanceQuery.TryComp(ent, out var appearance))
        {
            _appearance.QueueUpdate(ent, appearance);
        }
    }

    private ComponentRegistry BuildVisualRegistry(Entity<ChameleonDisguiseComponent> ent)
    {
        var registry = new ComponentRegistry();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!TerminatingOrDeleted(ent.Comp.SourceEntity))
        {
            foreach (var component in AllComps(ent.Comp.SourceEntity))
            {
                var name = EntityManager.ComponentFactory.GetComponentName(component.GetType());
                if (!ShouldCopyVisualComponent(name) || !seen.Add(name))
                    continue;

                registry[name] = new EntityPrototype.ComponentRegistryEntry(component, new MappingDataNode());
            }
        }

        if (ent.Comp.SourceProto is not { } protoId ||
            !_prototype.TryIndex<EntityPrototype>(protoId, out var prototype))
        {
            return registry;
        }

        foreach (var (name, entry) in prototype.Components)
        {
            if (!ShouldCopyVisualComponent(name) || !seen.Add(name))
                continue;

            registry[name] = entry;
        }

        return registry;
    }

    private static bool ShouldCopyVisualComponent(string componentName)
    {
        return componentName is "GenericVisualizer"
            or "IconSmooth"
            or "ApcPowerReceiverComponent"
            or "AtmosMonitoringConsoleComponent"
            or "BarSignComponent"
            or "FaxMachineComponent"
            or "HandheldLightComponent"
            or "LightBulbComponent"
            or "RandomIconSmooth"
            or "SpriteFade"
            or "VendingMachineComponent"
            or "WH40KWaveShader"
            || componentName.EndsWith("Visualizer", StringComparison.Ordinal)
            || componentName.EndsWith("Visuals", StringComparison.Ordinal);
    }

    private void OnStartup(Entity<ChameleonDisguisedComponent> ent, ref ComponentStartup args)
    {
        if (!_spriteQuery.TryComp(ent, out var sprite))
            return;

        ent.Comp.WasVisible = sprite.Visible;
        _sprite.SetVisible((ent.Owner, sprite), false);
    }

    private void OnShutdown(Entity<ChameleonDisguisedComponent> ent, ref ComponentShutdown args)
    {
        if (_spriteQuery.TryComp(ent, out var sprite))
            _sprite.SetVisible((ent.Owner, sprite), ent.Comp.WasVisible);
    }

    private void OnGetFlashEffectTargetEvent(Entity<ChameleonDisguisedComponent> ent, ref GetFlashEffectTargetEvent args)
    {
        args.Target = ent.Comp.Disguise;
    }
}
