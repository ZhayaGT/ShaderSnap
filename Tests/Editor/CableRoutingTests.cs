using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class CableRoutingTests
    {
        static string TerrainPath => TestPaths.Terrain;

        static GraphLayout Lay(string path, float balance = 1f)
        {
            GraphModel model = ShaderGraphParser.Parse(path);
            Assert.IsNotNull(model, "fixture must parse");
            return GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, balance);
        }

        [Test]
        public void EveryEdgeGetsARoute()
        {
            GraphLayout layout = Lay(TerrainPath);

            int expected = 0;
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            foreach (GraphEdge edge in model.edges)
                if (model.TryGetPort(edge.outputNodeId, edge.outputSlotId, out _) &&
                    model.TryGetPort(edge.inputNodeId, edge.inputSlotId, out _)) expected++;

            Assert.AreEqual(expected, layout.routes.Count, "every edge with resolvable ports must be routed");
            foreach (WireRoute route in layout.routes)
                Assert.GreaterOrEqual(route.points.Count, 2, "a route needs at least a start and an end");
        }

        [Test]
        public void RoutesAreOrthogonal()
        {
            GraphLayout layout = Lay(TerrainPath);

            foreach (WireRoute route in layout.routes)
            {
                for (int i = 1; i < route.points.Count; i++)
                {
                    Vector2 delta = route.points[i] - route.points[i - 1];
                    bool horizontal = Mathf.Abs(delta.y) < 0.01f;
                    bool vertical = Mathf.Abs(delta.x) < 0.01f;
                    Assert.IsTrue(horizontal || vertical,
                        $"segment {i} of a route must be axis aligned, delta was {delta}");
                }
            }
        }

        [Test]
        public void NoWireSegmentCrossesANodeBox()
        {
            GraphLayout layout = Lay(TerrainPath);
            var boxes = new List<Rect>(layout.nodeRects.Values);

            foreach (WireRoute route in layout.routes)
            {
                for (int i = 1; i < route.points.Count; i++)
                {
                    Vector2 a = route.points[i - 1];
                    Vector2 b = route.points[i];
                    Rect span = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

                    foreach (Rect box in boxes)
                    {
                        Rect shrunk = new Rect(box.x + 0.5f, box.y + 0.5f, box.width - 1f, box.height - 1f);
                        Assert.IsFalse(span.Overlaps(shrunk),
                            $"wire segment ({a}) -> ({b}) runs across node box {box}");
                    }
                }
            }
        }

        [Test]
        public void RoutesStartAndEndOnTheirPorts()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            foreach (WireRoute route in layout.routes)
            {
                Rect source = layout.nodeRects[route.edge.outputNodeId];
                Rect target = layout.nodeRects[route.edge.inputNodeId];
                Vector2 start = GraphAutoLayoutEngine.PortCenter(source, model.nodeById[route.edge.outputNodeId],
                    route.outputPort, LayoutMetrics.Default);
                Vector2 end = GraphAutoLayoutEngine.PortCenter(target, model.nodeById[route.edge.inputNodeId],
                    route.inputPort, LayoutMetrics.Default);

                Assert.AreEqual(start.x, route.points[0].x, 0.01f, "route must start on the output port");
                Assert.AreEqual(start.y, route.points[0].y, 0.01f, "route must start on the output port");
                Assert.AreEqual(end.x, route.points[route.points.Count - 1].x, 0.01f, "route must end on the input port");
                Assert.AreEqual(end.y, route.points[route.points.Count - 1].y, 0.01f, "route must end on the input port");
            }
        }

        [Test]
        public void NoTwoWiresShareAVerticalLaneInTheSameGap()
        {
            GraphLayout layout = Lay(TerrainPath);
            float nodeWidth = LayoutMetrics.Default.NodeWidth;

            var columns = new SortedSet<float>();
            foreach (Rect rect in layout.nodeRects.Values) columns.Add(rect.x);
            var ordered = new List<float>(columns);
            Assert.Greater(ordered.Count, 1, "fixture must span more than one column");

            for (int i = 0; i + 1 < ordered.Count; i++)
            {
                float left = ordered[i] + nodeWidth;
                float right = ordered[i + 1];
                var laneXs = new List<float>();

                foreach (WireRoute route in layout.routes)
                {
                    for (int p = 1; p < route.points.Count; p++)
                    {
                        Vector2 a = route.points[p - 1];
                        Vector2 b = route.points[p];
                        if (Mathf.Abs(a.x - b.x) > 0.01f) continue;
                        if (a.x <= left + 0.01f || a.x >= right - 0.01f) continue;

                        Assert.IsFalse(laneXs.Contains(a.x),
                            $"two wires share lane x={a.x} in gap [{left}, {right}]");
                        laneXs.Add(a.x);
                    }
                }
            }
        }
    }
}
