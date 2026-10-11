using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    /// <summary>
    /// Asset and export half of the window: tracks the selected .shadergraph, loads and reloads it,
    /// keeps the status line honest, and writes the PNG and the preset asset to disk.
    /// </summary>
    public partial class SnippetExporterWindow : EditorWindow
    {
        /// <summary>True between an export starting and its completion callback.</summary>
        bool exporting;

        void LoadPreset(SnippetExportPreset asset)
        {
            presetAsset = asset;
            bool coerced = false;
            if (asset != null)
            {
                Preset.CopyFrom(asset);
                coerced = CoerceUnavailableBackgroundMode();
                hasUnsavedChanges = false;
            }
            RebuildControls();
            canvas.SetData(model, Preset, shaderName);
            ApplyZoom();
            UpdateStatus();
            if (coerced && statusLabel != null)
                statusLabel.text += "\nBackground mode reset to Solid Color: the gradient modes need " +
                                    ShaderSnapCapabilities.GradientBackgroundsSince + ".";
        }

        /// <summary>
        /// Replaces a background mode this editor cannot draw with Solid Color.
        ///
        /// A preset saved on Unity 6000.3 can name Gradient or Blurred Editor. Below 6000.3
        /// <c>Painter2D.fillGradient</c> does not exist and the renderer falls back to a flat fill, so the
        /// mode is rewritten rather than left to render as something the user did not choose. Returns true
        /// when it changed the preset, so the caller can say so.
        /// </summary>
        bool CoerceUnavailableBackgroundMode()
        {
            if (ShaderSnapCapabilities.IsBackgroundModeAvailable(Preset.backgroundMode,
                    ShaderSnapCapabilities.GradientBackgrounds))
                return false;

            Preset.backgroundMode = SnippetExportPreset.BackgroundMode.SolidColor;
            return true;
        }

        void OnRefresh()
        {
            if (string.IsNullOrEmpty(currentAssetPath)) return;
            ShaderGraphParser.Invalidate(currentAssetPath);
            LoadCurrentAsset();
        }

        void OnAssetSelected(UnityEngine.Object asset)
        {
            currentAssetPath = asset == null ? null : AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(currentAssetPath))
            {
                ClearSelection();
                return;
            }

            if (!currentAssetPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
            {
                string rejected = Path.GetFileName(currentAssetPath);
                if (string.IsNullOrEmpty(rejected)) rejected = asset.name;
                ClearSelection();
                if (statusLabel != null)
                    statusLabel.text = $"{rejected} is not a .shadergraph asset.";
                return;
            }

            LoadCurrentAsset();
        }

        void ClearSelection()
        {
            currentAssetPath = null;
            model = null;
            shaderName = "";
            lastAssetStamp = 0;
            canvas.SetData(null, Preset, "");
            ApplyZoom();
            RefreshExportButton();
            UpdateStatus();
        }

        void LoadCurrentAsset()
        {
            model = ShaderGraphParser.Parse(currentAssetPath);
            shaderName = Path.GetFileNameWithoutExtension(currentAssetPath);
            canvas.SetData(model, Preset, shaderName);
            ApplyZoom();
            RefreshExportButton();
            lastAssetStamp = File.Exists(currentAssetPath) ? File.GetLastWriteTimeUtc(currentAssetPath).Ticks : 0;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusLabel == null) return;

            if (model == null)
            {
                statusLabel.text = "Select a .shadergraph asset to begin.";
                return;
            }

            statusLabel.text = $"Node: {model.nodes.Count}  Edge: {model.edges.Count}  Canvas: " +
                               $"{Mathf.CeilToInt(canvas.ContentSize.x)}x{Mathf.CeilToInt(canvas.ContentSize.y)}";

            if (model.nodes.Count > Preset.nodeCountWarningThreshold)
                statusLabel.text += "\nLarge graph; turn on Light Preview if the preview feels slow.";
        }

        /// <summary>
        /// The export button is off while no graph is selected, while the selected graph is empty, and
        /// while an export is already running — an export finishes on a later editor tick, so a second
        /// click would start a second render.
        /// </summary>
        void RefreshExportButton()
        {
            if (exportButton == null) return;
            exportButton.SetEnabled(!exporting && model != null && model.nodes.Count > 0);
        }

        void OnExport()
        {
            if (model == null)
            {
                ShowNotification(new GUIContent("Select a .shadergraph asset first."));
                return;
            }

            // The export finishes on a later editor tick, so a second click before then would start a
            // second render and let the two callbacks race for the status line.
            if (exporting) return;

            int multiplier = Mathf.Clamp(Preset.resolutionMultiplier, 1, 4);
            int width = Mathf.CeilToInt(canvas.ContentSize.x * multiplier);
            int height = Mathf.CeilToInt(canvas.ContentSize.y * multiplier);
            string suggested = PNGExportUtility.BuildSuggestedName(shaderName, width, height);

            string path = EditorUtility.SaveFilePanel("Export ShaderSnap PNG",
                Directory.GetCurrentDirectory(), suggested, "png");
            if (string.IsNullOrEmpty(path)) return;

            exporting = true;
            if (exportButton != null) exportButton.SetEnabled(false);
            if (statusLabel != null) statusLabel.text = "Exporting " + Path.GetFileName(path) + "...";

            PNGExportUtility.ExportAsync(model, Preset, shaderName, path, (success, error) =>
            {
                exporting = false;

                // The render finishes on a later tick, by which point the window may have been closed.
                // Touching its labels then would throw inside the editor's update loop, where the
                // exception would surface as an unrelated error rather than as a closed window.
                if (this == null) return;

                RefreshExportButton();

                if (!success)
                {
                    ShowNotification(new GUIContent(error));
                    if (statusLabel != null) statusLabel.text = "Export failed: " + error;
                    return;
                }

                ShowNotification(new GUIContent("PNG saved: " + Path.GetFileName(path)));
                if (statusLabel != null) statusLabel.text = "Saved: " + path;
            });
        }

        internal void PollAssetChange()
        {
            if (string.IsNullOrEmpty(currentAssetPath)) return;
            if (!File.Exists(currentAssetPath)) return;

            long stamp = File.GetLastWriteTimeUtc(currentAssetPath).Ticks;
            if (stamp == lastAssetStamp) return;

            if (lastAssetStamp != 0)
            {
                ShaderGraphParser.Invalidate(currentAssetPath);
                LoadCurrentAsset();
                Repaint();
                return;
            }
            lastAssetStamp = stamp;
        }

        public override void SaveChanges()
        {
            base.SaveChanges();
            SavePreset();
        }

        void SavePreset()
        {
            if (presetAsset == null)
            {
                string path = EditorUtility.SaveFilePanelInProject("Save ShaderSnap Preset",
                    "SnippetExportPreset", "asset", "Choose where to save the preset.");
                if (string.IsNullOrEmpty(path)) return;

                presetAsset = CreateInstance<SnippetExportPreset>();
                presetAsset.CopyFrom(Preset);
                AssetDatabase.CreateAsset(presetAsset, path);
            }
            else
            {
                presetAsset.CopyFrom(Preset);
                EditorUtility.SetDirty(presetAsset);
            }

            AssetDatabase.SaveAssets();
            hasUnsavedChanges = false;
            if (presetField != null) presetField.SetValueWithoutNotify(presetAsset);
            if (statusLabel != null) statusLabel.text = "Preset saved.";
        }
    }
}
