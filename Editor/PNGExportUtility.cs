using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    public static class PNGExportUtility
    {
        public const long MaxPixelBudget = 96L * 1024 * 1024;

        /// <summary>Frames the panel may draw nothing before the export is declared stuck.</summary>
        const int MaxRenderFrames = 60;

        /// <summary>
        /// Seconds without a single player-loop frame before the editor is declared unable to draw.
        /// An editor window in the background does not run the player loop, so this is short: waiting
        /// longer cannot help, because nothing is being drawn.
        /// </summary>
        const double StallSeconds = 1.0;

        /// <summary>Overall wall-clock guard, for a machine slow enough that the frame budget is not the limit.</summary>
        const double RenderTimeoutSeconds = 10.0;

        public static string BuildSuggestedName(string shaderName, int width, int height)
        {
            string safe = string.IsNullOrEmpty(shaderName) ? "ShaderSnap" : shaderName;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            return $"{safe}_{width}x{height}_{System.DateTime.Now:yyyyMMdd}.png";
        }

        /// <summary>
        /// Renders the graph offscreen and writes it to <paramref name="path"/> as a PNG, reporting the
        /// outcome through <paramref name="onComplete"/> exactly once.
        ///
        /// Asynchronous because there is no public API that makes a UI Toolkit panel lay out and draw:
        /// <see cref="PanelSettings.targetTexture"/> says where the panel should draw, but the drawing
        /// happens in the editor's player loop. The export therefore gives the panel a render target,
        /// attaches the canvas to a hidden <see cref="UIDocument"/>, asks for a player-loop update on
        /// every editor tick and watches the target until the graph appears in it. The callback arrives
        /// on the tick that finishes the work.
        ///
        /// Everything that can be decided without the panel — a missing graph, an empty canvas, a size
        /// the device or the pixel budget refuses — is decided before the first tick, and reported
        /// through the same callback.
        /// </summary>
        /// <param name="onComplete">Called with <c>true</c> and a null error once the PNG is written, or
        /// with <c>false</c> and a message naming what went wrong.</param>
        public static void ExportAsync(GraphModel model, SnippetExportPreset preset, string shaderName,
                                       string path, Action<bool, string> onComplete)
        {
            // A caller with no callback has no way to learn the outcome, so this is a programming error
            // rather than an export failure.
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));

            if (model == null || preset == null)
            {
                onComplete(false, "No graph is ready to export.");
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                onComplete(false, "The export path is empty.");
                return;
            }

            int multiplier = Mathf.Clamp(preset.resolutionMultiplier, 1, 4);

            var canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, preset, shaderName);
            // Text is drawn in render-target pixels rather than canvas units, so the renderer needs the
            // panel's pixel scale. Unity 6000.0 and newer expose that as VisualElement.scaledPixelsPerPoint;
            // below 6000.0 it is internal, and the renderer reads this value instead. The panel is driven at
            // PanelSettings.scale = multiplier, so that is what it has to be: leaving it at its default of 1
            // drew every label at 1/multiplier of its intended size at 2x and 4x.
            canvas.TextPixelScaleOverride = multiplier;
            Vector2 size = canvas.ContentSize;
            if (size.x <= 1f || size.y <= 1f)
            {
                onComplete(false, "The canvas came out empty; the graph failed to lay out.");
                return;
            }

            int width = Mathf.CeilToInt(size.x * multiplier);
            int height = Mathf.CeilToInt(size.y * multiplier);
            if (!TryValidateSize(width, height, MaxTextureDimension(), MaxPixelBudget, out string error))
            {
                onComplete(false, error);
                return;
            }

            new RenderJob(canvas, preset, path, width, height, multiplier, onComplete).Start();
        }

        /// <summary>
        /// Largest texture edge this device will accept. A RenderTexture and a Texture2D both fail with
        /// <c>Failed to create texture because of invalid parameters</c> past this, and the failure comes
        /// from the graphics API, so it has to be checked before anything is allocated.
        /// </summary>
        public static int MaxTextureDimension()
        {
            // There is no separate render-texture dimension limit; maxTextureSize is the edge both a
            // RenderTexture and a Texture2D are checked against.
            return SystemInfo.maxTextureSize;
        }

        /// <summary>
        /// Whether an export of this size can be rendered, with a message naming the limit it breaks.
        ///
        /// Two separate limits, and only the second is about memory:
        /// <list type="bullet">
        /// <item>each edge must fit the device's maximum texture size, which is commonly 16384;</item>
        /// <item>the total must fit <see cref="MaxPixelBudget"/>, because a square image at the maximum
        /// edge would still be 268 megapixels.</item>
        /// </list>
        /// A wide graph hits the edge limit long before the pixel budget: a 99-node graph is 9205 units
        /// wide, so 2x asks for 18410 pixels and is refused by the hardware, not by the budget. Taking the
        /// limits as parameters keeps the arithmetic testable without a device that large.
        /// </summary>
        public static bool TryValidateSize(int width, int height, int maxDimension, long maxPixels, out string error)
        {
            if (width > maxDimension || height > maxDimension)
            {
                error = $"Export is {width}x{height}, which exceeds this device's {maxDimension}px maximum " +
                        "texture size; lower Resolution Multiplier.";
                return false;
            }

            if ((long)width * height > maxPixels)
            {
                // MiB, not megapixels: the constant is defined in bytes.
                error = $"Resolution {width}x{height} exceeds the {maxPixels / (1024 * 1024)} MiB memory " +
                        "budget; lower Resolution Multiplier.";
                return false;
            }

            error = null;
            return true;
        }

        static bool IsInsideProject(string path)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return false;
            string full = Path.GetFullPath(path);
            return full.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// One export in flight.
        ///
        /// It owns every resource the render allocates and releases all of them on every exit path —
        /// success, a throw, or the timeout — because the host GameObject is <c>HideAndDontSave</c> and
        /// would otherwise survive scene loads, and a leaked RenderTexture keeps its whole pixel buffer.
        /// <see cref="RenderTexture.active"/> is global state and is restored for the same reason:
        /// leaving it pointing at a released texture breaks every later render in the editor, not just
        /// the next export.
        /// </summary>
        sealed class RenderJob
        {
            readonly SnippetCanvasRenderer canvas;
            readonly SnippetExportPreset preset;
            readonly string path;
            readonly int width;
            readonly int height;
            readonly int multiplier;
            readonly Action<bool, string> onComplete;

            RenderTexture target;
            PanelSettings panelSettings;
            GameObject host;
            Texture2D texture;
            RenderTexture previousActive;
            bool capturedActive;
            Color32 clearValue;

            int ticks;
            int frames;
            int lastFrame;
            double startedAt;
            bool finished;

            internal RenderJob(SnippetCanvasRenderer canvas, SnippetExportPreset preset, string path,
                               int width, int height, int multiplier, Action<bool, string> onComplete)
            {
                this.canvas = canvas;
                this.preset = preset;
                this.path = path;
                this.width = width;
                this.height = height;
                this.multiplier = multiplier;
                this.onComplete = onComplete;
            }

            internal void Start()
            {
                try
                {
                    // Captured first, before anything that can throw: the finally path restores it, and a
                    // value captured later would be missed by an early failure.
                    previousActive = RenderTexture.active;
                    capturedActive = true;

                    target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                    target.Create();

                    panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                    panelSettings.targetTexture = target;
                    panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                    panelSettings.scale = multiplier;
                    panelSettings.clearColor = true;
                    panelSettings.colorClearValue =
                        preset.backgroundMode == SnippetExportPreset.BackgroundMode.Transparent
                            ? Color.clear
                            : preset.backgroundColor;
                    panelSettings.clearDepthStencil = true;

                    host = new GameObject("ShaderSnapExportHost");
                    host.hideFlags = HideFlags.HideAndDontSave;
                    host.SetActive(false);
                    UIDocument document = host.AddComponent<UIDocument>();
                    document.panelSettings = panelSettings;
                    host.SetActive(true);
                    document.rootVisualElement.Add(canvas);

                    texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    clearValue = panelSettings.colorClearValue;
                    lastFrame = Time.frameCount;
                    startedAt = EditorApplication.timeSinceStartup;

                    EditorApplication.update += Tick;
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                catch (Exception exception)
                {
                    Finish(false, Describe(exception));
                }
            }

            void Tick()
            {
                if (finished) return;
                ticks++;
                EditorApplication.QueuePlayerLoopUpdate();

                // Only a player-loop frame can have drawn the panel, so a tick that did not advance the
                // loop has nothing to look at. Checking that first also keeps a stalled loop cheap: the
                // editor skips the player loop entirely while its window is in the background, and
                // without this guard every one of those ticks would pay for a full readback.
                if (Time.frameCount == lastFrame)
                {
                    if (EditorApplication.timeSinceStartup - startedAt >= StallSeconds && frames == 0)
                        Finish(false, StalledMessage());
                    else if (Expired()) Finish(false, TimeoutMessage());
                    return;
                }

                lastFrame = Time.frameCount;
                frames++;

                try
                {
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    texture.Apply();
                    RenderTexture.active = previousActive;
                }
                catch (Exception exception)
                {
                    Finish(false, Describe(exception));
                    return;
                }

                if (!HasContent())
                {
                    if (Expired()) Finish(false, TimeoutMessage());
                    return;
                }

                Write();
            }

            /// <summary>
            /// Whether the target holds a drawn frame rather than the panel's own clear colour.
            ///
            /// The panel clears the target itself, so a target that is uniformly the clear colour is one
            /// the canvas has not been drawn into yet — waiting another frame is the right response. The
            /// test is a tolerance rather than an equality: the clear colour survives a round trip
            /// through an ARGB32 target and back, but a channel one off would otherwise read as content
            /// and let a blank image be written as a success, which is the one outcome this must not
            /// produce. A drawn graph covers a large fraction of the canvas (18% or more on the shipped
            /// fixtures), so a 1% floor separates "drawn" from "cleared, with rounding".
            ///
            /// Reading the raw buffer rather than <c>GetPixels32</c> avoids a full copy of an image that
            /// can be a hundred megapixels.
            /// </summary>
            bool HasContent()
            {
                const int tolerance = 8;

                NativeArray<Color32> pixels = texture.GetRawTextureData<Color32>();
                int required = Mathf.Max(1, pixels.Length / 100);
                int differing = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    if (Mathf.Abs(pixel.r - clearValue.r) <= tolerance &&
                        Mathf.Abs(pixel.g - clearValue.g) <= tolerance &&
                        Mathf.Abs(pixel.b - clearValue.b) <= tolerance &&
                        Mathf.Abs(pixel.a - clearValue.a) <= tolerance) continue;

                    differing++;
                    if (differing >= required) return true;
                }
                return false;
            }

            void Write()
            {
                byte[] png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                {
                    Finish(false, "PNG encoding produced no data.");
                    return;
                }

                try
                {
                    File.WriteAllBytes(path, png);
                    if (IsInsideProject(path)) AssetDatabase.Refresh();
                }
                catch (Exception exception)
                {
                    Finish(false, Describe(exception));
                    return;
                }

                Finish(true, null);
            }

            bool Expired()
            {
                return frames >= MaxRenderFrames ||
                       EditorApplication.timeSinceStartup - startedAt >= RenderTimeoutSeconds;
            }

            string TimeoutMessage()
            {
                return $"The offscreen panel did not render within {frames} frames " +
                       $"({ticks} editor updates).";
            }

            /// <summary>
            /// The editor ran our ticks but never the player loop, which is what happens while the editor
            /// window is in the background: the panel can only draw inside a player-loop frame, so no
            /// amount of waiting would have produced an image.
            /// </summary>
            string StalledMessage()
            {
                return $"The offscreen panel did not render within {ticks} editor updates: the editor " +
                       "player loop did not run, and the panel only draws inside it. Bring the Unity " +
                       "editor window to the front and export again.";
            }

            static string Describe(Exception exception)
            {
                return $"Export failed: {exception.GetType().Name}: {exception.Message}";
            }

            void Finish(bool success, string error)
            {
                if (finished) return;
                finished = true;
                EditorApplication.update -= Tick;

                if (capturedActive) RenderTexture.active = previousActive;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (panelSettings != null) UnityEngine.Object.DestroyImmediate(panelSettings);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                onComplete(success, error);
            }
        }
    }
}
