using System;
using System.Collections.Generic;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Which optional drawing features the running Unity version has.
    ///
    /// ShaderSnap supports Unity 2022.3 and newer. Everything the renderer needs exists in 2022.3 except
    /// <c>Painter2D.fillGradient</c>, which arrived in Unity 6000.3. The renderer handles its absence with a
    /// compile-time fallback that fills flat; this type is what the window reads so it never offers a
    /// background mode that would silently render as something other than what it says.
    /// </summary>
    public static class ShaderSnapCapabilities
    {
        /// <summary>
        /// Whether the gradient and blurred-editor background modes can be drawn.
        ///
        /// <c>static readonly</c> rather than <c>const</c> on purpose. As a constant the compiler folds every
        /// read, so the window's <c>if (GradientBackgrounds)</c> would make its other branch unreachable and
        /// warn (CS0162). A field keeps both branches compiled and type-checked on every editor — which is
        /// the only way the popup path below 6000.3 is compile-verified on an editor that has the feature.
        /// </summary>
        public static readonly bool GradientBackgrounds =
#if UNITY_6000_3_OR_NEWER
            true;
#else
            false;
#endif

        /// <summary>The first Unity version with <see cref="GradientBackgrounds"/>, for the UI to name.</summary>
        public const string GradientBackgroundsSince = "Unity 6000.3";

        /// <summary>
        /// Whether <paramref name="mode"/> can be drawn.
        ///
        /// <paramref name="gradientBackgrounds"/> is a parameter rather than a read of
        /// <see cref="GradientBackgrounds"/> so the unavailable-mode path can be tested on an editor that
        /// has the feature. The window passes <see cref="GradientBackgrounds"/>.
        /// </summary>
        public static bool IsBackgroundModeAvailable(SnippetExportPreset.BackgroundMode mode, bool gradientBackgrounds)
        {
            if (gradientBackgrounds) return true;
            return mode == SnippetExportPreset.BackgroundMode.SolidColor ||
                   mode == SnippetExportPreset.BackgroundMode.Transparent;
        }

        /// <summary>
        /// The background modes this editor can draw, in <see cref="SnippetExportPreset.BackgroundMode"/>
        /// declaration order. Same parameterisation reason as
        /// <see cref="IsBackgroundModeAvailable"/>.
        /// </summary>
        public static List<SnippetExportPreset.BackgroundMode> AvailableBackgroundModes(bool gradientBackgrounds)
        {
            var modes = new List<SnippetExportPreset.BackgroundMode>();
            foreach (SnippetExportPreset.BackgroundMode mode in
                     (SnippetExportPreset.BackgroundMode[])Enum.GetValues(typeof(SnippetExportPreset.BackgroundMode)))
            {
                if (IsBackgroundModeAvailable(mode, gradientBackgrounds)) modes.Add(mode);
            }
            return modes;
        }
    }
}
