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

            // Notes anchored to a group are drawn in a gutter to the left of every column. Widening the
            // first gap reserves that gutter before anything is placed, so a note there can never sit on
            // top of the graph: the columns simply start further right.
            // The gutter is where notes go, so it is reserved whenever there is a note to put in it — not
            // only when the group frames are drawn. The group rectangles exist either way, because
            // BuildGroupFrames runs regardless, so a note can be aligned with its group's nodes and drawn
            // beside the graph even with the frames switched off. Keying the gutter off the frames made the
            // notes jump to the bottom band when the frames were hidden, which is the behaviour that was
            // reported as strange.
            float notesGutterWidth = options.reserveNotesBand && HasAnchoredNotes(model)
                ? NotesGutterWidth(metrics)
                : 0f;
            gapWidth[0] += notesGutterWidth;
            // A frame wraps its members and adds padding on every side, so two frames in adjacent columns
            // need twice that padding between the columns or their edges meet. The design gap is a fixed
            // 56 units, which clears it at the default text scale and stops doing so once the padding grows
            // with the font — the frames then overlap by a few units along the column boundary.
            float frameSideClearance = options.showGroupFrames
                ? GroupFramePad(metrics) * 2f + LayoutMetrics.GridSize
                : 0f;

            for (int i = 0; i + 1 < keys.Count; i++)
            {
                int lanes = segmentsPerGap[i + 1];
                float gap = lanes <= 0
                    ? metrics.HorizontalGap
                    : Mathf.Max(metrics.HorizontalGap, lanes * LayoutMetrics.WireLaneSpacing + LayoutMetrics.WireLaneMargin);
                gapWidth[i + 1] = Mathf.Max(gap, frameSideClearance);
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
            // Two placement strategies. Without group frames the columns are independent and stacking each
            // one on its own is enough. With them, a frame wraps its members across several columns, and
            // independent stacking lets two groups interleave: the frames then cover overlapping rectangles
            // and the graph reads as a pile of boxes. The band path gives every group a fixed vertical slot
            // that holds in all of its columns, so no two frames can cross.
            float bottom = options.showGroupFrames && model.groups.Count > 0
                ? PlaceByGroupOrder(model, metrics, unitOfNode, heights, columnOf, layers, keys, columnX,
                                    extendedPredecessors, extendedSuccessors, snapToGrid, pad, itemRects)
                : PlaceByColumn(metrics, heights, layers, keys, columnX, snapToGrid, pad, itemRects);
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

            // Notes need the frames to exist, because an anchored note aligns with the frame of the group
            // it belongs to.
            result.notesGutterWidth = notesGutterWidth;
            float notesBottom = bottom;
            if (options.reserveNotesBand && model.notes.Count > 0)
            {
                // The notes may only be anchored when the gutter was actually reserved for them, which is
                // the same condition that widened the first gap. Testing for a frame instead let a note be
                // anchored into a gutter that was never reserved — the group rectangles are built whether or
                // not the frames are drawn — so with group frames off the notes landed on top of the graph.
                notesBottom = PlaceNotes(model, metrics, result, pad, bottom, notesGutterWidth > 0f);
                result.notesBandHeight = NoteBandHeight(model, metrics, result);
            }
            if (options.reserveLegendBand)
                result.legendBandHeight = LegendBandHeight(model, metrics);

            // The watermark owns the bottom strip. Its height is the caller's, because only the renderer
            // knows how tall the logo is and how many text lines the preset prints.
            result.watermarkBandHeight = Mathf.Max(0f, options.watermarkBandHeight);

            // The gutter notes sit inside the graph's own vertical range but may reach past its bottom, so
            // the canvas has to close over whichever is lower.
            canvasHeight = Mathf.Max(bottom, notesBottom) + pad + result.BandHeight;

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

        /// <summary>
        /// Stacks each column on its own and returns the bottom of the tallest one.
        ///
        /// This is the placement for a graph without group frames: nothing spans columns, so each column
        /// can be filled independently.
        /// </summary>
        static float PlaceByColumn(LayoutMetrics metrics, Dictionary<string, float> heights,
                                   Dictionary<int, List<string>> layers, List<int> keys, float[] columnX,
                                   bool snapToGrid, float pad, Dictionary<string, Rect> itemRects)
        {
            float bottom = pad;
            for (int i = 0; i < keys.Count; i++)
            {
                float y = pad;
                foreach (string id in layers[keys[i]])
                {
                    float x = columnX[i];
                    if (snapToGrid)
                    {
                        x = Mathf.Round(x / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                        y = Mathf.Round(y / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                    }
                    itemRects[id] = new Rect(x, y, metrics.NodeWidth, heights[id]);
                    y += heights[id] + metrics.VerticalGap;
                }
                bottom = Mathf.Max(bottom, y - metrics.VerticalGap);
            }
            return bottom;
        }

        /// <summary>
        /// Places units in vertical bands so that no two group frames can overlap.
        ///
        /// A frame wraps the union of its members, so it spans every column they land in. Two frames overlap
        /// exactly when they share a column and their vertical extents intersect, and because each frame's
        /// extent is measured over *all* of its columns, ordering the two inside one shared column is not
        /// enough — a group whose members reach lower in some other column would still collide. What is
        /// needed is a vertical extent per group that holds in every column it touches, which is what a band
        /// is: a fixed slot whose height is the group's tallest single-column run.
        ///
        /// Bands are then packed so that any two whose column ranges intersect get disjoint slots. Ordering
        /// the packing by descending height is what keeps the canvas short: a tall band placed first pushes
        /// only the bands it actually conflicts with, whereas processing in author order lets a tall band
        /// discovered late displace everything below it. The author's own arrangement is the tiebreaker, so
        /// the result still reads in the order the graph was drawn in.
        /// </summary>
        static float PlaceByGroupOrder(GraphModel model, LayoutMetrics metrics,
                                       Dictionary<string, string> unitOfNode, Dictionary<string, float> heights,
                                       Dictionary<string, int> columnOf, Dictionary<int, List<string>> layers,
                                       List<int> keys, float[] columnX,
                                       Dictionary<string, List<string>> extendedPredecessors,
                                       Dictionary<string, List<string>> extendedSuccessors,
                                       bool snapToGrid, float pad, Dictionary<string, Rect> itemRects)
        {
            // Which group a unit belongs to. A stack counts as belonging to the group of its first block
            // that has one; a unit whose nodes carry no group stays loose.
            var groupOfUnit = new Dictionary<string, string>(heights.Count, System.StringComparer.Ordinal);
            foreach (GraphNode node in model.nodes)
            {
                string unit = unitOfNode[node.id];
                if (groupOfUnit.ContainsKey(unit)) continue;
                if (!string.IsNullOrEmpty(node.groupId)) groupOfUnit[unit] = node.groupId;
            }

            var groupsById = new Dictionary<string, GraphGroup>(model.groups.Count, System.StringComparer.Ordinal);
            foreach (GraphGroup group in model.groups) groupsById[group.id] = group;

            var bands = new List<Band>(model.groups.Count + heights.Count);
            var bandOfUnit = new Dictionary<string, Band>(heights.Count, System.StringComparer.Ordinal);
            var bandOfGroup = new Dictionary<string, Band>(model.groups.Count, System.StringComparer.Ordinal);

            foreach (GraphGroup group in model.groups)
            {
                var band = new Band
                {
                    groupId = group.id,
                    authoredY = group.authoredPosition.y,
                    topOverhang = GroupChromeOverhang(metrics),
                    bottomPad = GroupFramePad(metrics)
                };
                bands.Add(band);
                bandOfGroup[group.id] = band;
            }

            foreach (KeyValuePair<string, string> entry in groupOfUnit)
            {
                if (!bandOfGroup.TryGetValue(entry.Value, out Band band)) continue;
                band.units.Add(entry.Key);
                bandOfUnit[entry.Key] = band;
            }

            float meanGroupY = 0f;
            foreach (GraphGroup group in model.groups) meanGroupY += group.authoredPosition.y;
            if (model.groups.Count > 0) meanGroupY /= model.groups.Count;

            foreach (int key in keys)
            {
                foreach (string id in layers[key])
                {
                    if (bandOfUnit.ContainsKey(id)) continue;
                    var band = new Band
                    {
                        authoredY = InterpolateBandY(id, groupOfUnit, model, extendedPredecessors,
                                                     extendedSuccessors, meanGroupY)
                    };
                    band.units.Add(id);
                    bands.Add(band);
                    bandOfUnit[id] = band;
                }
            }

            // Measure each band: the columns it spans and the tallest run it needs in any one of them.
            var runPerColumn = new Dictionary<int, float>();
            foreach (Band band in bands)
            {
                band.firstColumn = int.MaxValue;
                band.lastColumn = int.MinValue;
                runPerColumn.Clear();

                foreach (string id in band.units)
                {
                    int column = columnOf[id];
                    band.firstColumn = Mathf.Min(band.firstColumn, column);
                    band.lastColumn = Mathf.Max(band.lastColumn, column);
                    runPerColumn.TryGetValue(column, out float run);
                    runPerColumn[column] = run + heights[id] + metrics.VerticalGap;
                }

                if (band.units.Count == 0)
                {
                    band.firstColumn = 0;
                    band.lastColumn = -1;   // spans nothing, so it can never conflict
                    band.height = 0f;
                    continue;
                }

                float tallest = 0f;
                foreach (float run in runPerColumn.Values)
                    tallest = Mathf.Max(tallest, run - metrics.VerticalGap);
                band.height = tallest;
            }

            // Tallest first, author order as the tiebreak.
            bands.Sort((a, b) =>
            {
                int compare = b.height.CompareTo(a.height);
                if (compare != 0) return compare;
                compare = a.authoredY.CompareTo(b.authoredY);
                if (compare != 0) return compare;
                string an = a.units.Count > 0 ? a.units[0] : string.Empty;
                string bn = b.units.Count > 0 ? b.units[0] : string.Empty;
                return string.CompareOrdinal(an, bn);
            });

            // Pack: a band starts below every already-placed band whose column range it overlaps, so that
            // the two frames clear each other.
            var placed = new List<Band>(bands.Count);
            foreach (Band band in bands)
            {
                float offset = 0f;
                foreach (Band other in placed)
                {
                    if (other.lastColumn < band.firstColumn || other.firstColumn > band.lastColumn) continue;
                    offset = Mathf.Max(offset, other.offset + other.height + other.bottomPad
                                               + band.topOverhang + metrics.VerticalGap);
                }
                band.offset = offset;
                placed.Add(band);
            }

            float bottom = pad;
            foreach (Band band in placed)
                bottom = Mathf.Max(bottom, pad + band.offset + band.height + band.bottomPad);

            for (int i = 0; i < keys.Count; i++)
            {
                int column = keys[i];
                foreach (Band band in placed)
                {
                    if (column < band.firstColumn || column > band.lastColumn) continue;

                    float y = pad + band.offset;
                    // Within its band a column keeps the barycenter order the ordering pass produced.
                    foreach (string id in layers[column])
                    {
                        if (bandOfUnit[id] != band) continue;
                        float x = columnX[i];
                        float placedY = y;
                        if (snapToGrid)
                        {
                            x = Mathf.Round(x / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                            placedY = Mathf.Round(placedY / LayoutMetrics.GridSize) * LayoutMetrics.GridSize;
                        }
                        itemRects[id] = new Rect(x, placedY, metrics.NodeWidth, heights[id]);
                        y += heights[id] + metrics.VerticalGap;
                    }
                }
            }

            return bottom;
        }

        /// <summary>
        /// One vertical slot of the canvas: the corridor a group owns, or the single slot a loose unit
        /// occupies.
        /// </summary>
        sealed class Band
        {
            public string groupId;

            /// <summary>Sort key taken from the author's own arrangement; smaller is higher up.</summary>
            public float authoredY;

            public int firstColumn;
            public int lastColumn;

            /// <summary>Tallest run the band needs in any one of its columns.</summary>
            public float height;

            /// <summary>How far this band's frame reaches above its content; 0 when it has no frame.</summary>
            public float topOverhang;

            /// <summary>How far this band's frame reaches below its content; 0 when it has no frame.</summary>
            public float bottomPad;

            /// <summary>Distance from the top of the content area to the band's content.</summary>
            public float offset;

            public readonly List<string> units = new List<string>();
        }

        /// <summary>
        /// A loose unit's ordering key: the average authored height of the groups it connects to, so a node
        /// feeding a group is drawn beside that group rather than at the far end of the canvas.
        /// </summary>
        static float InterpolateBandY(string unit, Dictionary<string, string> groupOfUnit, GraphModel model,
                                      Dictionary<string, List<string>> extendedPredecessors,
                                      Dictionary<string, List<string>> extendedSuccessors, float fallback)
        {
            float sum = 0f;
            int count = 0;
            AccumulateNeighbourBandY(unit, extendedPredecessors, groupOfUnit, model, ref sum, ref count);
            AccumulateNeighbourBandY(unit, extendedSuccessors, groupOfUnit, model, ref sum, ref count);
            return count > 0 ? sum / count : fallback;
        }

        static void AccumulateNeighbourBandY(string unit, Dictionary<string, List<string>> neighbours,
                                             Dictionary<string, string> groupOfUnit, GraphModel model,
                                             ref float sum, ref int count)
        {
            if (neighbours == null || !neighbours.TryGetValue(unit, out List<string> list)) return;
            foreach (string neighbour in list)
            {
                if (!groupOfUnit.TryGetValue(neighbour, out string groupId)) continue;
                GraphGroup group = model.groups.Find(g => g.id == groupId);
                if (group == null) continue;
                sum += group.authoredPosition.y;
                count++;
            }
        }

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
        static float NoteBandHeight(GraphModel model, LayoutMetrics metrics, GraphLayout layout)
        {
            float tallest = 0f;
            foreach (GraphNote note in model.notes)
            {
                if (layout.gutterNotes.Contains(note.id)) continue;
                tallest = Mathf.Max(tallest, NoteCardHeight(note, metrics, NoteTextWidth(metrics)));
            }
            return tallest > 0f ? tallest + metrics.PortRowHeight : 0f;
        }

        /// <summary>Width of a note card. Matches what the renderer draws, so the layout reserves the truth.</summary>
        internal static float NoteTextWidth(LayoutMetrics metrics)
        {
            return metrics.NodeWidth * 1.6f;
        }

        /// <summary>
        /// Height of a note card once its text is wrapped to <paramref name="width"/>.
        ///
        /// The text column is the card less the renderer's own padding, taken from the renderer rather than
        /// repeated here: a different inset would make the layout reserve a different height from the one
        /// the card is drawn at, and the two would drift apart.
        /// </summary>
        static float NoteCardHeight(GraphNote note, LayoutMetrics metrics, float width)
        {
            float line = metrics.PortLabelFontSize * 1.45f;
            float textWidth = width - SnippetCanvasRenderer.NodeTextPadding * 2f;
            int lines = CountWrappedLines(note.content, textWidth, metrics.PortLabelFontSize);
            return line + lines * line + metrics.PortRowHeight * 0.6f;
        }

        /// <summary>
        /// Reserves the left-hand annotation gutter: as wide as a note card, plus a gap to the first column.
        /// </summary>
        /// <summary>
        /// Whether any note will be drawn in the gutter.
        ///
        /// Only a note whose group produces a frame can be anchored; a graph whose notes are all free keeps
        /// its full width, because reserving a gutter nothing would be drawn in would push the graph right
        /// for no reason.
        /// </summary>
        static bool HasAnchoredNotes(GraphModel model)
        {
            foreach (GraphNote note in model.notes)
            {
                if (string.IsNullOrEmpty(note.groupId)) continue;
                foreach (GraphGroup group in model.groups)
                    if (group.id == note.groupId && group.members.Count > 0) return true;
            }
            return false;
        }

        static float NotesGutterWidth(LayoutMetrics metrics)
        {
            // The card, then enough clear space for a leader tick between it and the first column.
            return NoteTextWidth(metrics) + metrics.PortRowHeight * 3f;
        }

        /// <summary>
        /// Places the notes that belong to a group, and returns the lowest point any of them reaches.
        ///
        /// Such a note goes into the gutter, aligned with its group's frame. That is the answer to "which
        /// part of the graph is this note about": it sits level with the thing it describes and carries a
        /// leader line across to it.
        ///
        /// A note with no group of its own has nothing to align to and keeps the band under the graph, whose
        /// height <see cref="NoteBandHeight"/> reserves. <paramref name="anchorToGroups"/> carries that
        /// decision in from the caller, which is the only place that knows whether the gutter was reserved.
        ///
        /// Only gutter notes are recorded in <see cref="GraphLayout.noteRects"/>; the band notes are the
        /// renderer's, which already knows how to wrap them across the canvas width.
        /// </summary>
        static float PlaceNotes(GraphModel model, LayoutMetrics metrics, GraphLayout layout, float pad,
                                float graphBottom, bool anchorToGroups)
        {
            float cardWidth = NoteTextWidth(metrics);
            float gap = metrics.PortRowHeight;
            float lowest = graphBottom;

            var anchored = new List<GraphNote>();
            if (anchorToGroups)
            {
                foreach (GraphNote note in model.notes)
                {
                    if (string.IsNullOrEmpty(note.groupId)) continue;
                    if (!layout.groupRects.ContainsKey(note.groupId)) continue;
                    anchored.Add(note);
                }
            }

            // In the order of the frames they point at, so the gutter reads top to bottom like the graph.
            anchored.Sort((a, b) =>
            {
                int compare = layout.groupRects[a.groupId].y.CompareTo(layout.groupRects[b.groupId].y);
                return compare != 0 ? compare : string.CompareOrdinal(a.id, b.id);
            });

            float y = pad;
            foreach (GraphNote note in anchored)
            {
                float height = NoteCardHeight(note, metrics, cardWidth);
                // Align with the group's frame, but never climb back over an earlier note: two groups can
                // share rows, and their notes must not land on top of each other.
                y = Mathf.Max(y, layout.groupRects[note.groupId].y);
                var rect = new Rect(pad, y, cardWidth, height);
                layout.noteRects[note.id] = rect;
                layout.gutterNotes.Add(note.id);
                y += height + gap;
                lowest = Mathf.Max(lowest, rect.yMax);
            }

            return lowest;
        }

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
