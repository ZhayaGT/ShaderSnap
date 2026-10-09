using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// The contract between group frames and the nodes they wrap.
    ///
    /// A frame is the union of its members, so it spans every column they land in, and two frames overlap
    /// exactly when they share a column and their vertical extents intersect there. The first layout stacked
    /// each column on its own, which let groups interleave: group A's members above group B's in one column
    /// and below them in another, so both frames covered the same rows. On the 12-group reference graph that
    /// produced 22 overlapping pairs, some covering thousands of square units.
    /// </summary>
    public class GroupLayoutTests
    {
        static GraphModel Load(string path)
        {
            GraphModel model = ShaderGraphParser.Parse(path);
            Assert.IsNotNull(model, $"fixture '{path}' must parse");
            return model;
        }

        static SnippetExportPreset Preset()
        {
            return ScriptableObject.CreateInstance<SnippetExportPreset>();
        }

        static GraphLayout LayoutOf(GraphModel model, SnippetExportPreset preset)
        {
            var canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, preset, "test");
            return canvas.Layout;
        }

        static float OverlapArea(Rect a, Rect b)
        {
            float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return width > 0f && height > 0f ? width * height : 0f;
        }

        /// <summary>Every fixture that carries more than one group, so the overlap check has something to find.</summary>
        static IEnumerable<TestCaseData> MultiGroupFixtures()
        {
            yield return new TestCaseData(TestPaths.GroupedNotes).SetName("GroupedNotes");
            yield return new TestCaseData(TestPaths.Terrain).SetName("TerrainSimple");
        }

        [TestCaseSource(nameof(MultiGroupFixtures))]
        public void NoTwoGroupFramesOverlap(string path)
        {
            GraphModel model = Load(path);
            Assert.Greater(model.groups.Count, 0, "the fixture must contain groups for this to test anything");

            var preset = Preset();
            preset.showGroups = true;
            GraphLayout layout = LayoutOf(model, preset);

            var ids = new List<string>(layout.groupRects.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    float area = OverlapArea(layout.groupRects[ids[i]], layout.groupRects[ids[j]]);
                    Assert.AreEqual(0f, area, 0.01f,
                        $"frames '{Title(model, ids[i])}' and '{Title(model, ids[j])}' overlap by {area:F0} square units");
                }
            }
        }

        [Test]
        public void FramesDoNotOverlapAtAnyTextScaleOrSpread()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            foreach (float scale in new[] { 0.5f, 1f, 1.5f, 2.5f })
            {
                foreach (float spread in new[] { 1f, 4f, 12f })
                {
                    var preset = Preset();
                    preset.showGroups = true;
                    preset.textScale = scale;
                    preset.verticalSpread = spread;
                    preset.autoAspect = false;
                    GraphLayout layout = LayoutOf(model, preset);

                    var ids = new List<string>(layout.groupRects.Keys);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        for (int j = i + 1; j < ids.Count; j++)
                        {
                            float area = OverlapArea(layout.groupRects[ids[i]], layout.groupRects[ids[j]]);
                            Assert.AreEqual(0f, area, 0.01f,
                                $"scale {scale} spread {spread}: '{Title(model, ids[i])}' and " +
                                $"'{Title(model, ids[j])}' overlap by {area:F0}");
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(MultiGroupFixtures))]
        public void GroupedNotesAreAnchoredToTheirGroup(string path)
        {
            GraphModel model = Load(path);

            var grouped = new List<GraphNote>();
            foreach (GraphNote note in model.notes)
                if (!string.IsNullOrEmpty(note.groupId)) grouped.Add(note);
            if (grouped.Count == 0) Assert.Ignore("fixture has no notes attached to a group");

            var preset = Preset();
            preset.showGroups = true;
            preset.showNotes = true;
            GraphLayout layout = LayoutOf(model, preset);

            Assert.Greater(layout.notesGutterWidth, 0f, "grouped notes must reserve the annotation gutter");

            foreach (GraphNote note in grouped)
            {
                Assert.IsTrue(layout.noteRects.TryGetValue(note.id, out Rect card),
                    "a grouped note must have a placed rect");
                Assert.IsTrue(layout.groupRects.TryGetValue(note.groupId, out Rect frame),
                    "its group must have a frame");

                // In the gutter, so it cannot sit on the graph.
                Assert.Less(card.xMax, layout.columns[0].x,
                    "a grouped note must sit left of every column");

                // Level with the group it describes: the note's top sits inside the frame's vertical range,
                // so a reader can see which group it belongs to. Its own height may carry it past the
                // frame's bottom, which is expected for a long note on a short group.
                Assert.GreaterOrEqual(card.yMin, frame.yMin - 1f,
                    "the note must not float above the group it describes");
                Assert.LessOrEqual(card.yMin, frame.yMax,
                    "the note must not float below the group it describes");
            }
        }

        /// <summary>
        /// Hiding the group frames must not move the notes.
        ///
        /// The first fix for the reported overlap reserved the gutter only when the frames were drawn, but
        /// anchored notes whenever their group had a rectangle — and those rectangles are built regardless.
        /// With frames off the notes were therefore anchored into a gutter that had not been reserved and
        /// landed on top of the graph: four notes over nodes on the four-group fixture, the worst covering
        /// 17319 square units. Keying the gutter off the frames was the wrong idea in the first place — the
        /// gutter is where notes go, and its reason to exist does not depend on whether a frame is drawn.
        /// </summary>
        [Test]
        public void HidingGroupFramesDoesNotMoveTheNotes()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            var withFrames = Preset();
            withFrames.showGroups = true;
            withFrames.showNotes = true;
            GraphLayout framed = LayoutOf(model, withFrames);

            var withoutFrames = Preset();
            withoutFrames.showGroups = false;
            withoutFrames.showNotes = true;
            GraphLayout plain = LayoutOf(model, withoutFrames);

            Assert.Greater(framed.notesGutterWidth, 0f, "the gutter must be reserved for the notes");
            Assert.AreEqual(framed.notesGutterWidth, plain.notesGutterWidth, 0.01f,
                "hiding the frames must not change the space the notes need");

            // The same notes are anchored either way. Their absolute position is allowed to differ: with the
            // frames on, the content inset grows by the frames' overhang, and the nodes are placed in bands
            // rather than column by column, so both the padding and the frames they align with move.
            CollectionAssert.AreEquivalent(framed.gutterNotes, plain.gutterNotes,
                "the same notes must be anchored whether or not the frames are drawn");
        }

        /// <summary>Anchored notes must clear the graph with the frames hidden as well as shown.</summary>
        [Test]
        public void AnchoredNotesClearTheGraphWithFramesHidden()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            var preset = Preset();
            preset.showGroups = false;
            preset.showNotes = true;
            GraphLayout layout = LayoutOf(model, preset);

            Assert.IsNotEmpty(layout.gutterNotes, "the fixture's grouped notes must still be anchored");

            foreach (string id in layout.gutterNotes)
            {
                Assert.Less(layout.noteRects[id].xMax, layout.columns[0].x,
                    $"note {id} must sit left of every column");

                foreach (KeyValuePair<string, Rect> node in layout.nodeRects)
                {
                    float area = OverlapArea(layout.noteRects[id], node.Value);
                    Assert.AreEqual(0f, area, 0.01f,
                        $"note {id} covers node {node.Key} by {area:F0} square units");
                }
            }
        }

        /// <summary>The gutter and the anchoring decision must agree, whichever way the option is set.</summary>
        [Test]
        public void AnchoringFollowsTheReservedGutter()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            foreach (bool showGroups in new[] { true, false })
            {
                var preset = Preset();
                preset.showGroups = showGroups;
                preset.showNotes = true;
                GraphLayout layout = LayoutOf(model, preset);

                if (layout.notesGutterWidth > 0f)
                {
                    Assert.IsNotEmpty(layout.gutterNotes,
                        $"showGroups {showGroups}: a reserved gutter must be used");
                }
                else
                {
                    Assert.IsEmpty(layout.gutterNotes,
                        $"showGroups {showGroups}: nothing may be anchored without a reserved gutter");
                }
            }
        }

        [Test]
        public void NotesNeverCoverTheGraph()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            var preset = Preset();
            preset.showGroups = true;
            preset.showNotes = true;
            GraphLayout layout = LayoutOf(model, preset);

            Assert.IsNotEmpty(layout.noteRects, "the fixture must place at least one note");

            foreach (KeyValuePair<string, Rect> entry in layout.noteRects)
            {
                foreach (KeyValuePair<string, Rect> node in layout.nodeRects)
                {
                    float area = OverlapArea(entry.Value, node.Value);
                    Assert.AreEqual(0f, area, 0.01f,
                        $"note {entry.Key} covers node {node.Key} by {area:F0} square units");
                }

                foreach (KeyValuePair<string, Rect> group in layout.groupRects)
                {
                    float area = OverlapArea(entry.Value, group.Value);
                    Assert.AreEqual(0f, area, 0.01f,
                        $"note {entry.Key} covers frame {group.Key} by {area:F0} square units");
                }
            }
        }

        [Test]
        public void AnchoredNotesDoNotOverlapEachOther()
        {
            GraphModel model = Load(TestPaths.GroupedNotes);

            var preset = Preset();
            preset.showGroups = true;
            preset.showNotes = true;
            GraphLayout layout = LayoutOf(model, preset);

            var ids = new List<string>(layout.gutterNotes);
            Assert.Greater(ids.Count, 1, "the fixture must anchor more than one note");

            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    float area = OverlapArea(layout.noteRects[ids[i]], layout.noteRects[ids[j]]);
                    Assert.AreEqual(0f, area, 0.01f,
                        $"gutter notes {ids[i]} and {ids[j]} overlap by {area:F0} square units");
                }
            }
        }

        [Test]
        public void EveryGroupedNoteFallsBackToTheBandWhenTheGroupHasNoFrame()
        {
            // A note can name a group whose members were all dropped, so the group gets no frame. Such a note
            // must still be drawn, in the band, rather than silently disappearing.
            GraphModel model = Load(TestPaths.GroupedNotes);

            var preset = Preset();
            preset.showGroups = false;      // no frames at all, so nothing can be anchored
            preset.showNotes = true;
            GraphLayout layout = LayoutOf(model, preset);

            Assert.Greater(layout.notesGutterWidth, 0f,
                "the gutter exists for the notes, so it is reserved with the frames hidden too");
            foreach (GraphNote note in model.notes)
                Assert.IsTrue(layout.noteRects.TryGetValue(note.id, out Rect card) && card.width > 0f,
                    $"note {note.id} must still be placed when its group's frame is not drawn");
        }

        /// <summary>
        /// The watermark must never be drawn over the graph.
        ///
        /// It used to grow upward from the graph's bottom edge at the right, which is exactly where the last
        /// column's nodes sit, so a small graph with a tall final column had the mark printed across its
        /// nodes. Reserving a strip first is the fix; this pins that the strip exists and that no node
        /// reaches into it.
        /// </summary>
        [Test]
        public void TheWatermarkStripIsReservedBelowTheGraph()
        {
            GraphModel model = Load(TestPaths.Terrain);

            var withMark = Preset();
            withMark.showWatermark = true;
            withMark.authorName = "Tests";

            var withoutMark = Preset();
            withoutMark.showWatermark = false;

            GraphLayout marked = LayoutOf(model, withMark);
            GraphLayout plain = LayoutOf(model, withoutMark);

            Assert.AreEqual(0f, plain.watermarkBandHeight, 0.01f, "nothing is reserved when the mark is off");
            Assert.Greater(marked.watermarkBandHeight, 0f, "the mark must reserve a strip");
            Assert.That(marked.size.y, Is.GreaterThan(plain.size.y),
                "the canvas must grow by the strip, not steal room from the graph");

            float stripTop = marked.size.y - marked.watermarkBandHeight;
            foreach (KeyValuePair<string, Rect> node in marked.nodeRects)
                Assert.LessOrEqual(node.Value.yMax, stripTop + 0.01f,
                    $"node {node.Key} reaches into the watermark strip");
        }

        /// <summary>A logo needs more room than the text alone, and the strip must grow to match.</summary>
        [Test]
        public void TheStripGrowsForTheLogo()
        {
            GraphModel model = Load(TestPaths.Terrain);

            var textOnly = Preset();
            textOnly.showWatermark = true;
            textOnly.authorName = "Tests";

            var withLogo = Preset();
            withLogo.showWatermark = true;
            withLogo.authorName = "Tests";
            var logo = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            logo.SetPixels32(new Color32[16 * 16]);
            logo.Apply();
            withLogo.watermarkLogo = logo;

            float textHeight = LayoutOf(model, textOnly).watermarkBandHeight;
            float logoHeight = LayoutOf(model, withLogo).watermarkBandHeight;

            Object.DestroyImmediate(logo);

            Assert.That(logoHeight, Is.GreaterThan(textHeight),
                "the strip must reserve the logo as well as the text");
        }

        /// <summary>
        /// Assigning a logo must take effect on the next refresh.
        ///
        /// The logo is a child element, not painted geometry, so a repaint alone did not cover it: the
        /// element was only positioned from Rebuild, and assigning a logo changes no layout option. The
        /// sprite therefore stayed invisible until some unrelated option was toggled, which is exactly the
        /// behaviour that was reported.
        /// </summary>
        [Test]
        public void AssigningALogoTakesEffectOnRefresh()
        {
            GraphModel model = Load(TestPaths.Unlit);

            var preset = Preset();
            preset.showWatermark = true;
            preset.authorName = "Tests";

            var canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, preset, "LogoRefresh");

            int before = CountVisibleLogoElements(canvas);
            Assert.AreEqual(0, before, "no logo element while no logo is assigned");

            var logo = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            logo.SetPixels32(new Color32[16 * 16]);
            logo.Apply();
            preset.watermarkLogo = logo;

            canvas.Refresh();

            int after = CountVisibleLogoElements(canvas);
            Object.DestroyImmediate(logo);

            Assert.AreEqual(1, after,
                "Refresh alone must show the logo; it must not wait for an unrelated option to force a Rebuild");
        }

        static int CountVisibleLogoElements(VisualElement root)
        {
            int count = 0;
            foreach (VisualElement child in root.Children())
                if (child.name == "watermark-logo" && child.resolvedStyle.display != DisplayStyle.None) count++;
            return count;
        }

        static string Title(GraphModel model, string groupId)
        {
            foreach (GraphGroup group in model.groups)
                if (group.id == groupId) return group.title;
            return groupId;
        }
    }
}
