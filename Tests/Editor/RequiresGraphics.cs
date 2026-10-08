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
    }
}
