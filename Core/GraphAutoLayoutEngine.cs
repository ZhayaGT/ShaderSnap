using System.Collections.Generic;
using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Placement phase of the shader-graph auto layout: node and stack height estimates, port
    /// positions, stack/dummy-chain column packing, gap and frame sizing, the band heights, and the
    /// public <c>Apply</c> / <c>ApplyWithTargetAspect</c> entry points. The ranking phase lives in
    /// <c>GraphAutoLayoutEngine.Ranking.cs</c> and the wire routing phase in
    /// <c>GraphAutoLayoutEngine.Routing.cs</c>.
    /// </summary>
    public static partial class GraphAutoLayoutEngine
    {
        public const int BarycenterPasses = 3;

        const float DummyHeight = 0f;
        const string DummyPrefix = "__dummy:";
        const string StackUnitPrefix = "__stack:";

        public static float EstimateNodeHeight(GraphNode node, LayoutMetrics metrics)
        {
            // A block row lives inside the master stack: the stack owns the title bar, so the row is
            // just the port band. Shader Graph draws it the same way (BlockNode drops its title).
            if (node.IsStackBlock) return metrics.PortRowHeight;

            int inputs = 0;
            int outputs = 0;
            foreach (PortSlot port in node.ports)
            {
                if (port.hidden) continue;
                if (port.isInput) inputs++;
                else outputs++;
            }

            float height = metrics.TitleHeight + Mathf.Max(inputs, outputs) * metrics.PortRowHeight;
            if (metrics.ShowNodeValues && node.HasValue) height += metrics.PortRowHeight;
            return Mathf.Max(metrics.TitleHeight, height);
        }

        /// <summary>Total height of a master stack: the context title bar plus one row per block.</summary>
        public static float EstimateStackHeight(GraphStack stack, LayoutMetrics metrics)
        {
            return metrics.TitleHeight + stack.blocks.Count * metrics.PortRowHeight;
        }

        public static Vector2 PortCenter(Rect rect, GraphNode node, PortSlot slot, LayoutMetrics metrics)
        {
            // A block row rect already excludes the stack title, and the row holds a single port band.
            if (node.IsStackBlock) return new Vector2(rect.x, rect.y + rect.height * 0.5f);

            float top = metrics.TitleHeight;
            if (metrics.ShowNodeValues && node.HasValue) top += metrics.PortRowHeight;

            int row = 0;
            foreach (PortSlot port in node.ports)
            {
                if (port.hidden) continue;
                if (port == slot) break;
                if (port.isInput == slot.isInput) row++;
            }
            float y = rect.y + top + (row + 0.5f) * metrics.PortRowHeight;
            float x = slot.isInput ? rect.x : rect.xMax;
            return new Vector2(x, y);
        }

        public static GraphLayout Apply(GraphModel model, LayoutMetrics metrics, float balance)
        {
            return Apply(model, metrics, new LayoutOptions { balance = balance, locality = 1f });
        }

        public static GraphLayout Apply(GraphModel model, LayoutMetrics metrics, float balance, bool snapToGrid)
        {
            return Apply(model, metrics, new LayoutOptions { balance = balance, locality = 1f, snapToGrid = snapToGrid });
        }

        public static GraphLayout Apply(GraphModel model, LayoutMetrics metrics, LayoutOptions options)
        {
            bool snapToGrid = options.snapToGrid;
            float balance = options.balance;
            // The frame is drawn around the content, so the content has to keep clear of it. Using the
            // larger of the design padding and the requested inset keeps both the graph and the bands
            // inside the frame no matter how wide the frame margin is set.
            //
            // Group frames are then added on top: a frame reaches GroupChromeOverhang above the topmost
            // node it wraps, and the caller's inset only describes where the nodes may start. Without this
            // the frame's title strip was laid out above the content area and drawn under the window
            // chrome, half cut off.
            float pad = Mathf.Max(metrics.Padding, options.canvasInset);
            if (options.showGroupFrames && model != null && model.groups.Count > 0)
                pad += GroupChromeOverhang(metrics);
            var result = new GraphLayout();
            if (model == null || model.nodes.Count == 0) return result;

            Dictionary<string, List<string>> predecessors = BuildAdjacency(model, true);
            Dictionary<string, List<string>> successors = BuildAdjacency(model, false);
            Dictionary<string, int> rank = Compact(BalanceRanks(model, AssignRanks(model, predecessors),
                predecessors, successors, metrics, balance, options.locality));

            // A master stack is placed as one unit: its blocks share a column and sit flush against each
            // other under the context title, exactly like Shader Graph. Everything else is its own unit.
            var stackOfUnit = new Dictionary<string, GraphStack>();
            var unitOfNode = new Dictionary<string, string>(model.nodes.Count);
            foreach (GraphNode node in model.nodes) unitOfNode[node.id] = node.id;
            foreach (GraphStack stack in model.stacks)
            {
                string unit = StackUnitPrefix + stack.id;
                stackOfUnit[unit] = stack;
                int stackRank = 0;
                foreach (GraphNode block in stack.blocks)
                {
                    unitOfNode[block.id] = unit;
                    stackRank = Mathf.Max(stackRank, rank[block.id]);
                }
                // Pin every block to the stack's column so a block can never drift away from its stack.
                foreach (GraphNode block in stack.blocks) rank[block.id] = stackRank;
            }

            var heights = new Dictionary<string, float>(model.nodes.Count * 2);
            var layers = new Dictionary<int, List<string>>();
            foreach (GraphNode node in model.nodes)
            {
                string unit = unitOfNode[node.id];
                if (heights.ContainsKey(unit)) continue;
                heights[unit] = stackOfUnit.TryGetValue(unit, out GraphStack stack)
                    ? EstimateStackHeight(stack, metrics)
                    : EstimateNodeHeight(node, metrics);

                int key = rank[node.id];
                if (!layers.TryGetValue(key, out List<string> list)) layers[key] = list = new List<string>();
                list.Add(unit);
            }

            // An edge spanning more than one rank is split into rank-adjacent segments threaded through
            // dummy items. Each dummy claims a row in the column it passes, so the wire crosses that
            // column through reserved empty space instead of over whatever node happens to be there.
            var chains = new List<List<string>>(model.edges.Count);
            for (int e = 0; e < model.edges.Count; e++)
            {
                GraphEdge edge = model.edges[e];
                if (!rank.TryGetValue(edge.outputNodeId, out int from) ||
                    !rank.TryGetValue(edge.inputNodeId, out int to))
                {
                    chains.Add(null);
                    continue;
                }

                string fromUnit = unitOfNode[edge.outputNodeId];
                string toUnit = unitOfNode[edge.inputNodeId];
                if (fromUnit == toUnit)
                {
                    // Both ends live in the same stack. There is no column to cross, so the wire is a
                    // single segment that leaves into the gap beside the stack and comes back.
                    chains.Add(new List<string> { fromUnit });
                    continue;
                }

                var chain = new List<string> { fromUnit };
                for (int r = from + 1; r < to; r++)
                {
                    string id = DummyPrefix + e + ":" + r;
                    heights[id] = DummyHeight;
                    if (!layers.TryGetValue(r, out List<string> list)) layers[r] = list = new List<string>();
                    list.Add(id);
                    chain.Add(id);
                }
                chain.Add(toUnit);
                chains.Add(chain);
            }

            var keys = new List<int>(layers.Keys);
            keys.Sort();

            var columnOf = new Dictionary<string, int>(heights.Count);
            foreach (int key in keys)
                foreach (string id in layers[key])
                    columnOf[id] = key;

            Dictionary<string, List<string>> extendedSuccessors = BuildExtendedAdjacency(heights, chains, true);
            Dictionary<string, List<string>> extendedPredecessors = BuildExtendedAdjacency(heights, chains, false);

            foreach (int key in keys) layers[key].Sort(System.StringComparer.Ordinal);
            for (int pass = 0; pass < BarycenterPasses; pass++)
            {
                if ((pass & 1) == 0) OrderByBarycenter(layers, keys, extendedPredecessors, true);
                else OrderByBarycenter(layers, keys, extendedSuccessors, false);
            }

            // Wires that share a gap get their own lane, and the gap widens until the lanes fit.
            // Index 0 is the padding before the first column; index i+1 is the gap after column i.
            var segmentsPerGap = new int[keys.Count + 1];
            foreach (List<string> chain in chains)
            {
                if (chain == null) continue;
                if (chain.Count == 1)
                {
                    segmentsPerGap[Mathf.Clamp(columnOf[chain[0]], 0, keys.Count)]++;
                    continue;
                }
                for (int i = 0; i + 1 < chain.Count; i++) segmentsPerGap[columnOf[chain[i]] + 1]++;
            }

            // The left padding only ever hosts same-unit detours, so it can grow on demand.
            var gapWidth = new float[keys.Count + 1];
            gapWidth[0] = Mathf.Max(pad,
                segmentsPerGap[0] * LayoutMetrics.WireLaneSpacing + LayoutMetrics.WireLaneMargin);
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                int lanes = segmentsPerGap[i + 1];
                gapWidth[i + 1] = lanes <= 0
                    ? metrics.HorizontalGap
                    : Mathf.Max(metrics.HorizontalGap, lanes * LayoutMetrics.WireLaneSpacing + LayoutMetrics.WireLaneMargin);
            }

            var columnX = new float[keys.Count];
            float cursor = gapWidth[0];
            for (int i = 0; i < keys.Count; i++)
            {
                columnX[i] = cursor;
                cursor += metrics.NodeWidth;
                if (i + 1 < keys.Count) cursor += gapWidth[i + 1];
            }
            float canvasWidth = cursor + pad;

            var itemRects = new Dictionary<string, Rect>(heights.Count);
            float bottom = pad;
            for (int i = 0; i < keys.Count; i++)
            {
                float y = pad;
                foreach (string id in layers[keys[i]])
                {
                    float height = heights[id];
                    float x = columnX[i];
                    if (snapToGrid)
                    {
                        x = Mathf.Round(x / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                        y = Mathf.Round(y / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                    }
                    itemRects[id] = new Rect(x, y, metrics.NodeWidth, height);
                    y += height + metrics.VerticalGap;
                }
                bottom = Mathf.Max(bottom, y - metrics.VerticalGap);
            }
            float canvasHeight = bottom + pad;

            // Expand each unit back into per-node rects. A stack's blocks become consecutive rows
            // beneath the context title, so port positions and wire endpoints stay node-addressable.
            foreach (GraphNode node in model.nodes)
            {
                if (!itemRects.TryGetValue(unitOfNode[node.id], out Rect unitRect)) continue;

                if (stackOfUnit.TryGetValue(unitOfNode[node.id], out GraphStack stack))
                {
                    result.nodeRects[node.id] = new Rect(
                        unitRect.x,
                        unitRect.y + metrics.TitleHeight + node.stackOrder * metrics.PortRowHeight,
                        unitRect.width,
                        metrics.PortRowHeight);
                    result.stackRects[stack.id] = unitRect;
                }
                else
                {
                    result.nodeRects[node.id] = unitRect;
                }
            }

            BuildRoutes(model, metrics, chains, itemRects, unitOfNode, columnOf, columnX, gapWidth, result);

            for (int i = 0; i < keys.Count; i++)
                result.columns.Add(new LayoutColumn
                {
                    index = i,
                    x = columnX[i],
                    width = metrics.NodeWidth
                });

            BuildGroupFrames(model, metrics, result);
            if (options.highlightCriticalPath) MarkCriticalPath(model, predecessors, successors, result);

            // Bands sit under the graph and are measured before the canvas is closed, so the watermark and
            // the frame both know how much of the bottom is already spoken for.
            if (options.reserveNotesBand && model.notes.Count > 0)
                result.notesBandHeight = NoteBandHeight(model, metrics);
            if (options.reserveLegendBand)
                result.legendBandHeight = LegendBandHeight(model, metrics);

            canvasHeight = bottom + pad + result.BandHeight;

            if (snapToGrid)
            {
                canvasWidth = Mathf.Ceil(canvasWidth / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                canvasHeight = Mathf.Ceil(canvasHeight / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
            }
            result.size = new Vector2(canvasWidth, canvasHeight);
            return result;
        }

        /// <summary>
        /// How far a group frame reaches above its topmost member.
        ///
        /// A frame wraps its members and adds room for its title, so the frame's top edge sits above the
        /// topmost node by this much. Anything reserving space around the graph has to account for it, or
        /// the frame — and the title drawn in it — ends up outside the area that was reserved. Public so
        /// the renderer can compute the same inset the engine places against.
        /// </summary>
        public static float GroupChromeOverhang(LayoutMetrics metrics)
        {
            return GroupFramePad(metrics) + GroupFrameTitleStrip(metrics);
        }

        static float GroupFramePad(LayoutMetrics metrics) => metrics.PortRowHeight * 0.6f;

        static float GroupFrameTitleStrip(LayoutMetrics metrics) => metrics.PortRowHeight;

        /// <summary>Frame around every group, sized to its members plus room for the title.</summary>
        static void BuildGroupFrames(GraphModel model, LayoutMetrics metrics, GraphLayout result)
        {
            foreach (GraphGroup group in model.groups)
            {
                bool started = false;
                Rect frame = default;
                foreach (GraphNode member in group.members)
                {
                    if (!result.nodeRects.TryGetValue(member.id, out Rect rect)) continue;
                    if (!started)
                    {
                        frame = rect;
                        started = true;
                        continue;
                    }
                    frame = Rect.MinMaxRect(
                        Mathf.Min(frame.xMin, rect.xMin), Mathf.Min(frame.yMin, rect.yMin),
                        Mathf.Max(frame.xMax, rect.xMax), Mathf.Max(frame.yMax, rect.yMax));
                }
                if (!started) continue;

                // The pad has to clear the title strip and stay inside the gap so frames never touch.
                float pad = GroupFramePad(metrics);
                float titleStrip = GroupFrameTitleStrip(metrics);
                result.groupRects[group.id] = new Rect(
                    frame.x - pad,
                    frame.y - pad - titleStrip,
                    frame.width + pad * 2f,
                    frame.height + pad * 2f + titleStrip);
            }
        }

        /// <summary>
        /// Marks the longest chain of dependencies. Ties are broken deterministically so the same graph
        /// always highlights the same spine.
        /// </summary>
        static void MarkCriticalPath(GraphModel model, Dictionary<string, List<string>> predecessors,
                                     Dictionary<string, List<string>> successors, GraphLayout result)
        {
            var order = new List<GraphNode>(model.nodes);
            order.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            var length = new Dictionary<string, int>(model.nodes.Count);
            var parent = new Dictionary<string, string>(model.nodes.Count);
            int bestLength = 0;
            string bestEnd = null;

            // Longest-path DP in topological order; AssignRanks already proved the graph is acyclic.
            var pending = new Dictionary<string, int>(model.nodes.Count);
            foreach (GraphNode node in model.nodes)
                pending[node.id] = predecessors.TryGetValue(node.id, out List<string> p) ? p.Count : 0;

            var queue = new Queue<string>();
            foreach (GraphNode node in model.nodes)
                if (pending[node.id] == 0) queue.Enqueue(node.id);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                length.TryGetValue(id, out int own);
                own += 1;
                length[id] = own;

                if (own > bestLength || (own == bestLength && string.CompareOrdinal(id, bestEnd) < 0))
                {
                    bestLength = own;
                    bestEnd = id;
                }

                if (!successors.TryGetValue(id, out List<string> next)) continue;
                foreach (string consumer in next)
                {
                    length.TryGetValue(consumer, out int known);
                    if (own > known)
                    {
                        length[consumer] = own;
                        parent[consumer] = id;
                    }
                    if (!pending.ContainsKey(consumer)) continue;
                    if (--pending[consumer] == 0) queue.Enqueue(consumer);
                }
            }

            if (bestEnd == null) return;
            string cursor = bestEnd;
            while (cursor != null)
            {
                result.criticalPath.Add(cursor);
                parent.TryGetValue(cursor, out cursor);
            }

            foreach (WireRoute route in result.routes)
                route.onCriticalPath = result.criticalPath.Contains(route.edge.outputNodeId)
                                    && result.criticalPath.Contains(route.edge.inputNodeId);
        }

        /// <summary>Height the notes band needs for one row of cards at the widest wrap.</summary>
        static float NoteBandHeight(GraphModel model, LayoutMetrics metrics)
        {
            float line = metrics.PortLabelFontSize * 1.45f;
            float tallest = 0f;
            foreach (GraphNote note in model.notes)
            {
                int lines = CountWrappedLines(note.content, NoteTextWidth(metrics), metrics.PortLabelFontSize);
                float height = line;                                  // title line
                height += lines * line;
                height += metrics.PortRowHeight;                      // card padding
                tallest = Mathf.Max(tallest, height);
            }
            return tallest + metrics.PortRowHeight;
        }

        /// <summary>
        /// Legend height for the port types this graph actually uses. Counting entries here is what keeps
        /// the box and the band in agreement; sizing it for a fixed number of rows let the box grow past
        /// the space reserved for it and collide with the notes.
        /// </summary>
        static float LegendBandHeight(GraphModel model, LayoutMetrics metrics)
        {
            int entries = CountPortTypes(model);
            if (entries == 0) return 0f;

            float rowHeight = metrics.PortLabelFontSize * 1.6f;
            float padding = metrics.PortRowHeight * 0.6f;
            return entries * rowHeight + padding * 2f + metrics.PortRowHeight * 0.6f;
        }

        /// <summary>Number of distinct port types the graph exposes, the legend's row count.</summary>
        internal static int CountPortTypes(GraphModel model)
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (GraphNode node in model.nodes)
                foreach (PortSlot port in node.ports)
                {
                    if (port.hidden) continue;
                    string label = SnippetStyle.DescribePortType(port.typeName);
                    if (label == "Unknown") continue;
                    seen.Add(label);
                }
            return seen.Count;
        }

        /// <summary>Rough wrap count for a proportional font; good enough to size a card.</summary>
        internal static int CountWrappedLines(string text, float width, float fontSize)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f) return 0;

            float perChar = Mathf.Max(1f, fontSize * 0.52f);
            int perLine = Mathf.Max(8, Mathf.FloorToInt(width / perChar));
            int lines = 0;
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    lines++;
                    continue;
                }
                lines += Mathf.Max(1, Mathf.CeilToInt(paragraph.Length / (float)perLine));
            }
            return lines;
        }

        static float NoteTextWidth(LayoutMetrics metrics)
        {
            return metrics.NodeWidth * 1.6f;
        }

        /// <summary>
        /// Lays the graph out, then stretches the vertical gaps until the canvas is as close to
        /// <paramref name="targetAspect"/> (width / height) as the spread limit allows. The width is
        /// fixed by the graph's longest path — one column per rank — so height is the only dimension
        /// that can be traded for a squarer output. When the target is unreachable the widest spread
        /// that still fits is used, which is the closest approximation available.
        /// </summary>
        public static GraphLayout ApplyWithTargetAspect(GraphModel model, LayoutMetrics metrics, float balance,
                                                        float targetAspect)
        {
            return ApplyWithTargetAspect(model, metrics,
                new LayoutOptions { balance = balance, locality = 1f }, targetAspect);
        }

        public static GraphLayout ApplyWithTargetAspect(GraphModel model, LayoutMetrics metrics, LayoutOptions options,
                                                        float targetAspect)
        {
            if (model == null || model.nodes.Count == 0 || targetAspect <= 0f)
                return Apply(model, metrics, options);

            GraphLayout tight = Apply(model, metrics, options);
            if (tight.size.y <= 0f || tight.size.x / tight.size.y <= targetAspect) return tight;

            float wanted = tight.size.x / targetAspect;
            float low = LayoutMetrics.MinVerticalSpread;
            float high = LayoutMetrics.MaxVerticalSpread;
            GraphLayout best = tight;

            for (int i = 0; i < 14; i++)
            {
                float mid = (low + high) * 0.5f;
                // The solver only varies the spread, so every other metric has to be carried over;
                // rebuilding from the font scale alone would silently drop the measured node width and
                // the value-row flag, leaving the rendered boxes a different size from the ones the
                // layout reserved space for.
                LayoutOptions attemptOptions = options;
                GraphLayout attempt = Apply(model,
                    LayoutMetrics.FromFontScale(metrics.FontScale, mid, metrics.ShowNodeValues, metrics.NodeWidth),
                    attemptOptions);
                if (Mathf.Abs(attempt.size.y - wanted) < Mathf.Abs(best.size.y - wanted)) best = attempt;

                if (attempt.size.y < wanted) low = mid;
                else high = mid;
            }
            return best;
        }

        static Dictionary<string, int> Compact(Dictionary<string, int> rank)
        {
            var distinct = new SortedSet<int>();
            foreach (int value in rank.Values) distinct.Add(value);

            var map = new Dictionary<int, int>();
            int next = 0;
            foreach (int value in distinct) map[value] = next++;

            var compacted = new Dictionary<string, int>(rank.Count);
            foreach (KeyValuePair<string, int> entry in rank) compacted[entry.Key] = map[entry.Value];
            return compacted;
        }

        static Dictionary<string, List<string>> BuildExtendedAdjacency(Dictionary<string, float> heights,
                                                                       List<List<string>> chains, bool forward)
        {
            var adjacency = new Dictionary<string, List<string>>(heights.Count);
            foreach (string id in heights.Keys) adjacency[id] = new List<string>();

            foreach (List<string> chain in chains)
            {
                if (chain == null) continue;
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    string from = forward ? chain[i] : chain[i + 1];
                    string to = forward ? chain[i + 1] : chain[i];
                    adjacency[from].Add(to);
                }
            }
            return adjacency;
        }

        static void Simplify(List<Vector2> points)
        {
            for (int i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < 0.0001f) points.RemoveAt(i);

            for (int i = points.Count - 2; i > 0; i--)
            {
                Vector2 previous = points[i - 1];
                Vector2 current = points[i];
                Vector2 next = points[i + 1];
                bool vertical = Mathf.Abs(previous.x - current.x) < 0.01f && Mathf.Abs(current.x - next.x) < 0.01f;
                bool horizontal = Mathf.Abs(previous.y - current.y) < 0.01f && Mathf.Abs(current.y - next.y) < 0.01f;
                if (vertical || horizontal) points.RemoveAt(i);
            }
        }

    }
}
