using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class ColumnBalanceTests
    {
        static string TerrainPath => TestPaths.Terrain;

        static float TallestColumn(GraphLayout layout)
        {
            var columns = new Dictionary<float, float>();
            foreach (KeyValuePair<string, Rect> entry in layout.nodeRects)
            {
                Rect rect = entry.Value;
                columns.TryGetValue(rect.x, out float height);
                columns[rect.x] = Mathf.Max(height, rect.y + rect.height);
            }

            float tallest = 0f;
            foreach (float height in columns.Values) tallest = Mathf.Max(tallest, height);
            return tallest;
        }

        [Test]
        public void BalancingLowersTheTallestColumn()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            GraphLayout flat = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 0f);
            GraphLayout balanced = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            Assert.Less(TallestColumn(balanced), TallestColumn(flat),
                "balancing must even out the column heights");
        }

        [Test]
        public void EveryBalanceKeepsEdgesForward()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            foreach (float balance in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, balance);
                foreach (GraphEdge edge in model.edges)
                    Assert.Less(layout.nodeRects[edge.outputNodeId].xMin, layout.nodeRects[edge.inputNodeId].xMin,
                        $"edge must still run forward at balance {balance}");
            }
        }
    }
}
