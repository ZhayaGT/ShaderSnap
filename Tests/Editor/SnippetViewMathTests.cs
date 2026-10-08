using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class SnippetViewMathTests
    {
        [Test]
        public void FitScale_FitsContentInsideViewport()
        {
            var content = new Vector2(2000f, 1000f);
            var viewport = new Vector2(800f, 800f);

            float scale = SnippetViewMath.FitScale(content, viewport);

            Assert.AreEqual(0.4f, scale, 0.0001f, "limiting axis must drive the fit scale");
            Vector2 scaled = SnippetViewMath.ScaledSize(content, scale);
            Assert.LessOrEqual(scaled.x, viewport.x + 0.001f, "scaled width must fit the viewport");
            Assert.LessOrEqual(scaled.y, viewport.y + 0.001f, "scaled height must fit the viewport");
        }

        [Test]
        public void FitScale_ClampsToSupportedRange()
        {
            Assert.AreEqual(SnippetViewMath.MaxZoom, SnippetViewMath.FitScale(new Vector2(10f, 10f), new Vector2(4000f, 4000f)),
                "tiny content must not zoom past the maximum");
            Assert.AreEqual(SnippetViewMath.MinZoom, SnippetViewMath.FitScale(new Vector2(100000f, 100000f), new Vector2(100f, 100f)),
                "huge content must not zoom below the minimum");
        }

        [Test]
        public void FitScale_FallsBackToOneForDegenerateInput()
        {
            Assert.AreEqual(1f, SnippetViewMath.FitScale(Vector2.zero, new Vector2(800f, 600f)));
            Assert.AreEqual(1f, SnippetViewMath.FitScale(new Vector2(800f, 600f), Vector2.zero));
        }

        [Test]
        public void ScaledSize_TracksZoom()
        {
            Vector2 size = SnippetViewMath.ScaledSize(new Vector2(400f, 200f), 0.5f);
            Assert.AreEqual(200f, size.x, 0.0001f);
            Assert.AreEqual(100f, size.y, 0.0001f);
        }
    }
}
