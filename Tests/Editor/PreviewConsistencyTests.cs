using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// The preview canvas and the exporter have to agree on the layout. They read the same preset, but
    /// the preview decides whether to re-run the layout from a hand-written list of fields, so a field
    /// missing from that list makes the preview keep the old size while the export uses the new one.
    /// These tests pin that contract for the fields that switch layout entry points.
    /// </summary>
    public class PreviewConsistencyTests
    {
        SnippetExportPreset preset;
        GraphModel model;
        SnippetCanvasRenderer canvas;

        [SetUp]
        public void SetUp()
        {
            model = ShaderGraphParser.Parse(TestPaths.Terrain);
            Assert.IsNotNull(model, "fixture must parse");
            preset = ScriptableObject.CreateInstance<SnippetExportPreset>();
            canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, preset, "TerrainSimple");
        }

        [TearDown]
        public void TearDown()
        {
            if (preset != null) Object.DestroyImmediate(preset);
        }

        [Test]
        public void Refresh_PicksUpAutoAspectChange()
        {
            // autoAspect off is the tighter canvas; turning it on solves the vertical spread instead.
            preset.autoAspect = false;
            canvas.Refresh();
            Vector2 tight = canvas.ContentSize;

            preset.autoAspect = true;
            canvas.Refresh();
            Vector2 stretched = canvas.ContentSize;

            Assert.AreNotEqual(tight.y, stretched.y,
                "toggling autoAspect must change the canvas height in the preview, not only in the export");
        }

        [Test]
        public void Refresh_PicksUpTargetAspectChange()
        {
            preset.autoAspect = true;
            preset.targetAspect = 1.6f;
            canvas.Refresh();
            Vector2 wide = canvas.ContentSize;

            preset.targetAspect = 1.1f;
            canvas.Refresh();
            Vector2 square = canvas.ContentSize;

            Assert.Greater(square.y, wide.y,
                "asking for a squarer target must make the preview taller");
        }

        [Test]
        public void Refresh_PicksUpTextScaleChange()
        {
            preset.autoAspect = false;
            preset.textScale = 1f;
            canvas.Refresh();
            float small = canvas.ContentSize.y;

            preset.textScale = 2f;
            canvas.Refresh();
            float large = canvas.ContentSize.y;

            Assert.Greater(large, small, "raising the text scale must grow the preview");
        }

        [Test]
        public void PreviewMatchesWhatTheExporterLaysOut()
        {
            // The exporter builds its own renderer from the same preset. Any divergence between the two
            // means the preview is showing something the PNG will not contain.
            preset.autoAspect = true;
            preset.textScale = 1.5f;
            canvas.Refresh();

            var exporterCanvas = new SnippetCanvasRenderer();
            exporterCanvas.SetData(model, preset, "TerrainSimple");

            Assert.AreEqual(exporterCanvas.ContentSize.x, canvas.ContentSize.x, 0.01f, "width");
            Assert.AreEqual(exporterCanvas.ContentSize.y, canvas.ContentSize.y, 0.01f, "height");
            Assert.AreEqual(exporterCanvas.NodeWidth, canvas.NodeWidth, 0.01f, "node width");
        }
    }
}
