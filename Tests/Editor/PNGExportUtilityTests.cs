using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;
using ShaderSnap.Editor;

namespace ShaderSnap.Tests
{
    public class PNGExportUtilityTests
    {
        static string FixturePath => TestPaths.Unlit;

        static string OutputDirectory =>
            TestPaths.Output;

        SnippetExportPreset preset;

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(OutputDirectory);
            preset = ScriptableObject.CreateInstance<SnippetExportPreset>();
            preset.authorName = "ShaderSnap Tests";
            preset.showWatermark = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (preset != null) Object.DestroyImmediate(preset);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // The exports here are several megabytes each, so the scratch folder is cleared once the
            // class is done rather than left for the next run to trip over.
            TestPaths.CleanOutput();
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_WritesRequestedDimensionsForEveryMultiplier()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            GraphLayout result = LayoutFor(model, preset);
            Vector2 canvas = result.size;

            foreach (int multiplier in new[] { 1, 2, 4 })
            {
                preset.resolutionMultiplier = multiplier;
                string path = Path.Combine(OutputDirectory, $"unlit_{multiplier}x.png");

                Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

                Texture2D decoded = Decode(path);
                Assert.AreEqual(Mathf.CeilToInt(canvas.x * multiplier), decoded.width, $"width at {multiplier}x");
                Assert.AreEqual(Mathf.CeilToInt(canvas.y * multiplier), decoded.height, $"height at {multiplier}x");
                Object.DestroyImmediate(decoded);
            }
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_IsDeterministic()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.resolutionMultiplier = 2;
            preset.wireColorMode = SnippetExportPreset.WireColorMode.Gradient;

            string first = Path.Combine(OutputDirectory, "determinism_a.png");
            string second = Path.Combine(OutputDirectory, "determinism_b.png");

            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", first, out string errorA), errorA);
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", second, out string errorB), errorB);

            CollectionAssert.AreEqual(File.ReadAllBytes(first), File.ReadAllBytes(second),
                "identical parameters must produce identical PNG bytes");
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_IsDeterministicWithWatermarkEnabled()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.resolutionMultiplier = 2;
            preset.showWatermark = true;
            preset.authorName = "ShaderSnap Tests";
            // The watermark stamps the date, which is the only time-dependent pixel in the image. The
            // clock hook freezes it, so the two exports can still be compared byte for byte instead of
            // being weakened to a length check that would pass even if the watermark moved.
            preset.clock = () => new System.DateTime(2026, 1, 1, 12, 0, 0);

            string first = Path.Combine(OutputDirectory, "determinism_wm_a.png");
            string second = Path.Combine(OutputDirectory, "determinism_wm_b.png");

            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", first, out string errorA), errorA);
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", second, out string errorB), errorB);

            CollectionAssert.AreEqual(File.ReadAllBytes(first), File.ReadAllBytes(second),
                "watermark date is the only time-dependent element and must be stable within a session");
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_KeepsNodesVisibleWhenWindowFrameEnabled()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            Dictionary<string, Rect> layout = LayoutFor(model, preset).nodeRects;

            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = true;
            preset.showDropShadow = true;
            preset.showWatermark = false;

