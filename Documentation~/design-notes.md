# Design notes

This page records the decisions behind ShaderSnap, including the ones that went against the obvious
approach. It exists so that a future change does not undo a choice whose reasoning has been forgotten.

## Parsing

### A `.shadergraph` is a stream of JSON documents, not one document

The intuitive implementation is a single `JObject.Parse(File.ReadAllText(path))`. It cannot work: the asset
is a concatenation of one JSON document per serialised object, and the graph itself is one
`UnityEditor.ShaderGraph.GraphData` document that refers to every other object by `m_ObjectId`.

ShaderSnap therefore splits the stream with a brace-depth scanner (accounting for braces inside strings),
indexes the documents by `m_ObjectId`, finds the `GraphData` document, and resolves nodes, slots and edges
by id.

This also means the parser does not need the Shader Graph editor to be open, and does not depend on
`GraphView` state. Parsing is a pure function of the file's bytes.

### Results are cached by path and last-write time

Re-parsing on every repaint would be wasteful, and the preview repaints on every slider drag. The cache is
keyed on the absolute path and validated against `File.GetLastWriteTimeUtc`, so an edit on disk invalidates
it without any watcher plumbing. It is bounded at 32 graphs, because nothing else would ever evict them and
a long session would otherwise retain every graph ever opened.

## Layout

### Nodes are not placed where the author left them

The positions in the asset are whatever the author dragged. Reproducing them would inherit every overlap,
every crossing and every ragged column. ShaderSnap recomputes the layout from the graph's topology instead,
so the same graph always produces the same image regardless of editing history.

The cost is that the output does not match the editor's node positions. That is the point.

### Long edges get reserved rows, not avoidance

A wire that spans several columns has to cross the columns in between. The naive fix is to detect
collisions at draw time and bend around them, which produces unpredictable routes.

Instead, each edge is split into rank-adjacent segments and each intermediate column receives a zero-height
*dummy* item that claims a row. The wire then crosses through space the layout has already reserved, and
"no wire is drawn across a node" becomes a property of the layout rather than a property of the renderer.
The tests assert it over every node and every wire in the fixtures.

### Column gaps widen to hold the lanes they need

A fixed horizontal gap works until a gap has to carry a dozen cables, at which point the lanes overlap and
the graph becomes unreadable. Each gap is sized as
`max(horizontalGap, segments × wireLaneSpacing + wireLaneMargin)`, so a busy gap is wider than a quiet one.
This is why canvas width is not a simple function of node count.

### Bezier wires are flattened by hand

`Painter2D.BezierCurveTo` delegates to a native tessellator
(`UIPainter2D.ExecuteSnapshotFromJob`). On a 99-node graph with 108 curves it overflowed the stack and
crashed the editor — not an exception, a hard crash.

The curve is flattened into a polyline of 12–64 segments instead. Visually identical at export resolution,
bounded in cost, and it cannot take the editor down.

### Text scale is the readability control, and it is 1.5

The canvas width is fixed by the graph's topology: one column per rank of the longest path. No setting can
make it narrower. So the only way to make the text larger *relative to the image* is to make it larger
outright — which is what `textScale` does.

The default was 1.0, which on a 38-node graph put a node title at 8.4 px once the image was fitted to a
1600×900 screen. That is the "text is too small" problem, and it was not a scaling bug: raising
`resolutionMultiplier` makes the image bigger while the text-to-canvas ratio stays identical, so the fitted
view looks exactly the same. The default is now 1.5, where the title reaches 10.5 px.

Measured across the range, at fit-to-window on 1600×900:

| Text Scale | Canvas | Node title on screen | Port label on screen |
|---|---|---|---|
| 1.0 | 2864 × 1274 | 8.4 px | 6.7 px |
| 1.5 | 3414 × 1765 | 10.5 px | 8.4 px |
| 2.0 | 4198 × 2273 | 11.4 px | 9.1 px |
| 3.0 | 5765 × 3238 | 12.5 px | 10.0 px |

The canvas grows with the text in both directions — height because rows and title bars get taller, width
because the measured node boxes widen so titles are not clipped. That is why the on-screen gain flattens:
from 1.5 to 3.0 the text doubles but the fitted view improves by only 2 px. 1.5 is where the curve is still
steep.

### `autoAspect` is off by default

The aspect solver stretches the vertical gaps to reach a target canvas shape. Measured on the fixtures at
text scale 1.5, turning it on grew the canvas from 3414×1765 to 3414×2133 for the Terrain graph and from
3588×792 to 3588×2241 for the property-types graph — while the text stayed exactly the same size.

