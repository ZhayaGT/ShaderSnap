using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShaderSnap.Editor
{
    /// <summary>
    /// Locates files that ship inside the ShaderSnap package.
    ///
    /// A package can be embedded under a project's <c>Assets</c> folder, referenced from
    /// <c>Packages</c>, or cached in <c>Library/PackageCache</c>, and each layout puts the files
    /// somewhere different. Resolving through the package registry means a single code path works in
    /// all of them, which a literal <c>Assets/ShaderSnap/…</c> path cannot do — that is why the
    /// stylesheet silently stopped loading and every test fixture path went stale once the tool moved
    /// out of <c>Assets</c>.
    /// </summary>
    public static class ShaderSnapPaths
    {
        static string assetRoot;
        static string fileRoot;

        /// <summary>Project-relative package root, for example <c>Packages/com.zhayagt.shadersnap</c>.</summary>
        public static string AssetRoot
        {
            get
            {
                if (assetRoot != null) return assetRoot;
                UnityEditor.PackageManager.PackageInfo info = FindPackage();
                assetRoot = info != null ? info.assetPath : "Assets/ShaderSnap";
                return assetRoot;
            }
        }

        /// <summary>Absolute package root on disk, for raw file access such as the test fixtures.</summary>
        public static string FileRoot
        {
            get
            {
                if (fileRoot != null) return fileRoot;
                UnityEditor.PackageManager.PackageInfo info = FindPackage();
                fileRoot = info != null
                    ? info.resolvedPath
                    : Path.Combine(Application.dataPath, "ShaderSnap");
                return fileRoot;
            }
        }

        /// <summary>Project-relative path to a file inside the package, for the AssetDatabase.</summary>
        public static string Asset(string relativePath)
        {
            return AssetRoot + "/" + relativePath.TrimStart('/');
        }

        /// <summary>Absolute path to a file inside the package, for <see cref="File"/> and <see cref="Directory"/>.</summary>
        public static string File(string relativePath)
        {
            return Path.Combine(FileRoot, relativePath.TrimStart('/'));
        }

        /// <summary>Absolute path to one of the <c>.shadergraph</c> fixtures, which live in a <c>~</c> folder.</summary>
        public static string Fixture(string fileName)
        {
            return File("Tests/Fixtures~/" + fileName);
        }

        /// <summary>Absolute path to the folder holding the committed visual baselines.</summary>
        public static string ReferenceFolder => File("Tests/Reference~");

        static UnityEditor.PackageManager.PackageInfo FindPackage()
        {
            // The package that owns this assembly. Null when the sources sit loose under Assets, which
            // is why the getters carry a fallback.
            // Fully qualified: UnityEditor.PackageInfo exists as well and would make the bare name
            // ambiguous.
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderSnapPaths).Assembly);
        }
    }
}
