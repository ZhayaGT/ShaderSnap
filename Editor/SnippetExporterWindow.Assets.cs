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
        void LoadPreset(SnippetExportPreset asset)
        {
            presetAsset = asset;
            if (asset != null)
            {
                Preset.CopyFrom(asset);
                hasUnsavedChanges = false;
            }
            RebuildControls();
            canvas.SetData(model, Preset, shaderName);
            ApplyZoom();
            UpdateStatus();
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
                    statusLabel.text = $"{rejected} bukan aset .shadergraph.";
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
            if (exportButton != null) exportButton.SetEnabled(false);
            UpdateStatus();
        }

        void LoadCurrentAsset()
        {
            model = ShaderGraphParser.Parse(currentAssetPath);
            shaderName = Path.GetFileNameWithoutExtension(currentAssetPath);
            canvas.SetData(model, Preset, shaderName);
            ApplyZoom();
            if (exportButton != null) exportButton.SetEnabled(model != null && model.nodes.Count > 0);
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
                statusLabel.text += "\nGraph besar; aktifkan Light Preview bila preview terasa berat.";
        }

        void OnExport()
        {
            if (model == null)
            {
                ShowNotification(new GUIContent("Select a .shadergraph asset first."));
                return;
            }

            int multiplier = Mathf.Clamp(Preset.resolutionMultiplier, 1, 4);
            int width = Mathf.CeilToInt(canvas.ContentSize.x * multiplier);
            int height = Mathf.CeilToInt(canvas.ContentSize.y * multiplier);
            string suggested = PNGExportUtility.BuildSuggestedName(shaderName, width, height);

            string path = EditorUtility.SaveFilePanel("Export ShaderSnap PNG",
                Directory.GetCurrentDirectory(), suggested, "png");
            if (string.IsNullOrEmpty(path)) return;

            if (!PNGExportUtility.Export(model, Preset, shaderName, path, out string error))
            {
                ShowNotification(new GUIContent(error));
                statusLabel.text = "Export failed: " + error;
                return;
            }

            ShowNotification(new GUIContent("PNG saved: " + Path.GetFileName(path)));
            statusLabel.text = "Tersimpan: " + path;
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
                string path = EditorUtility.SaveFilePanelInProject("Simpan Preset ShaderSnap",
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