That is pure empty space. Once the image is fitted to a screen, a taller canvas with the same text makes the
text look *smaller*, which is the opposite of the tool's purpose. It stays available, off by default, for
the case where the frame shape matters more than how large the graph reads.

### The default balance and locality are the best of the sweep

The canvas is sized by the tallest column, so the two ranking controls decide how large the graph reads
once fitted to a screen. Measured on the 38-node Terrain fixture across fifteen combinations:

| Column Balance | Node Locality | Canvas | Tallest column | Fit scale |
|---|---|---|---|---|
| 0.0 | any | 3574 × 2559 | 2164 | 0.352 |
| 0.25 | 1.0 | 3574 × 2450 | 2055 | 0.367 |
| 0.5 | 1.0 | 3514 × 1856 | 1461 | 0.455 |
| 0.75 | 1.0 | 3474 × 1745 | 1322 | 0.461 |
| **1.0** | **1.0** | **3414 × 1765** | **1342** | **0.469** |

`balance = 1`, `locality = 1` — the defaults — give the tallest fit scale of the fifteen, so a graph with a
narrowing tail is laid out as compactly as this algorithm can make it. Lowering `Column Balance` shortens
the canvas width slightly but raises its height a great deal, because nodes fall back to the longest-path
ranking and pile up in the early columns.

The remaining empty area is the graph's own silhouette: one tall column sets the height and the shorter
ones leave space beneath them. No setting moves nodes into that space without moving them out of the
column their consumers are in.

### `resolutionMultiplier` defaults to 1

A 38-node graph already produces a 3414 px-wide image at 1x, which is wider than any viewer shows at once.
Doubling it spends four times the memory and four times the render time for detail the viewer will not see
without zooming. It is a quality knob for print and crop, not a default.

### Node boxes are measured, not fixed

Shader Graph fixes its nodes at 200 px and clips the title. In the editor that is survivable, because the
inspector names the selected node. In an exported PNG a title cut to `Split Texture Tra…` is information
the reader can never recover.

The renderer measures the labels the graph actually contains and sizes the boxes to hold them. Graphs with
short names keep the familiar 200 px proportions; graphs with long names get wider boxes instead of clipped
titles. Measuring also keeps the canvas no wider than it needs to be, and width is what decides how large
the text looks once fitted to a screen — so this improves readability twice over.

## Export

### The panel is rendered to a texture, not a camera

A `UIDocument` is drawn by its own panel, not by a scene camera, so pointing a camera at the canvas renders
the scene behind it and nothing else.

The supported route is `PanelSettings.targetTexture` plus `PanelSettings.scale`. With `ConstantPixelSize`
and `scale = n`, one canvas unit becomes `n` pixels, and the panel lays the tree out again at that scale.
The result is genuine supersampling: vectors and text are rasterised at full resolution, not upscaled.

### Reflection is used, and failure is loud

`PanelSettings.panel` is an internal property, and `ApplyStyles`, `ValidateLayout`, `UpdateForRepaint` and
`Render` are methods on the internal `Panel` type. There is no public API for rendering a UI Toolkit tree
offscreen.

All four are therefore resolved as a set. Skipping a missing one would produce a PNG with a stale or zeroed
layout that still looks plausible — worse than an error. Export refuses, and the message names the missing
member and the Unity version the tool is tested against.

### Everything is released in a `finally`

Between creating the `RenderTexture` and writing the file, the exporter owns a `RenderTexture`, a
`PanelSettings`, a hidden `GameObject` (with `HideFlags.HideAndDontSave`, so it survives scene loads), a
`Texture2D` readback buffer, and the global `RenderTexture.active`. Any throw from the reflected calls, from
`ReadPixels` or from the encoder would otherwise leak all of them and leave `RenderTexture.active` pointing
at a released texture, breaking every later render in the editor.

### Blurred-editor background is a gradient, not a capture

The preset offers a "blurred editor" backdrop. Unity exposes no public API to capture the editor window
behind a docked panel, so the mode draws a tinted radial gradient in the preset's `blurTint` instead.

This is a deliberate deviation and it is documented in the UI's option name and in the README. It is
cosmetic: nothing about the graph's readability depends on it.

### The frame is drawn as a border, not a fill

The draw order is shadow, background, cables, nodes, frame, watermark — the frame goes on last so it
overlays the graph. The first implementation filled the whole frame body with the frame colour, which
painted over every node and wire.

The frame now fills only the title bar and strokes the border, leaving the body transparent. A regression
test guards this: with the bug reintroduced, the exported PNG loses all node pixels.

## Packaging

### Everything is editor-only

All three assemblies set `includePlatforms: ["Editor"]`. The tool is an editor tool; shipping `Core` into
players would also drag `Newtonsoft.Json` along with it.

