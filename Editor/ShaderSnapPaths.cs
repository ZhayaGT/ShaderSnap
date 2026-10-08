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
    /// out of <c>Assets/</c>.
    ///
    /// The registry can also be <em>stale</em>: change <c>Packages/manifest.json</c> without letting
    /// Unity resolve it and <see cref="UnityEditor.PackageManager.PackageInfo.resolvedPath"/> still
    /// names the previous location, which may no longer exist. Every root is therefore validated
    /// against a file that must be there, and an invalid one is not cached — so the next call can pick
    /// up the corrected value instead of failing for the rest of the session.
    /// </summary>
    public static class ShaderSnapPaths
    {
        /// <summary>File that must exist in any real package root; used to validate a candidate.</summary>
        const string PackageMarker = "package.json";

        const string LooseFolderName = "ShaderSnap";

        static string assetRoot;
        static string fileRoot;

        /// <summary>Project-relative package root, for example <c>Packages/com.zhayagt.shadersnap</c>.</summary>
        public static string AssetRoot
        {
            get
            {
                if (assetRoot != null) return assetRoot;
                UnityEditor.PackageManager.PackageInfo info = FindPackage();
                assetRoot = info != null ? info.assetPath : "Assets/" + LooseFolderName;
                return assetRoot;
            }
        }

        /// <summary>Absolute package root on disk, for raw file access such as the test fixtures.</summary>
        public static string FileRoot
        {
            get
            {
                if (fileRoot != null) return fileRoot;
                string resolved = ResolveFileRoot(out ResolveSource source);
                // Only a validated root is cached. Caching a stale one would make a transient registry
                // problem permanent.
                if (source != ResolveSource.Unvalidated) fileRoot = resolved;
                return resolved;
            }
        }

        /// <summary>
        /// Where <see cref="FileRoot"/> came from, and whether that root was validated. Included in
        /// diagnostics so a failure names the path that was tried rather than just reporting a null.
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

        enum ResolveSource { Registry, Embedded, Loose, Unvalidated }

        static string ResolveFileRoot(out ResolveSource source)
        {
            UnityEditor.PackageManager.PackageInfo info = FindPackage();

            if (info != null)
            {
                if (IsPackageRoot(info.resolvedPath))
                {
                    source = ResolveSource.Registry;
                    return info.resolvedPath;
                }

                // An embedded package lives in a real folder under Packages/, which assetPath names
                // directly. For a git or registry package assetPath is virtual and this will not exist.
                string fromAssetPath = ProjectAbsolute(info.assetPath);
                if (IsPackageRoot(fromAssetPath))
                {
                    source = ResolveSource.Embedded;
                    return fromAssetPath;
                }
            }

            // Sources dropped loose under Assets/ during development.
            string loose = Path.Combine(Application.dataPath, LooseFolderName);
            if (IsPackageRoot(loose))
            {
                source = ResolveSource.Loose;
                return loose;
            }

            // Nothing validated. Return the registry's answer so the failure surfaces as a missing file
            // naming a real-looking path, and leave it uncached so a later call can retry.
            source = ResolveSource.Unvalidated;
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
