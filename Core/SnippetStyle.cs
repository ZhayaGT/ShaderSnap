using System.Collections.Generic;
using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Palette and resolver helpers that mirror the Shader Graph editor look.
    /// Colors are copied verbatim from the Shader Graph package stylesheets:
    /// <c>Editor/Resources/Styles/ColorMode.uss</c> for the node category titles and
    /// <c>Editor/Resources/Styles/ShaderPort.uss</c> for the port colors.
    /// </summary>
    public static class SnippetStyle
    {
        public static readonly Color NodeBody = new Color32(0x2B, 0x2B, 0x2B, 0xFF);
        public static readonly Color NodeTitleBar = new Color32(0x3F, 0x3F, 0x3F, 0xFF);
        public static readonly Color NodeBorder = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color NodeTitleText = Color.white;
        public static readonly Color PortLabelText = new Color32(0xC1, 0xC1, 0xC1, 0xFF);

        /// <summary>
        /// Data type shown in the node title bar; dimmer than the title so it reads as a label, but still
        /// above 4.5:1 against the #3F3F3F title bar (5.1:1).
        /// </summary>
        public static readonly Color TypeBadgeText = new Color32(0xB4, 0xB4, 0xB4, 0xFF);

        /// <summary>Stored value shown in its own row; brighter than the port labels.</summary>
        public static readonly Color NodeValueText = new Color32(0xE0, 0xE0, 0xE4, 0xFF);

        // --- Reading aids -------------------------------------------------------

        /// <summary>Column separators and their index numbers; deliberately below the noise floor.</summary>
        public static readonly Color ColumnGuide = new Color(1f, 1f, 1f, 0.07f);

        public static readonly Color GroupFill = new Color(1f, 1f, 1f, 0.035f);
        public static readonly Color GroupBorder = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color GroupTitle = new Color32(0xC8, 0xC8, 0xD2, 0xFF);

        /// <summary>
        /// Scrim over nodes that are not on the critical path. Alpha is bounded by legibility rather than
        /// taste: at 0.45 the port labels fell to 3.40:1 and at 0.62 the titles to 2.97:1, both under the
        /// 4.5:1 floor for body text. 0.30 is the strongest scrim where every label still clears it
        /// (port labels 4.67:1), so the emphasis is carried mainly by the heavier, opaque spine wires
        /// while the off-path nodes recede without becoming unreadable.
        /// </summary>
        public static readonly Color DimOverlay = new Color(0.05f, 0.05f, 0.07f, 0.30f);

        public static readonly Color NoteBackground = new Color32(0xFC, 0xD7, 0x6E, 0xFF);

        /// <summary>
        /// Leader line from a note in the gutter to the frame of the group it describes. Dim enough not to
        /// compete with the graph, solid enough to follow.
        /// </summary>
        public static readonly Color NoteLeader = new Color32(0xFC, 0xD7, 0x6E, 0x66);
        public static readonly Color NoteTitle = new Color32(0x4A, 0x37, 0x06, 0xFF);
        public static readonly Color NoteBody = new Color32(0x58, 0x43, 0x08, 0xFF);

        public static readonly Color PanelFill = new Color(0f, 0f, 0f, 0.28f);
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color PortConnectorFill = new Color32(0x21, 0x21, 0x21, 0xFF);
        public static readonly Color DefaultPortColor = new Color32(0xC8, 0xC8, 0xC8, 0xFF);

        public static readonly Color CategoryMath = new Color32(0x4B, 0x92, 0xF3, 0xFF);
        public static readonly Color CategoryInput = new Color32(0xCB, 0x30, 0x22, 0xFF);
        public static readonly Color CategoryUtility = new Color32(0xAE, 0xAE, 0xAE, 0xFF);
        public static readonly Color CategoryArtistic = new Color32(0xDB, 0x77, 0x3B, 0xFF);
        public static readonly Color CategoryUV = new Color32(0x08, 0xD7, 0x8B, 0xFF);
        public static readonly Color CategoryProcedural = new Color32(0x9C, 0x4F, 0xFF, 0xFF);
        public static readonly Color CategoryChannel = new Color32(0x97, 0xD1, 0x3D, 0xFF);

        public static readonly Color PortFloat1 = new Color32(0x84, 0xE4, 0xE7, 0xFF);
        public static readonly Color PortFloat2 = new Color32(0x9A, 0xEF, 0x92, 0xFF);
        public static readonly Color PortFloat3 = new Color32(0xF6, 0xFF, 0x9A, 0xFF);
        public static readonly Color PortFloat4 = new Color32(0xFB, 0xCB, 0xF4, 0xFF);
        public static readonly Color PortTexture = new Color32(0xFF, 0x8B, 0x8B, 0xFF);
        public static readonly Color PortMatrix = new Color32(0x8F, 0xC1, 0xDF, 0xFF);
        public static readonly Color PortBoolean = new Color32(0x94, 0x81, 0xE6, 0xFF);

        // Node class names grouped by Shader Graph category. The category is not stored in the
        // .shadergraph asset, so it has to be recovered from the node's m_Type class name.
        static readonly string[] MathNodes =
        {
            "AbsoluteNode", "AddNode", "ArccosineNode", "ArcsineNode", "Arctangent2Node", "ArctangentNode",
            "CeilingNode", "ClampNode", "CosineNode", "CrossProductNode", "DDXNode", "DDXYNode", "DDYNode",
            "DegreesToRadiansNode", "DistanceNode", "DivideNode", "DotProductNode", "ExponentialNode",
            "FloorNode", "FractionNode", "FresnelNode", "HyperbolicCosineNode", "HyperbolicSineNode",
            "HyperbolicTangentNode", "InverseLerpNode", "LengthNode", "LerpNode", "LogNode",
            "MatrixConstructionNode", "MatrixDeterminantNode", "MatrixSplitNode", "MatrixTransposeNode",
            "MaximumNode", "MinimumNode", "ModuloNode", "MultiplyNode", "NegateNode", "NoiseSineWaveNode",
            "NormalizeNode", "OneMinusNode", "PosterizeNode", "PowerNode", "ProjectionNode",
            "RadiansToDegreesNode", "RandomRangeNode", "ReciprocalNode", "ReciprocalSquareRootNode",
            "ReflectionNode", "RefractNode", "RejectionNode", "RemapNode", "RotateAboutAxisNode", "RoundNode",
            "SaturateNode", "SawtoothWaveNode", "SignNode", "SineNode", "SmoothstepNode", "SphereMaskNode",
            "SquareRootNode", "SquareWaveNode", "StepNode", "SubtractNode", "TangentNode", "TransformNode",
            "TriangleWaveNode", "TruncateNode"
        };

        static readonly string[] InputNodes =
        {
            "AmbientNode", "BakedGINode", "BitangentVectorNode", "BlackbodyNode", "BooleanNode",
            "CalculateLevelOfDetailTexture2DNode", "CameraNode", "ColorNode", "ComputeDeformNode",
            "ConstantNode", "CubemapAssetNode", "DielectricSpecularNode", "ElementLayoutUV",
            "ElementTextureUVNode", "ElementTextureUVSize", "EyeIndexNode", "FogNode", "GatherTexture2DNode",
            "GradientNode", "InstanceIDNode", "IntegerNode", "LinearBlendSkinningNode",
            "MainLightDirectionNode", "Matrix2Node", "Matrix3Node", "Matrix4Node", "MetalReflectanceNode",
            "NormalVectorNode", "ObjectNode", "PositionNode", "ProceduralVirtualTextureNode", "PropertyNode",
            "ReflectionProbeNode", "SampleCubemapNode", "SampleGradient", "SampleRawCubemapNode",
            "SampleTexture2DArrayNode", "SampleTexture2DLODNode", "SampleTexture2DNode", "SampleTexture3DNode",
            "SampleVirtualTextureNode", "SamplerStateNode", "SceneColorNode", "SceneDepthDifferenceNode",
            "SceneDepthNode", "ScreenNode", "ScreenPositionNode", "SliderNode", "SplitTextureTransformNode",
            "TangentVectorNode", "Texture2DArrayAssetNode", "Texture2DAssetNode", "Texture2DPropertiesNode",
            "Texture3DAssetNode", "TimeNode", "TransformationMatrixNode", "UVNode", "Vector1Node",
            "Vector2Node", "Vector3Node", "Vector4Node", "VertexColorNode", "VertexIDNode", "ViewDirectionNode",
            "ViewVectorNode"
        };

        static readonly string[] UtilityNodes =
        {
            "AllNode", "AndNode", "AnyNode", "BranchNode", "BranchOnInputConnectionNode", "ComparisonNode",
            "CustomFunctionNode", "DropdownNode", "IsFrontFaceNode", "IsInfiniteNode", "IsNanNode",
            "KeywordNode", "NandNode", "NotNode", "OrNode", "PreviewNode", "SubGraphNode"
        };

        static readonly string[] ArtisticNodes =
        {
            "BlendNode", "ChannelMaskNode", "ChannelMixerNode", "ColorMaskNode", "ColorspaceConversionNode",
            "ContrastNode", "DitherNode", "FadeTransitionNode", "HueNode", "InvertColorsNode",
            "NormalBlendNode", "NormalFromHeightNode", "NormalFromTextureNode", "NormalReconstructZNode",
            "NormalStrengthNode", "NormalUnpackNode", "ReplaceColorNode", "SaturationNode", "WhiteBalanceNode"
        };

        static readonly string[] UVNodes =
        {
            "FlipbookNode", "ParallaxMappingNode", "ParallaxOcclusionMappingNode", "PolarCoordinatesNode",
            "RadialShearNode", "RotateNode", "SpherizeNode", "TilingAndOffsetNode", "TriplanarNode", "TwirlNode"
        };

        static readonly string[] ProceduralNodes =
        {
            "CheckerboardNode", "EllipseNode", "GradientNoiseNode", "NoiseNode", "PolygonNode", "RectangleNode",
            "RoundedPolygonNode", "RoundedRectangleNode", "VoronoiNode"
        };

        static readonly string[] ChannelNodes =
        {
            "AppendVectorNode", "CombineNode", "FlipNode", "SplitNode", "SwizzleNode"
        };

        static readonly Dictionary<string, Color> NodeCategoryColors = BuildNodeCategoryColors();

        static readonly Dictionary<string, Color> PortColors = BuildPortColors();

        public static Color ResolveNodeCategoryColor(string nodeTypeName)
        {
            string name = ShortName(nodeTypeName);
            if (name.Length == 0) return CategoryUtility;
            return NodeCategoryColors.TryGetValue(name, out Color color) ? color : CategoryUtility;
        }

        public static Color ResolvePortColor(string slotTypeName)
        {
            string name = ShortName(slotTypeName);
            if (name.Length == 0) return DefaultPortColor;
            return PortColors.TryGetValue(name, out Color color) ? color : DefaultPortColor;
        }

        /// <summary>
        /// Readable name for a slot type, matching the wording Shader Graph uses in the port tooltip.
        /// The legend needs this because a colour alone tells the reader nothing.
        /// </summary>
        public static string DescribePortType(string slotTypeName)
        {
            string name = ShortName(slotTypeName);
            if (name.Length == 0) return "Unknown";
            return PortTypeLabels.TryGetValue(name, out string label) ? label : "Unknown";
        }

        static readonly Dictionary<string, string> PortTypeLabels = BuildPortTypeLabels();

        static Dictionary<string, string> BuildPortTypeLabels()
        {
            var map = new Dictionary<string, string>(40, System.StringComparer.Ordinal);
            AddRange(map, "Float", "Vector1MaterialSlot");
            AddRange(map, "Vector 2", "Vector2MaterialSlot", "DefaultVector2MaterialSlot", "UVMaterialSlot");
            AddRange(map, "Vector 3", "Vector3MaterialSlot", "SpaceMaterialSlot", "ColorRGBMaterialSlot",
                "NormalMaterialSlot", "PositionMaterialSlot", "TangentMaterialSlot", "BitangentMaterialSlot",
                "ViewDirectionMaterialSlot");
            AddRange(map, "Vector 4", "Vector4MaterialSlot", "DefaultVector4MaterialSlot",
                "ColorRGBAMaterialSlot", "ScreenPositionMaterialSlot", "VertexColorMaterialSlot");
            AddRange(map, "Boolean", "BooleanMaterialSlot");
            AddRange(map, "Matrix", "Matrix2MaterialSlot", "Matrix3MaterialSlot", "Matrix4MaterialSlot",
                "DynamicMatrixMaterialSlot");
            AddRange(map, "Texture", "Texture2DMaterialSlot", "Texture2DInputMaterialSlot",
                "Texture2DArrayMaterialSlot", "Texture2DArrayInputMaterialSlot", "Texture3DMaterialSlot",
                "Texture3DInputMaterialSlot", "CubemapMaterialSlot", "CubemapInputMaterialSlot");
            return map;
        }

        static void AddRange(Dictionary<string, string> map, string label, params string[] names)
        {
            foreach (string name in names) map[name] = label;
        }

        static Dictionary<string, Color> BuildNodeCategoryColors()
        {
            // ColorMode.uss defines no color for the UI, Terrain and Custom Interpolators categories,
            // so those fall back to the neutral Utility grey. Block nodes are drawn as rows inside the
            // Master Stack in Shader Graph, so they have no category color to copy either.
            var map = new Dictionary<string, Color>(203, System.StringComparer.Ordinal);
            AddRange(map, MathNodes, CategoryMath);
            AddRange(map, InputNodes, CategoryInput);
            AddRange(map, UtilityNodes, CategoryUtility);
            AddRange(map, ArtisticNodes, CategoryArtistic);
            AddRange(map, UVNodes, CategoryUV);
            AddRange(map, ProceduralNodes, CategoryProcedural);
            AddRange(map, ChannelNodes, CategoryChannel);
            return map;
        }

        static Dictionary<string, Color> BuildPortColors()
        {
            // Mirrors SlotValueTypeUtil.k_ConcreteSlotValueTypeClassNames. Slot classes that Shader
            // Graph renders without a dedicated type color (gradient, sampler state, virtual texture,
            // property connection state, dynamic values) intentionally keep the default grey.
            var map = new Dictionary<string, Color>(40, System.StringComparer.Ordinal);

            AddRange(map, PortFloat1, "Vector1MaterialSlot");

            AddRange(map, PortFloat2, "Vector2MaterialSlot", "DefaultVector2MaterialSlot", "UVMaterialSlot");

            AddRange(map, PortFloat3,
                "Vector3MaterialSlot", "SpaceMaterialSlot", "ColorRGBMaterialSlot", "NormalMaterialSlot",
                "PositionMaterialSlot", "TangentMaterialSlot", "BitangentMaterialSlot",
                "ViewDirectionMaterialSlot");

            AddRange(map, PortFloat4,
                "Vector4MaterialSlot", "DefaultVector4MaterialSlot", "ColorRGBAMaterialSlot",
                "ScreenPositionMaterialSlot", "VertexColorMaterialSlot");

            AddRange(map, PortBoolean, "BooleanMaterialSlot");

            AddRange(map, PortMatrix,
                "Matrix2MaterialSlot", "Matrix3MaterialSlot", "Matrix4MaterialSlot", "DynamicMatrixMaterialSlot");

            AddRange(map, PortTexture,
                "Texture2DMaterialSlot", "Texture2DInputMaterialSlot", "Texture2DArrayMaterialSlot",
                "Texture2DArrayInputMaterialSlot", "Texture3DMaterialSlot", "Texture3DInputMaterialSlot",
                "CubemapMaterialSlot", "CubemapInputMaterialSlot");

            return map;
        }

        static void AddRange(Dictionary<string, Color> map, Color color, params string[] names)
        {
            foreach (string name in names) map[name] = color;
        }

        static void AddRange(Dictionary<string, Color> map, string[] names, Color color)
        {
            foreach (string name in names) map[name] = color;
        }

        static string ShortName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return string.Empty;
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }
    }
}
