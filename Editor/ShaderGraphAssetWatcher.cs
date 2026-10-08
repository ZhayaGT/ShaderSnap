using System.Collections.Generic;
using UnityEditor;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    /// <summary>
    /// Drives the ShaderSnap preview refresh by polling the selected asset's last-write time.
    ///
    /// An <c>AssetPostprocessor</c> was used before, but Unity's file watcher does not deliver
    /// change notifications on every volume (notably ntfs3 mounts), so edits made outside the
    /// editor never reached the window. Polling a timestamp works regardless of the filesystem.
    /// </summary>
    [InitializeOnLoad]
    public static class ShaderGraphAssetWatcher
    {
        const double PollInterval = 0.5;

        static readonly List<SnippetExporterWindow> windows = new List<SnippetExporterWindow>();
        static double nextPoll;

        static ShaderGraphAssetWatcher()
        {
            EditorApplication.update += Tick;
        }

        public static void Register(SnippetExporterWindow window)
        {
            if (window != null && !windows.Contains(window)) windows.Add(window);
        }

        public static void Unregister(SnippetExporterWindow window)
        {
            windows.Remove(window);
        }

        static void Tick()
        {
            if (windows.Count == 0) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + PollInterval;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                SnippetExporterWindow window = windows[i];
                if (window == null)
                {
                    windows.RemoveAt(i);
                    continue;
                }
                window.PollAssetChange();
            }
        }
    }
}
