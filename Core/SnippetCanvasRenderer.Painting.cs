// Painter2D shape primitives and the mesh-level texture blit shared by the other partials.
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Painting: the low-level Painter2D shape primitives and the texture blit every other partial builds on.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        static Gradient BuildGradient(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(to.a, 1f) });
            return gradient;
        }

        static void RoundedRectPath(Painter2D painter, float x, float y, float width, float height, float radius)
        {
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);
            painter.BeginPath();
            if (r <= 0.01f)
            {
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + width, y));
                painter.LineTo(new Vector2(x + width, y + height));
                painter.LineTo(new Vector2(x, y + height));
                painter.ClosePath();
                return;
            }

            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(x + width - r, y));
            painter.ArcTo(new Vector2(x + width, y), new Vector2(x + width, y + r), r);
            painter.LineTo(new Vector2(x + width, y + height - r));
            painter.ArcTo(new Vector2(x + width, y + height), new Vector2(x + width - r, y + height), r);
            painter.LineTo(new Vector2(x + r, y + height));
            painter.ArcTo(new Vector2(x, y + height), new Vector2(x, y + height - r), r);
            painter.LineTo(new Vector2(x, y + r));
            painter.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            painter.ClosePath();
        }

        static void HeaderPath(Painter2D painter, float x, float y, float width, float height, float radius)
        {
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(x + width - r, y));
            if (r > 0.01f) painter.ArcTo(new Vector2(x + width, y), new Vector2(x + width, y + r), r);
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.LineTo(new Vector2(x, y + r));
            if (r > 0.01f) painter.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            painter.ClosePath();
        }

        static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, 0f, 360f, ArcDirection.Clockwise);
            painter.Fill(FillRule.NonZero);
        }

    }
}
