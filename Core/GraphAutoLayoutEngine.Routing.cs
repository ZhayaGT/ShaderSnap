using System.Collections.Generic;
using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Wire routing phase of the shader-graph auto layout: turns each edge's chain of item rects into a
    /// polyline and assigns every segment a lane inside the gap it crosses.
    /// </summary>
    public static partial class GraphAutoLayoutEngine
    {
        static void BuildRoutes(GraphModel model, LayoutMetrics metrics, List<List<string>> chains,
                                Dictionary<string, Rect> itemRects, Dictionary<string, string> unitOfNode,
                                Dictionary<string, int> columnOf,
                                float[] columnX, float[] gapWidth, GraphLayout result)
        {
            var chainSegments = new List<Segment>[chains.Count];
            var outputPorts = new PortSlot[chains.Count];
            var inputPorts = new PortSlot[chains.Count];
            var sourcePoints = new Vector2[chains.Count];
            var targetPoints = new Vector2[chains.Count];
            var segments = new List<Segment>(chains.Count * 2);

            for (int c = 0; c < chains.Count; c++)
            {
                List<string> chain = chains[c];
                if (chain == null) continue;

                GraphEdge edge = model.edges[c];
                if (!model.TryGetPort(edge.outputNodeId, edge.outputSlotId, out PortSlot outputPort) ||
                    !model.TryGetPort(edge.inputNodeId, edge.inputSlotId, out PortSlot inputPort))
                    continue;

                outputPorts[c] = outputPort;
                inputPorts[c] = inputPort;
                sourcePoints[c] = PortCenter(result.nodeRects[edge.outputNodeId], model.nodeById[edge.outputNodeId],
                    outputPort, metrics);
                targetPoints[c] = PortCenter(result.nodeRects[edge.inputNodeId], model.nodeById[edge.inputNodeId],
                    inputPort, metrics);

                var list = new List<Segment>(chain.Count);
                if (chain.Count == 1)
                {
                    // Both endpoints sit in the same unit. Block ports face left, so the wire detours
                    // into the gap on the left; gap index -1 is the padding before the first column.
                    list.Add(new Segment
                    {
                        chain = c,
                        step = 0,
                        gap = columnOf[chain[0]] - 1,
                        entryY = sourcePoints[c].y,
                        exitY = targetPoints[c].y
                    });
                }
                else
                {
                    for (int s = 0; s + 1 < chain.Count; s++)
                    {
                        Rect from = itemRects[chain[s]];
                        Rect to = itemRects[chain[s + 1]];
                        list.Add(new Segment
                        {
                            chain = c,
                            step = s,
                            gap = columnOf[chain[s]],
                            entryY = s == 0 ? sourcePoints[c].y : from.y + from.height * 0.5f,
                            exitY = s + 1 == chain.Count - 1 ? targetPoints[c].y : to.y + to.height * 0.5f
                        });
                    }
                }
                foreach (Segment segment in list) segments.Add(segment);
                chainSegments[c] = list;
            }

            // Lanes must be settled before any route is emitted, because a route copies its lane x.
            AssignLanes(segments, metrics, columnX, gapWidth);

            for (int c = 0; c < chains.Count; c++)
            {
                List<Segment> list = chainSegments[c];
                if (list == null) continue;

                var route = new WireRoute
                {
                    edge = model.edges[c],
                    outputPort = outputPorts[c],
                    inputPort = inputPorts[c]
                };
                route.points.Add(sourcePoints[c]);
                foreach (Segment segment in list)
                {
                    route.points.Add(new Vector2(segment.laneX, segment.entryY));
                    route.points.Add(new Vector2(segment.laneX, segment.exitY));
                }
                route.points.Add(targetPoints[c]);
                Simplify(route.points);
                result.routes.Add(route);
            }
        }

        static void AssignLanes(List<Segment> segments, LayoutMetrics metrics, float[] columnX, float[] gapWidth)
        {
            // Gap index 0 is the padding before the first column, i+1 is the gap after column i.
            for (int gap = 0; gap < gapWidth.Length; gap++)
            {
                float left;
                float right;
                if (gap == 0)
                {
                    left = 0f;
                    right = columnX.Length > 0 ? columnX[0] : gapWidth[0];
                }
                else if (gap - 1 < columnX.Length)
                {
                    left = columnX[gap - 1] + metrics.NodeWidth;
                    right = gap < columnX.Length ? columnX[gap] : left + gapWidth[gap];
                }
                else
                {
                    continue;
                }

                var inGap = new List<Segment>();
                foreach (Segment segment in segments)
                    if (segment.gap + 1 == gap) inGap.Add(segment);
                if (inGap.Count == 0) continue;

                // Sorting by entry then exit y keeps a wire that enters high from leaving low,
                // which is the ordering that produces the fewest lane crossings.
                inGap.Sort((a, b) =>
                {
                    int compare = a.entryY.CompareTo(b.entryY);
                    if (compare != 0) return compare;
                    compare = a.exitY.CompareTo(b.exitY);
                    if (compare != 0) return compare;
                    compare = a.chain.CompareTo(b.chain);
                    return compare != 0 ? compare : a.step.CompareTo(b.step);
                });

                float span = right - left;
                for (int lane = 0; lane < inGap.Count; lane++)
                    inGap[lane].laneX = left + span * (lane + 1) / (inGap.Count + 1);
            }
        }

        sealed class Segment
        {
            public int chain;
            public int step;
            public int gap;
            public float entryY;
            public float exitY;
            public float laneX;
        }
    }
}
