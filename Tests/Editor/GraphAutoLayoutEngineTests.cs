using NUnit.Framework;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Tests
{
    public class GraphAutoLayoutEngineTests
    {
        static string FixturePath => TestPaths.Unlit;

        [Test]
        public void Apply_EmptyModelReturnsZeroSize()
        {
            GraphLayout result = GraphAutoLayoutEngine.Apply(new GraphModel(), LayoutMetrics.Default, 1f);

            Assert.AreEqual(Vector2.zero, result.size);
            Assert.AreEqual(0, result.nodeRects.Count);
            Assert.AreEqual(0, result.routes.Count);
        }

        [Test]
        public void Apply_SnapToGridAlignsCoordinates()
        {
            GraphModel model = ShaderGraphParser.Parse(FixturePath);
            GraphLayout result = GraphAutoLayoutEngine.Apply(model, LayoutMetrics.Default, 1f, true);
            Vector2 size = result.size;

            foreach (GraphNode node in model.nodes)
            {
                // Block rows are internal to a master stack: their offset inside the stack is fixed by
                // the title height and the row height, so only the stack itself snaps to the grid.
                if (node.IsStackBlock) continue;

                Rect rect = result.nodeRects[node.id];
                Assert.AreEqual(0f, Mathf.Repeat(rect.x, LayoutMetrics.GridSize), 0.001f, "x must snap to grid");
                Assert.AreEqual(0f, Mathf.Repeat(rect.y, LayoutMetrics.GridSize), 0.001f, "y must snap to grid");
            }

            foreach (Rect stack in result.stackRects.Values)
            {
                Assert.AreEqual(0f, Mathf.Repeat(stack.x, LayoutMetrics.GridSize), 0.001f, "stack x must snap to grid");
                Assert.AreEqual(0f, Mathf.Repeat(stack.y, LayoutMetrics.GridSize), 0.001f, "stack y must snap to grid");
            }
            Assert.AreEqual(0f, Mathf.Repeat(size.x, LayoutMetrics.GridSize), 0.001f, "canvas width must snap to grid");
            Assert.AreEqual(0f, Mathf.Repeat(size.y, LayoutMetrics.GridSize), 0.001f, "canvas height must snap to grid");
        }
    }
}
