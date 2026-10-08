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

        void DrawWatermark(MeshGenerationContext context)
        {
            if (!preset.showWatermark) return;

            // Inside the same inset the graph and bands use, so the mark never lands on the frame border
            // or on top of the notes.
            float pad = EffectivePadding();
            float right = contentSize.x - pad;
            float bottom = contentSize.y - pad - layout.BandHeight;

            if (preset.watermarkLogo != null)
            {
                float height = 48f;
                float width = height * ((float)preset.watermarkLogo.width / Mathf.Max(1, preset.watermarkLogo.height));
                DrawTexture(context, new Rect(right - width, bottom - height, width, height),
                    preset.watermarkLogo, Color.white);
                bottom -= height + 8f;
            }

            var lines = new List<string>(3);
            if (!string.IsNullOrEmpty(shaderDisplayName)) lines.Add(shaderDisplayName);
            if (!string.IsNullOrEmpty(preset.authorName)) lines.Add(preset.authorName);
            lines.Add((preset.clock != null ? preset.clock() : System.DateTime.Now).ToString("yyyy-MM-dd"));

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                float width = MeasureText(lines[i], metrics.WatermarkFontSize);
                DrawLabel(context, lines[i],
                    new Vector2(right - width, bottom - metrics.WatermarkFontSize),
                    metrics.WatermarkFontSize, new Color(0.88f, 0.88f, 0.92f));
                bottom -= metrics.WatermarkFontSize + 4f;
            }
        }
    }
}
