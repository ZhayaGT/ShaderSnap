using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ShaderSnap.Core
{
    public partial class SnippetCanvasRenderer : VisualElement
    {
        /// <summary>Inset between a node's edge and the text drawn inside it.</summary>
        public const float NodeTextPadding = 10f;
        const float TrafficLightRadius = 6f;
        const float TrafficLightSpacing = 18f;
        const float FrameTitleBarHeight = 28f;

        static FontAsset cachedFont;

        static Dictionary<char, float> glyphAdvances;

        GraphModel model;
        SnippetExportPreset preset;
        string shaderDisplayName = "";
        LayoutMetrics metrics = LayoutMetrics.Default;
        GraphLayout layout = new GraphLayout();
        float appliedTextScale = 1f;
        float appliedSpread = 1f;
        float appliedTargetAspect;
        bool appliedAutoAspect;
        LayoutOptions appliedOptions = LayoutOptions.Default;
        float textPixelScale = 1f;
        Vector2 contentSize;

        public SnippetCanvasRenderer()
        {
            usageHints = UsageHints.DynamicTransform;
            generateVisualContent += OnGenerateVisualContent;
        }

        public Vector2 ContentSize => contentSize;

        /// <summary>Width of the node boxes this renderer laid out, as measured from the graph's labels.</summary>
        public float NodeWidth => metrics.NodeWidth;

        /// <summary>The placed graph. Callers that need to know where a node ended up read it here.</summary>
        public GraphLayout Layout => layout;

        public void SetData(GraphModel graph, SnippetExportPreset exportPreset, string shaderName)
        {
            model = graph;
            // A null preset used to make OnGenerateVisualContent bail out, which painted an empty canvas
            // with no explanation. Falling back to defaults keeps the graph visible instead. The fallback
            // is created once and shared: a fresh instance per call leaked a ScriptableObject every time.
            preset = exportPreset != null ? exportPreset : FallbackPreset;
            shaderDisplayName = shaderName ?? "";
            Rebuild();
        }

        static SnippetExportPreset fallbackPreset;

        static SnippetExportPreset FallbackPreset
        {
            get
            {
                if (fallbackPreset == null)
                {
                    fallbackPreset = ScriptableObject.CreateInstance<SnippetExportPreset>();
                    fallbackPreset.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                }
                return fallbackPreset;
            }
        }

        /// <summary>Gap kept between the frame border and the content it surrounds.</summary>
        const float FrameInnerGap = 16f;

        /// <summary>Frame margin as configured, clamped to the slider range.</summary>
        float FrameMargin()
        {
            return Mathf.Clamp(preset != null ? preset.frameMargin : 0f, 0f, 64f);
        }

        /// <summary>
        /// Distance the graph and the bands keep from the canvas edge, so the macOS frame border never runs
        /// through the content and the bands never poke out below the frame. The engine applies the same
        /// value as its padding.
        ///
        /// Group frames are not part of this number: they reach above the topmost node they wrap, and that
        /// overhang is reserved by <see cref="GraphAutoLayoutEngine"/> itself.
        /// </summary>
        float ContentInset()
        {
            return preset != null ? InsetFor(preset, metrics) : metrics.Padding;
        }

        /// <summary>Layout options mirroring the preset, so the engine never sees the preset itself.</summary>
        static LayoutOptions OptionsFor(SnippetExportPreset preset, LayoutMetrics metrics)
        {
            if (preset == null) return LayoutOptions.Default;
            return new LayoutOptions
            {
                balance = preset.layoutBalance,
                locality = preset.nodeLocality,
                highlightCriticalPath = preset.highlightCriticalPath,
                reserveNotesBand = preset.showNotes,
                reserveLegendBand = preset.showPortLegend,
                showGroupFrames = preset.showGroups,
                canvasInset = InsetFor(preset, metrics)
            };
        }

        /// <summary>
        /// The inset a preset implies, independent of the instance the renderer holds.
        ///
        /// This is the frame chrome only. Group frames reach above the topmost node they wrap, but that
        /// overhang is added by the layout engine, which is the code that knows how far a frame reaches —
        /// adding it here as well would reserve it twice and push the graph down for no reason.
        /// </summary>
        static float InsetFor(SnippetExportPreset preset, LayoutMetrics metrics)
        {
            if (!preset.showMacOsFrame) return metrics.Padding;
            return Mathf.Max(metrics.Padding, Mathf.Clamp(preset.frameMargin, 0f, 64f) + FrameInnerGap);
        }

        public void Refresh()
        {
            // Only a handful of preset fields change the layout; everything else is a pure repaint.
            // Comparing them here means a new option cannot be forgotten and silently stop updating the
            // preview. autoAspect and targetAspect are part of the comparison because Rebuild picks a
            // different layout entry point when they are set: leaving them out made the preview keep the
            // old canvas size while the export produced the new one.
            LayoutOptions requested = OptionsFor(preset, metrics);
            if (!Mathf.Approximately(LayoutMetrics.FromFontScale(preset != null ? preset.textScale : 1f).FontScale,
                    metrics.FontScale) ||
                !Mathf.Approximately(preset != null ? preset.verticalSpread : 1f, appliedSpread) ||
                !Mathf.Approximately(preset != null ? preset.textScale : 1f, appliedTextScale) ||
                (preset != null && preset.showNodeValues) != metrics.ShowNodeValues ||
                (preset != null && preset.autoAspect) != appliedAutoAspect ||
                !Mathf.Approximately(preset != null ? preset.targetAspect : 0f, appliedTargetAspect) ||
                !SameLayoutOptions(requested, appliedOptions))
            {
                Rebuild();
                return;
            }
            MarkDirtyRepaint();
        }

        static bool SameLayoutOptions(LayoutOptions a, LayoutOptions b)
        {
            return Mathf.Approximately(a.balance, b.balance)
                && Mathf.Approximately(a.locality, b.locality)
                && a.snapToGrid == b.snapToGrid
                && a.highlightCriticalPath == b.highlightCriticalPath
                && a.reserveNotesBand == b.reserveNotesBand
                && a.reserveLegendBand == b.reserveLegendBand
                && a.showGroupFrames == b.showGroupFrames
                && Mathf.Approximately(a.canvasInset, b.canvasInset);
        }

        /// <summary>
        /// Narrowest node box that can show every label this graph actually contains, at the current
        /// font scale.
        ///
        /// Shader Graph itself fixes its nodes at 200px and clips whatever does not fit, which is
        /// tolerable in an editor where the inspector still shows the full name. An exported PNG has no
        /// inspector, so a title cut down to "Split Texture Tra…" is information the reader can never
        /// recover. Measuring the labels instead also keeps the canvas no wider than the graph needs,
        /// and canvas width is what decides how large the text looks once the image is fitted to a
        /// screen.
        /// </summary>
        float RequiredNodeWidth(GraphModel graph, LayoutMetrics metrics)
        {
            float widest = LayoutMetrics.MinNodeWidth;

            foreach (GraphNode node in graph.nodes)
            {
                // A block row sits inside a stack and is drawn without a title of its own, so only its
                // port label has to fit; the stack contributes the title requirement.
                if (node.IsStackBlock)
                {
                    widest = Mathf.Max(widest, RequiredWidthForPorts(node, metrics));
                    continue;
                }

                float title = MeasureText(node.title, metrics.TitleFontSize) + 2f * NodeTextPadding;
                if (node.HasTypeLabel && !string.Equals(node.value.typeLabel, node.title, StringComparison.Ordinal))
                    title += MeasureText(node.value.typeLabel, metrics.PortLabelFontSize) + 12f;

                float width = Mathf.Max(title, RequiredWidthForPorts(node, metrics));

                if (metrics.ShowNodeValues && node.HasValue)
                {
                    float value = MeasureText(node.value.text, metrics.PortLabelFontSize) + 2f * NodeTextPadding;
                    if (node.value.isColor) value += metrics.PortLabelFontSize;
                    width = Mathf.Max(width, value);
                }

                widest = Mathf.Max(widest, width);
            }

            foreach (GraphStack stack in graph.stacks)
                widest = Mathf.Max(widest,
                    MeasureText(stack.title, metrics.TitleFontSize) + 2f * NodeTextPadding);

            return widest;
        }

        /// <summary>Width a node's port labels need, mirroring how <see cref="DrawPorts"/> places them.</summary>
        float RequiredWidthForPorts(GraphNode node, LayoutMetrics metrics)
        {
            float widest = 0f;
            foreach (PortSlot port in node.ports)
            {
                if (port.hidden) continue;
                // A property node repeats its own name on the port and the renderer suppresses that
                // label, so it must not widen the node either.
                bool repeatsTitle = !node.IsStackBlock &&
                                    string.Equals(port.displayName, node.title, StringComparison.Ordinal);
                if (repeatsTitle) continue;
                widest = Mathf.Max(widest, MeasureText(port.displayName, metrics.PortLabelFontSize));
            }
            return widest + metrics.PortRadius + 8f;
        }

        public void Rebuild()
        {
            appliedTextScale = preset != null ? preset.textScale : 1f;
            appliedSpread = preset != null ? preset.verticalSpread : 1f;
            appliedAutoAspect = preset != null && preset.autoAspect;
            appliedTargetAspect = preset != null ? preset.targetAspect : 0f;
            bool showNodeValues = preset != null && preset.showNodeValues;
            // Font sizes drive the measurement, so the metrics are built twice: once to learn how large
            // the labels will be drawn, then again with the width those labels need.
            LayoutMetrics provisional = LayoutMetrics.FromFontScale(appliedTextScale, appliedSpread, showNodeValues);
            metrics = model != null
                ? LayoutMetrics.FromFontScale(appliedTextScale, appliedSpread, showNodeValues,
                    RequiredNodeWidth(model, provisional))
                : provisional;
            appliedOptions = OptionsFor(preset, metrics);

            if (model != null)
            {
                layout = appliedAutoAspect && appliedTargetAspect > 0f
                    ? GraphAutoLayoutEngine.ApplyWithTargetAspect(model, metrics, appliedOptions, appliedTargetAspect)
                    : GraphAutoLayoutEngine.Apply(model, metrics, appliedOptions);

                contentSize = layout.size;
                style.width = contentSize.x;
                style.height = contentSize.y;
            }
            else
            {
                layout = new GraphLayout();
                contentSize = Vector2.zero;
                style.width = 0f;
                style.height = 0f;
            }
            MarkDirtyRepaint();
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (model == null || preset == null || model.nodes.Count == 0) return;
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;

            Painter2D painter = context.painter2D;
            // MeshGenerationContext.DrawText takes its size in render-target pixels, not panel units:
            // it does not follow PanelSettings.scale. Geometry drawn through Painter2D does. Without
            // this correction the text keeps its absolute pixel size while nodes and wires grow with
            // the resolution multiplier, so a 4x export ends up with quarter-size labels.
            textPixelScale = scaledPixelsPerPoint;
            DrawBackdrop(painter);
            if (preset.showColumnGuides) DrawColumnGuides(painter, context);
            if (preset.showGroups) DrawGroupFrames(painter, context);
            DrawWires(painter);
            DrawNodes(painter, context);
            if (preset.highlightCriticalPath) DrawCriticalPathDimming(painter);
            if (preset.showPortLegend) DrawPortLegend(painter, context);
            if (preset.showNotes) DrawNotes(painter, context);
            DrawWindowFrame(painter, context);
            DrawWatermark(context);
        }
    }
}

