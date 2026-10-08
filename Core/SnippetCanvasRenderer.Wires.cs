using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Wires: the routed polylines between ports and the arrow heads that terminate them.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        void DrawWires(Painter2D painter)
        {
            bool highlight = preset.highlightCriticalPath && layout.criticalPath.Count > 0;

            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            foreach (WireRoute route in layout.routes)
            {
                if (route.points.Count < 2) continue;
                Color wireColor = ApplyWireColor(painter, route.outputPort);

                if (highlight)
                {
                    // The spine is drawn heavier and fully opaque; the rest recedes but stays readable, so
                    // the reader can still follow a branch once the main chain has been understood.
                    if (route.onCriticalPath)
                    {
                        painter.lineWidth = preset.wireWidth * 1.5f;
                    }
                    else
                    {
                        painter.lineWidth = preset.wireWidth * 0.85f;
                        painter.strokeGradient = null;
                        painter.strokeColor = new Color(wireColor.r, wireColor.g, wireColor.b, wireColor.a * 0.3f);
                    }
                }
                else
                {
                    painter.lineWidth = preset.wireWidth;
                }

                EmitRoute(painter, route.points, preset);

                if (preset.arrowHead)
                {
                    Vector2 start = route.points[route.points.Count - 2];
                    Vector2 end = route.points[route.points.Count - 1];
                    DrawArrowHead(painter, start, end, painter.strokeColor);
                }
            }
        }

        static void EmitRoute(Painter2D painter, List<Vector2> points, SnippetExportPreset preset)
        {
            if (points.Count < 2) return;

            switch (preset.wireStyle)
            {
                case SnippetExportPreset.WireStyle.Straight:
                    painter.BeginPath();
                    painter.MoveTo(points[0]);
                    painter.LineTo(points[points.Count - 1]);
                    painter.Stroke();
                    return;

                case SnippetExportPreset.WireStyle.Bezier:
                    EmitSmooth(painter, points);
                    return;

                case SnippetExportPreset.WireStyle.Conduit:
                    EmitCorners(painter, points, preset.wireCornerRadius, true);
                    return;

                default:
                    EmitCorners(painter, points, preset.wireCornerRadius, false);
                    return;
            }
        }

        /// <summary>Axis-aligned runs with either rounded or 45-degree cut corners.</summary>
        static void EmitCorners(Painter2D painter, List<Vector2> points, float cornerRadius, bool chamfer)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);

            if (cornerRadius <= 0.5f)
            {
                for (int i = 1; i < points.Count; i++) painter.LineTo(points[i]);
                painter.Stroke();
                return;
            }

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 previous = points[i - 1];
                Vector2 corner = points[i];
                Vector2 next = points[i + 1];
                Vector2 incoming = corner - previous;
                Vector2 outgoing = next - corner;
                float lengthIn = incoming.magnitude;
                float lengthOut = outgoing.magnitude;
                if (lengthIn < 0.01f || lengthOut < 0.01f) continue;

                float radius = Mathf.Min(cornerRadius, Mathf.Min(lengthIn, lengthOut) * 0.5f);
                Vector2 entry = corner - incoming / lengthIn * radius;
                Vector2 exit = corner + outgoing / lengthOut * radius;

                painter.LineTo(entry);
                if (chamfer)
                {
                    painter.LineTo(exit);
                    continue;
                }

                for (int step = 1; step <= 8; step++)
                {
                    float t = step / 8f;
                    float inverse = 1f - t;
                    painter.LineTo(inverse * inverse * entry + 2f * inverse * t * corner + t * t * exit);
                }
            }

            painter.LineTo(points[points.Count - 1]);
            painter.Stroke();
        }

        /// <summary>
        /// Sweeping curves at the corners. Each corner becomes a cubic Bezier whose two control points
        /// sit on the incoming and outgoing segments, so the curve is inside the convex hull of the
        /// corner and can never leave the corridor the layout reserved. A Catmull-Rom spline through
        /// the same waypoints was tried first and had to be dropped: with the uneven segment lengths a
        /// route produces it overshot the lane and looped back on itself.
        /// </summary>
        static void EmitSmooth(Painter2D painter, List<Vector2> points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 previous = points[i - 1];
                Vector2 corner = points[i];
                Vector2 next = points[i + 1];
                Vector2 incoming = corner - previous;
                Vector2 outgoing = next - corner;
                float lengthIn = incoming.magnitude;
                float lengthOut = outgoing.magnitude;
                if (lengthIn < 0.01f || lengthOut < 0.01f) continue;

                float radius = Mathf.Min(lengthIn, lengthOut) * 0.5f;
                Vector2 entry = corner - incoming / lengthIn * radius;
                Vector2 exit = corner + outgoing / lengthOut * radius;

                painter.LineTo(entry);
                for (int step = 1; step <= 10; step++)
                {
                    float t = step / 10f;
                    float u = 1f - t;
                    Vector2 point = u * u * u * entry
                        + 3f * u * u * t * (entry + incoming / lengthIn * radius * 0.55f)
                        + 3f * u * t * t * (exit - outgoing / lengthOut * radius * 0.55f)
                        + t * t * t * exit;
                    painter.LineTo(point);
                }
            }

            painter.LineTo(points[points.Count - 1]);
            painter.Stroke();
        }

        Color ApplyWireColor(Painter2D painter, PortSlot sourcePort)
        {
            if (preset.wireColorMode == SnippetExportPreset.WireColorMode.Single)
            {
                painter.strokeGradient = null;
                painter.strokeColor = preset.singleWireColor;
                return preset.singleWireColor;
            }

            if (preset.wireColorMode == SnippetExportPreset.WireColorMode.Gradient)
            {
                painter.strokeColor = Color.white;
                painter.strokeGradient = BuildGradient(preset.backgroundGradientTop, preset.singleWireColor);
                return preset.singleWireColor;
            }

            painter.strokeGradient = null;
            Color color = SnippetStyle.ResolvePortColor(sourcePort.typeName);
            painter.strokeColor = color;
            return color;
        }

        void DrawArrowHead(Painter2D painter, Vector2 start, Vector2 end, Color color)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction = direction.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float length = 10f;
            float halfWidth = 5f;
            Vector2 tip = end - direction * 2f;

            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(tip - direction * length + normal * halfWidth);
            painter.LineTo(tip - direction * length - normal * halfWidth);
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);
        }
    }
}
