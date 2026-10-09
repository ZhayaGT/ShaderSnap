using System.Collections.Generic;
using UnityEngine;

// The parsed representation of a .shadergraph asset: the nodes, edges, master stacks, groups and
// sticky notes the renderer draws. It is deliberately kept apart from ShaderGraphParser, which is
// the code that reads an asset and fills this model in, so a reader of the model does not have to
// see the JSON shape of the asset format.
//
// A doc comment rather than a <summary>: this sits above the namespace declaration, where a doc
// comment would attach to the namespace rather than to any type.
/// </summary>
namespace ShaderSnap.Core
{
    public class PortSlot
    {
        public string id;
        public int slotId;
        public string displayName;
        public bool isInput;
        public string typeName;
        public bool hidden;

        /// <summary>True when an edge drives this slot, which overrides whatever value it stores.</summary>
        public bool connected;
    }

    /// <summary>
    /// What the exporter can tell the reader about a node besides its title: the data type it carries
    /// and, when the asset stores one, its value.
    /// </summary>
    public class NodeValue
    {
        /// <summary>Readable data type, for example "Vector2" or "Color". Empty when not applicable.</summary>
        public string typeLabel;

        public bool hasValue;
        public string text;

        /// <summary>Set when <see cref="text"/> describes a colour, so the renderer can draw a swatch.</summary>
        public bool isColor;
        public Color color;
    }

    public class GraphNode
    {
        public string id;
        public string title;
        public string typeName;
        public List<PortSlot> ports = new List<PortSlot>();

        /// <summary>Non-null when this node is a block row of a master stack.</summary>
        public string stackId;
        public int stackOrder;

        /// <summary>Non-null when this node belongs to an authored group.</summary>
        public string groupId;

        /// <summary>Data type and value the renderer may display; null when the node carries neither.</summary>
        public NodeValue value;

        public bool IsStackBlock => !string.IsNullOrEmpty(stackId);
        public bool HasTypeLabel => value != null && !string.IsNullOrEmpty(value.typeLabel);
        public bool HasValue => value != null && value.hasValue;
    }

    public class GraphEdge
    {
        public string outputNodeId;
        public int outputSlotId;
        public string inputNodeId;
        public int inputSlotId;
    }

    /// <summary>
    /// A master stack: the Vertex or Fragment context together with its ordered block rows.
    /// Shader Graph draws these as a single node whose body is the list of blocks, not as one
    /// node per block, so the renderer needs them grouped rather than scattered by the layout.
    /// </summary>
    public class GraphStack
    {
        public string id;
        public string title;
        public List<GraphNode> blocks = new List<GraphNode>();
    }

    /// <summary>
    /// A node group authored in Shader Graph. The renderer draws a titled frame around its members,
    /// which is the strongest reading aid the asset itself provides.
    /// </summary>
    public class GraphGroup
    {
        public string id;
        public string title;
        public List<GraphNode> members = new List<GraphNode>();

        /// <summary>
        /// Top-left of the frame as the author placed it, in Shader Graph's own coordinate space.
        ///
        /// This is the only authored position the asset stores: a node's position lives in the editor's
        /// DrawState, not in the .shadergraph file, so it is not available here. Group positions are
        /// enough to reproduce the author's vertical arrangement of groups, which is what stops their
        /// frames from landing on top of each other.
        /// </summary>
        public Vector2 authoredPosition;

        /// <summary>False when the asset stores no position for this group.</summary>
        public bool hasAuthoredPosition;
    }

    /// <summary>
    /// A sticky note authored in Shader Graph.
    ///
    /// A note's authored rect is kept so it can be drawn beside the nodes it annotates. Notes placed at
    /// the bottom of the canvas, away from the graph, were reported as ambiguous: the reader could not
    /// tell which part of the graph a note was about.
    /// </summary>
    public class GraphNote
    {
        public string id;
        public string title;
        public string content;

        /// <summary>0 = small, 1 = medium, 2 = large, 3 = huge, matching StickyNoteData.m_TextSize.</summary>
        public int textSize;

        /// <summary>Stored theme index; kept so the note can carry its authored colour.</summary>
        public int theme;

        /// <summary>Authored rect in Shader Graph's coordinate space; width and height included.</summary>
        public Rect authoredPosition;

        /// <summary>False when the asset stores no position for this note.</summary>
        public bool hasAuthoredPosition;

        /// <summary>Owning group id, empty when the note floats free of any group.</summary>
        public string groupId;
    }

    public class GraphModel
    {
        public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphEdge> edges = new List<GraphEdge>();
        public List<GraphStack> stacks = new List<GraphStack>();
        public List<GraphGroup> groups = new List<GraphGroup>();
        public List<GraphNote> notes = new List<GraphNote>();
        public Dictionary<string, GraphNode> nodeById = new Dictionary<string, GraphNode>();
        public Dictionary<string, PortSlot> portByKey = new Dictionary<string, PortSlot>();

        public static string PortKey(string nodeId, int slotId)
        {
            return nodeId + ":" + slotId;
        }

        public bool TryGetPort(string nodeId, int slotId, out PortSlot port)
        {
            return portByKey.TryGetValue(PortKey(nodeId, slotId), out port);
        }
    }
}
