using System.IO;
using NUnit.Framework;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class ShaderGraphParserTests
    {
        static string FixturePath => TestPaths.Unlit;

        [TearDown]
        public void TearDown()
        {
            // The parser caches by path for the whole process, so a failing assertion must not leave a
            // half-validated entry behind for whichever test file runs next.
            ShaderGraphParser.Invalidate(FixturePath);
        }

        [Test]
        public void Parse_ReadsAllNodesAndEdges()
        {
            GraphModel model = ShaderGraphParser.Parse(FixturePath);

            Assert.IsNotNull(model, "fixture must parse");
            Assert.AreEqual(8, model.nodes.Count, "node count must match the Shader Graph editor");
            Assert.AreEqual(4, model.edges.Count, "edge count must match the Shader Graph editor");
            Assert.AreEqual(model.nodes.Count, model.nodeById.Count, "every node must be indexed by id");
        }

        [Test]
        public void Parse_ResolvesSlotTypesAndDirections()
        {
            GraphModel model = ShaderGraphParser.Parse(FixturePath);

            int inputs = 0;
            int outputs = 0;
            foreach (GraphNode node in model.nodes)
            {
                foreach (PortSlot port in node.ports)
                {
                    Assert.IsNotNull(port.typeName, $"slot {port.id} must expose its material slot type");
                    if (port.isInput) inputs++;
                    else outputs++;
                }
            }

            Assert.Greater(inputs, 0, "graph must expose input slots");
            Assert.Greater(outputs, 0, "graph must expose output slots");
        }

        [Test]
        public void Parse_ResolvesEdgeEndpointsToPorts()
        {
            GraphModel model = ShaderGraphParser.Parse(FixturePath);

            foreach (GraphEdge edge in model.edges)
            {
                Assert.IsTrue(model.nodeById.ContainsKey(edge.outputNodeId), "output node must exist");
                Assert.IsTrue(model.nodeById.ContainsKey(edge.inputNodeId), "input node must exist");
                Assert.IsTrue(model.TryGetPort(edge.outputNodeId, edge.outputSlotId, out PortSlot output), "output port must resolve");
                Assert.IsTrue(model.TryGetPort(edge.inputNodeId, edge.inputSlotId, out PortSlot input), "input port must resolve");
                Assert.IsFalse(output.isInput, "edge output port must be an output slot");
                Assert.IsTrue(input.isInput, "edge input port must be an input slot");
            }
        }

        [Test]
        public void Parse_UsesCacheUntilFileChanges()
        {
            GraphModel first = ShaderGraphParser.Parse(FixturePath);
            GraphModel second = ShaderGraphParser.Parse(FixturePath);
            Assert.AreSame(first, second, "unchanged file must be served from cache");

            ShaderGraphParser.Invalidate(FixturePath);
            GraphModel third = ShaderGraphParser.Parse(FixturePath);
            Assert.AreNotSame(first, third, "invalidated cache must reparse the file");
            Assert.AreEqual(first.nodes.Count, third.nodes.Count, "reparse must yield the same node count");
        }

        [Test]
        public void Parse_MissingFileReturnsNull()
        {
            Assert.IsNull(ShaderGraphParser.Parse(TestPaths.Missing));
        }

        [Test]
        public void Parse_DoesNotModifySourceFile()
        {
            byte[] before = File.ReadAllBytes(FixturePath);
            ShaderGraphParser.Parse(FixturePath);
            ShaderGraphParser.Invalidate(FixturePath);
            ShaderGraphParser.Parse(FixturePath);
            byte[] after = File.ReadAllBytes(FixturePath);

            Assert.AreEqual(before.Length, after.Length, "source asset length must not change");
            CollectionAssert.AreEqual(before, after, "source asset bytes must not change");
        }
    }
}
