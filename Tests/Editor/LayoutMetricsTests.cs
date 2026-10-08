using System;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class LayoutMetricsTests
    {
        static string TerrainPath => TestPaths.Terrain;

        [Test]
        public void FromFontScale_GrowsVerticalMetricsAndFonts()
        {
            float[] scales = { 0.5f, 1f, 2f, 3f };
            LayoutMetrics previous = default;

            for (int i = 0; i < scales.Length; i++)
            {
                LayoutMetrics metrics = LayoutMetrics.FromFontScale(scales[i]);

                Assert.AreEqual(scales[i], metrics.FontScale, 0.001f);

                if (i > 0)
                {
                    Assert.Greater(metrics.TitleHeight, previous.TitleHeight, $"title height must grow at {scales[i]}");
                    Assert.Greater(metrics.PortRowHeight, previous.PortRowHeight, $"port row must grow at {scales[i]}");
                    Assert.Greater(metrics.TitleFontSize, previous.TitleFontSize, $"title font must grow at {scales[i]}");
                    Assert.Greater(metrics.PortLabelFontSize, previous.PortLabelFontSize, $"port font must grow at {scales[i]}");
                }
                previous = metrics;
            }
        }

        [Test]
        public void FromFontScale_ClampsToSupportedRange()
        {
            Assert.AreEqual(LayoutMetrics.MinFontScale, LayoutMetrics.FromFontScale(0.1f).FontScale, 0.001f);
            Assert.AreEqual(LayoutMetrics.MinFontScale, LayoutMetrics.FromFontScale(-4f).FontScale, 0.001f);
            Assert.AreEqual(LayoutMetrics.MaxFontScale, LayoutMetrics.FromFontScale(10f).FontScale, 0.001f);
            Assert.AreEqual(1f, LayoutMetrics.Default.FontScale, 0.001f);
        }

        [Test]
        public void FromFontScale_KeepsCanvasWidthIndependentOfFontScale()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");

            GraphLayout small = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.FromFontScale(1f), 1f);
            GraphLayout large = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.FromFontScale(2f), 1f);

            Assert.AreEqual(small.size.x, large.size.x, 0.001f, "canvas width must not depend on the text scale");
            Assert.Greater(large.size.y, small.size.y, "canvas height must grow with the text scale");
        }

        [Test]
        public void VerticalSpreadStretchesHeightButNotWidth()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");

            GraphLayout tight = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.FromFontScale(1f, 1f), 1f);
            GraphLayout loose = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.FromFontScale(1f, 4f), 1f);

            Assert.AreEqual(tight.size.x, loose.size.x, 0.001f, "spread must not change the canvas width");
            Assert.Greater(loose.size.y, tight.size.y, "spread must stretch the canvas height");
        }

        [Test]
        public void TargetAspectStretchesHeightTowardTheRequestedRatio()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");

            GraphLayout tight = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);
            GraphLayout square = GraphAutoLayoutEngine.ApplyWithTargetAspect(model, LayoutMetrics.Default, 1f, 1.4f);

            Assert.Greater(square.size.y, tight.size.y, "the solver must add height");
            Assert.AreEqual(tight.size.x, square.size.x, 0.001f, "the solver must not change the width");

            float tightRatio = tight.size.x / tight.size.y;
            float squareRatio = square.size.x / square.size.y;
            Assert.Less(squareRatio, tightRatio, "the ratio must move toward the target");
            Assert.GreaterOrEqual(squareRatio, 1.4f - 0.15f,
                $"the ratio must get close to the target when reachable, got {squareRatio:F2}");
        }

        [Test]
        public void TargetAspectIsIgnoredWhenTheCanvasIsAlreadySquarerThanRequested()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout wide = GraphAutoLayoutEngine.ApplyWithTargetAspect(model, LayoutMetrics.Default, 1f, 100f);
            GraphLayout plain = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            Assert.AreEqual(plain.size.y, wide.size.y, 0.001f, "an unreachable-in-the-other-direction target must not stretch");
            Assert.AreEqual(plain.size.x, wide.size.x, 0.001f);
        }

        [Test]
        public void TargetAspectClampsToTheSpreadLimit()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout square = GraphAutoLayoutEngine.ApplyWithTargetAspect(model, LayoutMetrics.Default, 1f, 1f);
            GraphLayout widest = GraphAutoLayoutEngine.Apply(model,
                LayoutMetrics.FromFontScale(1f, LayoutMetrics.MaxVerticalSpread), 1f);

            Assert.LessOrEqual(square.size.y, widest.size.y + 0.001f,
                "the solver must never exceed the maximum spread");
            Assert.Greater(square.size.y, 0f);
        }

        [Test]
        public void EveryStackBlockCarriesAReadableTitle()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");
            Assert.IsNotEmpty(model.stacks, "the fixture must contain a master stack");

            int blocks = 0;
            foreach (GraphStack stack in model.stacks)
            {
                foreach (GraphNode block in stack.blocks)
                {
                    blocks++;
                    Assert.IsFalse(string.IsNullOrEmpty(block.title),
                        $"block {block.id} must carry a title");
                    // m_Name holds the internal path ("SurfaceDescription.BaseColor"); the port holds the
                    // label Shader Graph shows in the stack ("Base Color"). The readable one must win.
                    Assert.IsFalse(block.title.StartsWith("SurfaceDescription", StringComparison.Ordinal),
                        $"block {block.id} kept its internal name '{block.title}'");
                    Assert.IsFalse(block.title.Contains("."),
                        $"block {block.id} title '{block.title}' must not look like a path");
                }
            }

            Assert.Greater(blocks, 0, "the fixture must contain block rows");
        }

        [Test]
        public void StackBlockTitleMatchesItsPortLabel()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            foreach (GraphStack stack in model.stacks)
            {
                foreach (GraphNode block in stack.blocks)
                {
                    string label = null;
                    foreach (PortSlot port in block.ports)
                        if (!port.hidden && port.isInput) { label = port.displayName; break; }

                    Assert.IsNotNull(label, $"block {block.id} must expose an input port");
                    Assert.AreEqual(label, block.title,
                        "a block row is labelled by its port, which is the only label it draws");
                }
            }
        }

        [Test]
        public void BlockRowsKeepTheirLabelEvenWhenItEqualsTheTitle()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            // The renderer suppresses a port label only when the node draws its own title. A block row
            // never does, so rows like Emission (title == port) must still be labelled; before the fix
            // this dedup rule fired on rows too and left the middle rows blank.
            int rowsWithEqualTitleAndPort = 0;
            foreach (GraphStack stack in model.stacks)
            {
                foreach (GraphNode block in stack.blocks)
                {
                    Assert.IsTrue(layout.nodeRects.ContainsKey(block.id), "every block row must be laid out");
                    Assert.AreEqual(LayoutMetrics.Default.PortRowHeight, layout.nodeRects[block.id].height, 0.01f,
                        "a block row is one port band tall, so it has no title bar of its own");

                    foreach (PortSlot port in block.ports)
                        if (!port.hidden && port.isInput && port.displayName == block.title)
                            rowsWithEqualTitleAndPort++;
                }
            }

            Assert.Greater(rowsWithEqualTitleAndPort, 0,
                "the fixture must contain a row whose title equals its port label, which is the case that regressed");
        }

        [Test]
        public void MasterStackBlocksStayTogetherInOneColumn()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");
            Assert.IsNotEmpty(model.stacks, "the fixture must contain a master stack");

            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            foreach (GraphStack stack in model.stacks)
            {
                Assert.IsTrue(layout.stackRects.ContainsKey(stack.id), $"stack {stack.id} must be laid out");
                Rect frame = layout.stackRects[stack.id];

                for (int i = 0; i < stack.blocks.Count; i++)
                {
                    GraphNode block = stack.blocks[i];
                    Rect row = layout.nodeRects[block.id];

                    Assert.AreEqual(frame.x, row.x, 0.01f, "every block row must share the stack column");
                    Assert.AreEqual(frame.width, row.width, 0.01f, "every block row must share the stack width");
                    Assert.AreEqual(frame.y + LayoutMetrics.Default.TitleHeight + i * LayoutMetrics.Default.PortRowHeight,
                        row.y, 0.01f, "block rows must stack in declaration order with no gap");
                }

                Assert.AreEqual(LayoutMetrics.Default.TitleHeight
                                + stack.blocks.Count * LayoutMetrics.Default.PortRowHeight,
                    frame.height, 0.01f, "the stack frame must cover the title bar and every row");
            }
        }

        [Test]
        public void MasterStackIsPlacedAsASingleUnit()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            GraphLayout layout = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f);

            foreach (GraphStack stack in model.stacks)
            {
                Rect frame = layout.stackRects[stack.id];

                foreach (GraphNode other in model.nodes)
                {
                    if (other.IsStackBlock && other.stackId == stack.id) continue;
                    Rect otherRect = layout.nodeRects[other.id];

                    // Rows of the stack legitimately overlap their own frame; nothing else may.
                    if (!other.IsStackBlock)
                        Assert.IsFalse(frame.Overlaps(otherRect), $"stack {stack.id} must not overlap node {other.id}");
                }
            }
        }

        [Test]
        public void EveryStackBlockIsGrouped()
        {
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);

            int blockCount = 0;
            foreach (GraphNode node in model.nodes)
            {
                if (node.typeName == null || !node.typeName.EndsWith("BlockNode")) continue;
                blockCount++;
                Assert.IsTrue(node.IsStackBlock, $"block node {node.id} must belong to a stack");
            }

            int grouped = 0;
            foreach (GraphStack stack in model.stacks) grouped += stack.blocks.Count;
            Assert.AreEqual(blockCount, grouped, "every block node must appear in exactly one stack");
        }

        [Test]
        public void VerticalSpreadIsClamped()
        {
            Assert.AreEqual(LayoutMetrics.MinVerticalSpread, LayoutMetrics.FromFontScale(1f, 0.1f).VerticalSpread, 0.001f);
            Assert.AreEqual(LayoutMetrics.MaxVerticalSpread, LayoutMetrics.FromFontScale(1f, 99f).VerticalSpread, 0.001f);
            Assert.AreEqual(LayoutMetrics.BaseVerticalGap * 2f, LayoutMetrics.FromFontScale(1f, 2f).VerticalGap, 0.5f);
        }

        [Test]
        public void EstimateNodeHeight_RespectsMetrics()
        {
            var node = new GraphNode { id = "n", title = "Node" };
            node.ports.Add(new PortSlot { id = "a", displayName = "A", isInput = true, typeName = "Vector1MaterialSlot" });
            node.ports.Add(new PortSlot { id = "b", displayName = "B", isInput = false, typeName = "Vector1MaterialSlot" });

            LayoutMetrics metrics = LayoutMetrics.FromFontScale(1f);
            Assert.AreEqual(metrics.TitleHeight + metrics.PortRowHeight,
                GraphAutoLayoutEngine.EstimateNodeHeight(node, metrics), 0.001f);

            LayoutMetrics doubled = LayoutMetrics.FromFontScale(2f);
            Assert.AreEqual(doubled.TitleHeight + doubled.PortRowHeight,
                GraphAutoLayoutEngine.EstimateNodeHeight(node, doubled), 0.001f);
        }
    }
}
