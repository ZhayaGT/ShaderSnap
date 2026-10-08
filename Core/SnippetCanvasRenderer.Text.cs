using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Text: glyph measurement, label truncation and the actual text drawing calls.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        static string FitLabel(string text, float maxWidth, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (maxWidth <= 0f) return "…";
            if (MeasureText(text, fontSize) <= maxWidth) return text;

            for (int length = text.Length - 1; length > 0; length--)
            {
                string candidate = text.Substring(0, length) + "…";
                if (MeasureText(candidate, fontSize) <= maxWidth) return candidate;
            }
            return "…";
        }

        /// <summary>
        /// Width of <paramref name="text"/> in canvas units. Uses the real glyph advances of the font
        /// asset that will be used to draw it; the previous <c>length * fontSize * 0.55</c> guess was
        /// more than twice the true width, which pushed right-aligned labels away from their ports and
        /// made <see cref="FitLabel"/> truncate text that would have fit.
        /// </summary>
        public static float MeasureText(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            FontAsset fontAsset = ResolveFontAsset();
            if (fontAsset == null || glyphAdvances == null || glyphAdvances.Count == 0)
                return text.Length * fontSize * 0.55f;

            float pointSize = fontAsset.faceInfo.pointSize;
            if (pointSize <= 0f) return text.Length * fontSize * 0.55f;

            float units = 0f;
            foreach (char character in text)
            {
                if (glyphAdvances.TryGetValue(character, out float advance)) units += advance;
                else units += 0.55f * pointSize;
            }
            return units / pointSize * fontSize;
        }

        void DrawLabel(MeshGenerationContext context, string text, Vector2 position, float fontSize, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Position stays in canvas units (the panel scales it); the size does not, see
            // OnGenerateVisualContent. Keeping the two consistent is what makes text readable at 4x.
            context.DrawText(text, position, fontSize * textPixelScale, color, ResolveFontAsset());
        }

        static FontAsset ResolveFontAsset()
        {
            if (cachedFont != null) return cachedFont;
            Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtin == null) builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (builtin == null) return null;
            cachedFont = FontAsset.CreateFontAsset(builtin);
            if (cachedFont == null) return null;
            // HideAndDontSave includes DontUnloadUnusedAsset, which would keep this FontAsset and its
            // generated atlas alive past the domain reload that drops the static field holding it — one
            // leak per reload for the rest of the editor session. Hiding it is enough; the unload guard
            // is what has to go.
            cachedFont.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;

            // The asset starts empty (dynamic population), so pull in the printable ASCII range once
            // and cache the advances; MeasureText needs them before anything has been drawn.
            var ascii = new System.Text.StringBuilder(95);
            for (char c = ' '; c <= '~'; c++) ascii.Append(c);
            cachedFont.TryAddCharacters(ascii.ToString(), false);

            glyphAdvances = new Dictionary<char, float>(cachedFont.characterTable.Count);
            foreach (var character in cachedFont.characterTable)
                glyphAdvances[(char)character.unicode] = character.glyph.metrics.horizontalAdvance;

            return cachedFont;
        }
    }
}
