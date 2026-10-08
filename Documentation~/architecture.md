# Architecture

ShaderSnap is three editor-only assemblies. The split is not arbitrary: everything that does not need the
Unity editor API sits in `Core`, so the parser, the layout engine and the drawing can be exercised without
a window open.

```
ShaderSnap.Core     no editor API — parser, layout, drawing, palette, preset
ShaderSnap.Editor   the window, the file watcher, the exporter, the path helper
ShaderSnap.Tests    the EditMode suite
```

All three set `includePlatforms: ["Editor"]`. The tool is an editor tool; nothing in it belongs in a player
build, and shipping `Core` into players would drag `Newtonsoft.Json` along with it.

## Core

### Parsing

| Type | Role |
|---|---|
| `ShaderGraphParser` | Reads a `.shadergraph` asset into a `GraphModel` |
| `GraphModel` | The parsed representation: nodes, ports, edges, stacks, groups, notes |
| `GraphNode`, `PortSlot`, `GraphEdge` | One node, one port, one connection |
| `GraphStack` | A Fragment or Vertex context and its ordered block rows |
| `GraphGroup`, `GraphNote` | Authored group frames and sticky notes |
| `NodeValue` | A node's data type and stored value, for the badge and value row |

A `.shadergraph` asset is **not** a single JSON document. It is a stream of them, one per object, and the
graph itself is one `UnityEditor.ShaderGraph.GraphData` object that refers to the others by `m_ObjectId`.
Parsing it as one document — the intuitive approach — cannot work.

`ShaderGraphParser` therefore splits the stream with a brace-depth scanner, indexes the documents by
`m_ObjectId`, finds the `GraphData`, and resolves nodes, slots and edges by id. Results are cached per
absolute path, keyed on the file's last-write time, with the cache bounded at 32 graphs so a long session
does not retain every graph ever opened.

### Layout

| Type | Role |
|---|---|
| `GraphAutoLayoutEngine` | The four phases: rank, order, place, route |
| `GraphLayout` | The result: a rect per node, a polyline per wire, the canvas size, the bands |
| `LayoutMetrics` | Every spacing constant and font size, in one place |
| `LayoutOptions` | The per-call knobs the engine reads (balance, locality, insets, bands) |
| `WireRoute`, `LayoutColumn` | One routed wire; one placed column |

`GraphAutoLayoutEngine` is split across three files by phase — placement, `Ranking`, `Routing` — because
the phases are independent and the file was otherwise over 900 lines. See
[Layout algorithm](layout-algorithm.md).

### Drawing

`SnippetCanvasRenderer` is a `VisualElement` that paints itself through `Painter2D`. It is split across
seven files by what is being drawn, because the drawing code is the bulk of the package:

| File | Holds |
|---|---|
| `SnippetCanvasRenderer.cs` | State, lifecycle, measurement, and the rebuild decision |
| `.Wires.cs` | Routed polylines, corner styles, arrow heads |
| `.Nodes.cs` | Node boxes, title bars, type badges, value rows, stacks, ports |
| `.Bands.cs` | Column guides, group frames, critical-path dimming, legend, notes, backdrop |
| `.Frame.cs` | The macOS window frame and the watermark |
| `.Painting.cs` | The `Painter2D` primitives everything else builds on |
| `.Text.cs` | Glyph measurement, label truncation, text drawing |

`SnippetStyle` holds the palette and the resolvers that map a node's type name to a Shader Graph category
colour and a port's slot type to a port colour. The colours are copied verbatim from the Shader Graph
package's own stylesheets (`ColorMode.uss`, `ShaderPort.uss`), so the output matches the editor.

`SnippetExportPreset` is a `ScriptableObject` holding every user-facing option, savable as an asset so a
look can be reused across graphs.

## Editor

| Type | Role |
|---|---|
| `SnippetExporterWindow` | The `Window > ShaderSnap` window, split across three files |
| `PNGExportUtility` | Renders the canvas offscreen and writes the PNG |
| `ShaderGraphAssetWatcher` | Notices when the selected asset changes on disk |
| `ShaderSnapPaths` | Resolves files that ship inside the package |

### Why paths are resolved, not written

A package can be embedded under `Assets/`, referenced from `Packages/`, or cached in
`Library/PackageCache`, and each layout puts the shipped files somewhere different. A literal
`Assets/ShaderSnap/…` path therefore works in exactly one of them.

`ShaderSnapPaths` asks the package registry instead:
`PackageInfo.FindForAssembly(assembly).assetPath` for anything the `AssetDatabase` must load (the
stylesheet), and `.resolvedPath` for raw file access (the test fixtures). This is not a hypothetical
concern — the stylesheet stopped loading and all seventeen test fixture paths went stale the moment the
tool moved out of `Assets/`.

### Window layout

`SnippetExporterWindow` is split into three files: the window and its control construction, the preview
viewport (zoom, pan, fit), and everything that talks to the selected asset and to the export.

The preset is deliberately **not** serialized. Serialising it meant Unity destroyed the instance on every
domain reload and restored the field as `null`, while the UIElements tree survived — `CreateGUI` then had
nothing to rebuild from and the canvas painted nothing at all. It is created on first access instead, and
its `hideFlags` drop `DontUnloadUnusedAsset` so the unused-asset sweep can reclaim the orphan after a
reload.

## Tests

`Tests/Editor/` holds the suite; `Tests/Fixtures~/` holds the `.shadergraph` assets it parses. The `~`
suffix keeps Unity from importing them as real shaders — without it they fail to compile as missing
dependencies.

`TestPaths` resolves every fixture and the scratch output directory through `ShaderSnapPaths`, so the
suite works wherever the package is installed.

The suite only compiles when the package is listed under `testables` in the consuming project's
`Packages/manifest.json`. Without that entry the test assembly is silently not built, and the Test Runner
shows nothing at all.
