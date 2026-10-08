using System.IO;
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
    /// out of <c>Assets/</c>.
    ///
    /// The registry can also be stale: change <c>Packages/manifest.json</c> without letting Unity
    /// resolve it and <see cref="UnityEditor.PackageManager.PackageInfo.resolvedPath"/> still names the
    /// previous location, which may no longer exist. Every candidate root is therefore validated against
    /// <c>package.json</c> before it is accepted.
    ///
    /// Nothing is cached. The result depends on mutable project state — the manifest, the registry, the
    /// package cache — and a cached answer outlives a package being reinstalled from a different source.
    /// Resolving costs a registry lookup and a file check, and this type is only touched when a window
    /// opens, a test starts, or an export runs, so the cost is not worth a staleness bug.
    /// </summary>
    public static class ShaderSnapPaths
    {
        /// <summary>File that must exist in any real package root; used to validate a candidate.</summary>
        const string PackageMarker = "package.json";

        const string LooseFolderName = "ShaderSnap";

        /// <summary>Project-relative package root, for example <c>Packages/com.zhayagt.shadersnap</c>.</summary>
        public static string AssetRoot
        {
            get
            {
                UnityEditor.PackageManager.PackageInfo info = FindPackage();
                return info != null ? info.assetPath : "Assets/" + LooseFolderName;
            }
        }

        /// <summary>Absolute package root on disk, for raw file access such as the test fixtures.</summary>
        public static string FileRoot => ResolveFileRoot();

        /// <summary>
        /// What the resolution saw and chose. Included in failure messages so a missing file names the
        /// path that was tried and why the package root was wrong, rather than just reporting a null.
        /// </summary>
        public static string Describe()
        {
            UnityEditor.PackageManager.PackageInfo info = FindPackage();
            string assetPath = info != null ? info.assetPath : "(no package)";
            string resolvedPath = info != null ? info.resolvedPath : "(no package)";
            return $"package={assetPath} resolvedPath={resolvedPath} fileRoot={FileRoot}";
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

        static string ResolveFileRoot()
        {
            UnityEditor.PackageManager.PackageInfo info = FindPackage();

            if (info != null)
            {
                // The usual case: a git, registry or file: package lives in a real folder that the
                // registry names directly.
                if (IsPackageRoot(info.resolvedPath)) return info.resolvedPath;

                // An embedded package lives in a real folder under Packages/, which assetPath names. For
                // a git or registry package assetPath is virtual and this will not exist.
                string fromAssetPath = ProjectAbsolute(info.assetPath);
                if (IsPackageRoot(fromAssetPath)) return fromAssetPath;
            }

            // Sources dropped loose under Assets/ during development.
            string loose = Path.Combine(Application.dataPath, LooseFolderName);
            if (IsPackageRoot(loose)) return loose;

            // Nothing validated. Return the registry's answer so the failure surfaces as a missing file
            // naming a real-looking path.
            return info != null ? info.resolvedPath : loose;
        }

        static bool IsPackageRoot(string path)
        {
            return !string.IsNullOrEmpty(path) && System.IO.File.Exists(Path.Combine(path, PackageMarker));
        }

        static string ProjectAbsolute(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return projectRoot == null ? null : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        static UnityEditor.PackageManager.PackageInfo FindPackage()
        {
            // The package that owns this assembly. Null when the sources sit loose under Assets, which is
            // why the resolution carries fallbacks.
            // Fully qualified: UnityEditor.PackageInfo exists as well and would make the bare name
            // ambiguous.
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderSnapPaths).Assembly);
        }
    }
}
