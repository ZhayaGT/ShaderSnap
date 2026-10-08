using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    /// <summary>Covers the reading aids: groups, notes, critical path, column guides, port legend.</summary>
    public class ReadabilityTests
    {
        static string TerrainPath => TestPaths.Terrain;
        static string WavePath => TestPaths.PropertyTypes;

        static GraphModel Load(string path)
        {
            GraphModel model = ShaderGraphParser.Parse(path);
            Assert.IsNotNull(model, "fixture must parse");
            return model;
        }

        /// <summary>
        /// A node box has to be wide enough for its own title at every font scale. Shader Graph clips the
        /// title at a fixed 200px, which is survivable in the editor because the inspector still names the
        /// node, but in an exported PNG a clipped title is information the reader can never recover.
        /// </summary>
        [Test]
        public void NodeBoxesAreWideEnoughForEveryLabelTheyDraw()
        {
            GraphModel model = Load(TerrainPath);

            foreach (float textScale in new[] { 0.5f, 1f, 1.5f, 2f, 3f })
            {
                var preset = ScriptableObject.CreateInstance<SnippetExportPreset>();
                preset.textScale = textScale;

                var canvas = new SnippetCanvasRenderer();
                canvas.SetData(model, preset, "TerrainSimple");

                LayoutMetrics metrics = LayoutMetrics.FromFontScale(textScale, preset.verticalSpread, false,
                    canvas.NodeWidth);
                float available = canvas.NodeWidth - 2f * SnippetCanvasRenderer.NodeTextPadding;

                foreach (GraphNode node in model.nodes)
                {
                    if (node.IsStackBlock) continue;

                    float needed = SnippetCanvasRenderer.MeasureText(node.title, metrics.TitleFontSize);
                    if (node.HasTypeLabel && node.value.typeLabel != node.title)
                        needed += SnippetCanvasRenderer.MeasureText(node.value.typeLabel,
                            metrics.PortLabelFontSize) + 12f;

                    Assert.LessOrEqual(needed, available + 0.01f,
                        $"title '{node.title}' does not fit at textScale {textScale}");
                }

                foreach (GraphStack stack in model.stacks)
                {
                    float needed = SnippetCanvasRenderer.MeasureText(stack.title, metrics.TitleFontSize);
                    Assert.LessOrEqual(needed, available + 0.01f,
                        $"stack title '{stack.title}' does not fit at textScale {textScale}");
                }
            }
        }

        [Test]
        public void AuthoredGroupsAreParsedWithTheirMembers()
        {
            GraphModel model = Load(TerrainPath);

            Assert.IsNotEmpty(model.groups, "the Terrain fixture contains a group named Layer Mask");

            int members = 0;
            foreach (GraphGroup group in model.groups)
            {
                Assert.IsFalse(string.IsNullOrEmpty(group.id));
                Assert.IsFalse(string.IsNullOrEmpty(group.title));
                Assert.IsNotEmpty(group.members, $"group {group.title} must have members");
                members += group.members.Count;
            }

            Assert.Greater(members, 0);
            foreach (GraphGroup group in model.groups)
                foreach (GraphNode member in group.members)
                    Assert.AreEqual(group.id, member.groupId, "membership must be reciprocal");
        }

        [Test]
        public void EveryGroupGetsAFrameThatContainsItsMembers()
        {
            GraphModel model = Load(TerrainPath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, LayoutOptions.Default);

            foreach (GraphGroup group in model.groups)
            {
                Assert.IsTrue(layout.groupRects.ContainsKey(group.id), $"group {group.title} must be framed");
                Rect frame = layout.groupRects[group.id];

                foreach (GraphNode member in group.members)
                {
                    Rect rect = layout.nodeRects[member.id];
                    Assert.GreaterOrEqual(rect.xMin, frame.xMin, $"member {member.title} must sit inside the frame");
                    Assert.LessOrEqual(rect.xMax, frame.xMax, $"member {member.title} must sit inside the frame");
                    Assert.GreaterOrEqual(rect.yMin, frame.yMin, $"member {member.title} must sit inside the frame");
                    Assert.LessOrEqual(rect.yMax, frame.yMax, $"member {member.title} must sit inside the frame");
                }

                // The frame reserves a strip above the members for the title.
                Assert.Greater(frame.height, LayoutMetrics.Default.PortRowHeight, "the frame must leave room for a title");
            }
        }

        [Test]
        public void AuthoredStickyNotesAreParsed()
        {
            GraphModel model = Load(TerrainPath);

            Assert.IsNotEmpty(model.notes, "the Terrain fixture contains two sticky notes");

            bool anyContent = false;
            foreach (GraphNote note in model.notes)
            {
                Assert.IsFalse(string.IsNullOrEmpty(note.id));
                if (!string.IsNullOrEmpty(note.content)) anyContent = true;
            }
            Assert.IsTrue(anyContent, "at least one note must carry text");
        }

        [Test]
        public void NotesReserveABandUnderTheGraph()
        {
            GraphModel model = Load(TerrainPath);
            LayoutMetrics metrics = LayoutMetrics.Default;

            GraphLayout without = GraphAutoLayoutEngine.Apply(model, metrics,
                new LayoutOptions { balance = 1f, locality = 1f });
            GraphLayout with = GraphAutoLayoutEngine.Apply(model, metrics,
                new LayoutOptions { balance = 1f, locality = 1f, reserveNotesBand = true });

            Assert.AreEqual(0f, without.notesBandHeight, 0.01f, "no band unless asked for");
            Assert.Greater(with.notesBandHeight, 0f, "the notes band must reserve height");
            Assert.AreEqual(without.size.y + with.notesBandHeight, with.size.y, 0.5f,
                "the canvas must grow by exactly the band height");
            Assert.AreEqual(without.size.x, with.size.x, 0.01f, "the band must not change the width");
        }

        [Test]
        public void LegendReservesABand()
        {
            GraphModel model = Load(WavePath);
            LayoutMetrics metrics = LayoutMetrics.Default;

            GraphLayout without = GraphAutoLayoutEngine.Apply(model, metrics,
                new LayoutOptions { balance = 1f, locality = 1f });
            GraphLayout with = GraphAutoLayoutEngine.Apply(model, metrics,
                new LayoutOptions { balance = 1f, locality = 1f, reserveLegendBand = true });

            Assert.AreEqual(0f, without.legendBandHeight, 0.01f);
            Assert.Greater(with.legendBandHeight, 0f);
            Assert.AreEqual(without.size.y + with.legendBandHeight, with.size.y, 0.5f);
        }

        [Test]
        public void CriticalPathIsARealChainFromSourceToSink()
        {
            GraphModel model = Load(WavePath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                new LayoutOptions { balance = 1f, locality = 1f, highlightCriticalPath = true });

            Assert.IsNotEmpty(layout.criticalPath, "the critical path must be found");

            var successors = new Dictionary<string, List<string>>();
            var predecessors = new Dictionary<string, List<string>>();
            foreach (GraphNode node in model.nodes)
            {
                successors[node.id] = new List<string>();
                predecessors[node.id] = new List<string>();
            }
            foreach (GraphEdge edge in model.edges)
            {
                if (!successors.ContainsKey(edge.outputNodeId) || !predecessors.ContainsKey(edge.inputNodeId)) continue;
                successors[edge.outputNodeId].Add(edge.inputNodeId);
                predecessors[edge.inputNodeId].Add(edge.outputNodeId);
            }

            // Exactly one source and one sink, so the highlight is a single spine and not a scattering.
            int sources = 0;
            int sinks = 0;
            foreach (string id in layout.criticalPath)
            {
                bool hasParentOnPath = false;
                foreach (string parent in predecessors[id])
                    if (layout.criticalPath.Contains(parent)) hasParentOnPath = true;

                bool hasChildOnPath = false;
                foreach (string child in successors[id])
                    if (layout.criticalPath.Contains(child)) hasChildOnPath = true;

                if (!hasParentOnPath) sources++;
                if (!hasChildOnPath) sinks++;
            }

            Assert.AreEqual(1, sources, "the spine must start at exactly one node");
            Assert.AreEqual(1, sinks, "the spine must end at exactly one node");
        }

        [Test]
        public void CriticalPathFlagsOnlyItsOwnWires()
        {
            GraphModel model = Load(WavePath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default,
                new LayoutOptions { balance = 1f, locality = 1f, highlightCriticalPath = true });

            int flagged = 0;
            foreach (WireRoute route in layout.routes)
            {
                if (!route.onCriticalPath) continue;
                flagged++;
                Assert.IsTrue(layout.criticalPath.Contains(route.edge.outputNodeId));
                Assert.IsTrue(layout.criticalPath.Contains(route.edge.inputNodeId));
            }

            Assert.Greater(flagged, 0, "the spine must contain at least one wire");
            Assert.Less(flagged, layout.routes.Count, "the spine must not claim every wire");
        }

        [Test]
        public void CriticalPathIsOffByDefault()
        {
            GraphModel model = Load(WavePath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, LayoutOptions.Default);
            Assert.IsEmpty(layout.criticalPath, "the highlight must be opt-in");
        }

        [Test]
        public void ColumnsAreReportedLeftToRight()
        {
            GraphModel model = Load(WavePath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, LayoutOptions.Default);

            Assert.Greater(layout.columns.Count, 1, "a layered graph must report its columns");
            for (int i = 1; i < layout.columns.Count; i++)
            {
                Assert.AreEqual(i, layout.columns[i].index, "columns must be indexed in order");
                Assert.Greater(layout.columns[i].x, layout.columns[i - 1].x,
                    "each column must sit to the right of the previous one");
            }
        }

        [Test]
        public void PortTypesGetReadableNames()
        {
            Assert.AreEqual("Float", SnippetStyle.DescribePortType("Vector1MaterialSlot"));
            Assert.AreEqual("Vector 2", SnippetStyle.DescribePortType("Vector2MaterialSlot"));
            Assert.AreEqual("Vector 2", SnippetStyle.DescribePortType("UVMaterialSlot"));
            Assert.AreEqual("Vector 3", SnippetStyle.DescribePortType("ColorRGBMaterialSlot"));
            Assert.AreEqual("Vector 4", SnippetStyle.DescribePortType("ColorRGBAMaterialSlot"));
            Assert.AreEqual("Texture", SnippetStyle.DescribePortType("Texture2DInputMaterialSlot"));
            Assert.AreEqual("Boolean", SnippetStyle.DescribePortType("BooleanMaterialSlot"));
            Assert.AreEqual("Matrix", SnippetStyle.DescribePortType("Matrix4MaterialSlot"));
            Assert.AreEqual("Unknown", SnippetStyle.DescribePortType("SamplerStateMaterialSlot"));
            Assert.AreEqual("Unknown", SnippetStyle.DescribePortType(null));
        }

        /// <summary>
        /// A group frame reaches above the topmost node it wraps to make room for its title, so the space
        /// reserved around the graph has to include that overhang.
        ///
        /// It did not: the layout padded the canvas for the nodes only, the frame was placed above that
        /// padding, and its title was drawn outside the content area — under the window chrome and cut in
        /// half. The frame's own rectangle is what has to stay inside, not just the nodes'.
        /// </summary>
        [Test]
        public void GroupFramesStayInsideTheReservedInset()
        {
            GraphModel model = Load(TerrainPath);
            Assert.IsNotEmpty(model.groups, "the fixture must contain a group for this to test anything");

            const float frameInnerGap = 16f;
            foreach (float margin in new[] { 0f, 24f, 64f })
            {
                var preset = ScriptableObject.CreateInstance<SnippetExportPreset>();
                preset.showGroups = true;
                preset.showNotes = false;
                preset.frameMargin = margin;

                var canvas = new SnippetCanvasRenderer();
                canvas.SetData(model, preset, "TerrainSimple");
                GraphLayout layout = canvas.Layout;

                float inset = Mathf.Max(LayoutMetrics.Default.Padding, margin + frameInnerGap);
                float overhang = GraphAutoLayoutEngine.GroupChromeOverhang(LayoutMetrics.Default);
                Assert.Greater(overhang, 0f, "a group frame must reserve room for its title");

                foreach (GraphGroup group in model.groups)
                {
                    Assert.IsTrue(layout.groupRects.TryGetValue(group.id, out Rect frame),
                        $"group '{group.title}' must have a frame");

                    Assert.GreaterOrEqual(frame.xMin, inset - 0.01f,
                        $"margin {margin}: group '{group.title}' frame starts left of the inset");
                    Assert.GreaterOrEqual(frame.yMin, inset - 0.01f,
                        $"margin {margin}: group '{group.title}' title strip reaches above the reserved inset, " +
                        "so it is drawn outside the content area");
                    Assert.LessOrEqual(frame.xMax, layout.size.x - inset + 0.01f,
                        $"margin {margin}: group '{group.title}' frame runs past the right inset");
                    Assert.LessOrEqual(frame.yMax, layout.size.y - inset + 0.01f,
                        $"margin {margin}: group '{group.title}' frame runs past the bottom inset");
                }
            }
        }

        [Test]
        public void BandsStayInsideTheFrameForEveryMargin()
        {
            GraphModel model = Load(TerrainPath);
            LayoutMetrics metrics = LayoutMetrics.Default;
            const float frameInnerGap = 16f;

            // The legend and notes sit at the bottom of the content area. Whatever the frame margin is,
            // they must end above the frame's bottom border, or the border runs through the legend. The
            // same inset is what keeps the frame border clear of the graph, so a margin wide enough to
            // push the inset past the design padding also has to grow the canvas.
            float narrowestWidth = 0f;
            float narrowestHeight = 0f;
            GraphLayout widest = null;
            float widestInset = 0f;
            foreach (float margin in new[] { 0f, 8f, 24f, 40f, 64f })
            {
                float inset = Mathf.Max(metrics.Padding, margin + frameInnerGap);
                GraphLayout layout = GraphAutoLayoutEngine.Apply(model, metrics, new LayoutOptions
                {
                    balance = 1f,
                    locality = 1f,
                    reserveNotesBand = true,
                    reserveLegendBand = true,
                    canvasInset = inset
                });

                float contentBottom = layout.size.y - inset;
                float frameBottom = layout.size.y - margin;

                Assert.Less(contentBottom, frameBottom,
                    $"margin {margin}: bands must end above the frame border (content {contentBottom}, frame {frameBottom})");
                Assert.GreaterOrEqual(inset, margin + frameInnerGap - 0.01f,
                    $"margin {margin}: the inset must clear the frame border");

                if (widest == null)
                {
                    narrowestWidth = layout.size.x;
                    narrowestHeight = layout.size.y;
                }
                widest = layout;
                widestInset = inset;
            }

            // Only the largest margin lifts the inset past the design padding, so that is the step where
            // the canvas has to grow; the smaller margins are all still pinned at metrics.Padding.
            Assert.Greater(widest.size.x, narrowestWidth, "a wider inset must widen the canvas");
            Assert.Greater(widest.size.y, narrowestHeight, "a wider inset must heighten the canvas");

            // Every node must still sit inside the inset, which is what keeps the frame border clear.
            foreach (Rect rect in widest.nodeRects.Values)
            {
                Assert.GreaterOrEqual(rect.xMin, widestInset - 0.01f, "no node may start inside the frame border");
                Assert.LessOrEqual(rect.xMax, widest.size.x - widestInset + 0.01f,
                    "no node may end inside the frame border");
            }
        }

        [Test]
        public void MissingGroupAndNoteDataNeverThrows()
        {
            Assert.DoesNotThrow(() => ShaderGraphParser.ParseText("{ \"m_Type\": \"UnityEditor.ShaderGraph.GraphData\" }"));
            Assert.DoesNotThrow(() => ShaderGraphParser.ParseText(
                "{ \"m_Type\": \"UnityEditor.ShaderGraph.GraphData\", \"m_GroupDatas\": [], \"m_StickyNoteDatas\": [] }"));
        }
    }
}
