using System.Linq;
using Content.Shared.Construction.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction
{
    public sealed class ConstructionPlacementHijack : PlacementHijack
    {
        private readonly ConstructionSystem _constructionSystem;
        private readonly ConstructionPrototype? _prototype;

        public ConstructionSystem? CurrentConstructionSystem { get { return _constructionSystem; } }
        public ConstructionPrototype? CurrentPrototype { get { return _prototype; } }

        public override bool CanRotate { get; }

        public ConstructionPlacementHijack(ConstructionSystem constructionSystem, ConstructionPrototype? prototype)
        {
            _constructionSystem = constructionSystem;
            _prototype = prototype;
            CanRotate = prototype?.CanRotate ?? true;
        }

        /// <inheritdoc />
        public override bool HijackPlacementRequest(EntityCoordinates coordinates)
        {
            if (_prototype != null)
            {
                var dir = Manager.Direction;
                _constructionSystem.SpawnGhost(_prototype, coordinates, dir);
            }
            return true;
        }

        /// <inheritdoc />
        public override bool HijackDeletion(EntityUid entity)
        {
            if (IoCManager.Resolve<IEntityManager>().HasComponent<ConstructionGhostComponent>(entity))
            {
                _constructionSystem.ClearGhost(entity.GetHashCode());
            }
            return true;
        }

        /// <inheritdoc />
        public override void StartHijack(PlacementManager manager)
        {
            base.StartHijack(manager);

            if (_prototype is null || !_constructionSystem.TryGetRecipePrototype(_prototype.ID, out var targetProtoId))
                return;

            if (!IoCManager.Resolve<IPrototypeManager>().TryIndex(targetProtoId, out EntityPrototype? proto))
                return;

            var entMan = IoCManager.Resolve<IEntityManager>();
            var spriteSystem = entMan.System<SpriteSystem>();

            if (TryPreparePlacementSprite(manager, targetProtoId, entMan, spriteSystem))
                return;

            var textures = spriteSystem.GetPrototypeTextures(proto, out var noRot).ToList();
            manager.PreparePlacementTexList(textures, noRot || !CanRotate, proto);
        }

        private static bool TryPreparePlacementSprite(
            PlacementManager manager,
            EntProtoId targetProtoId,
            IEntityManager entMan,
            SpriteSystem spriteSystem)
        {
            var dummy = entMan.SpawnEntity(targetProtoId, MapCoordinates.Nullspace);

            try
            {
                if (!entMan.TryGetComponent(dummy, out SpriteComponent? targetSprite))
                    return false;

                entMan.System<AppearanceSystem>().OnChangeData(dummy, targetSprite);
                manager.PreparePlacementSprite((dummy, targetSprite));

                if (manager.CurrentPlacementOverlayEntity is not { Valid: true } overlayUid ||
                    !entMan.TryGetComponent(overlayUid, out SpriteComponent? overlaySprite))
                {
                    return false;
                }

                for (var i = 0; i < overlaySprite.AllLayers.Count(); i++)
                {
                    overlaySprite.LayerSetShader(i, "unshaded");
                }

                return true;
            }
            finally
            {
                if (!entMan.Deleted(dummy))
                    entMan.DeleteEntity(dummy);
            }
        }
    }
}
