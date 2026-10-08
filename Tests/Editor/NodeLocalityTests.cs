using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// Covers the placement objective: nodes must land near the nodes that consume them, not merely in
    /// whichever column happens to be lightest.
    /// </summary>
    public class NodeLocalityTests
    {
        static string TerrainPath => TestPaths.Terrain;
        static string WavePath => TestPaths.PropertyTypes;

        /// <summary>
        /// Mean number of columns between a node and each of its consumers. This is the distance a reader
        /// has to trace with their eye, so it is the quantity locality is supposed to shrink.
        /// </summary>
        static float AverageConsumerDistance(GraphModel model, GraphLayout layout)
        {
            var columnOf = new Dictionary<string, int>();
            foreach (KeyValuePair<string, Rect> entry in layout.nodeRects)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                foreach (LayoutColumn column in layout.columns)
                {
                    float distance = Mathf.Abs(column.x - entry.Value.x);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = column.index;
                }
                columnOf[entry.Key] = best;
            }

            var consumers = new Dictionary<string, List<string>>();
            foreach (GraphNode node in model.nodes) consumers[node.id] = new List<string>();
            foreach (GraphEdge edge in model.edges)
                if (consumers.ContainsKey(edge.outputNodeId)) consumers[edge.outputNodeId].Add(edge.inputNodeId);

            float total = 0f;
            int counted = 0;
            foreach (GraphNode node in model.nodes)
            {
                if (consumers[node.id].Count == 0) continue;
                float sum = 0f;
                foreach (string consumer in consumers[node.id]) sum += columnOf[consumer] - columnOf[node.id];
                total += sum / consumers[node.id].Count;
                counted++;
            }
            return counted == 0 ? 0f : total / counted;
        }

        static float WorstConsumerDistance(GraphModel model, GraphLayout layout)
        {
            var columnOf = new Dictionary<string, int>();
            foreach (KeyValuePair<string, Rect> entry in layout.nodeRects)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                foreach (LayoutColumn column in layout.columns)
                {
                    float distance = Mathf.Abs(column.x - entry.Value.x);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = column.index;
                }
                columnOf[entry.Key] = best;
            }

            var consumers = new Dictionary<string, List<string>>();
            foreach (GraphNode node in model.nodes) consumers[node.id] = new List<string>();
            foreach (GraphEdge edge in model.edges)
                if (consumers.ContainsKey(edge.outputNodeId)) consumers[edge.outputNodeId].Add(edge.inputNodeId);

            float worst = 0f;
            foreach (GraphNode node in model.nodes)
            {
                if (consumers[node.id].Count == 0) continue;
                float sum = 0f;
                foreach (string consumer in consumers[node.id]) sum += columnOf[consumer] - columnOf[node.id];
                worst = Mathf.Max(worst, sum / consumers[node.id].Count);
            }
            return worst;
        }

        [Test]
        public void LocalityShortensTheDistanceToConsumers()
        {
            foreach (string path in new[] { WavePath, TerrainPath })
            {
                GraphModel model = ShaderGraphParser.Parse(path);
                Assert.IsNotNull(model, "fixture must parse");

                GraphLayout spread = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                    new LayoutOptions { balance = 1f, locality = 0f });
                GraphLayout local = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                    new LayoutOptions { balance = 1f, locality = 1f });

                float spreadAverage = AverageConsumerDistance(model, spread);
                float localAverage = AverageConsumerDistance(model, local);

                Assert.LessOrEqual(localAverage, spreadAverage,
                    $"{Path.GetFileName(path)}: hugging consumers must not lengthen the average trace");
                Assert.LessOrEqual(WorstConsumerDistance(model, local), WorstConsumerDistance(model, spread),
                    $"{Path.GetFileName(path)}: the worst trace must not get longer");
            }
        }

        [Test]
        public void LocalityKeepsEveryNodeLeftOfItsConsumers()
        {
            GraphModel model = ShaderGraphParser.Parse(WavePath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                new LayoutOptions { balance = 1f, locality = 1f });

            foreach (GraphEdge edge in model.edges)
                Assert.Less(layout.nodeRects[edge.outputNodeId].xMin, layout.nodeRects[edge.inputNodeId].xMin,
                    "locality must never pull a node past something that reads it");
        }

        [Test]
        public void LocalityIsDeterministic()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            var options = new LayoutOptions { balance = 1f, locality = 1f };

            GraphLayout first = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, options);
            GraphLayout second = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, options);

            Assert.AreEqual(first.size, second.size);
            foreach (KeyValuePair<string, Rect> entry in first.nodeRects)
                Assert.AreEqual(entry.Value, second.nodeRects[entry.Key], $"node {entry.Key} must not move between runs");
        }

        [Test]
        public void ZeroBalanceStillPinsEverySourceToTheFirstColumn()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                new LayoutOptions { balance = 0f, locality = 1f });

            float firstColumn = float.MaxValue;
            foreach (Rect rect in layout.nodeRects.Values) firstColumn = Mathf.Min(firstColumn, rect.x);

            int inFirst = 0;
            foreach (Rect rect in layout.nodeRects.Values)
                if (Mathf.Approximately(rect.x, firstColumn)) inFirst++;

            Assert.Greater(inFirst, 1, "balance 0 must keep the classic longest-path ranking");
        }
    }
}
