using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class SnippetStyleTests
    {
        [Test]
        public void ResolveNodeCategoryColor_MapsKnownClasses()
        {
            Assert.AreEqual(SnippetStyle.CategoryMath, SnippetStyle.ResolveNodeCategoryColor("UnityEditor.ShaderGraph.MultiplyNode"));
            Assert.AreEqual(SnippetStyle.CategoryMath, SnippetStyle.ResolveNodeCategoryColor("LerpNode"));
            Assert.AreEqual(SnippetStyle.CategoryInput, SnippetStyle.ResolveNodeCategoryColor("UnityEditor.ShaderGraph.PropertyNode"));
            Assert.AreEqual(SnippetStyle.CategoryInput, SnippetStyle.ResolveNodeCategoryColor("SampleTexture2DNode"));
            Assert.AreEqual(SnippetStyle.CategoryUV, SnippetStyle.ResolveNodeCategoryColor("TilingAndOffsetNode"));
            Assert.AreEqual(SnippetStyle.CategoryChannel, SnippetStyle.ResolveNodeCategoryColor("SwizzleNode"));
            Assert.AreEqual(SnippetStyle.CategoryProcedural, SnippetStyle.ResolveNodeCategoryColor("NoiseNode"));
            Assert.AreEqual(SnippetStyle.CategoryArtistic, SnippetStyle.ResolveNodeCategoryColor("BlendNode"));
            Assert.AreEqual(SnippetStyle.CategoryUtility, SnippetStyle.ResolveNodeCategoryColor("SubGraphNode"));
        }

        [Test]
        public void ResolveNodeCategoryColor_FallsBackToUtilityForUnknownAndBlockNodes()
        {
            Assert.AreEqual(SnippetStyle.CategoryUtility, SnippetStyle.ResolveNodeCategoryColor("UnityEditor.ShaderGraph.BlockNode"));
            Assert.AreEqual(SnippetStyle.CategoryUtility, SnippetStyle.ResolveNodeCategoryColor("SomethingUnknownNode"));
            Assert.AreEqual(SnippetStyle.CategoryUtility, SnippetStyle.ResolveNodeCategoryColor(null));
            Assert.AreEqual(SnippetStyle.CategoryUtility, SnippetStyle.ResolveNodeCategoryColor(""));
        }

        [Test]
        public void ResolvePortColor_UsesShaderGraphPalette()
        {
            Assert.AreEqual(SnippetStyle.PortFloat1, SnippetStyle.ResolvePortColor("Vector1MaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat2, SnippetStyle.ResolvePortColor("Vector2MaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat3, SnippetStyle.ResolvePortColor("Vector3MaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat3, SnippetStyle.ResolvePortColor("ColorRGBMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat4, SnippetStyle.ResolvePortColor("ColorRGBAMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat4, SnippetStyle.ResolvePortColor("ScreenPositionMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortTexture, SnippetStyle.ResolvePortColor("Texture2DMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortTexture, SnippetStyle.ResolvePortColor("CubemapInputMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortMatrix, SnippetStyle.ResolvePortColor("Matrix4MaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortBoolean, SnippetStyle.ResolvePortColor("BooleanMaterialSlot"));
            Assert.AreEqual(SnippetStyle.PortFloat2, SnippetStyle.ResolvePortColor("UnityEditor.ShaderGraph.UVMaterialSlot"));
        }

        [Test]
        public void ResolvePortColor_HandlesNullEmptyAndUnmappedSlots()
        {
            Assert.AreEqual(SnippetStyle.DefaultPortColor, SnippetStyle.ResolvePortColor("DynamicValueMaterialSlot"));
            Assert.AreEqual(SnippetStyle.DefaultPortColor, SnippetStyle.ResolvePortColor("SamplerStateMaterialSlot"));
            Assert.AreEqual(SnippetStyle.DefaultPortColor, SnippetStyle.ResolvePortColor("GradientMaterialSlot"));
            Assert.AreEqual(SnippetStyle.DefaultPortColor, SnippetStyle.ResolvePortColor(null));
            Assert.AreEqual(SnippetStyle.DefaultPortColor, SnippetStyle.ResolvePortColor(""));
        }

        [Test]
        public void ResolvePortColor_DistinguishesEveryVectorWidth()
        {
            Color[] widths =
            {
                SnippetStyle.ResolvePortColor("Vector1MaterialSlot"),
                SnippetStyle.ResolvePortColor("Vector2MaterialSlot"),
                SnippetStyle.ResolvePortColor("Vector3MaterialSlot"),
                SnippetStyle.ResolvePortColor("Vector4MaterialSlot")
            };

            for (int i = 0; i < widths.Length; i++)
                for (int j = i + 1; j < widths.Length; j++)
                    Assert.AreNotEqual(widths[i], widths[j], $"Vector{i + 1} and Vector{j + 1} must use different colors");
        }
    }
}
