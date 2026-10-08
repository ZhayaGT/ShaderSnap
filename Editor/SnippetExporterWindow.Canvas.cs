using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    /// <summary>
    /// Viewport half of the window: zoom, pan, and the empty-state overlay painted over the canvas.
    /// Kept separate so the wheel and pointer handling reads as one piece instead of being
    /// interrupted by the control rail.
    /// </summary>
    public partial class SnippetExporterWindow : EditorWindow
    {
        void ApplyZoom()
        {
            Vector2 content = canvas.ContentSize;
            Vector2 scaled = SnippetViewMath.ScaledSize(content, zoom);
            canvasHost.style.width = scaled.x;
            canvasHost.style.height = scaled.y;
            canvas.style.scale = new Scale(new Vector2(zoom, zoom));
            canvas.style.transformOrigin = new TransformOrigin(Length.Percent(0f), Length.Percent(0f), 0f);
            if (zoomLabel != null) zoomLabel.text = Mathf.RoundToInt(zoom * 100f) + "%";
            UpdateEmptyState();
        }

        void UpdateEmptyState()
        {
            if (canvasEmptyState == null) return;
            bool empty = model == null || model.nodes.Count == 0;
            canvasEmptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Zooms about the viewport centre, so the button and the wheel feel like one control.</summary>
        void StepZoom(float factor)
        {
            Rect viewport = canvasArea.contentRect;
            SetZoom(zoom * factor, viewport.center);
        }

        void SetZoom(float value)
        {
            SetZoom(value, canvasArea.contentRect.center);
        }

        /// <summary>
        /// Rescales the canvas while keeping <paramref name="anchor"/> (viewport-local) over the same
        /// point of the graph. Without the scroll correction the content jumps away from the cursor,
        /// which is the usual reason a wheel-zoom feels broken.
        /// </summary>
        void SetZoom(float value, Vector2 anchor)
        {
            float target = SnippetViewMath.Clamp(value);
            if (Mathf.Approximately(target, zoom)) return;

            Vector2 contentPoint = canvasArea.scrollOffset + anchor;
            float previous = zoom;
            zoom = target;
            ApplyZoom();
            canvasArea.scrollOffset = contentPoint * (zoom / previous) - anchor;
        }

        void OnCanvasWheel(WheelEvent evt)
        {
            if (canvas == null || canvas.ContentSize.x <= 0f) return;
            Vector2 pointer = (Vector2)canvasArea.WorldToLocal(evt.mousePosition);
            SetZoom(zoom * Mathf.Pow(1.12f, -evt.delta.y), pointer);
            evt.StopPropagation();
        }

        void OnCanvasPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 && evt.button != 2) return;
            panning = true;
            panOrigin = evt.position;
            panScrollOrigin = canvasArea.scrollOffset;
            canvasArea.CapturePointer(evt.pointerId);
            canvasArea.AddToClassList("panning");
            evt.StopPropagation();
        }

        void OnCanvasPointerMove(PointerMoveEvent evt)
        {
            if (!panning) return;
            Vector2 delta = (Vector2)evt.position - panOrigin;
            canvasArea.scrollOffset = panScrollOrigin - delta;
            evt.StopPropagation();
        }

        void OnCanvasPointerUp(PointerUpEvent evt)
        {
            if (!panning) return;
            EndPan();
            evt.StopPropagation();
        }

        void EndPan()
        {
            if (!panning) return;
            panning = false;
            canvasArea.RemoveFromClassList("panning");
            if (canvasArea.HasPointerCapture(PointerId.mousePointerId))
                canvasArea.ReleasePointer(PointerId.mousePointerId);
        }

        void OnFitToView()
        {
            if (canvasArea == null || canvas == null) return;
            Rect viewport = canvasArea.contentRect;
            if (viewport.width <= 1f || viewport.height <= 1f) return;

            zoom = SnippetViewMath.FitScale(canvas.ContentSize, new Vector2(viewport.width, viewport.height));
            ApplyZoom();

            // Fit means the whole graph is visible and centred, not pinned to the top-left corner.
            Vector2 scaled = SnippetViewMath.ScaledSize(canvas.ContentSize, zoom);
            canvasArea.scrollOffset = new Vector2(
                Mathf.Max(0f, (scaled.x - viewport.width) * 0.5f),
                Mathf.Max(0f, (scaled.y - viewport.height) * 0.5f));
        }
    }
}
