using System.Collections;
using NUnit.Framework;
using UnityEditor;
using ShaderSnap.Core;
using ShaderSnap.Editor;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// Drives an asynchronous export from a test.
    ///
    /// <see cref="PNGExportUtility.ExportAsync"/> finishes on a later editor tick, so the export tests are
    /// coroutines that yield until the completion callback fires. The callback records the outcome and the
    /// test asserts on it, which keeps the failure message the exporter produced rather than replacing it
    /// with a timeout.
    /// </summary>
    public sealed class ExportHarness
    {
        /// <summary>Whether the callback has run. Until it has, the export is still in flight.</summary>
        public bool Completed { get; private set; }

        public bool Success { get; private set; }

        /// <summary>The failure message the exporter reported, or null on success.</summary>
        public string Error { get; private set; }

        public IEnumerator Run(GraphModel model, SnippetExportPreset preset, string shaderName, string path)
        {
            // The panel draws inside the player loop, so there has to be one before the export starts.
            // If the editor window is in the background there is not, and the export would spend its
            // whole budget waiting for a frame that can never come.
            yield return RequiresGraphics.SkipIfPlayerLoopIsStalled();

            PNGExportUtility.ExportAsync(model, preset, shaderName, path, (success, error) =>
            {
                Success = success;
                Error = error;
                Completed = true;
            });

            while (!Completed)
            {
                // The panel draws inside the player loop, which the editor runs when its window is in
                // front. Asking for an update every frame is what keeps a test that runs with the editor
                // focused from waiting on the editor's own repaint schedule.
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        /// <summary>
        /// Runs one export and asserts it succeeded, so the common case reads as a single line.
        ///
        /// The exporter has its own five-second budget and reports a timeout through the callback, so a
        /// panel that never draws fails here with the exporter's explanation rather than hanging.
        /// </summary>
        public static IEnumerator RunAndAssert(GraphModel model, SnippetExportPreset preset,
                                              string shaderName, string path)
        {
            var harness = new ExportHarness();
            yield return harness.Run(model, preset, shaderName, path);
            Assert.IsTrue(harness.Success, harness.Error);
        }
    }
}
