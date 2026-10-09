using System.Collections.Generic;
using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// One wire, already routed. <see cref="points"/> is an orthogonal polyline in canvas space:
    /// every segment is horizontal or vertical, and every vertical run sits inside a column gap
    /// while every horizontal run inside a column sits on a reserved corridor row. That is what
    /// keeps wires from ever being drawn across a node box.
    /// </summary>
    public sealed class WireRoute
    {
        public GraphEdge edge;
        public PortSlot outputPort;
        public PortSlot inputPort;
        public readonly List<Vector2> points = new List<Vector2>();

        /// <summary>True when both endpoints sit on the critical path, so the wire can be emphasised.</summary>
        public bool onCriticalPath;
    }

    /// <summary>A placed column: its left edge, its width, and its index from left to right.</summary>
    public struct LayoutColumn
    {
        public int index;
        public float x;
        public float width;
    }

    /// <summary>Result of <see cref="GraphAutoLayoutEngine.Apply(GraphModel, LayoutMetrics, float)"/>.</summary>
    public sealed class GraphLayout
    {
        public Vector2 size;
        public readonly Dictionary<string, Rect> nodeRects = new Dictionary<string, Rect>();

        /// <summary>Full frame of each master stack, keyed by <see cref="GraphStack.id"/>.</summary>
        public readonly Dictionary<string, Rect> stackRects = new Dictionary<string, Rect>();

        /// <summary>Frame around each authored group, keyed by <see cref="GraphGroup.id"/>.</summary>
        public readonly Dictionary<string, Rect> groupRects = new Dictionary<string, Rect>();

        public readonly List<WireRoute> routes = new List<WireRoute>();
        public readonly List<LayoutColumn> columns = new List<LayoutColumn>();

        /// <summary>Node ids on the longest dependency chain, empty when it is not being highlighted.</summary>
        public readonly HashSet<string> criticalPath = new HashSet<string>();

        /// <summary>Height reserved at the bottom of the canvas for the notes band; 0 when unused.</summary>
        public float notesBandHeight;

        /// <summary>
        /// The notes the layout placed in the annotation gutter, keyed by note id, aligned with the group
        /// each belongs to.
        ///
        /// A note absent from this map has no gutter position and is drawn in the band under the graph,
        /// which the renderer lays out from <see cref="BandHeight"/>. That covers a note with no group of
        /// its own, and every note when group frames are off — there is no frame to align to and no gutter
        /// was reserved, so anchoring one would put it on top of the graph.
        /// </summary>
        public readonly Dictionary<string, Rect> noteRects = new Dictionary<string, Rect>();

        /// <summary>
        /// Width of the left-hand annotation gutter. Zero when no note is anchored to a group. The columns
        /// start after it, so nothing in the graph can ever sit under a note drawn there.
        /// </summary>
        public float notesGutterWidth;

        /// <summary>Ids of the notes in <see cref="noteRects"/>, i.e. the ones drawn in the gutter.</summary>
        public readonly List<string> gutterNotes = new List<string>();

        /// <summary>Height reserved at the bottom of the canvas for the port legend; 0 when unused.</summary>
        public float legendBandHeight;

        /// <summary>Height reserved at the very bottom of the canvas for the watermark; 0 when unused.</summary>
        public float watermarkBandHeight;

        /// <summary>Vertical space at the bottom that belongs to bands rather than to the graph.</summary>
        public float BandHeight => notesBandHeight + legendBandHeight + watermarkBandHeight;
    }
}
