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

        static void DrawTexture(MeshGenerationContext context, Rect rect, Texture2D texture, Color tint)
        {
            if (texture == null) return;
            var vertices = new Vertex[4];
            var indices = new ushort[] { 0, 1, 2, 2, 3, 0 };

            vertices[0].position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ);
            vertices[1].position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ);
            vertices[2].position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ);
            vertices[3].position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ);
            vertices[0].uv = new Vector2(0f, 0f);
            vertices[1].uv = new Vector2(0f, 1f);
            vertices[2].uv = new Vector2(1f, 1f);
            vertices[3].uv = new Vector2(1f, 0f);
            for (int i = 0; i < 4; i++) vertices[i].tint = tint;

            MeshWriteData mesh = context.Allocate(4, 6, texture);
            mesh.SetAllVertices(vertices);
            mesh.SetAllIndices(indices);
        }
    }
}