The folder is named `Core` rather than `Runtime` for that reason: a folder called `Runtime` that never runs
at runtime is a lie.

### Package files are resolved through the registry

A package can be embedded under `Assets/`, referenced from `Packages/`, or cached in
`Library/PackageCache`. A literal `Assets/ShaderSnap/…` path works in exactly one of those.

`ShaderSnapPaths` asks `PackageInfo.FindForAssembly` for `assetPath` (for the `AssetDatabase`) and
`resolvedPath` (for raw file access). This is not theoretical: the stylesheet silently stopped loading and
all seventeen test fixture paths went stale the moment the tool moved out of `Assets/`.

### The resolved package root is validated, not trusted

`PackageInfo.resolvedPath` can name a directory that no longer exists. Changing `Packages/manifest.json`
without letting Unity resolve it — which happens when the manifest is edited outside the editor — leaves the
registry pointing at the previous layout. Switching a project from a git URL to a local `file:` dependency
and back produced exactly this: the registry still named the `Library/PackageCache` folder from the previous
install, so every fixture resolved into a deleted directory.

The symptom was 62 tests failing with `fixture must parse`, which names neither the path nor the reason.

Resolution now validates each candidate root against `package.json` before accepting it, falling back to the
embedded folder under `Packages/` and then to loose sources under `Assets/`. `ShaderSnapPaths.Describe()`
reports the package name, the `resolvedPath` and the chosen root, and `TestPaths` puts that in its failure
message — so the next occurrence is diagnosable in one reading instead of a restart-and-hope cycle.

Nothing is cached. The answer depends on mutable project state, and an earlier version that cached it held
the stale path across the same switch. Resolving costs one registry lookup and one file check, and the type
is only touched when a window opens, a test starts, or an export runs.

### Test fixtures live in `~` folders

`Tests/Fixtures~/` and `Tests/Reference~/` carry the `~` suffix, which makes Unity ignore them entirely. A
`.shadergraph` file inside the package's asset tree is imported as a real shader and fails to compile as a
missing dependency; the suffix avoids that while keeping the fixtures next to the tests that read them.

### Visual baselines are committed but not asserted

`Tests/Reference~` holds exported PNGs for manual comparison. They are not asserted automatically:
rasterisation can differ by a pixel or two between GPU drivers, so a byte or pixel comparison would be a
flaky test rather than a useful one. What *is* asserted is determinism — two exports with the same preset
must be byte-identical on the same machine — and the structural properties of the layout.

### The preset is not serialized on the window

Serialising the preset on the `EditorWindow` meant Unity destroyed the instance on every domain reload and
restored the field as `null`, while the UIElements tree survived. `CreateGUI` then had nothing to rebuild
from and the canvas painted nothing at all.

It is created on first access instead, with `hideFlags` that drop `DontUnloadUnusedAsset` so the
unused-asset sweep can reclaim the orphan after a reload.

## Things deliberately not built

| Not built | Why |
|---|---|
| Shader preview thumbnails per node | Requires rendering each node's shader; the value is low next to the cost, and it is not what makes a graph readable |
| Live `GraphView` embedding | ShaderSnap draws its own canvas; embedding the real GraphView would reintroduce the editor state the tool exists to avoid |
| Per-node watermark logo | One global logo covers the use case without a per-node settings surface |
| Companion JSON metadata alongside the PNG | Nothing consumed it; the PNG is the deliverable |
| Expanding sub-graphs inline | A sub-graph is drawn as a single node. Expanding it changes the graph's semantics on the page and would need a whole interaction model |
| A public runtime API | The tool is editor-only by design |

## Verification

What was actually exercised, rather than assumed:

- The parser's counts were checked against the assets' ground truth: 8 nodes / 4 edges / 17 slots for the
  small fixture, 38 / 45 for the mid-size one, 99 / 108 / 263 for the large reference graph.
- Export dimensions were verified as exact for 1x and 2x (3130×2150 and 6260×4300 on the reference graph at
  the time of measurement), with the larger sizes verified by construction through the multiplier.
- Determinism was verified by exporting twice and comparing bytes.
- The frame regression was verified by reintroducing the bug and watching the test fail (25/26 → 26/26).
- The readability changes were verified by measuring the exported PNGs: node coverage rose from 18.6% to
  25.4% of the canvas, the bounding-box fill from 22.6% to 33.5%, and the effective node-title size at
  fit-to-window from 5.6 px to 10.5 px.
- The text-scale table in the README and here was produced by measuring the canvas and the fitted scale for
  six values, not by estimation.
- The package-path fix was verified by loading the stylesheet through `PackageInfo` and resolving every
  fixture path, both of which returned real files where the old literal paths returned nothing.
