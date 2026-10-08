using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    public static class PNGExportUtility
    {
        public const long MaxPixelBudget = 96L * 1024 * 1024;

        static PropertyInfo panelProperty;
        static MethodInfo applyStylesMethod;
        static MethodInfo validateLayoutMethod;
        static MethodInfo updateForRepaintMethod;
        static MethodInfo renderMethod;
        static bool membersResolved;
        static string resolveError;

        public static string BuildSuggestedName(string shaderName, int width, int height)
        {
            string safe = string.IsNullOrEmpty(shaderName) ? "ShaderSnap" : shaderName;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safe = safe.Replace(invalid, '_');
            return $"{safe}_{width}x{height}_{System.DateTime.Now:yyyyMMdd}.png";
        }

        public static bool Export(GraphModel model, SnippetExportPreset preset, string shaderName, string path, out string error)
        {
            error = null;
            if (model == null || preset == null)
            {
                error = "No graph is ready to export.";
                return false;
            }
            if (string.IsNullOrEmpty(path))
            {
                error = "The export path is empty.";
                return false;
            }
            if (!ResolvePanelMembers(out error)) return false;

            int multiplier = Mathf.Clamp(preset.resolutionMultiplier, 1, 4);

            var canvas = new SnippetCanvasRenderer();
            canvas.SetData(model, preset, shaderName);
            Vector2 size = canvas.ContentSize;
            if (size.x <= 1f || size.y <= 1f)
            {
                error = "The canvas came out empty; the graph failed to lay out.";
                return false;
            }

            int width = Mathf.CeilToInt(size.x * multiplier);
            int height = Mathf.CeilToInt(size.y * multiplier);
            if (!TryValidateSize(width, height, MaxTextureDimension(), MaxPixelBudget, out error)) return false;

            // Everything below allocates GPU memory, a hidden GameObject and a Texture2D. Any throw
            // from the panel or the encoder would otherwise leave all of it alive: the host object is
            // HideAndDontSave so it survives scene loads, and a leaked RenderTexture keeps its full
            // pixel buffer. The finally block is what makes a failed export cost nothing.
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();

            PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            GameObject host = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                panelSettings.targetTexture = target;
                panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                panelSettings.scale = multiplier;
                panelSettings.clearColor = true;
                panelSettings.colorClearValue = preset.backgroundMode == SnippetExportPreset.BackgroundMode.Transparent
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

                object panel = panelProperty.GetValue(panelSettings);
                if (panel == null)
                {
                    error = "The offscreen UI Toolkit panel could not be created.";
                    return false;
                }

                // The four calls are reflection because Panel is internal. They are resolved as a set:
                // skipping one produces a PNG with stale or zeroed layout while still returning true,
                // which is worse than failing, so ResolvePanelMembers refuses a partial match.
                applyStylesMethod.Invoke(panel, null);
                validateLayoutMethod.Invoke(panel, null);
                updateForRepaintMethod.Invoke(panel, null);
                renderMethod.Invoke(panel, null);

                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                texture.Apply();
                RenderTexture.active = previousActive;
                previousActive = null;

                byte[] png = ImageConversion.EncodeToPNG(texture);
                if (png == null || png.Length == 0)
                {
                    error = "PNG encoding produced no data.";
                    return false;
                }

                File.WriteAllBytes(path, png);
                if (IsInsideProject(path)) AssetDatabase.Refresh();
                return true;
            }
            catch (Exception exception)
            {
                // The panel calls are reflection, so a failure inside them arrives wrapped. Reporting
                // TargetInvocationException instead of the real cause would tell the user nothing.
                Exception cause = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException
                    : exception;
                error = $"Export failed: {cause.GetType().Name}: {cause.Message}";
                return false;
            }
            finally
            {
                if (previousActive != null) RenderTexture.active = previousActive;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (panelSettings != null) UnityEngine.Object.DestroyImmediate(panelSettings);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
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

        static bool ResolvePanelMembers(out string error)
        {
            error = resolveError;
            // Only a success is cached. Caching a failure would make a transient problem permanent for
            // the rest of the session, and the retry costs four lookups on a path that has already
            // failed.
            if (membersResolved) return true;

            const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                panelProperty = typeof(PanelSettings).GetProperty("panel", anyInstance);
                if (panelProperty == null)
                {
                    resolveError = error = MissingApi("PanelSettings.panel");
                    return false;
                }

                Type panelType = panelProperty.PropertyType;
                // The parameterless overload is selected explicitly. A bare GetMethod(name, flags) throws
                // AmbiguousMatchException if the type ever declares a second overload, and that exception
                // would escape the bool/out-error contract this method promises.
                applyStylesMethod = panelType.GetMethod("ApplyStyles", anyInstance, null, Type.EmptyTypes, null);
                validateLayoutMethod = panelType.GetMethod("ValidateLayout", anyInstance, null, Type.EmptyTypes, null);
                updateForRepaintMethod = panelType.GetMethod("UpdateForRepaint", anyInstance, null, Type.EmptyTypes, null);
                renderMethod = panelType.GetMethod("Render", anyInstance, null, Type.EmptyTypes, null);

                // All four or none. ApplyStyles and ValidateLayout lay the tree out, UpdateForRepaint and
                // Render produce the frame; skipping any of them yields a plausible but wrong image, so
                // the tool refuses to export rather than write a PNG that looks fine at a glance.
                string missing = MissingMember();
                if (missing != null)
                {
                    resolveError = error = MissingApi(missing);
                    return false;
                }
            }
            catch (Exception exception)
            {
                resolveError = error = MissingApi($"{exception.GetType().Name}: {exception.Message}");
                return false;
            }

            membersResolved = true;
            resolveError = error = null;
            return true;
        }

        /// <summary>
        /// Names the missing member together with the Unity version the tool is built and tested against,
        /// so a user on a different version can tell how far away they are instead of just being told
        /// something is unavailable.
        /// </summary>
        static string MissingApi(string member)
        {
            return $"The UI Toolkit panel rendering API is unavailable on this Unity version ({member}). " +
                   $"ShaderSnap {ShaderSnapVersion} is built and tested against Unity 6000.3.";
        }

        const string ShaderSnapVersion = "1.0.0";

        static string MissingMember()
        {
            if (applyStylesMethod == null) return "ApplyStyles";
            if (validateLayoutMethod == null) return "ValidateLayout";
            if (updateForRepaintMethod == null) return "UpdateForRepaint";
            if (renderMethod == null) return "Render";
            return null;
        }
    }
}
