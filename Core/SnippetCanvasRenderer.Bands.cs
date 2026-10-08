// Everything drawn in the bands around the graph: the guides and group frames over it, the
// legend and sticky notes underneath, and the backdrop behind the whole drawing.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    /// <summary>Bands: the column guides and group frames over the graph, the legend and notes under it, and the backdrop.</summary>
    public partial class SnippetCanvasRenderer : VisualElement
    {
        /// <summary>
        /// Faint separators between columns plus the column index under each one. The graph is a layered
        /// DAG, and the guides make that structure visible instead of leaving the reader to infer it from
        /// where the nodes happen to sit.
        /// </summary>
        void DrawColumnGuides(Painter2D painter, MeshGenerationContext context)
        {
            float pad = EffectivePadding();
            float top = pad * 0.55f;
            float graphBottom = contentSize.y - pad - layout.BandHeight;
            if (graphBottom <= top) return;

            painter.lineWidth = 1f;
            painter.strokeColor = SnippetStyle.ColumnGuide;
            for (int i = 0; i + 1 < layout.columns.Count; i++)
            {
                LayoutColumn left = layout.columns[i];
                LayoutColumn right = layout.columns[i + 1];
                float x = left.x + left.width + (right.x - left.x - left.width) * 0.5f;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, top));
                painter.LineTo(new Vector2(x, graphBottom));
                painter.Stroke();
            }

            float labelSize = metrics.PortLabelFontSize;
            float labelY = graphBottom + metrics.PortRowHeight * 0.35f;
            foreach (LayoutColumn column in layout.columns)
            {
                string text = (column.index + 1).ToString();
                float width = MeasureText(text, labelSize);
                DrawLabel(context, text,
                    new Vector2(column.x + (column.width - width) * 0.5f, labelY),
                    labelSize, SnippetStyle.ColumnGuide);
            }
        }

        /// <summary>Titled frame around each group authored in Shader Graph.</summary>
        void DrawGroupFrames(Painter2D painter, MeshGenerationContext context)
        {
            if (layout.groupRects.Count == 0) return;

            float radius = Mathf.Max(4f, preset.cornerRadius * 0.4f);
            float titleSize = metrics.PortLabelFontSize;

            foreach (GraphGroup group in model.groups)
            {
                if (!layout.groupRects.TryGetValue(group.id, out Rect frame)) continue;

                painter.fillColor = SnippetStyle.GroupFill;
                RoundedRectPath(painter, frame.x, frame.y, frame.width, frame.height, radius);
                painter.Fill(FillRule.NonZero);

                painter.strokeColor = SnippetStyle.GroupBorder;
                painter.lineWidth = 1.5f;
                RoundedRectPath(painter, frame.x, frame.y, frame.width, frame.height, radius);
                painter.Stroke();

                DrawLabel(context, FitLabel(group.title, frame.width - 2f * NodeTextPadding, titleSize),
                    new Vector2(frame.x + NodeTextPadding, frame.y + (metrics.PortRowHeight - titleSize) * 0.5f),
                    titleSize, SnippetStyle.GroupTitle);
            }
        }

        /// <summary>
        /// Recedes everything off the critical path so the spine of the graph reads first. Nodes get a
        /// dark scrim rather than being skipped, which keeps the whole graph legible while ranking it.
        /// </summary>
        void DrawCriticalPathDimming(Painter2D painter)
        {
            if (layout.criticalPath.Count == 0) return;

            float radius = preset.cornerRadius * 0.5f;
            painter.fillColor = SnippetStyle.DimOverlay;
            foreach (GraphNode node in model.nodes)
            {
                if (layout.criticalPath.Contains(node.id)) continue;
                if (!layout.nodeRects.TryGetValue(node.id, out Rect rect)) continue;

                RoundedRectPath(painter, rect.x, rect.y, rect.width, rect.height, radius);
                painter.Fill(FillRule.NonZero);
            }
        }

        /// <summary>Width of the legend box, so the notes band can leave room for it.</summary>
        float LegendBoxWidth()
        {
            float size = metrics.PortLabelFontSize;
            float swatch = size * 0.85f;
            float labelWidth = 0f;
            foreach (KeyValuePair<string, Color> entry in CollectLegendEntries())
                labelWidth = Mathf.Max(labelWidth, MeasureText(entry.Key, size));
            return NodeTextPadding * 2f + swatch + 8f + labelWidth;
        }

        /// <summary>
        /// Legend of the port colours this graph actually uses. Listing only what is present keeps the
        /// box short and means it never claims a type the reader cannot find on the canvas.
        /// </summary>
        void DrawPortLegend(Painter2D painter, MeshGenerationContext context)
        {
            List<KeyValuePair<string, Color>> entries = CollectLegendEntries();
            if (entries.Count == 0) return;

            float size = metrics.PortLabelFontSize;
            float rowHeight = size * 1.6f;
            float swatch = size * 0.85f;
            float boxWidth = LegendBoxWidth();
            float boxHeight = metrics.PortRowHeight * 0.6f * 2f + entries.Count * rowHeight;

            // Anchored to the bottom of the content area, which EffectivePadding keeps clear of the frame.
            float pad = EffectivePadding();
            var box = new Rect(pad, contentSize.y - pad - boxHeight, boxWidth, boxHeight);

            painter.fillColor = SnippetStyle.PanelFill;
            RoundedRectPath(painter, box.x, box.y, box.width, box.height, 5f);
            painter.Fill(FillRule.NonZero);
            painter.strokeColor = SnippetStyle.PanelBorder;
            painter.lineWidth = 1f;
            RoundedRectPath(painter, box.x, box.y, box.width, box.height, 5f);
            painter.Stroke();

            float y = box.y + metrics.PortRowHeight * 0.6f;
            foreach (KeyValuePair<string, Color> entry in entries)
            {
                float swatchY = y + (rowHeight - swatch) * 0.5f;
                painter.fillColor = entry.Value;
                painter.BeginPath();
                painter.MoveTo(new Vector2(box.x + NodeTextPadding, swatchY));
                painter.LineTo(new Vector2(box.x + NodeTextPadding + swatch, swatchY));
                painter.LineTo(new Vector2(box.x + NodeTextPadding + swatch, swatchY + swatch));
                painter.LineTo(new Vector2(box.x + NodeTextPadding, swatchY + swatch));
                painter.ClosePath();
                painter.Fill(FillRule.NonZero);

                DrawLabel(context, entry.Key,
                    new Vector2(box.x + NodeTextPadding + swatch + 8f, y + (rowHeight - size) * 0.5f),
                    size, SnippetStyle.PortLabelText);
                y += rowHeight;
            }
        }

        /// <summary>Distinct port types used by the graph, ordered by first appearance so it is stable.</summary>
        List<KeyValuePair<string, Color>> CollectLegendEntries()
        {
            var order = new List<string>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (GraphNode node in model.nodes)
            {
                foreach (PortSlot port in node.ports)
                {
                    if (port.hidden) continue;
                    string label = SnippetStyle.DescribePortType(port.typeName);
                    if (label == "Unknown" || !seen.Add(label)) continue;
                    order.Add(label);
                }
            }

            var entries = new List<KeyValuePair<string, Color>>(order.Count);
            foreach (string label in order)
                entries.Add(new KeyValuePair<string, Color>(label, LegendColorFor(label)));
            return entries;
        }

        static Color LegendColorFor(string label)
        {
            switch (label)
            {
                case "Float": return SnippetStyle.PortFloat1;
                case "Vector 2": return SnippetStyle.PortFloat2;
                case "Vector 3": return SnippetStyle.PortFloat3;
                case "Vector 4": return SnippetStyle.PortFloat4;
                case "Boolean": return SnippetStyle.PortBoolean;
                case "Matrix": return SnippetStyle.PortMatrix;
                case "Texture": return SnippetStyle.PortTexture;
                default: return SnippetStyle.DefaultPortColor;
            }
        }

        /// <summary>
        /// Sticky notes printed as cards in the band under the graph. The asset stores an absolute
        /// position for each note, but that point means nothing once the graph has been re-laid out, so
        /// the notes become a reading block instead of floating annotations.
        /// </summary>
        void DrawNotes(Painter2D painter, MeshGenerationContext context)
        {
            if (model.notes.Count == 0) return;

            float size = metrics.PortLabelFontSize;
            float line = size * 1.45f;
            float cardWidth = metrics.NodeWidth * 1.6f;
            float gap = metrics.PortRowHeight;

            // The bands live inside the same inset the graph respects, so the frame never clips them.
            float pad = EffectivePadding();
            float bandsBottom = contentSize.y - pad;
            float top = bandsBottom - layout.BandHeight;

            float x = pad;
            float y = top;
            float rowHeight = 0f;

            // The legend owns the bottom-left corner, so the cards wrap in the space to its right and then
            // continue below. Starting at x=0 would let the first card sit on top of the legend box.
            float available = contentSize.x - pad * 2f;
            if (layout.legendBandHeight > 0f)
            {
                available -= LegendBoxWidth() + gap;
                x += LegendBoxWidth() + gap;
            }

            foreach (GraphNote note in model.notes)
            {
                if (x > metrics.Padding && x + cardWidth > metrics.Padding + available)
                {
                    x = metrics.Padding;
                    y += rowHeight + gap;
                    rowHeight = 0f;
                }

                int bodyLines = GraphAutoLayoutEngine.CountWrappedLines(note.content, cardWidth - NodeTextPadding * 2f, size);
                float cardHeight = line + bodyLines * line + metrics.PortRowHeight * 0.6f;

                var card = new Rect(x, y, cardWidth, cardHeight);
                painter.fillColor = SnippetStyle.NoteBackground;
                RoundedRectPath(painter, card.x, card.y, card.width, card.height, 4f);
                painter.Fill(FillRule.NonZero);

                float textX = card.x + NodeTextPadding;
                float textWidth = card.width - NodeTextPadding * 2f;
                float cursorY = card.y + metrics.PortRowHeight * 0.3f;

                if (!string.IsNullOrEmpty(note.title))
                {
                    DrawLabel(context, FitLabel(note.title, textWidth, size * 1.1f),
                        new Vector2(textX, cursorY), size * 1.1f, SnippetStyle.NoteTitle);
                    cursorY += line;
                }

                DrawWrappedText(context, note.content, textX, cursorY, textWidth, size, line, SnippetStyle.NoteBody);

                x += cardWidth + gap;
                rowHeight = Mathf.Max(rowHeight, cardHeight);
            }
        }

        /// <summary>Word wrap that measures with the real glyph advances, so it never overflows a card.</summary>
        void DrawWrappedText(MeshGenerationContext context, string text, float x, float y,
                             float maxWidth, float fontSize, float lineHeight, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            float cursorY = y;
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    cursorY += lineHeight;
                    continue;
                }

                string line = string.Empty;
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (MeasureText(candidate, fontSize) > maxWidth && line.Length > 0)
                    {
                        DrawLabel(context, line, new Vector2(x, cursorY), fontSize, color);
                        cursorY += lineHeight;
                        line = word;
                        continue;
                    }
                    line = candidate;
                }

                if (line.Length > 0)
                {
                    DrawLabel(context, FitLabel(line, maxWidth, fontSize), new Vector2(x, cursorY), fontSize, color);
                    cursorY += lineHeight;
                }
            }
        }

        void DrawBackdrop(Painter2D painter)
        {
            if (preset.backgroundMode == SnippetExportPreset.BackgroundMode.Transparent) return;

            if (preset.backgroundMode == SnippetExportPreset.BackgroundMode.Gradient)
            {
                painter.fillColor = Color.white;
                painter.fillGradient = FillGradient.MakeLinearGradient(
                    BuildGradient(preset.backgroundGradientTop, preset.backgroundGradientBottom),
                    new Vector2(0f, 0f), new Vector2(0f, contentSize.y), AddressMode.Clamp);
            }
            else if (preset.backgroundMode == SnippetExportPreset.BackgroundMode.BlurredEditor)
            {
                var center = new Vector2(contentSize.x * 0.5f, contentSize.y * 0.42f);
                painter.fillColor = Color.white;
                painter.fillGradient = FillGradient.MakeRadialGradient(
                    BuildGradient(preset.blurTint, preset.backgroundGradientBottom),
                    center, Mathf.Max(contentSize.x, contentSize.y) * 0.75f, center, AddressMode.Clamp);
            }
            else
            {
                painter.fillColor = preset.backgroundColor;
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(contentSize.x, 0f));
            painter.LineTo(contentSize);
            painter.LineTo(new Vector2(0f, contentSize.y));
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);
            painter.fillGradient = default;
        }
    }
}
