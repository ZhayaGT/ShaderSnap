using System.Collections.Generic;
using NUnit.Framework;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// The capability filter the export window uses to decide which background modes to offer.
    ///
    /// The filter takes the capability as a parameter rather than reading
    /// <see cref="ShaderSnapCapabilities.GradientBackgrounds"/>, so the editor-below-6000.3 branch is
    /// exercised here even though this suite runs on an editor that has the feature. Nothing here renders
    /// and nothing waits on the player loop, so the tests run everywhere, including a headless runner.
    /// </summary>
    public class ShaderSnapCapabilitiesTests
    {
        static readonly SnippetExportPreset.BackgroundMode[] AllModes =
        {
            SnippetExportPreset.BackgroundMode.SolidColor,
            SnippetExportPreset.BackgroundMode.Gradient,
            SnippetExportPreset.BackgroundMode.Transparent,
            SnippetExportPreset.BackgroundMode.BlurredEditor
        };

        [Test]
        public void WithoutGradientsOnlySolidColourAndTransparentRemain()
        {
            List<SnippetExportPreset.BackgroundMode> modes = ShaderSnapCapabilities.AvailableBackgroundModes(false);

            Assert.AreEqual(new[] { SnippetExportPreset.BackgroundMode.SolidColor,
                                    SnippetExportPreset.BackgroundMode.Transparent }, modes);
        }

        [Test]
        public void WithGradientsEveryModeIsAvailableInDeclarationOrder()
        {
            List<SnippetExportPreset.BackgroundMode> modes = ShaderSnapCapabilities.AvailableBackgroundModes(true);

            Assert.AreEqual(AllModes, modes);
        }

        [Test]
        public void GradientModesFollowTheCapability()
        {
            Assert.IsFalse(ShaderSnapCapabilities.IsBackgroundModeAvailable(
                SnippetExportPreset.BackgroundMode.Gradient, false));
            Assert.IsFalse(ShaderSnapCapabilities.IsBackgroundModeAvailable(
                SnippetExportPreset.BackgroundMode.BlurredEditor, false));

            Assert.IsTrue(ShaderSnapCapabilities.IsBackgroundModeAvailable(
                SnippetExportPreset.BackgroundMode.Gradient, true));
            Assert.IsTrue(ShaderSnapCapabilities.IsBackgroundModeAvailable(
                SnippetExportPreset.BackgroundMode.BlurredEditor, true));
        }

        [Test]
        public void TransparentSurvivesWithoutGradients()
        {
            Assert.IsTrue(ShaderSnapCapabilities.IsBackgroundModeAvailable(
                SnippetExportPreset.BackgroundMode.Transparent, false));
        }
    }
}
