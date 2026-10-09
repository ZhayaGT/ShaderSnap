using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// Every location the suite reads from or writes to.
    ///
    /// Fixtures and baselines ship inside the package, so they are resolved through
    /// <see cref="ShaderSnap.Editor.ShaderSnapPaths"/> instead of being assumed to sit under
    /// <c>Assets/</c>. Scratch output goes to the project's <c>Temp</c> folder, which Unity clears
    /// between sessions and version control ignores.
    /// </summary>
    public static class TestPaths
    {
        public static string Unlit => Require(ShaderSnap.Editor.ShaderSnapPaths.Fixture("UnlitBasic.shadergraph"));
        public static string Terrain => Require(ShaderSnap.Editor.ShaderSnapPaths.Fixture("TerrainSimple.shadergraph"));
        public static string PropertyTypes => Require(ShaderSnap.Editor.ShaderSnapPaths.Fixture("PropertyTypes.shadergraph"));

        /// <summary>Four authored groups with four notes attached to them.</summary>
        public static string GroupedNotes => Require(ShaderSnap.Editor.ShaderSnapPaths.Fixture("GroupedNotes.shadergraph"));

        /// <summary>
        /// A path that deliberately does not exist, for the negative parse test. Not validated, because
        /// not existing is the point.
        /// </summary>
        public static string Missing => ShaderSnap.Editor.ShaderSnapPaths.Fixture("DoesNotExist.shadergraph");

        public static string ReferenceFolder => ShaderSnap.Editor.ShaderSnapPaths.ReferenceFolder;

        /// <summary>
        /// Fails with the path and the package resolution state when a fixture is absent.
        ///
        /// Without this, a fixture that cannot be found surfaces as <c>Parse</c> returning null and the
        /// test reporting "fixture must parse", which says nothing about which path was tried or why the
        /// package root was wrong. That costs a lot of time to diagnose for something that is almost
        /// always a stale package registry.
        /// </summary>
        static string Require(string path)
        {
            Assert.IsTrue(File.Exists(path),
                $"test fixture not found at '{path}'. Package resolution: {ShaderSnap.Editor.ShaderSnapPaths.Describe()}");
            return path;
        }

        /// <summary>Scratch folder for exported PNGs, under the project root's <c>Temp</c>.</summary>
        public static string Output
        {
            get
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                     ?? Application.dataPath;
                return Path.Combine(projectRoot, "Temp", "ShaderSnapTests");
            }
        }

        /// <summary>Creates <see cref="Output"/> if needed and returns it.</summary>
        public static string EnsureOutput()
        {
            Directory.CreateDirectory(Output);
            return Output;
        }

        /// <summary>Removes everything the suite wrote, so a run leaves no residue behind.</summary>
        public static void CleanOutput()
        {
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
        }
    }
}
