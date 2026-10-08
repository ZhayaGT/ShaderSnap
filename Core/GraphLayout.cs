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

        /// <summary>Height reserved at the bottom of the canvas for the port legend; 0 when unused.</summary>
        public float legendBandHeight;

        /// <summary>Vertical space at the bottom that belongs to bands rather than to the graph.</summary>
        public float BandHeight => notesBandHeight + legendBandHeight;
    }
}
