using System.Collections.Generic;
using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Ranking and ordering phase of the shader-graph auto layout: assigns every node a column (its
    /// rank), relaxes those ranks towards their consumers, compacts them onto consecutive columns, and
    /// orders each column with alternating barycenter sweeps.
    /// </summary>
    public static partial class GraphAutoLayoutEngine
    {
        /// <summary>
        /// Assigns each node a rank inside its feasible window <c>[earliest, maxEarliest - depth]</c>.
        ///
        /// Nodes are placed from the sinks backwards so that every consumer already has a column, and the
        /// chosen column minimises a blend of two costs: how far it sits from the consumers that use it,
        /// and how loaded the column already is. Weighting the first term is what stops a property node
        /// from being parked at the far left when the node reading it lives near the output; the second
        /// keeps the silhouette from collapsing into one tall column. <paramref name="locality"/> sets the
        /// balance, and <paramref name="balance"/> caps how far a node may move at all.
        /// </summary>
        static Dictionary<string, int> BalanceRanks(GraphModel model, Dictionary<string, int> earliest,
                                                    Dictionary<string, List<string>> predecessors,
                                                    Dictionary<string, List<string>> successors,
                                                    LayoutMetrics metrics, float balance, float locality)
        {
            balance = Mathf.Clamp01(balance);
            locality = Mathf.Clamp01(locality);
            if (balance <= 0f) return earliest;

            var order = new List<GraphNode>(model.nodes);
            order.Sort((a, b) =>
            {
                int compare = earliest[a.id].CompareTo(earliest[b.id]);
                return compare != 0 ? compare : string.CompareOrdinal(a.id, b.id);
            });

            // depth = longest remaining chain to a sink, so maxEarliest - depth is the latest legal column.
            var depth = new Dictionary<string, int>(model.nodes.Count);
            for (int i = order.Count - 1; i >= 0; i--)
            {
                string id = order[i].id;
                int best = 0;
                if (successors.TryGetValue(id, out List<string> list))
                    foreach (string next in list)
                        if (depth.TryGetValue(next, out int nextDepth)) best = Mathf.Max(best, nextDepth + 1);
                depth[id] = best;
            }

            int maxEarliest = 0;
            foreach (GraphNode node in model.nodes) maxEarliest = Mathf.Max(maxEarliest, earliest[node.id]);

            var load = new Dictionary<int, float>();
            var rank = new Dictionary<string, int>(model.nodes.Count);

            // Reverse topological order: every successor is placed before the node that feeds it, which is
            // what makes "how far are my consumers" answerable at decision time.
            for (int i = order.Count - 1; i >= 0; i--)
            {
                GraphNode node = order[i];
                string id = node.id;

                int low = earliest[id];
                int high = Mathf.Max(low, maxEarliest - depth[id]);
                int cap = low + Mathf.FloorToInt((high - low) * balance);

                if (cap <= low)
                {
                    Place(node, low);
                    continue;
                }

                // Consumers, when there are any. A sink prefers the rightmost column so the master stack
                // keeps sitting at the edge, which is where a reader expects the output.
                bool hasConsumers = successors.TryGetValue(id, out List<string> next)
                                    && next.Count > 0;

                float minDistance = float.MaxValue;
                float maxDistance = 0f;
                float minLoad = float.MaxValue;
                float maxLoad = 0f;

                var distances = new float[cap - low + 1];
                for (int column = low; column <= cap; column++)
                {
                    float distance = 0f;
                    if (hasConsumers)
                    {
                        float sum = 0f;
                        int count = 0;
                        foreach (string consumer in next)
                        {
                            if (!rank.TryGetValue(consumer, out int consumerRank)) continue;
                            // Consumers are always to the right, so this stays non-negative.
                            sum += consumerRank - column;
                            count++;
                        }
                        distance = count > 0 ? sum / count : high - column;
                    }
                    else
                    {
                        distance = high - column;
                    }

                    distances[column - low] = distance;
                    minDistance = Mathf.Min(minDistance, distance);
                    maxDistance = Mathf.Max(maxDistance, distance);

                    load.TryGetValue(column, out float currentLoad);
                    minLoad = Mathf.Min(minLoad, currentLoad);
                    maxLoad = Mathf.Max(maxLoad, currentLoad);
                }

                float distanceSpan = Mathf.Max(0.001f, maxDistance - minDistance);
                float loadSpan = Mathf.Max(0.001f, maxLoad - minLoad);

                int chosen = low;
                float best = float.MaxValue;
                for (int column = low; column <= cap; column++)
                {
                    float distanceScore = (distances[column - low] - minDistance) / distanceSpan;
                    load.TryGetValue(column, out float currentLoad);
                    float loadScore = (currentLoad - minLoad) / loadSpan;

                    // A small deterministic tiebreak keeps identical graphs from flickering between runs.
                    float cost = locality * distanceScore + (1f - locality) * loadScore + column * 1e-6f;
                    if (cost >= best) continue;
                    best = cost;
                    chosen = column;
                }

                Place(node, chosen);
            }

            return rank;

            void Place(GraphNode node, int column)
            {
                rank[node.id] = column;
                load.TryGetValue(column, out float used);
                load[column] = used + EstimateNodeHeight(node, metrics) + metrics.VerticalGap;
            }
        }

        static Dictionary<string, List<string>> BuildAdjacency(GraphModel model, bool reverse)
        {
            var adjacency = new Dictionary<string, List<string>>(model.nodes.Count);
            foreach (GraphNode node in model.nodes) adjacency[node.id] = new List<string>();
            foreach (GraphEdge edge in model.edges)
            {
                string from = reverse ? edge.inputNodeId : edge.outputNodeId;
                string to = reverse ? edge.outputNodeId : edge.inputNodeId;
                if (adjacency.TryGetValue(from, out List<string> list)) list.Add(to);
            }
            return adjacency;
        }

        static Dictionary<string, int> AssignRanks(GraphModel model, Dictionary<string, List<string>> predecessors)
        {
            var rank = new Dictionary<string, int>(model.nodes.Count);
            var visiting = new HashSet<string>();
            foreach (GraphNode node in model.nodes) RankOf(node.id, predecessors, rank, visiting);
            return rank;
        }

        static int RankOf(string id, Dictionary<string, List<string>> predecessors,
                         Dictionary<string, int> rank, HashSet<string> visiting)
        {
            if (rank.TryGetValue(id, out int known)) return known;
            if (!visiting.Add(id)) return 0;

            int best = 0;
            if (predecessors.TryGetValue(id, out List<string> list))
                foreach (string previous in list)
                    best = Mathf.Max(best, RankOf(previous, predecessors, rank, visiting) + 1);

            visiting.Remove(id);
            rank[id] = best;
            return best;
        }

        static void OrderByBarycenter(Dictionary<int, List<string>> layers, List<int> keys,
                                      Dictionary<string, List<string>> neighbours, bool neighboursArePredecessors)
        {
            var nodeRank = new Dictionary<string, int>();
            var nodeIndex = new Dictionary<string, int>();
            foreach (KeyValuePair<int, List<string>> layer in layers)
            {
                for (int i = 0; i < layer.Value.Count; i++)
                {
                    nodeRank[layer.Value[i]] = layer.Key;
                    nodeIndex[layer.Value[i]] = i;
                }
            }

            if (neighboursArePredecessors)
            {
                for (int i = 1; i < keys.Count; i++) SortLayer(layers, keys[i], keys[i - 1], neighbours, nodeRank, nodeIndex);
            }
            else
            {
                for (int i = keys.Count - 2; i >= 0; i--) SortLayer(layers, keys[i], keys[i + 1], neighbours, nodeRank, nodeIndex);
            }
        }

        static void SortLayer(Dictionary<int, List<string>> layers, int key, int neighbourRank,
                              Dictionary<string, List<string>> neighbours,
                              Dictionary<string, int> nodeRank, Dictionary<string, int> nodeIndex)
        {
            var scores = new Dictionary<string, float>();
            foreach (string id in layers[key])
            {
                float sum = 0f;
                int count = 0;
                if (neighbours.TryGetValue(id, out List<string> list))
                {
                    foreach (string neighbour in list)
                    {
                        if (!nodeRank.TryGetValue(neighbour, out int r) || r != neighbourRank) continue;
                        sum += nodeIndex[neighbour];
                        count++;
                    }
                }
                scores[id] = count == 0 ? float.MaxValue : sum / count;
            }

            layers[key].Sort((a, b) =>
            {
                int compare = scores[a].CompareTo(scores[b]);
                return compare != 0 ? compare : string.CompareOrdinal(a, b);
            });
        }
    }
}
