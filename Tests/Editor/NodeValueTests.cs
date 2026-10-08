using NUnit.Framework;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class NodeValueTests
    {
        static string WavePath => TestPaths.PropertyTypes;
        static string TerrainPath => TestPaths.Terrain;
        static string UnlitPath => TestPaths.Unlit;

        static GraphNode Find(GraphModel model, string title)
        {
            Assert.IsNotNull(model, "fixture must parse");
            foreach (GraphNode node in model.nodes)
                if (node.title == title) return node;
            Assert.Fail($"fixture must contain a node titled '{title}'");
            return null;
        }

        static GraphNode Value(GraphModel model, string title)
        {
            GraphNode node = Find(model, title);
            Assert.IsNotNull(node.value, $"node '{title}' must carry value metadata");
            return node;
        }

        [Test]
        public void PropertyNodeCarriesItsDataType()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);
            Assert.IsNotNull(model, "fixture must parse");

            Assert.AreEqual("Float", Value(model, "Base Size").value.typeLabel);
            Assert.AreEqual("Color", Value(model, "Color").value.typeLabel);
            Assert.AreEqual("Vector2", Value(model, "Vector 2").value.typeLabel);
        }

        [Test]
        public void PropertyNodeCarriesItsDefaultValue()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);

            Assert.IsTrue(Value(model, "Base Size").HasValue);
            Assert.AreEqual("0.5", Value(model, "Base Size").value.text);

            GraphNode color = Value(model, "Color");
            Assert.IsTrue(color.value.isColor, "a colour property must be flagged so a swatch can be drawn");
            Assert.AreEqual("#FF0000", color.value.text);
            Assert.AreEqual(1f, color.value.color.r, 0.01f);
            Assert.AreEqual(0f, color.value.color.g, 0.01f);
        }

        [Test]
        public void ConstantNodeReadsItsEditableSlotsNotItsStaleSerialisedValue()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);
            GraphNode vector = Value(model, "Vector 2");

            // The asset stores m_Value as (0,0) while the editable X and Y slots hold 0.5; the slots
            // are what the node actually outputs, so those are the ones that must be reported.
            Assert.AreEqual("Vector2", vector.value.typeLabel);
            Assert.IsTrue(vector.value.hasValue);
            Assert.AreEqual("(0.5, 0.5)", vector.value.text);
        }

        [Test]
        public void ConnectedInputIsNotReportedAsAValue()
        {
            GraphModel model = ShaderGraphParser.Parse(UnlitPath);

            foreach (GraphNode node in model.nodes)
            {
                if (node.value == null || !node.value.hasValue) continue;
                foreach (PortSlot port in node.ports)
                    Assert.IsFalse(port.isInput && port.connected,
                        $"node {node.title} reports a value but its input {port.displayName} is driven by a wire");
            }
        }

        [Test]
        public void NonValueNodesGetNoValueBlock()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            foreach (GraphNode node in model.nodes)
            {
                if (node.typeName == null) continue;
                bool valueCarrier = node.typeName.EndsWith("PropertyNode")
                                 || node.typeName.EndsWith("Vector2Node")
                                 || node.typeName.EndsWith("ColorNode");
                if (valueCarrier) continue;
                Assert.IsFalse(node.HasValue, $"{node.typeName} must not claim to carry a displayable value");
            }
        }

        [Test]
        public void ValueRowOnlyGrowsNodesWhenEnabled()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);
            GraphNode withValue = Value(model, "Base Size");

            LayoutMetrics off = LayoutMetrics.FromFontScale(1f, 1f, false);
            LayoutMetrics on = LayoutMetrics.FromFontScale(1f, 1f, true);

            float tight = GraphAutoLayoutEngine.EstimateNodeHeight(withValue, off);
            float loose = GraphAutoLayoutEngine.EstimateNodeHeight(withValue, on);

            Assert.AreEqual(tight + on.PortRowHeight, loose, 0.01f,
                "enabling value rows must add exactly one row to a value-carrying node");

            GraphNode withoutValue = null;
            foreach (GraphNode node in model.nodes)
                if (!node.HasValue && !node.IsStackBlock) { withoutValue = node; break; }
            Assert.IsNotNull(withoutValue, "fixture must contain a node without a value");
            Assert.AreEqual(GraphAutoLayoutEngine.EstimateNodeHeight(withoutValue, off),
                GraphAutoLayoutEngine.EstimateNodeHeight(withoutValue, on), 0.01f,
                "nodes without a value must keep their height");
        }

        [Test]
        public void ValueRowsSurviveTheAspectSolver()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);
            LayoutMetrics on = LayoutMetrics.FromFontScale(1f, 1f, true);

            GraphLayout plain = GraphAutoLayoutEngine.Apply(model, on, 1f);
            GraphLayout solved = GraphAutoLayoutEngine.ApplyWithTargetAspect(model, on, 1f, 1.6f);

            Assert.AreEqual(plain.size.x, solved.size.x, 0.01f, "the solver must not change the width");

            foreach (GraphNode node in model.nodes)
            {
                if (!node.HasValue || node.IsStackBlock) continue;
                Assert.GreaterOrEqual(solved.nodeRects[node.id].height, on.TitleHeight + 2f * on.PortRowHeight,
                    $"node {node.title} must keep room for its title and value row after the solver runs");
            }
        }

        [Test]
        public void MissingValueDataNeverThrows()
        {
            Assert.DoesNotThrow(() => ShaderGraphParser.ParseText("{}"));
            Assert.DoesNotThrow(() => ShaderGraphParser.ParseText(""));
            Assert.DoesNotThrow(() => ShaderGraphParser.ParseText("{ \"m_Type\": \"UnityEditor.ShaderGraph.GraphData\" }"));
        }
    }
}
