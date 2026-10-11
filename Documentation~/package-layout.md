# ShaderSnap package

This is a Unity Package Manager (UPM) package. Everything the tool ships is declared in `package.json`;
Unity discovers the package through that file, not through a folder convention.

## Layout

```
package.json            package manifest
README.md               the user-facing page
CHANGELOG.md            release history
CONTRIBUTING.md         how to work on this
LICENSE.md              MIT
Third-Party Notices.txt licences of the third-party components this package depends on
Core/                   editor-only, no editor API — parser, layout, drawing, palette, preset
Editor/                 editor-only — window, exporter, watcher, package path helper
Tests/Editor/           EditMode suite
Tests/Fixtures~/        .shadergraph assets the suite parses (the `~` keeps Unity from importing them)
Tests/Reference~/       exported PNGs for manual comparison
Samples~/PortTypes/     the sample the Package Manager can import into a project
Documentation~/         the documentation pages (the `~` keeps Unity from importing them)
.github/workflows/      CI
```

## What the `~` suffix means

A folder whose name ends in `~` is ignored by Unity's asset pipeline entirely. That is deliberate here:

- `Tests/Fixtures~` holds `.shadergraph` assets. Inside the asset tree Unity would import them as real
  shaders and fail to compile them as missing dependencies.
- `Tests/Reference~` holds PNGs that would otherwise be imported as textures and bloat the project.
- `Documentation~` holds markdown, which Unity would otherwise try to import.
- `Samples~/PortTypes` is the sample listed in `package.json`. Unity does not import it into the project
  on its own — the Package Manager's **Import** button copies it out of `~` and into `Assets/`, which is
  exactly the behaviour a sample wants: the graph is not compiled until the user asks for it.

Files in these folders have no `.meta` files, and must not have any.

## Everything else needs a `.meta`

Every `.cs`, `.asmdef`, `.uss` and `.json` file under `Core/`, `Editor/` and `Tests/Editor/` carries a
`.meta` file holding its GUID. GUIDs are how Unity references assets and how the assembly definitions
reference each other, so a missing or duplicated GUID breaks compilation in ways that are hard to read:
duplicated GUIDs in particular produce thousands of conflict errors and can drop the project into Safe Mode.

When you add a file, let Unity generate its `.meta`, or copy the shape of a neighbouring one with a fresh
GUID.

## Assemblies

| Assembly | Platform | References |
|---|---|---|
| `ShaderSnap.Core` | Editor only | `Newtonsoft.Json` |
| `ShaderSnap.Editor` | Editor only | `ShaderSnap.Core` |
| `ShaderSnap.Tests` | Editor only | `ShaderSnap.Core`, `ShaderSnap.Editor`, the test runners, `nunit.framework` |

All three are editor-only because the tool is an editor tool. `ShaderSnap.Tests` is gated on
`UNITY_INCLUDE_TESTS` and is not auto-referenced.

## What ships to the Asset Store

`Tests/` is excluded from the uploaded package: three of its four `.shadergraph` fixtures are byte-identical
copies of assets shipped inside `com.unity.shadergraph`, which is licensed under the Unity Companion License
and grants no redistribution right, and a test suite is not part of what the tool does for a buyer. The
folder is removed for the duration of the upload and restored afterwards, so the repository keeps the suite
and the published package does not contain it.

The `Samples~/PortTypes` graph is original for the same reason: the graph in the README's hero image comes
from Unity's own Shader Graph samples and cannot be redistributed.

## Using the package from a checkout

Add it to a test project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zhayagt.shadersnap": "file:../../ShaderSnap"
  },
  "testables": [
    "com.zhayagt.shadersnap"
  ]
}
```

The `testables` entry is what makes Unity compile the test assembly at all. Without it the Test Runner
shows nothing and there is no error explaining why.
