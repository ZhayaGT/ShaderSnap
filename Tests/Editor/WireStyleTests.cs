using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;
using ShaderSnap.Editor;

namespace ShaderSnap.Tests
{
    public class WireStyleTests
    {
        static string TerrainPath => TestPaths.Terrain;

        static SnippetExportPreset MakePreset(SnippetExportPreset.WireStyle style)
        {
            var preset = ScriptableObject.CreateInstance<SnippetExportPreset>();
            preset.wireStyle = style;
            preset.resolutionMultiplier = 1;
            preset.showMacOsFrame = false;
            preset.showDropShadow = false;
            preset.showWatermark = false;
            return preset;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Every style writes a multi-megabyte PNG, so the scratch folder is cleared once the class
            // is done rather than left for the next run to trip over.
            TestPaths.CleanOutput();
        }

        [Test]
        [Category("RequiresGPU")]
        public void EveryStyleExports()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            Assert.IsNotNull(model, "fixture must parse");
            string directory = TestPaths.Output;
            Directory.CreateDirectory(directory);

            foreach (SnippetExportPreset.WireStyle style in System.Enum.GetValues(typeof(SnippetExportPreset.WireStyle)))
            {
                SnippetExportPreset preset = MakePreset(style);
                string path = Path.Combine(directory, $"wire_{style}.png");
                Assert.IsTrue(PNGExportUtility.Export(model, preset, "TerrainSimple", path, out string error),
                    $"{style} must export: {error}");
                Assert.IsTrue(File.Exists(path), $"{style} must write a file");
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void StyleIsCopiedWithThePreset()
        {
            SnippetExportPreset source = MakePreset(SnippetExportPreset.WireStyle.Conduit);
            source.showNodeValues = true;
            var target = ScriptableObject.CreateInstance<SnippetExportPreset>();

            target.CopyFrom(source);

            Assert.AreEqual(SnippetExportPreset.WireStyle.Conduit, target.wireStyle);
            Assert.IsTrue(target.showNodeValues, "the value-row toggle must survive a preset load");

            Object.DestroyImmediate(source);
            Object.DestroyImmediate(target);
        }

        [Test]
        [Category("RequiresGPU")]
        public void StylesProduceDifferentImages()
        {
            RequiresGraphics.SkipIfUnavailable();
            GraphModel model = ShaderGraphParser.Parse(TerrainPath);
            string directory = TestPaths.Output;
            Directory.CreateDirectory(directory);

            var bytes = new Dictionary<SnippetExportPreset.WireStyle, byte[]>();
            foreach (SnippetExportPreset.WireStyle style in System.Enum.GetValues(typeof(SnippetExportPreset.WireStyle)))
            {
                SnippetExportPreset preset = MakePreset(style);
                string path = Path.Combine(directory, $"distinct_{style}.png");
                Assert.IsTrue(PNGExportUtility.Export(model, preset, "TerrainSimple", path, out string error), error);
                bytes[style] = File.ReadAllBytes(path);
                Object.DestroyImmediate(preset);
            }

            CollectionAssert.AreNotEqual(bytes[SnippetExportPreset.WireStyle.Orthogonal],
                bytes[SnippetExportPreset.WireStyle.Bezier],
                "a curved style must not render identically to the rounded-corner style");
            CollectionAssert.AreNotEqual(bytes[SnippetExportPreset.WireStyle.Orthogonal],
                bytes[SnippetExportPreset.WireStyle.Conduit],
                "chamfered corners must not render identically to rounded corners");
        }
    }
}
