// The macOS window chrome and the watermark stamp, drawn after the graph so they overlay it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Frame: the macOS window chrome and the watermark, both drawn last so they sit on top.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        void DrawWindowFrame(Painter2D painter, MeshGenerationContext context)
        {
            if (!preset.showMacOsFrame) return;

            float margin = FrameMargin();
            var frame = new Rect(margin, margin, contentSize.x - margin * 2f, contentSize.y - margin * 2f);
            if (frame.width <= 4f || frame.height <= 4f) return;

            float radius = Mathf.Min(preset.cornerRadius, Mathf.Min(frame.width, frame.height) * 0.5f);

            if (preset.showDropShadow)
            {
                painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
                painter.lineWidth = 10f;
                RoundedRectPath(painter, frame.x + preset.shadowOffset, frame.y + preset.shadowOffset,
                    frame.width, frame.height, radius);
                painter.Stroke();
            }

            painter.fillColor = preset.frameColor;
            HeaderPath(painter, frame.x, frame.y, frame.width, FrameTitleBarHeight, radius);
            painter.Fill(FillRule.NonZero);

            painter.strokeColor = preset.frameBorderColor;
            painter.lineWidth = 2f;
            RoundedRectPath(painter, frame.x, frame.y, frame.width, frame.height, radius);
            painter.Stroke();

            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(frame.x, frame.y + FrameTitleBarHeight));
            painter.LineTo(new Vector2(frame.xMax, frame.y + FrameTitleBarHeight));
            painter.Stroke();

            float centerY = frame.y + FrameTitleBarHeight * 0.5f;
            float firstLightX = frame.x + 16f;
            FillCircle(painter, new Vector2(firstLightX, centerY), TrafficLightRadius, new Color(1.00f, 0.37f, 0.34f));
            FillCircle(painter, new Vector2(firstLightX + TrafficLightSpacing, centerY), TrafficLightRadius, new Color(1.00f, 0.74f, 0.24f));
            FillCircle(painter, new Vector2(firstLightX + TrafficLightSpacing * 2f, centerY), TrafficLightRadius, new Color(0.27f, 0.80f, 0.29f));

            if (!string.IsNullOrEmpty(shaderDisplayName))
            {
                float width = MeasureText(shaderDisplayName, metrics.WatermarkFontSize);
                DrawLabel(context, shaderDisplayName,
                    new Vector2(frame.x + (frame.width - width) * 0.5f, centerY - metrics.WatermarkFontSize * 0.62f),
                    metrics.WatermarkFontSize, new Color(0.85f, 0.85f, 0.88f));
            }
        }

        /// <summary>
        /// Height of the watermark logo block, and the gap it leaves above the text lines.
        /// </summary>
        const float WatermarkLogoHeight = 48f;
        const float WatermarkLogoGap = 8f;

        /// <summary>Clear space above the first watermark line, inside its reserved strip.</summary>
        const float WatermarkTopGap = 8f;

        /// <summary>Leading added to each watermark line's font size.</summary>
        const float WatermarkLineGap = 2f;

        /// <summary>
        /// Draws the watermark text into the strip the layout reserved for it at the bottom of the canvas,
        /// right-aligned: the shader's name, the author and the date, stacked.
        ///
        /// The strip is the point. The watermark used to grow upward from the graph's bottom edge, at the
        /// right, which is exactly where the last column's nodes sit — so it was drawn over them. Reserving
        /// the space first means the graph simply ends above it.
        ///
        /// The logo is not drawn here. It is a child <see cref="VisualElement"/> carrying the texture as a
        /// background image; see <see cref="UpdateWatermarkLogo"/> for why.
        /// </summary>
        void DrawWatermark(MeshGenerationContext context)
        {
            var lines = WatermarkLines(preset, shaderDisplayName);
            if (lines.Count == 0) return;

            float pad = ContentInset();
            float right = contentSize.x - pad;
            float y = WatermarkStripTop() + WatermarkTopGap;

            foreach (string line in lines)
            {
                float width = MeasureText(line, metrics.WatermarkFontSize);
                DrawLabel(context, line, new Vector2(right - width, y),
                    metrics.WatermarkFontSize, new Color(0.88f, 0.88f, 0.92f));
                y += metrics.WatermarkFontSize + WatermarkLineGap;
            }
        }

        /// <summary>Top of the strip the layout reserved for the watermark, in canvas units.</summary>
        float WatermarkStripTop()
        {
            return contentSize.y - ContentInset() - layout.watermarkBandHeight;
        }

        /// <summary>Where the logo goes: below the text lines, inside the reserved strip.</summary>
        Vector2 WatermarkLogoTopLeft()
        {
            float pad = ContentInset();
            float right = contentSize.x - pad;
            float height = WatermarkLogoHeight;
            float width = height * ((float)preset.watermarkLogo.width / Mathf.Max(1, preset.watermarkLogo.height));

            float textHeight = WatermarkLines(preset, shaderDisplayName).Count
                * (metrics.WatermarkFontSize + WatermarkLineGap);
            float top = WatermarkStripTop() + WatermarkTopGap + textHeight + WatermarkLogoGap;

            return new Vector2(right - width, top);
        }

        bool HasWatermarkLogo => preset.showWatermark && preset.watermarkLogo != null;

        /// <summary>
        /// Positions the watermark logo, which is a child element rather than something painted.
        ///
        /// Painting a texture did not work here. <c>MeshGenerationContext.Allocate</c> with a texture produced
        /// nothing at all, and <c>Painter2D.fillTexture</c> drew the shape without sampling the texture — both
        /// verified by exporting with a solid magenta texture and counting pixels, and by swapping in the
        /// built-in <c>Texture2D.whiteTexture</c>, which was equally invisible. A background image on a child
        /// element is the supported way to show a texture in UI Toolkit, and it needs no mesh plumbing.
        ///
        /// Children are drawn after their parent's generated content, so the logo still sits on top of the
        /// graph and the frame, which is the order a watermark wants.
        ///
        /// Called from both <see cref="Rebuild"/> and <see cref="Refresh"/>: it is a child-element update
        /// rather than painted geometry, so a repaint alone does not cover it. Omitting the Refresh call is
        /// what made an assigned logo appear only after some unrelated option was toggled.
        /// </summary>
        void UpdateWatermarkLogo()
        {
            if (!HasWatermarkLogo)
            {
                if (watermarkLogo != null) watermarkLogo.style.display = DisplayStyle.None;
                return;
            }

            if (watermarkLogo == null)
            {
                watermarkLogo = new VisualElement { name = "watermark-logo", pickingMode = PickingMode.Ignore };
                watermarkLogo.style.position = Position.Absolute;
                Add(watermarkLogo);
            }

            Texture2D logo = preset.watermarkLogo;
            Vector2 topLeft = WatermarkLogoTopLeft();

            watermarkLogo.style.display = DisplayStyle.Flex;
            watermarkLogo.style.backgroundImage = new StyleBackground(logo);
            watermarkLogo.style.width = WatermarkLogoHeight
                * ((float)logo.width / Mathf.Max(1, logo.height));
            watermarkLogo.style.height = WatermarkLogoHeight;
            watermarkLogo.style.left = topLeft.x;
            watermarkLogo.style.top = topLeft.y;
        }
    }
}
