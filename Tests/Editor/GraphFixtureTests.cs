using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class GraphFixtureTests
    {
        static string UnlitPath => TestPaths.Unlit;
        static string TerrainPath => TestPaths.Terrain;

        [Test]
        public void Parse_MatchesEditorCountsOnSmallGraph()
        {
            GraphModel model = ShaderGraphParser.Parse(UnlitPath);

            Assert.AreEqual(8, model.nodes.Count, "small fixture node count");
            Assert.AreEqual(4, model.edges.Count, "small fixture edge count");
            Assert.AreEqual(17, CountPorts(model), "small fixture port count");
        }

        [Test]
        public void Parse_MatchesEditorCountsOnMidGraph()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            Assert.AreEqual(38, model.nodes.Count, "mid fixture node count");
            Assert.AreEqual(45, model.edges.Count, "mid fixture edge count");
            Assert.AreEqual(model.nodes.Count, model.nodeById.Count, "every node must be indexed");
        }

        [Test]
        public void Layout_KeepsMidGraphReadable()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout result = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);
            Vector2 size = result.size;

            Assert.AreEqual(model.nodes.Count, result.nodeRects.Count, "every node must receive a position");
            Assert.Greater(size.x, 0f);
            Assert.Greater(size.y, 0f);

            var rects = new List<Rect>(result.nodeRects.Values);
            for (int i = 0; i < rects.Count; i++)
            {
                Assert.GreaterOrEqual(rects[i].xMin, 0f, "node must stay inside the canvas");
                Assert.LessOrEqual(rects[i].xMax, size.x, "node must stay inside the canvas");
                Assert.LessOrEqual(rects[i].yMax, size.y, "node must stay inside the canvas");

                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), "nodes must not overlap");
            }

            foreach (GraphEdge edge in model.edges)
                Assert.Less(result.nodeRects[edge.outputNodeId].xMin, result.nodeRects[edge.inputNodeId].xMin,
                    "every edge must run from a lower rank to a higher rank");
        }

        static int CountPorts(GraphModel model)
        {
            int total = 0;
            foreach (GraphNode node in model.nodes) total += node.ports.Count;
            return total;
        }
    }
}
