using System.IO;
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
        public static string Unlit => ShaderSnap.Editor.ShaderSnapPaths.Fixture("UnlitBasic.shadergraph");
        public static string Terrain => ShaderSnap.Editor.ShaderSnapPaths.Fixture("TerrainSimple.shadergraph");
        public static string PropertyTypes => ShaderSnap.Editor.ShaderSnapPaths.Fixture("PropertyTypes.shadergraph");
        public static string Missing => ShaderSnap.Editor.ShaderSnapPaths.Fixture("DoesNotExist.shadergraph");

        public static string ReferenceFolder => ShaderSnap.Editor.ShaderSnapPaths.ReferenceFolder;

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
