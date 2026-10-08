using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Nodes: the box, title bar, type badge, value row, category strip, stack and port drawing.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        void DrawNodes(Painter2D painter, MeshGenerationContext context)
        {
            float radius = preset.cornerRadius * 0.5f;

            foreach (GraphNode node in model.nodes)
            {
                // Blocks are rows of a master stack and are drawn by DrawStacks with their context.
                if (node.IsStackBlock) continue;
                if (!layout.nodeRects.TryGetValue(node.id, out Rect rect)) continue;

                if (preset.showDropShadow)
                {
                    painter.fillColor = new Color(0f, 0f, 0f, 0.35f);
                    RoundedRectPath(painter, rect.x + preset.shadowOffset, rect.y + preset.shadowOffset,
                        rect.width, rect.height, radius);
                    painter.Fill(FillRule.NonZero);
                }

                painter.fillColor = SnippetStyle.NodeBody;
                RoundedRectPath(painter, rect.x, rect.y, rect.width, rect.height, radius);
                painter.Fill(FillRule.NonZero);

                painter.fillColor = SnippetStyle.NodeTitleBar;
                HeaderPath(painter, rect.x, rect.y, rect.width, metrics.TitleHeight, radius);
                painter.Fill(FillRule.NonZero);

                DrawCategoryStrip(painter, rect.x, rect.y, rect.width, node.typeName);

                painter.strokeColor = SnippetStyle.NodeBorder;
                painter.lineWidth = 1f;
                RoundedRectPath(painter, rect.x, rect.y, rect.width, rect.height, radius);
                painter.Stroke();

                DrawLabel(context, FitLabel(node.title, TitleTextWidth(metrics, node), metrics.TitleFontSize),
                    new Vector2(rect.x + NodeTextPadding, rect.y + (metrics.TitleHeight - metrics.TitleFontSize) * 0.5f),
                    metrics.TitleFontSize, SnippetStyle.NodeTitleText);

                DrawTypeBadge(context, rect, node);
                DrawValueRow(painter, context, rect, node);

                DrawPorts(painter, context, rect, node);
            }

            DrawStacks(painter, context, radius);
        }

        /// <summary>Space the title may use, leaving room for the type badge when one is shown.</summary>
        float TitleTextWidth(LayoutMetrics metrics, GraphNode node)
        {
            float available = metrics.NodeWidth - 2f * NodeTextPadding;
            if (!node.HasTypeLabel) return available;
            return Mathf.Max(metrics.TitleFontSize * 3f,
                available - MeasureText(node.value.typeLabel, metrics.PortLabelFontSize) - 12f);
        }

        /// <summary>
        /// Draws the node's data type right-aligned inside the title bar. Shader Graph puts this in the
        /// inspector, but a PNG has no inspector, so the type has to travel with the node.
        /// </summary>
        void DrawTypeBadge(MeshGenerationContext context, Rect rect, GraphNode node)
        {
            if (!node.HasTypeLabel) return;
            // On a property node the type often repeats the name, as with a Color property called
            // "Color"; showing it twice only adds noise.
            if (string.Equals(node.value.typeLabel, node.title, StringComparison.Ordinal)) return;

            float size = metrics.PortLabelFontSize;
            float width = MeasureText(node.value.typeLabel, size);
            float x = rect.xMax - NodeTextPadding - width;
            if (x < rect.x + NodeTextPadding) return;

            DrawLabel(context, node.value.typeLabel,
                new Vector2(x, rect.y + (metrics.TitleHeight - size) * 0.5f),
                size, SnippetStyle.TypeBadgeText);
        }

        /// <summary>
        /// Draws the stored value in its own row under the title. Only shown when the preset asks for
        /// it, because it makes every value-carrying node taller and the canvas correspondingly bigger.
        /// </summary>
        void DrawValueRow(Painter2D painter, MeshGenerationContext context, Rect rect, GraphNode node)
        {
            if (!metrics.ShowNodeValues || !node.HasValue) return;

            float size = metrics.PortLabelFontSize;
            float y = rect.y + metrics.TitleHeight;
            float textX = rect.x + NodeTextPadding;
            float available = metrics.NodeWidth - 2f * NodeTextPadding;

            if (node.value.isColor)
            {
                float swatch = size;
                float swatchY = y + (metrics.PortRowHeight - swatch) * 0.5f;
                painter.fillColor = node.value.color;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.x + NodeTextPadding, swatchY));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding + swatch, swatchY));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding + swatch, swatchY + swatch));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding, swatchY + swatch));
                painter.ClosePath();
                painter.Fill(FillRule.NonZero);

                painter.strokeColor = new Color(0f, 0f, 0f, 0.45f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.x + NodeTextPadding, swatchY));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding + swatch, swatchY));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding + swatch, swatchY + swatch));
                painter.LineTo(new Vector2(rect.x + NodeTextPadding, swatchY + swatch));
                painter.ClosePath();
                painter.Stroke();

                textX += swatch + 8f;
                available -= swatch + 8f;
            }

            DrawLabel(context, FitLabel(node.value.text, available, size),
                new Vector2(textX, y + (metrics.PortRowHeight - size) * 0.5f),
                size, SnippetStyle.NodeValueText);
        }

        void DrawCategoryStrip(Painter2D painter, float x, float y, float width, string typeName)
        {
            float top = y + metrics.TitleHeight - metrics.CategoryStripHeight;
            painter.fillColor = SnippetStyle.ResolveNodeCategoryColor(typeName);
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, top));
            painter.LineTo(new Vector2(x + width, top));
            painter.LineTo(new Vector2(x + width, y + metrics.TitleHeight));
            painter.LineTo(new Vector2(x, y + metrics.TitleHeight));
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);
        }

        /// <summary>
        /// Draws each master stack as one node: a context title bar followed by the block rows, matching
        /// how Shader Graph presents the Vertex and Fragment contexts. Drawing the blocks as separate
        /// nodes made them look like unrelated boxes that could drift into other columns.
        /// </summary>
        void DrawStacks(Painter2D painter, MeshGenerationContext context, float radius)
        {
            foreach (GraphStack stack in model.stacks)
            {
                if (!layout.stackRects.TryGetValue(stack.id, out Rect rect)) continue;

                if (preset.showDropShadow)
                {
                    painter.fillColor = new Color(0f, 0f, 0f, 0.35f);
                    RoundedRectPath(painter, rect.x + preset.shadowOffset, rect.y + preset.shadowOffset,
                        rect.width, rect.height, radius);
                    painter.Fill(FillRule.NonZero);
                }

                painter.fillColor = SnippetStyle.NodeBody;
                RoundedRectPath(painter, rect.x, rect.y, rect.width, rect.height, radius);
                painter.Fill(FillRule.NonZero);

                painter.fillColor = SnippetStyle.NodeTitleBar;
                HeaderPath(painter, rect.x, rect.y, rect.width, metrics.TitleHeight, radius);
                painter.Fill(FillRule.NonZero);

                // Separator between block rows, and one under the context title.
                painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
                painter.lineWidth = 1f;
                for (int i = 0; i <= stack.blocks.Count; i++)
                {
                    float y = rect.y + metrics.TitleHeight + i * metrics.PortRowHeight;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(rect.x, y));
                    painter.LineTo(new Vector2(rect.xMax, y));
                    painter.Stroke();
                }

                painter.strokeColor = SnippetStyle.NodeBorder;
                painter.lineWidth = 1f;
                RoundedRectPath(painter, rect.x, rect.y, rect.width, rect.height, radius);
                painter.Stroke();

                DrawLabel(context, FitLabel(stack.title, metrics.NodeWidth - 2f * NodeTextPadding, metrics.TitleFontSize),
                    new Vector2(rect.x + NodeTextPadding, rect.y + (metrics.TitleHeight - metrics.TitleFontSize) * 0.5f),
                    metrics.TitleFontSize, SnippetStyle.NodeTitleText);

                foreach (GraphNode block in stack.blocks)
                {
                    if (!layout.nodeRects.TryGetValue(block.id, out Rect row)) continue;
                    // DrawPorts already places the block name beside its input port, the same way
                    // Shader Graph labels a block row.
                    DrawPorts(painter, context, row, block);
                }
            }
        }

        void DrawPorts(Painter2D painter, MeshGenerationContext context, Rect rect, GraphNode node)
        {
            foreach (PortSlot port in node.ports)
            {
                if (port.hidden) continue;
                Vector2 center = GraphAutoLayoutEngine.PortCenter(rect, node, port, metrics);
                FillCircle(painter, center, metrics.PortRadius, SnippetStyle.PortConnectorFill);
                FillCircle(painter, center, metrics.PortRadius * 0.5f, SnippetStyle.ResolvePortColor(port.typeName));
                if (preset.lightPreview) continue;

                float available = metrics.NodeWidth - 2f * (metrics.PortRadius + 10f) - 8f;

                // Suppress the label only when the node draws its own title and the port would repeat it,
                // as a property node does. A block row never draws a title of its own, so its port label
                // is the row's only label and must always be drawn.
                bool repeatsTitle = !node.IsStackBlock &&
                                    string.Equals(port.displayName, node.title, StringComparison.Ordinal);
                string label = repeatsTitle
                    ? string.Empty
                    : FitLabel(port.displayName, available, metrics.PortLabelFontSize);

                float width = MeasureText(label, metrics.PortLabelFontSize);
                float x = port.isInput ? center.x + metrics.PortRadius + 6f : center.x - metrics.PortRadius - 6f - width;
                x = Mathf.Clamp(x, rect.x + 2f, rect.xMax - 2f - width);
                DrawLabel(context, label,
                    new Vector2(x, center.y - metrics.PortLabelFontSize * 0.62f),
                    metrics.PortLabelFontSize, SnippetStyle.PortLabelText);
            }
        }
    }
}
