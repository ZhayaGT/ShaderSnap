using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ShaderSnap.Core
{

    public static class ShaderGraphParser
    {
        const string GraphDataType = "UnityEditor.ShaderGraph.GraphData";

        /// <summary>
        /// How many parsed graphs to keep. Each entry holds a whole <see cref="GraphModel"/> including
        /// its port lookup, and nothing else evicts them, so an unbounded cache would retain every graph
        /// opened during a long session. A parsed graph is cheap to rebuild from the asset, so the cap is
        /// about bounding memory rather than about hit rate.
        /// </summary>
        const int MaxCachedGraphs = 32;

        static readonly Dictionary<string, GraphModel> cache = new Dictionary<string, GraphModel>();
        static readonly Dictionary<string, long> stamps = new Dictionary<string, long>();

        public static GraphModel Parse(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return null;

            long stamp = File.GetLastWriteTimeUtc(assetPath).Ticks;
            if (cache.TryGetValue(assetPath, out GraphModel cached) &&
                stamps.TryGetValue(assetPath, out long cachedStamp) && cachedStamp == stamp)
                return cached;

            GraphModel model = ParseText(File.ReadAllText(assetPath));
            if (cache.Count >= MaxCachedGraphs && !cache.ContainsKey(assetPath)) Clear();
            cache[assetPath] = model;
            stamps[assetPath] = stamp;
            return model;
        }

        /// <summary>Drops every cached graph. Used to bound the cache and available to callers that know
        /// the assets on disk changed underneath them.</summary>
        public static void Clear()
        {
            cache.Clear();
            stamps.Clear();
        }

        public static void Invalidate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            cache.Remove(assetPath);
            stamps.Remove(assetPath);
        }

        public static GraphModel ParseText(string text)
        {
            GraphModel model = new GraphModel();
            if (string.IsNullOrEmpty(text)) return model;

            List<JObject> documents = ReadDocuments(text);
            Dictionary<string, JObject> byObjectId = new Dictionary<string, JObject>();
            JObject graph = null;

            foreach (JObject doc in documents)
            {
                string objectId = ReadString(doc, "m_ObjectId");
                if (!string.IsNullOrEmpty(objectId)) byObjectId[objectId] = doc;
                if (graph == null && ReadString(doc, "m_Type") == GraphDataType) graph = doc;
            }

            if (graph == null)
            {
                foreach (JObject doc in documents)
                    if (doc["m_Nodes"] is JArray) { graph = doc; break; }
            }
            if (graph == null) return model;

            List<string> nodeIds = CollectNodeIds(graph);
            foreach (string nodeId in nodeIds)
            {
                if (!byObjectId.TryGetValue(nodeId, out JObject nodeDoc)) continue;
                GraphNode node = BuildNode(nodeDoc, byObjectId);
                if (node == null) continue;
                if (model.nodeById.ContainsKey(node.id)) continue;
                model.nodes.Add(node);
                model.nodeById[node.id] = node;
                foreach (PortSlot port in node.ports)
                    model.portByKey[GraphModel.PortKey(node.id, port.slotId)] = port;
            }

            BuildStacks(graph, model);
            BuildGroups(graph, model, byObjectId);
            BuildNotes(graph, model, byObjectId);

            JToken edges = graph["m_Edges"];
            if (edges is JArray edgeArray)
            {
                foreach (JToken edgeToken in edgeArray)
                {
                    GraphEdge edge = BuildEdge(edgeToken);
                    if (edge == null) continue;
                    if (!model.nodeById.ContainsKey(edge.outputNodeId)) continue;
                    if (!model.nodeById.ContainsKey(edge.inputNodeId)) continue;
                    model.edges.Add(edge);
                    if (model.portByKey.TryGetValue(GraphModel.PortKey(edge.inputNodeId, edge.inputSlotId),
                            out PortSlot driven))
                        driven.connected = true;
                }
            }

            // Values are resolved last because a connected input overrides its stored number, and
            // connectivity is only known once the edges are in.
            ResolveNodeValues(model, byObjectId);

            return model;
        }

        /// <summary>
        /// Fills in the data type and value the renderer can show. Only nodes whose purpose is to carry
        /// a value qualify: property nodes, where the value lives in the blackboard entry, and the
        /// constant nodes. Showing the input defaults of a Multiply node would just be noise.
        /// </summary>
        static void ResolveNodeValues(GraphModel model, Dictionary<string, JObject> byObjectId)
        {
            foreach (GraphNode node in model.nodes)
            {
                string typeName = node.typeName ?? string.Empty;
                string shortType = ShortTypeName(typeName);

                if (shortType == "PropertyNode" || shortType == "KeywordNode")
                {
                    node.value = BuildPropertyValue(node, byObjectId);
                    continue;
                }

                if (ConstantNodeTypes.TryGetValue(shortType, out string label))
                    node.value = BuildConstantValue(node, shortType, label, byObjectId);
            }
        }

        static readonly Dictionary<string, string> ConstantNodeTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Vector1Node", "Float" },
            { "Vector2Node", "Vector2" },
            { "Vector3Node", "Vector3" },
            { "Vector4Node", "Vector4" },
            { "ColorNode", "Color" },
            { "BooleanNode", "Boolean" },
            { "IntegerNode", "Integer" },
            { "ConstantNode", "Float" },
            { "Matrix2Node", "Matrix2" },
            { "Matrix3Node", "Matrix3" },
            { "Matrix4Node", "Matrix4" }
        };

        static NodeValue BuildPropertyValue(GraphNode node, Dictionary<string, JObject> byObjectId)
        {
            if (!byObjectId.TryGetValue(node.id, out JObject nodeDoc)) return null;

            string referenceId = PropertyReferenceId(nodeDoc);
            if (string.IsNullOrEmpty(referenceId)) return null;
            if (!byObjectId.TryGetValue(referenceId, out JObject reference)) return null;

            string propertyType = ShortTypeName(ReadString(reference, "m_Type"));
            var value = new NodeValue { typeLabel = PropertyTypeLabel(propertyType) };

            JToken raw = reference["m_Value"];
            if (raw == null || raw.Type == JTokenType.Null) return value;

            switch (propertyType)
            {
                case "Vector1ShaderProperty":
                case "IntegerShaderProperty":
                    if (TryReadFloat(raw, out float scalar))
                    {
                        value.hasValue = true;
                        value.text = FormatFloat(scalar);
                    }
                    break;

                case "BooleanShaderProperty":
                    value.hasValue = true;
                    bool flag = ReadBool(reference, "m_Value");
                    if (!flag && TryReadFloat(raw, out float numeric)) flag = numeric >= 0.5f;
                    value.text = flag ? "true" : "false";
                    break;

                case "Vector2ShaderProperty":
                case "Vector3ShaderProperty":
                case "Vector4ShaderProperty":
                    value.hasValue = true;
                    value.text = FormatVector(raw, VectorComponentCount(propertyType));
                    break;

                case "ColorShaderProperty":
                    if (TryReadColor(raw, out Color color))
                    {
                        value.hasValue = true;
                        value.isColor = true;
                        value.color = color;
                        value.text = FormatColor(color);
                    }
                    break;

                // Texture, gradient, matrix, sampler state and virtual texture defaults have no
                // compact text form, so only their type label is shown.
            }

            return value;
        }

        static NodeValue BuildConstantValue(GraphNode node, string shortType, string label,
                                            Dictionary<string, JObject> byObjectId)
        {
            var value = new NodeValue { typeLabel = label };

            // The editable fields of a constant node are its own input slots, in slot order. A
            // connected input is driven by a wire, so its stored number is not what the graph uses.
            var inputs = new List<PortSlot>();
            foreach (PortSlot port in node.ports)
                if (port.isInput && !port.hidden && !port.connected) inputs.Add(port);
            if (inputs.Count == 0 || inputs.Count > 4) return value;
            inputs.Sort((a, b) => a.slotId.CompareTo(b.slotId));

            var components = new List<float>(inputs.Count);
            foreach (PortSlot port in inputs)
            {
                if (!byObjectId.TryGetValue(port.id, out JObject slotDoc)) return value;
                if (!TryReadFloat(slotDoc["m_Value"], out float component)) return value;
                components.Add(component);
            }

            if (shortType == "ColorNode" && components.Count == 4)
            {
                var color = new Color(components[0], components[1], components[2], components[3]);
                value.hasValue = true;
                value.isColor = true;
                value.color = color;
                value.text = FormatColor(color);
                return value;
            }

            value.hasValue = true;
            if (components.Count == 1)
            {
                value.text = shortType == "BooleanNode"
                    ? (components[0] >= 0.5f ? "true" : "false")
                    : FormatFloat(components[0]);
                return value;
            }

            var builder = new System.Text.StringBuilder("(");
            for (int i = 0; i < components.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(FormatFloat(components[i]));
            }
            value.text = builder.Append(')').ToString();
            return value;
        }

        static int VectorComponentCount(string propertyType)
        {
            if (propertyType == "Vector2ShaderProperty") return 2;
            if (propertyType == "Vector3ShaderProperty") return 3;
            return 4;
        }

        static string PropertyTypeLabel(string propertyType)
        {
            if (string.IsNullOrEmpty(propertyType)) return string.Empty;
            if (propertyType == "Vector1ShaderProperty") return "Float";

            const string suffix = "ShaderProperty";
            return propertyType.EndsWith(suffix, StringComparison.Ordinal)
                ? propertyType.Substring(0, propertyType.Length - suffix.Length)
                : propertyType;
        }

        static string FormatFloat(float value)
        {
            if (Math.Abs(value - Math.Round(value)) < 0.0005 && Math.Abs(value) < 1e7)
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string FormatVector(JToken raw, int count)
        {
            string[] axes = { "x", "y", "z", "w" };
            var builder = new System.Text.StringBuilder("(");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(", ");
                TryReadFloat(raw[axes[i]], out float component);
                builder.Append(FormatFloat(component));
            }
            return builder.Append(')').ToString();
        }

        static string FormatColor(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        static bool TryReadColor(JToken raw, out Color color)
        {
            color = default;
            if (raw == null) return false;
            if (!TryReadFloat(raw["r"], out float r)) return false;
            if (!TryReadFloat(raw["g"], out float g)) return false;
            if (!TryReadFloat(raw["b"], out float b)) return false;

            float a = 1f;
            if (TryReadFloat(raw["a"], out float parsedAlpha)) a = parsedAlpha;

            color = new Color(r, g, b, a);
            return true;
        }

        static bool TryReadFloat(JToken token, out float value)
        {
            value = 0f;
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                value = token.Value<float>();
                return true;
            }
            return token.Type == JTokenType.String && float.TryParse(token.Value<string>(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string PropertyReferenceId(JObject nodeDoc)
        {
            return ReadString(nodeDoc?["m_Property"] ?? nodeDoc?["m_Keyword"], "m_Id");
        }

        static string ShortTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return string.Empty;
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        static void BuildGroups(JObject graph, GraphModel model, Dictionary<string, JObject> byObjectId)
        {
            if (!(graph["m_GroupDatas"] is JArray refs)) return;

            foreach (JToken entry in refs)
            {
                string groupId = ReadString(entry, "m_Id");
                if (string.IsNullOrEmpty(groupId)) continue;
                if (!byObjectId.TryGetValue(groupId, out JObject doc)) continue;

                string title = ReadString(doc, "m_Title");
                if (string.IsNullOrEmpty(title)) title = "Group";

                model.groups.Add(new GraphGroup { id = groupId, title = title });
            }

            if (model.groups.Count == 0) return;

            var byId = new Dictionary<string, GraphGroup>(model.groups.Count);
            foreach (GraphGroup group in model.groups) byId[group.id] = group;

            foreach (GraphNode node in model.nodes)
            {
                if (string.IsNullOrEmpty(node.groupId)) continue;
                if (!byId.TryGetValue(node.groupId, out GraphGroup group))
                {
                    node.groupId = null;
                    continue;
                }
                group.members.Add(node);
            }

            // A group with nothing in it has no frame to draw.
            model.groups.RemoveAll(group => group.members.Count == 0);
        }

        static void BuildNotes(JObject graph, GraphModel model, Dictionary<string, JObject> byObjectId)
        {
            if (!(graph["m_StickyNoteDatas"] is JArray refs)) return;

            foreach (JToken entry in refs)
            {
                string noteId = ReadString(entry, "m_Id");
                if (string.IsNullOrEmpty(noteId)) continue;
                if (!byObjectId.TryGetValue(noteId, out JObject doc)) continue;

                string content = ReadString(doc, "m_Content");
                string title = ReadString(doc, "m_Title");
                if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(title)) continue;

                model.notes.Add(new GraphNote
                {
                    id = noteId,
                    title = title,
                    content = content,
                    textSize = ReadInt(doc, "m_TextSize", 0),
                    theme = ReadInt(doc, "m_Theme", 0)
                });
            }
        }

        static void BuildStacks(JObject graph, GraphModel model)
        {
            AddStack(graph, model, "m_VertexContext", "vertex", "Vertex");
            AddStack(graph, model, "m_FragmentContext", "fragment", "Fragment");
        }

        static void AddStack(JObject graph, GraphModel model, string key, string id, string title)
        {
            if (!(graph[key]?["m_Blocks"] is JArray blocks) || blocks.Count == 0) return;

            var stack = new GraphStack { id = id, title = title };
            foreach (JToken entry in blocks)
            {
                string nodeId = ReadString(entry, "m_Id");
                if (string.IsNullOrEmpty(nodeId)) continue;
                if (!model.nodeById.TryGetValue(nodeId, out GraphNode node)) continue;

                node.stackId = id;
                node.stackOrder = stack.blocks.Count;
                stack.blocks.Add(node);
            }

            if (stack.blocks.Count > 0) model.stacks.Add(stack);
        }

        static List<string> CollectNodeIds(JObject graph)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>();

            void AddFrom(JToken array)
            {
                if (!(array is JArray list)) return;
                foreach (JToken entry in list)
                {
                    string id = ReadString(entry, "m_Id");
                    if (!string.IsNullOrEmpty(id) && seen.Add(id)) ids.Add(id);
                }
            }

            AddFrom(graph["m_Nodes"]);
            AddFrom(graph["m_VertexContext"]?["m_Blocks"]);
            AddFrom(graph["m_FragmentContext"]?["m_Blocks"]);
            return ids;
        }

        static GraphNode BuildNode(JObject nodeDoc, Dictionary<string, JObject> byObjectId)
        {
            string id = ReadString(nodeDoc, "m_ObjectId");
            if (string.IsNullOrEmpty(id)) return null;

            string typeName = ReadString(nodeDoc, "m_Type");
            var node = new GraphNode
            {
                id = id,
                typeName = typeName,
                title = ResolveTitle(nodeDoc, byObjectId),
                groupId = ReadString(nodeDoc["m_Group"], "m_Id")
            };

            if (nodeDoc["m_Slots"] is JArray slots)
            {
                foreach (JToken entry in slots)
                {
                    string slotId = ReadString(entry, "m_Id");
                    if (string.IsNullOrEmpty(slotId)) continue;
                    if (!byObjectId.TryGetValue(slotId, out JObject slotDoc)) continue;
                    node.ports.Add(new PortSlot
                    {
                        id = slotId,
                        slotId = ReadInt(slotDoc, "m_Id", -1),
                        displayName = ReadString(slotDoc, "m_DisplayName"),
                        isInput = ReadInt(slotDoc, "m_SlotType", 0) == 0,
                        typeName = ReadString(slotDoc, "m_Type"),
                        hidden = ReadBool(slotDoc, "m_Hidden")
                    });
                }
            }

            foreach (PortSlot port in node.ports)
                if (string.IsNullOrEmpty(port.displayName)) port.displayName = "Port" + port.slotId;

            // A block row's title comes from m_Name, which is the internal path ("SurfaceDescription.
            // BaseColor"). The port already carries the human-readable label Shader Graph shows in the
            // stack ("Base Color", "Ambient Occlusion"), so the row uses that instead.
            if (typeName != null && typeName.EndsWith("BlockNode", System.StringComparison.Ordinal))
            {
                foreach (PortSlot port in node.ports)
                {
                    if (port.hidden || !port.isInput || string.IsNullOrEmpty(port.displayName)) continue;
                    node.title = port.displayName;
                    break;
                }
            }

            return node;
        }

        static GraphEdge BuildEdge(JToken edgeToken)
        {
            JToken output = edgeToken["m_OutputSlot"];
            JToken input = edgeToken["m_InputSlot"];
            string outputNode = ReadString(output?["m_Node"], "m_Id");
            string inputNode = ReadString(input?["m_Node"], "m_Id");
            if (string.IsNullOrEmpty(outputNode) || string.IsNullOrEmpty(inputNode)) return null;

            return new GraphEdge
            {
                outputNodeId = outputNode,
                outputSlotId = ReadInt(output, "m_SlotId", -1),
                inputNodeId = inputNode,
                inputSlotId = ReadInt(input, "m_SlotId", -1)
            };
        }

        static string ResolveTitle(JObject nodeDoc, Dictionary<string, JObject> byObjectId)
        {
            string typeName = ReadString(nodeDoc, "m_Type");

            if (typeName != null && (typeName.EndsWith("PropertyNode") || typeName.EndsWith("KeywordNode")))
            {
                string referenceId = PropertyReferenceId(nodeDoc);
                if (!string.IsNullOrEmpty(referenceId) &&
                    byObjectId.TryGetValue(referenceId, out JObject referenceDoc))
                {
                    string referenced = ReadString(referenceDoc, "m_Name");
                    if (!string.IsNullOrEmpty(referenced)) return referenced;
                }
            }

            string name = ReadString(nodeDoc, "m_Name");
            if (!string.IsNullOrEmpty(name))
            {
                int dot = name.LastIndexOf('.');
                return dot >= 0 ? name.Substring(dot + 1) : name;
            }

            if (!string.IsNullOrEmpty(typeName))
            {
                int dot = typeName.LastIndexOf('.');
                string shortName = dot >= 0 ? typeName.Substring(dot + 1) : typeName;
                return shortName.EndsWith("Node") ? shortName.Substring(0, shortName.Length - 4) : shortName;
            }

            return "Node";
        }

        static List<JObject> ReadDocuments(string text)
        {
            var documents = new List<JObject>();
            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        documents.Add(JObject.Parse(text.Substring(start, i - start + 1)));
                        start = -1;
                    }
                }
            }

            return documents;
        }

        static string ReadString(JToken token, string key)
        {
            JToken value = token?[key];
            if (value == null || value.Type == JTokenType.Null) return null;
            return value.Type == JTokenType.String ? value.Value<string>() : value.ToString();
        }

        static int ReadInt(JToken token, string key, int fallback)
        {
            JToken value = token?[key];
            if (value == null) return fallback;
            return value.Type == JTokenType.Integer ? value.Value<int>() : fallback;
        }

        static bool ReadBool(JToken token, string key)
        {
            JToken value = token?[key];
            return value != null && value.Type == JTokenType.Boolean && value.Value<bool>();
        }
    }
}
