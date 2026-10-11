using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// Guard for the tests that export through the offscreen panel.
    ///
    /// The exporter renders into a RenderTexture and reads the pixels back, which needs a real graphics
    /// device. Under <c>-batchmode -nographics</c> the panel still lays out but every read returns an
    /// empty buffer, so those tests fail on the image rather than on the code they cover. Skipping them
    /// keeps a headless run honest about what it did and did not exercise.
    /// </summary>
    public static class RequiresGraphics
    {
        public static bool Available => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public static void SkipIfUnavailable()
        {
            if (!Available) Assert.Ignore("requires a graphics device; the offscreen panel cannot render under -nographics");
        }

        /// <summary>
        /// Waits for the editor to run a player-loop frame, and skips the test if it does not.
        ///
        /// A graphics device is not enough: the panel draws inside the player loop, and the editor does
        /// not run the player loop while its window is in the background. Nothing the test can call
        /// changes that, so an export started with the editor behind another window cannot be exercised —
        /// it is a skip for the same reason <c>-nographics</c> is, and the message says which window to
        /// bring forward.
        /// </summary>
        public static IEnumerator SkipIfPlayerLoopIsStalled(float timeoutSeconds = 2f)
        {
            int startFrame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.frameCount == startFrame)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Assert.Ignore("requires a running editor player loop; the offscreen panel only draws " +
                                  "inside it, and the editor does not run the player loop while its window " +
                                  "is in the background. Bring the Unity editor window to the front and " +
                                  "run the suite again.");
                }
                yield return null;
            }
        }
    }
}
