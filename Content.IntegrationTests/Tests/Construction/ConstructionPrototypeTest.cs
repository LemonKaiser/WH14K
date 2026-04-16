using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Utility;
using Content.Server.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction
{
    [TestFixture]
    public sealed class ConstructionPrototypeTest : GameTest
    {
        // discount linter for construction graphs
        // TODO: Create serialization validators for these?
        // Top test definitely can be but writing a serializer takes ages.

        private static readonly string[] _constructablePrototypes = GameDataScrounger.EntitiesWithComponent("Construction");
        private static readonly string[] _constructions = GameDataScrounger.PrototypesOfKind<ConstructionPrototype>();

        /// <summary>
        /// Checks every entity prototype with a construction component has a valid start node.
        /// </summary>
        [Test]
        [TestOf(typeof(ConstructionComponent))]
        [Description("Tests that a given entity specifies a valid node for construction, and optionally a valid one for deconstruction.")]
        public async Task ConstructionComponentsValid()
        {
            var pair = Pair;
            var server = pair.Server;

            var protoMan = server.ResolveDependency<IPrototypeManager>();

            await server.WaitAssertion(() =>
            {
                var errors = new StringBuilder();

                foreach (var protoKey in _constructablePrototypes)
                {
                    var proto = protoMan.Index(protoKey);
                    var construction = (ConstructionComponent)proto.Components["Construction"].Component;

                    if (!protoMan.TryIndex(construction.Graph, out ConstructionGraphPrototype graph))
                    {
                        errors.AppendLine($"Found no graph \"{construction.Graph}\" for construction entity \"{proto.ID}\"!");
                        continue;
                    }

                    if (!graph.Nodes.ContainsKey(construction.Node))
                    {
                        errors.AppendLine($"Found no node \"{construction.Node}\" on graph \"{graph.ID}\" for entity \"{proto.ID}\"!");
                    }

                    if (construction.DeconstructionNode is { } target && !graph.Nodes.ContainsKey(target))
                    {
                        errors.AppendLine($"Invalid deconstruction node \"{target}\" on graph \"{graph.ID}\" for construction entity \"{proto.ID}\"!");
                    }
                }

                FailIfErrors(errors);
            });
        }

        [Test]
        [TestOf(typeof(ConstructionPrototype))]
        [Description("Tests that a given construction prototype has a valid starting and target node, and a valid path between them.")]
        public async Task ConstructionFormsValidGraphs()
        {
            var pair = Pair;
            var server = pair.Server;

            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var entMan = server.ResolveDependency<IEntityManager>();

            await server.WaitAssertion(() =>
            {
                var errors = new StringBuilder();

                foreach (var protoKey in _constructions)
                {
                    var proto = protoMan.Index<ConstructionPrototype>(protoKey);
                    var start = proto.StartNode;
                    var target = proto.TargetNode;

                    if (!protoMan.TryIndex(proto.Graph, out ConstructionGraphPrototype graph))
                    {
                        errors.AppendLine($"Found no graph \"{proto.Graph}\" for construction prototype \"{proto.ID}\"!");
                        continue;
                    }

                    var hasStart = graph.Nodes.ContainsKey(start);
                    if (!hasStart)
                    {
                        errors.AppendLine($"Found no startNode \"{start}\" on graph \"{graph.ID}\" for construction prototype \"{proto.ID}\"!");
                    }

                    var hasTarget = graph.Nodes.ContainsKey(target);
                    if (!hasTarget)
                    {
                        errors.AppendLine($"Found no targetNode \"{target}\" on graph \"{graph.ID}\" for construction prototype \"{proto.ID}\"!");
                    }

                    if (!hasStart || !hasTarget)
                        continue;

                    if (!graph.TryPath(start, target, out var path))
                    {
                        errors.AppendLine($"Unable to find path from \"{start}\" to \"{target}\" on graph \"{graph.ID}\" for construction prototype \"{proto.ID}\".");
                        continue;
                    }

                    if (path.Length < 1)
                    {
                        errors.AppendLine($"Unable to find path from \"{start}\" to \"{target}\" on graph \"{graph.ID}\" for construction prototype \"{proto.ID}\".");
                        continue;
                    }

                    var next = path[0];
                    var nextId = next.Entity.GetId(null, null, new(entMan));

                    if (nextId == null)
                    {
                        errors.AppendLine($"The next node ({next.Name}) in the path from the start node ({start}) to the target node ({target}) must specify an entity! Graph: {graph.ID}");
                        continue;
                    }

                    if (!protoMan.TryIndex(nextId, out EntityPrototype entity))
                    {
                        errors.AppendLine($"The next node ({next.Name}) in the path from the start node ({start}) to the target node ({target}) specified an invalid entity prototype ({nextId} [{next.Entity}])");
                        continue;
                    }

                    if (!entity.Components.ContainsKey("Construction"))
                    {
                        errors.AppendLine($"The next node ({next.Name}) in the path from the start node ({start}) to the target node ({target}) specified an entity prototype ({next.Entity}) without a ConstructionComponent.");
                    }
                }

                FailIfErrors(errors);
            });
        }

        private static void FailIfErrors(StringBuilder errors)
        {
            if (errors.Length == 0)
                return;

            Assert.Fail(errors.ToString());
        }
    }
}