            string path = Path.Combine(OutputDirectory, "frame_nodes.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);

            int margin = Mathf.Clamp(Mathf.RoundToInt(preset.frameMargin), 0, (int)LayoutMetrics.Default.Padding - 8);
            int bodyTop = margin + 28;
            var histogram = new Dictionary<int, int>();
            for (int x = margin + 2; x < decoded.width - margin - 2; x += 3)
            {
                for (int y = bodyTop; y < decoded.height - margin - 2; y += 3)
                {
                    Color32 pixel = decoded.GetPixel(x, y);
                    int key = (pixel.r << 16) | (pixel.g << 8) | pixel.b;
                    histogram.TryGetValue(key, out int count);
                    histogram[key] = count + 1;
                }
            }

            Assert.IsNotEmpty(histogram, "frame body must be sampled");
            int dominant = 0;
            int dominantCount = 0;
            foreach (KeyValuePair<int, int> entry in histogram)
            {
                if (entry.Value <= dominantCount) continue;
                dominant = entry.Key;
                dominantCount = entry.Value;
            }

            Color32 dominantColor = new Color32((byte)((dominant >> 16) & 0xFF), (byte)((dominant >> 8) & 0xFF), (byte)(dominant & 0xFF), 255);
            int visible = 0;
            foreach (Rect rect in layout.Values)
            {
                int x = Mathf.RoundToInt(rect.x + rect.width * 0.5f);
                int top = Mathf.RoundToInt(rect.y + 6f);
                int y = decoded.height - 1 - top;
                if (x < 0 || x >= decoded.width || y < 0 || y >= decoded.height) continue;

                Color sample = decoded.GetPixel(x, y);
                float delta = Mathf.Abs(sample.r - dominantColor.r / 255f)
                            + Mathf.Abs(sample.g - dominantColor.g / 255f)
                            + Mathf.Abs(sample.b - dominantColor.b / 255f);
                if (delta > 0.02f) visible++;
            }

            Assert.Greater(visible, 0,
                "node headers must stand out from the frame body; a flat body means the frame fill covers the graph");
            Object.DestroyImmediate(decoded);
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_TransparentModeKeepsZeroAlphaBackground()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.resolutionMultiplier = 1;
            preset.backgroundMode = SnippetExportPreset.BackgroundMode.Transparent;

            string path = Path.Combine(OutputDirectory, "transparent.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            Assert.AreEqual(0f, decoded.GetPixel(1, 1).a, 0.004f, "background corner must be fully transparent");
            Object.DestroyImmediate(decoded);
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_DoesNotModifySourceAsset()
        {
            RequiresGraphics.SkipIfUnavailable();
            byte[] before = File.ReadAllBytes(FixturePath);
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.resolutionMultiplier = 1;

            string path = Path.Combine(OutputDirectory, "readonly.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            CollectionAssert.AreEqual(before, File.ReadAllBytes(FixturePath), "source .shadergraph must stay byte identical");
        }

        [Test]
        public void Export_RejectsEmptyGraph()
        {
            preset.resolutionMultiplier = 1;
            string path = Path.Combine(OutputDirectory, "empty.png");

            Assert.IsFalse(PNGExportUtility.Export(new GraphModel(), preset, "Empty", path, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_DrawsCategoryStrip()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            Dictionary<string, Rect> layout = LayoutFor(model, preset).nodeRects;

            GraphNode node = FindNode(model, "MultiplyNode");
            Assert.IsNotNull(node, "fixture must contain a MultiplyNode to carry the Math category color");
            Rect rect = layout[node.id];

            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = false;
            preset.showDropShadow = false;
            preset.showWatermark = false;

            string path = Path.Combine(OutputDirectory, "category_strip.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            LayoutMetrics metrics = LayoutMetrics.FromFontScale(preset.textScale, preset.verticalSpread);
            int x = Mathf.RoundToInt(rect.x + rect.width * 0.5f);
            int stripY = Mathf.RoundToInt(rect.y + metrics.TitleHeight - metrics.CategoryStripHeight * 0.5f);
            Color expected = SnippetStyle.CategoryMath;

            float best = float.MaxValue;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    int px = x + dx;
                    int py = decoded.height - 1 - (stripY + dy);
                    if (px < 0 || px >= decoded.width || py < 0 || py >= decoded.height) continue;
                    Color sample = decoded.GetPixel(px, py);
                    float delta = Mathf.Abs(sample.r - expected.r)
                                + Mathf.Abs(sample.g - expected.g)
                                + Mathf.Abs(sample.b - expected.b);
                    best = Mathf.Min(best, delta);
                }
            }

            Assert.Less(best, 0.08f,
                $"the Math category strip must be drawn below the title bar; closest sample was off by {best:F3}");
            Object.DestroyImmediate(decoded);
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_ScalesNodeTextWithTextScale()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            Dictionary<string, Rect> layout = LayoutFor(model, preset).nodeRects;

            GraphNode node = FindNode(model, "MultiplyNode");
            Assert.IsNotNull(node, "fixture must contain a MultiplyNode");
            Rect rect = layout[node.id];

            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = false;
            preset.showDropShadow = false;
            preset.showWatermark = false;

            int small = MeasureTitleTextHeight(model, "MultiplyNode", 1f, "text_scale_1.png");
            int large = MeasureTitleTextHeight(model, "MultiplyNode", 2f, "text_scale_2.png");

            Assert.Greater(small, 0, "the node title must render at textScale 1");
            Assert.GreaterOrEqual(large, small * 1.6f,
                $"doubling textScale must roughly double the title text height (got {small} -> {large})");
        }

        int MeasureTitleTextHeight(GraphModel model, string typeSuffix, float textScale, string fileName)
        {
            preset.textScale = textScale;

            // The layout depends on textScale, so the node rect has to be recomputed for the scale
            // being measured; sampling a rect from another scale would read the wrong rows.
            GraphNode node = FindNode(model, typeSuffix);
            Assert.IsNotNull(node, $"fixture must contain a {typeSuffix}");
            Rect rect = LayoutFor(model, preset).nodeRects[node.id];

            string path = Path.Combine(OutputDirectory, fileName);
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            LayoutMetrics metrics = LayoutMetrics.FromFontScale(textScale, preset.verticalSpread);

            int left = Mathf.Max(0, Mathf.RoundToInt(rect.x));
            int right = Mathf.Min(decoded.width - 1, Mathf.RoundToInt(rect.xMax));
            int top = Mathf.RoundToInt(rect.y);
            int bottom = Mathf.RoundToInt(rect.y + metrics.TitleHeight);

            int firstRow = -1;
            int lastRow = -1;
            for (int row = top; row < bottom; row++)
            {
                int textureY = decoded.height - 1 - row;
                if (textureY < 0 || textureY >= decoded.height) continue;

                int bright = 0;
                for (int x = left; x <= right; x++)
                {
                    Color pixel = decoded.GetPixel(x, textureY);
                    if (pixel.r > 0.58f && pixel.g > 0.58f && pixel.b > 0.58f) bright++;
                }

                if (bright < 2) continue;
                if (firstRow < 0) firstRow = row;
                lastRow = row;
            }

            Object.DestroyImmediate(decoded);
            return firstRow < 0 ? 0 : lastRow - firstRow + 1;
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_ScalesTextWithResolutionMultiplier()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            GraphNode node = FindNode(model, "MultiplyNode");
            Assert.IsNotNull(node, "fixture must contain a MultiplyNode");

            preset.showMacOsFrame = false;
            preset.showDropShadow = false;
            preset.showWatermark = false;
            preset.textScale = 1f;

            float at1x = MeasureTitleTextHeightInCanvasUnits(model, node.id, 1);
            float at2x = MeasureTitleTextHeightInCanvasUnits(model, node.id, 2);
            float at4x = MeasureTitleTextHeightInCanvasUnits(model, node.id, 4);

            Assert.Greater(at1x, 0f, "title text must render at 1x");
            Assert.AreEqual(at1x, at2x, at1x * 0.2f,
                $"title text must keep its size relative to the canvas at 2x (1x={at1x}, 2x={at2x})");
            Assert.AreEqual(at1x, at4x, at1x * 0.2f,
                $"title text must keep its size relative to the canvas at 4x (1x={at1x}, 4x={at4x})");
        }

        [Test]
        [Category("RequiresGPU")]
        public void Export_RightAlignedPortLabelSitsNearItsPort()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            GraphNode node = FindNode(model, "MultiplyNode");
            Assert.IsNotNull(node, "fixture must contain a MultiplyNode");

            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = false;
            preset.showDropShadow = false;
            preset.showWatermark = false;
            preset.textScale = 1f;

            Rect rect = LayoutFor(model, preset).nodeRects[node.id];
            LayoutMetrics metrics = LayoutMetrics.FromFontScale(preset.textScale, preset.verticalSpread);

            string path = Path.Combine(OutputDirectory, "label_anchor.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            int left = Mathf.RoundToInt(rect.x);
            // Stop short of the output connector so the port dot is not mistaken for label text.
            int right = Mathf.RoundToInt(rect.xMax) - 6;
            int top = Mathf.RoundToInt(rect.y + metrics.TitleHeight);
            int bottom = Mathf.RoundToInt(rect.y + rect.height);

            // Port labels are drawn at 12px, so antialiasing keeps their peak coverage near 0.5;
            // the node body sits at 0.17, which leaves plenty of separation at this threshold.
            const float threshold = 0.45f;
            int rightmost = -1;
            for (int row = top; row < bottom; row++)
            {
                int textureY = decoded.height - 1 - row;
                if (textureY < 0 || textureY >= decoded.height) continue;
                for (int x = left; x <= right; x++)
                {
                    Color pixel = decoded.GetPixel(x, textureY);
                    if (pixel.r > threshold && pixel.g > threshold && pixel.b > threshold)
                        rightmost = Mathf.Max(rightmost, x);
                }
            }

            Assert.Greater(rightmost, 0, "the port label must render");
            float gap = rect.xMax - rightmost;
            Assert.Less(gap, 24f,
                $"a right-aligned port label must sit near its port; the gap was {gap:F1}px " +
                "(a large gap means the text width estimate is too wide)");
            Object.DestroyImmediate(decoded);
        }

        float MeasureTitleTextHeightInCanvasUnits(GraphModel model, string nodeId, int multiplier)
        {
            preset.resolutionMultiplier = multiplier;
            string path = Path.Combine(OutputDirectory, $"text_scale_{multiplier}x.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            Rect rect = LayoutFor(model, preset).nodeRects[nodeId];
            LayoutMetrics metrics = LayoutMetrics.FromFontScale(preset.textScale, preset.verticalSpread);

            int left = Mathf.Max(0, Mathf.RoundToInt((rect.x + 2f) * multiplier));
            int right = Mathf.Min(decoded.width - 1, Mathf.RoundToInt((rect.xMax - 2f) * multiplier));
            int top = Mathf.RoundToInt(rect.y * multiplier);
            int bottom = Mathf.RoundToInt((rect.y + metrics.TitleHeight) * multiplier);

            int firstRow = -1;
            int lastRow = -1;
            for (int row = top; row < bottom; row++)
            {
                int textureY = decoded.height - 1 - row;
                if (textureY < 0 || textureY >= decoded.height) continue;

                int bright = 0;
                for (int x = left; x <= right; x++)
                {
                    Color pixel = decoded.GetPixel(x, textureY);
                    if (pixel.r > 0.58f && pixel.g > 0.58f && pixel.b > 0.58f) bright++;
                }

                if (bright < 2) continue;
                if (firstRow < 0) firstRow = row;
                lastRow = row;
            }

            Object.DestroyImmediate(decoded);
            return firstRow < 0 ? 0f : (lastRow - firstRow + 1) / (float)multiplier;
        }

        static GraphNode FindNode(GraphModel model, string typeSuffix)
        {
            foreach (GraphNode node in model.nodes)
                if (node.typeName != null && node.typeName.EndsWith(typeSuffix, System.StringComparison.Ordinal))
                    return node;
            return null;
        }

        /// <summary>
        /// Mirrors the layout path SnippetCanvasRenderer uses, so a test comparing against the
        /// exported dimensions cannot drift from the renderer when the preset enables Auto Aspect.
        /// </summary>
        /// <summary>
        /// The placement the exporter will actually use, read from the renderer.
        ///
        /// Rebuilding it here from the metrics is how this helper and the exporter drifted apart: the
        /// renderer decides the node width by measuring the graph's labels, which no metric-only
        /// reconstruction can reproduce, so the tests sampled node rectangles that were offset from the
        /// ones in the PNG.
        /// </summary>
        static GraphLayout LayoutFor(GraphModel model, SnippetExportPreset settings)
        {
            var canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, settings, "UnlitBasic");
            return canvas.Layout;
        }

        /// <summary>
        /// The watermark logo must actually appear in the export.
        ///
        /// It did not. The logo was painted as a mesh allocated with <c>MeshGenerationContext.Allocate</c>,
        /// which produced nothing at all in the offscreen panel, and the first fix attempt —
        /// <c>Painter2D.fillTexture</c> — drew the shape without sampling the texture. Both were invisible
        /// with a solid magenta texture *and* with the built-in <c>Texture2D.whiteTexture</c>, so nothing
        /// short of counting pixels in the output catches it.
        /// </summary>
        [Test]
        [Category("RequiresGPU")]
        public void Export_DrawsTheWatermarkLogo()
        {
            RequiresGraphics.SkipIfUnavailable();

            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.showWatermark = true;
            preset.authorName = "";
            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = false;
            preset.showDropShadow = false;

            // A colour nothing else in the export uses, so counting it is unambiguous.
            var logo = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255);
            logo.SetPixels32(pixels);
            logo.Apply();
            preset.watermarkLogo = logo;

            string path = Path.Combine(OutputDirectory, "watermark_logo.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            Color32[] output = decoded.GetPixels32();

            int matching = 0;
            foreach (Color32 pixel in output)
                if (pixel.r > 200 && pixel.g < 60 && pixel.b > 200) matching++;

            Object.DestroyImmediate(decoded);
            Object.DestroyImmediate(logo);

            // The logo is 48 units tall at 1x, so roughly 48x48 pixels. Allow a wide margin for the exact
            // placement; the point is that a meaningful block of the texture is present, not none of it.
            Assert.Greater(matching, 1000,
                "the watermark logo must appear in the export, not just be assigned to the preset");
        }

        /// <summary>With no logo assigned the watermark must still print its text lines.</summary>
        [Test]
        [Category("RequiresGPU")]
        public void Export_DrawsWatermarkTextWithoutALogo()
        {
            RequiresGraphics.SkipIfUnavailable();

            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            preset.showWatermark = true;
            preset.authorName = "ShaderSnap Tests";
            preset.watermarkLogo = null;
            preset.resolutionMultiplier = 1;

            string path = Path.Combine(OutputDirectory, "watermark_text.png");
            Assert.IsTrue(PNGExportUtility.Export(model, preset, "UnlitBasic", path, out string error), error);

            Texture2D decoded = Decode(path);
            // The bottom-right corner is where the watermark prints; it must not be flat background.
            int lit = 0;
            for (int y = 0; y < decoded.height / 6; y++)
                for (int x = decoded.width - decoded.width / 3; x < decoded.width; x++)
                    if (decoded.GetPixel(x, y).r > 0.5f) lit++;

            Object.DestroyImmediate(decoded);
            Assert.Greater(lit, 20, "the watermark text must be drawn in the bottom-right corner");
        }

        static Texture2D Decode(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(ImageConversion.LoadImage(texture, File.ReadAllBytes(path)), "exported PNG must decode");
            return texture;
        }
    }
}
