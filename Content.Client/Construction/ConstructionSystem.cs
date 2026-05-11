using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client.Popups;
using Content.Client._WH40K.StrategicPoints;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared._WH40K.StrategicPoints.Construction;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Wall;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Construction
{
    /// <summary>
    /// The client-side implementation of the construction system, which is used for constructing entities in game.
    /// </summary>
    [UsedImplicitly]
    public sealed class ConstructionSystem : SharedConstructionSystem
    {
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly ExamineSystemShared _examineSystem = default!;
        [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
        [Dependency] private readonly SpriteSystem _sprite = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;
        [Dependency] private readonly EntityLookupSystem _lookup = default!;

        private readonly Dictionary<int, EntityUid> _ghosts = new();
        private readonly Dictionary<string, ConstructionGuide> _guideCache = new();

        private readonly Dictionary<string, string> _recipesMetadataCache = [];
        private const float GhostClickCaptureRange = 1.25f;
        private const float StrategicAnchorClickCaptureRange = 1.35f;
        private const float StrategicGhostClickCaptureRange = 2.5f;
        private const float StrategicAnchorGhostFallbackRange = 3.0f;
        public bool CraftingEnabled { get; private set; }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            WarmupRecipesCache();

            UpdatesOutsidePrediction = true;
            SubscribeLocalEvent<LocalPlayerAttachedEvent>(HandlePlayerAttached);
            SubscribeNetworkEvent<AckStructureConstructionMessage>(HandleAckStructure);
            SubscribeNetworkEvent<ResponseConstructionGuide>(OnConstructionGuideReceived);

            CommandBinds.Builder
                .Bind(ContentKeyFunctions.OpenCraftingMenu,
                    new PointerInputCmdHandler(HandleOpenCraftingMenu, outsidePrediction: true))
                .BindBefore(EngineKeyFunctions.Use,
                    new PointerInputCmdHandler(HandleUse, outsidePrediction: true),
                    new[] { typeof(SharedInteractionSystem) })
                .Bind(ContentKeyFunctions.EditorFlipObject,
                    new PointerInputCmdHandler(HandleFlip, outsidePrediction: true))
                .Register<ConstructionSystem>();

            SubscribeLocalEvent<ConstructionGhostComponent, ExaminedEvent>(HandleConstructionGhostExamined);
            SubscribeLocalEvent<ConstructionGhostComponent, ComponentShutdown>(HandleGhostComponentShutdown);
        }

        private void HandleGhostComponentShutdown(EntityUid uid, ConstructionGhostComponent component, ComponentShutdown args)
        {
            ClearGhost(component.GhostId);
        }

        public bool TryGetRecipePrototype(string constructionProtoId, [NotNullWhen(true)] out string? targetProtoId)
        {
            if (_recipesMetadataCache.TryGetValue(constructionProtoId, out targetProtoId))
                return true;

            targetProtoId = null;
            return false;
        }

        private void WarmupRecipesCache()
        {
            foreach (var constructionProto in PrototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (!PrototypeManager.Resolve(constructionProto.Graph, out var graphProto))
                    continue;

                if (constructionProto.TargetNode is not { } targetNodeId)
                    continue;

                if (!graphProto.Nodes.TryGetValue(targetNodeId, out var targetNode))
                    continue;

                // Recursion is for wimps.
                var stack = new Stack<ConstructionGraphNode>();
                stack.Push(targetNode);

                do
                {
                    var node = stack.Pop();

                    // I never realized if this uid affects anything...
                    // EntityUid? userUid = args.SenderSession.State.ControlledEntity.HasValue
                    //     ? GetEntity(args.SenderSession.State.ControlledEntity.Value)
                    //     : null;

                    // We try to get the id of the target prototype, if it fails, we try going through the edges.
                    if (node.Entity.GetId(null, null, new(EntityManager)) is not { } entityId)
                    {
                        // If the stack is not empty, there is a high probability that the loop will go to infinity.
                        if (stack.Count == 0)
                        {
                            foreach (var edge in node.Edges)
                            {
                                if (graphProto.Nodes.TryGetValue(edge.Target, out var graphNode))
                                    stack.Push(graphNode);
                            }
                        }

                        continue;
                    }

                    // If we got the id of the prototype, we exit the “recursion” by clearing the stack.
                    stack.Clear();

                    if (!PrototypeManager.Resolve(entityId, out var proto))
                        continue;

                    var name = constructionProto.SetName.HasValue ? Loc.GetString(constructionProto.SetName) : proto.Name;
                    var desc = constructionProto.SetDescription.HasValue ? Loc.GetString(constructionProto.SetDescription) : proto.Description;

                    constructionProto.Name = name;
                    constructionProto.Description = desc;

                    _recipesMetadataCache.Add(constructionProto.ID, entityId);
                } while (stack.Count > 0);
            }
        }

        private void OnConstructionGuideReceived(ResponseConstructionGuide ev)
        {
            _guideCache[ev.ConstructionId] = ev.Guide;
            ConstructionGuideAvailable?.Invoke(this, ev.ConstructionId);
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            base.Shutdown();

            CommandBinds.Unregister<ConstructionSystem>();
        }

        public ConstructionGuide? GetGuide(ConstructionPrototype prototype)
        {
            if (_guideCache.TryGetValue(prototype.ID, out var guide))
                return guide;

            RaiseNetworkEvent(new RequestConstructionGuide(prototype.ID));
            return null;
        }

        private void HandleConstructionGhostExamined(EntityUid uid, ConstructionGhostComponent component, ExaminedEvent args)
        {
            if (component.Prototype?.Name is null)
                return;

            using (args.PushGroup(nameof(ConstructionGhostComponent)))
            {
                args.PushMarkup(Loc.GetString(
                    "construction-ghost-examine-message",
                    ("name", component.Prototype.Name)));

                if (!PrototypeManager.Resolve(component.Prototype.Graph, out var graph))
                    return;

                var startNode = graph.Nodes[component.Prototype.StartNode];

                if (!graph.TryPath(component.Prototype.StartNode, component.Prototype.TargetNode, out var path) ||
                    !startNode.TryGetEdge(path[0].Name, out var edge))
                {
                    return;
                }

                foreach (var step in edge.Steps)
                {
                    step.DoExamine(args);
                }
            }
        }

        public event EventHandler<CraftingAvailabilityChangedArgs>? CraftingAvailabilityChanged;
        public event EventHandler<string>? ConstructionGuideAvailable;
        public event EventHandler? ToggleCraftingWindow;
        public event EventHandler? FlipConstructionPrototype;

        private void HandleAckStructure(AckStructureConstructionMessage msg)
        {
            // We get sent a NetEntity but it actually corresponds to our local Entity.
            ClearGhost(msg.GhostId);
        }

        private void HandlePlayerAttached(LocalPlayerAttachedEvent msg)
        {
            var available = IsCraftingAvailable(msg.Entity);
            UpdateCraftingAvailability(available);
        }

        private bool HandleOpenCraftingMenu(in PointerInputCmdHandler.PointerInputCmdArgs args)
        {
            if (args.State == BoundKeyState.Down)
                ToggleCraftingWindow?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private bool HandleFlip(in PointerInputCmdHandler.PointerInputCmdArgs args)
        {
            if (args.State == BoundKeyState.Down)
                FlipConstructionPrototype?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void UpdateCraftingAvailability(bool available)
        {
            if (CraftingEnabled == available)
                return;

            CraftingAvailabilityChanged?.Invoke(this, new CraftingAvailabilityChangedArgs(available));
            CraftingEnabled = available;
        }

        private static bool IsCraftingAvailable(EntityUid? entity)
        {
            if (entity == default)
                return false;

            // TODO: Decide if entity can craft, using capabilities or something
            return true;
        }

        private bool HandleUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
        {
            if (args.EntityUid.IsValid() &&
                IsClientSide(args.EntityUid) &&
                HasComp<ConstructionGhostComponent>(args.EntityUid))
            {
                TryStartConstruction(args.EntityUid);
                return true;
            }

            if (!TryFindGhostForUse(args.EntityUid, args.Coordinates, out var ghostUid))
                return false;

            TryStartConstruction(ghostUid.Value);
            return true;
        }

        /// <summary>
        /// Creates a construction ghost at the given location.
        /// </summary>
        public void SpawnGhost(ConstructionPrototype prototype, EntityCoordinates loc, Direction dir)
            => TrySpawnGhost(prototype, loc, dir, out _);

        /// <summary>
        /// Creates a construction ghost at the given location.
        /// </summary>
        public bool TrySpawnGhost(
            ConstructionPrototype prototype,
            EntityCoordinates loc,
            Direction dir,
            [NotNullWhen(true)] out EntityUid? ghost)
        {
            ghost = null;
            if (_playerManager.LocalEntity is not { } user ||
                !user.IsValid())
            {
                return false;
            }

            if (!TryGetRecipePrototype(prototype.ID, out var targetProtoId) || !PrototypeManager.TryIndex(targetProtoId, out EntityPrototype? targetProto))
                return false;

            if (GhostPresent(loc))
                return false;

            var predicate = GetPredicate(prototype.CanBuildInImpassable, _transformSystem.ToMapCoordinates(loc));
            if (!_examineSystem.InRangeUnOccluded(user, loc, 20f, predicate: predicate))
                return false;

            if (!UsesPlacementDrivenValidation(prototype) &&
                !CheckConstructionConditions(prototype, loc, dir, user, showPopup: true))
            {
                return false;
            }

            ghost = Spawn("constructionghost", loc);
            var comp = Comp<ConstructionGhostComponent>(ghost.Value);
            comp.Prototype = prototype;
            comp.GhostId = ghost.GetHashCode();
            if (TryResolveStrategicPlacementTarget(prototype, loc, out var placementTarget))
            {
                comp.PlacementTarget = placementTarget;
            }
            Comp<TransformComponent>(ghost.Value).LocalRotation = dir.ToAngle();
            _ghosts.Add(comp.GhostId, ghost.Value);

            var sprite = Comp<SpriteComponent>(ghost.Value);

            if (targetProto.Components.TryGetValue("Sprite", out _))
            {
                var dummy = EntityManager.SpawnEntity(targetProtoId, MapCoordinates.Nullspace);
                var targetSprite = EnsureComp<SpriteComponent>(dummy);
                EntityManager.System<AppearanceSystem>().OnChangeData(dummy, targetSprite);

                _sprite.CopySprite((dummy, targetSprite), (ghost.Value, sprite));

                for (var i = 0; i < sprite.AllLayers.Count(); i++)
                {
                    sprite.LayerSetShader(i, "unshaded");
                }

                Del(dummy);
            }
            else if (targetProto.TryGetComponent(out IconComponent? icon, EntityManager.ComponentFactory))
            {
                _sprite.AddBlankLayer((ghost.Value, sprite), 0);
                _sprite.LayerSetSprite((ghost.Value, sprite), 0, icon.Icon);
                sprite.LayerSetShader(0, "unshaded");
                _sprite.LayerSetVisible((ghost.Value, sprite), 0, true);
            }
            else
                return false;

            _sprite.SetColor((ghost.Value, sprite), new Color(48, 255, 48, 128));

            if (prototype.CanBuildInImpassable)
                EnsureComp<WallMountComponent>(ghost.Value).Arc = new(Math.Tau);

            return true;
        }

        private bool CheckConstructionConditions(ConstructionPrototype prototype, EntityCoordinates loc, Direction dir,
            EntityUid user, bool showPopup = false)
        {
            foreach (var condition in prototype.Conditions)
            {
                if (!condition.Condition(user, loc, dir))
                {
                    if (showPopup)
                    {
                        var message = condition.GenerateGuideEntry()?.Localization;
                        if (message != null)
                        {
                            // Show the reason to the user:
                            _popupSystem.PopupCoordinates(Loc.GetString(message), loc);
                        }
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if any construction ghosts are present at the given position
        /// </summary>
        private bool GhostPresent(EntityCoordinates loc)
        {
            foreach (var ghost in _ghosts)
            {
                if (Comp<TransformComponent>(ghost.Value).Coordinates.Equals(loc))
                    return true;
            }

            return false;
        }

        private bool TryFindGhostNearCoordinates(
            EntityCoordinates coordinates,
            [NotNullWhen(true)] out EntityUid? ghostUid)
        {
            ghostUid = null;

            var clickMapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
            var maxDistanceSquared = GhostClickCaptureRange * GhostClickCaptureRange;
            var bestDistanceSquared = float.MaxValue;

            foreach (var ghost in _ghosts.Values)
            {
                if (!Exists(ghost))
                    continue;

                var ghostMapCoordinates = _transformSystem.GetMapCoordinates(ghost);
                if (ghostMapCoordinates.MapId != clickMapCoordinates.MapId)
                    continue;

                var distanceSquared = (ghostMapCoordinates.Position - clickMapCoordinates.Position).LengthSquared();
                if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                ghostUid = ghost;
            }

            return ghostUid != null;
        }

        private bool TryFindGhostForUse(
            EntityUid clickedEntity,
            EntityCoordinates coordinates,
            [NotNullWhen(true)] out EntityUid? ghostUid)
        {
            ghostUid = null;

            if (clickedEntity.IsValid() &&
                HasComp<WH40KStrategicPointAnchorComponent>(clickedEntity) &&
                TryFindGhostForPlacementTarget(clickedEntity, coordinates, out ghostUid))
            {
                return true;
            }

            if (clickedEntity.IsValid())
                return false;

            if (TryFindStrategicGhostForCoordinates(coordinates, out ghostUid))
                return true;

            if (HasNonConstructionEntityAtCoordinates(coordinates))
                return false;

            return TryFindGhostNearCoordinates(coordinates, out ghostUid);
        }

        private bool HasNonConstructionEntityAtCoordinates(EntityCoordinates coordinates)
        {
            foreach (var uid in _lookup.GetEntitiesIntersecting(coordinates))
            {
                if (!Exists(uid) ||
                    uid == coordinates.EntityId ||
                    uid == _playerManager.LocalEntity)
                {
                    continue;
                }

                if (IsClientSide(uid) && HasComp<ConstructionGhostComponent>(uid))
                    continue;

                if (HasComp<WH40KStrategicPointAnchorComponent>(uid))
                    continue;

                return true;
            }

            return false;
        }

        private bool TryFindStrategicGhostForCoordinates(
            EntityCoordinates clickCoordinates,
            [NotNullWhen(true)] out EntityUid? ghostUid)
        {
            ghostUid = null;

            var clickMapCoordinates = _transformSystem.ToMapCoordinates(clickCoordinates);
            var bestDistanceSquared = float.MaxValue;
            var anchorCaptureSquared = StrategicAnchorClickCaptureRange * StrategicAnchorClickCaptureRange;
            var ghostCaptureSquared = StrategicGhostClickCaptureRange * StrategicGhostClickCaptureRange;

            foreach (var ghost in _ghosts.Values)
            {
                if (!Exists(ghost) ||
                    !TryComp<ConstructionGhostComponent>(ghost, out var ghostComp) ||
                    !TryResolveGhostStrategicAnchor(ghost, ghostComp, out var placementTarget) ||
                    !TryComp<WH40KStrategicPointAnchorComponent>(placementTarget.Value, out _) ||
                    ghostComp.Prototype?.Conditions.OfType<WH40KStrategicPointAnchorCondition>().FirstOrDefault() is not { } anchorCondition)
                {
                    continue;
                }

                var ghostMapCoordinates = _transformSystem.GetMapCoordinates(ghost);
                if (ghostMapCoordinates.MapId != clickMapCoordinates.MapId)
                    continue;

                var anchorMapCoordinates = _transformSystem.GetMapCoordinates(placementTarget.Value);
                var maxDistanceSquared = anchorCondition.MaxDistance * anchorCondition.MaxDistance;
                var ghostDistanceSquared = (ghostMapCoordinates.Position - clickMapCoordinates.Position).LengthSquared();
                var anchorDistanceSquared = (anchorMapCoordinates.Position - clickMapCoordinates.Position).LengthSquared();

                if (ghostDistanceSquared > ghostCaptureSquared &&
                    anchorDistanceSquared > anchorCaptureSquared)
                {
                    continue;
                }

                var distanceSquared = Math.Min(ghostDistanceSquared, anchorDistanceSquared);
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                if (distanceSquared > ghostCaptureSquared &&
                    distanceSquared > anchorCaptureSquared)
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                ghostUid = ghost;
            }

            return ghostUid != null;
        }

        private bool TryFindGhostForPlacementTarget(
            EntityUid placementTarget,
            EntityCoordinates clickCoordinates,
            [NotNullWhen(true)] out EntityUid? ghostUid)
        {
            ghostUid = null;

            if (!TryComp<WH40KStrategicPointAnchorComponent>(placementTarget, out var targetAnchor))
                return false;

            var clickMapCoordinates = _transformSystem.ToMapCoordinates(clickCoordinates);
            var anchorMapCoordinates = _transformSystem.GetMapCoordinates(placementTarget);
            var effectiveAnchorPosition = anchorMapCoordinates.Position + targetAnchor.BuiltOffset;
            var fallbackRangeSquared = StrategicAnchorGhostFallbackRange * StrategicAnchorGhostFallbackRange;
            var bestDistanceSquared = float.MaxValue;

            foreach (var ghost in _ghosts.Values)
            {
                if (!Exists(ghost) ||
                    !TryComp<ConstructionGhostComponent>(ghost, out var ghostComp) ||
                    !TryGetStrategicAnchorCondition(ghostComp, out var anchorCondition) ||
                    anchorCondition.PointType != targetAnchor.PointType)
                {
                    continue;
                }

                var ghostMapCoordinates = _transformSystem.GetMapCoordinates(ghost);
                if (ghostMapCoordinates.MapId != clickMapCoordinates.MapId)
                    continue;

                var exactMatch = TryResolveGhostStrategicAnchor(ghost, ghostComp, out var resolvedTarget) &&
                                 resolvedTarget == placementTarget;

                var distanceSquared = (ghostMapCoordinates.Position - clickMapCoordinates.Position).LengthSquared();
                if (!exactMatch)
                {
                    var distanceToBuildSquared = (ghostMapCoordinates.Position - effectiveAnchorPosition).LengthSquared();
                    if (distanceSquared > fallbackRangeSquared &&
                        distanceToBuildSquared > fallbackRangeSquared)
                    {
                        continue;
                    }

                    distanceSquared = Math.Min(distanceSquared, distanceToBuildSquared);
                }

                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                ghostUid = ghost;
                ghostComp.PlacementTarget = placementTarget;
            }

            return ghostUid != null;
        }

        private bool TryResolveGhostStrategicAnchor(
            EntityUid ghostUid,
            ConstructionGhostComponent ghostComp,
            [NotNullWhen(true)] out EntityUid? anchorUid)
        {
            anchorUid = null;

            var prototype = ghostComp.Prototype;
            if (prototype == null)
                return false;

            if (!TryGetStrategicAnchorCondition(ghostComp, out var anchorCondition))
                return false;

            var ghostCoordinates = Comp<TransformComponent>(ghostUid).Coordinates;

            if (_placementManager.CurrentMode is WH40KStrategicPointPlacement strategicPlacement &&
                strategicPlacement.PreviewAnchorUid is { Valid: true } previewAnchorUid &&
                TryComp<WH40KStrategicPointAnchorComponent>(previewAnchorUid, out var previewAnchor) &&
                IsMatchingStrategicAnchor(previewAnchorUid, previewAnchor, anchorCondition, ghostCoordinates))
            {
                anchorUid = previewAnchorUid;
                ghostComp.PlacementTarget = previewAnchorUid;
                return true;
            }

            if (ghostComp.PlacementTarget is { Valid: true } placementTarget &&
                Exists(placementTarget) &&
                TryComp<WH40KStrategicPointAnchorComponent>(placementTarget, out var cachedAnchor) &&
                IsMatchingStrategicAnchor(placementTarget, cachedAnchor, anchorCondition, ghostCoordinates))
            {
                anchorUid = placementTarget;
                return true;
            }

            if (TryResolveStrategicPlacementTarget(
                    prototype,
                    ghostCoordinates,
                    out anchorUid))
            {
                ghostComp.PlacementTarget = anchorUid;
                return true;
            }

            if (TryResolveStrategicPlacementTargetRelaxed(
                    prototype,
                    ghostCoordinates,
                    StrategicAnchorGhostFallbackRange,
                    out anchorUid))
            {
                ghostComp.PlacementTarget = anchorUid;
                return true;
            }

            return false;
        }

        private bool TryResolveStrategicPlacementTargetRelaxed(
            ConstructionPrototype prototype,
            EntityCoordinates location,
            float maxDistance,
            [NotNullWhen(true)] out EntityUid? anchorUid)
        {
            anchorUid = null;

            if (!UsesPlacementDrivenValidation(prototype) ||
                prototype.Conditions.OfType<WH40KStrategicPointAnchorCondition>().FirstOrDefault() is not { } anchorCondition)
            {
                return false;
            }

            var targetMapCoordinates = _transformSystem.ToMapCoordinates(location);
            var maxDistanceSquared = maxDistance * maxDistance;
            var bestDistanceSquared = float.MaxValue;

            var anchors = EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>();
            while (anchors.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (anchor.PointType != anchorCondition.PointType)
                    continue;

                if (anchorCondition.RequireFree &&
                    anchor.BuiltPoint is { Valid: true } builtPoint &&
                    Exists(builtPoint))
                {
                    continue;
                }

                var anchorMapCoordinates = _transformSystem.GetMapCoordinates(uid, xform: xform);
                if (anchorMapCoordinates.MapId != targetMapCoordinates.MapId)
                    continue;

                var effectiveAnchorPosition = anchorMapCoordinates.Position + anchor.BuiltOffset;
                var distanceSquared = (effectiveAnchorPosition - targetMapCoordinates.Position).LengthSquared();
                if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                anchorUid = uid;
            }

            return anchorUid != null;
        }

        private static bool TryGetStrategicAnchorCondition(
            ConstructionGhostComponent ghostComp,
            [NotNullWhen(true)] out WH40KStrategicPointAnchorCondition? anchorCondition)
        {
            anchorCondition = ghostComp.Prototype?.Conditions
                .OfType<WH40KStrategicPointAnchorCondition>()
                .FirstOrDefault();
            return anchorCondition != null;
        }

        private bool TryResolveStrategicPlacementTarget(
            ConstructionPrototype prototype,
            EntityCoordinates location,
            [NotNullWhen(true)] out EntityUid? anchorUid)
        {
            anchorUid = null;

            if (!UsesPlacementDrivenValidation(prototype) ||
                prototype.Conditions.OfType<WH40KStrategicPointAnchorCondition>().FirstOrDefault() is not { } anchorCondition)
            {
                return false;
            }

            if (_placementManager.CurrentMode is WH40KStrategicPointPlacement strategicPlacement &&
                strategicPlacement.PreviewAnchorUid is { Valid: true } previewAnchorUid &&
                TryComp<WH40KStrategicPointAnchorComponent>(previewAnchorUid, out var previewAnchor) &&
                IsMatchingStrategicAnchor(previewAnchorUid, previewAnchor, anchorCondition, location))
            {
                anchorUid = previewAnchorUid;
                return true;
            }

            var targetMapCoordinates = _transformSystem.ToMapCoordinates(location);
            var maxDistanceSquared = anchorCondition.MaxDistance * anchorCondition.MaxDistance;
            var bestDistanceSquared = float.MaxValue;

            var anchors = EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>();
            while (anchors.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (anchor.PointType != anchorCondition.PointType)
                    continue;

                if (anchorCondition.RequireFree &&
                    anchor.BuiltPoint is { Valid: true } builtPoint &&
                    Exists(builtPoint))
                {
                    continue;
                }

                var anchorMapCoordinates = _transformSystem.GetMapCoordinates(uid, xform: xform);
                if (anchorMapCoordinates.MapId != targetMapCoordinates.MapId)
                    continue;

                var effectiveAnchorPosition = anchorMapCoordinates.Position + anchor.BuiltOffset;
                var distanceSquared = (effectiveAnchorPosition - targetMapCoordinates.Position).LengthSquared();
                if (distanceSquared > maxDistanceSquared || distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                anchorUid = uid;
            }

            return anchorUid != null;
        }

        private bool IsMatchingStrategicAnchor(
            EntityUid anchorUid,
            WH40KStrategicPointAnchorComponent anchor,
            WH40KStrategicPointAnchorCondition anchorCondition,
            EntityCoordinates location)
        {
            if (anchor.PointType != anchorCondition.PointType)
                return false;

            if (anchorCondition.RequireFree &&
                anchor.BuiltPoint is { Valid: true } builtPoint &&
                Exists(builtPoint))
            {
                return false;
            }

            var targetMapCoordinates = _transformSystem.ToMapCoordinates(location);
            var anchorMapCoordinates = _transformSystem.GetMapCoordinates(anchorUid);
            if (anchorMapCoordinates.MapId != targetMapCoordinates.MapId)
                return false;

            var effectiveAnchorPosition = anchorMapCoordinates.Position + anchor.BuiltOffset;
            var maxDistanceSquared = anchorCondition.MaxDistance * anchorCondition.MaxDistance;
            return (effectiveAnchorPosition - targetMapCoordinates.Position).LengthSquared() <= maxDistanceSquared;
        }

        private static bool UsesPlacementDrivenValidation(ConstructionPrototype prototype)
        {
            return prototype.PlacementMode == "WH40KStrategicPointPlacement";
        }

        public void TryStartConstruction(EntityUid ghostId, ConstructionGhostComponent? ghostComp = null)
        {
            if (!Resolve(ghostId, ref ghostComp))
                return;

            if (ghostComp.Prototype == null)
            {
                throw new ArgumentException($"Can't start construction for a ghost with no prototype. Ghost id: {ghostId}");
            }

            var transform = Comp<TransformComponent>(ghostId);
            EntityUid? resolvedPlacementTarget = null;
            if (UsesPlacementDrivenValidation(ghostComp.Prototype))
            {
                TryResolveGhostStrategicAnchor(ghostId, ghostComp, out resolvedPlacementTarget);
            }

            var targetToSend = resolvedPlacementTarget ?? ghostComp.PlacementTarget;
            NetEntity? placementTarget = targetToSend is { Valid: true } target
                ? GetNetEntity(target)
                : null;
            var msg = new TryStartStructureConstructionMessage(
                GetNetCoordinates(transform.Coordinates),
                ghostComp.Prototype.ID,
                transform.LocalRotation,
                ghostId.GetHashCode(),
                placementTarget);
            RaiseNetworkEvent(msg);
        }

        /// <summary>
        /// Starts constructing an item underneath the attached entity.
        /// </summary>
        public void TryStartItemConstruction(string prototypeName)
        {
            RaiseNetworkEvent(new TryStartItemConstructionMessage(prototypeName));
        }

        /// <summary>
        /// Removes a construction ghost entity with the given ID.
        /// </summary>
        public void ClearGhost(int ghostId)
        {
            if (!_ghosts.TryGetValue(ghostId, out var ghost))
                return;

            QueueDel(ghost);
            _ghosts.Remove(ghostId);
        }

        /// <summary>
        /// Removes all construction ghosts.
        /// </summary>
        public void ClearAllGhosts()
        {
            foreach (var ghost in _ghosts.Values)
            {
                QueueDel(ghost);
            }

            _ghosts.Clear();
        }
    }

    public sealed class CraftingAvailabilityChangedArgs : EventArgs
    {
        public bool Available { get; }

        public CraftingAvailabilityChangedArgs(bool available)
        {
            Available = available;
        }
    }
}
