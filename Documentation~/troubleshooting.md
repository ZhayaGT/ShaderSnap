# Troubleshooting

## The window opens, but unstyled

**Symptom.** The window opens with no rail, no palette, default Unity control styling, and the layout
looks nothing like the screenshots.

**Cause.** The stylesheet failed to load. The console carries
`[ShaderSnap] Could not load the editor stylesheet at '<path>'`.

The path is resolved through the package registry, so this means the registry and the files on disk
disagree — usually after moving or renaming the package folder without letting Unity reimport.

**Fix.** Reimport the package: `Window > Package Manager`, select ShaderSnap, and use the context menu's
reimport action. If it persists, check that `Editor/Styles/ShaderSnap.uss` exists at the path in the error.

## "The UI Toolkit panel rendering API is unavailable on this Unity version"

**Cause.** The exporter reflects into `PanelSettings.panel` and four methods on the internal `Panel` type.
Unity renamed or removed one of them. The message names which.

**Fix.** None from your side — the package needs updating for that Unity version. ShaderSnap is built and
tested against Unity 6000.3.

## The tests do not appear in the Test Runner

**Cause.** Unity only compiles a package's test assembly when the package is listed as a testable. Without
the entry the assembly is silently not built.

**Fix.** Add this to the project's `Packages/manifest.json`:

```json
{
  "testables": [
    "com.zhayagt.shadersnap"
  ]
}
```

## Export tests fail with a graphics error, or skip

**Cause.** The export renders an offscreen UI Toolkit panel and reads the pixels back, so it needs a real
graphics device.

**Behaviour.** Under `-batchmode -nographics` (the usual CI invocation) the tests call
`RequiresGraphics.SkipIfUnavailable()` and report as skipped with an explanatory message, rather than
failing. If they *fail* instead, the graphics device reported itself as available and then the render
failed — check the console for the underlying Unity error.

## The determinism test fails

**Symptom.** `Export_IsDeterministic` reports that two exports differ.

**Cause.** Rasterisation can differ by a pixel between GPU drivers and between Unity versions. The test is
a single-machine guarantee, not a cross-platform one.

**What to check first.** Whether it fails on the same machine twice in a row. If it does, that is a real
bug — something in the drawing path has picked up a non-deterministic input, such as iterating a `HashSet`
or reading the clock. The watermark's date is injectable through `SnippetExportPreset.clock` for exactly
this reason.

## "Resolution … exceeds the … budget" or "exceeds this device's … maximum texture size"

**Cause.** Two limits, both checked before anything is allocated:

- **maximum texture size** — the device refuses any texture edge past `SystemInfo.maxTextureSize`
  (commonly 16384). A wide graph hits this first: the 99-node reference graph is 9310 units wide, so a 2x
  export asks for 18620 pixels, which fits the memory budget and still cannot be allocated.
- **memory budget** — 96 MiB, about 100 million pixels, because a square image at the maximum edge would be
  268 megapixels.

**Fix.** Lower `Resolution Multiplier`. If you need more detail in one region, export at a multiplier that
fits and crop; 1x is already legible because the text scale is chosen from the graph rather than from the
window. Raising `Text Scale` instead is the cheaper route to readability.

**Diagnosing the limit on your machine.** `PNGExportUtility.MaxTextureDimension()` reports the device edge
and `PNGExportUtility.MaxPixelBudget` the area cap.

## The text is too small in the exported PNG

**Cause.** The canvas is wide because the graph has many ranks, so fitting it to a screen shrinks
everything.

**Fix.** Raise `Text Scale`. This is the readability control: it makes the text larger relative to a canvas
whose width is fixed by the graph's topology. `Resolution Multiplier` will not help — it makes the image
bigger but the text-to-canvas ratio identical, so the fitted view looks the same.

The default is already 1.5 for this reason. Past about 1.5 the image height starts binding and further
increases stop improving the on-screen size.

## A node title is cut off with an ellipsis

**Cause.** The node box is narrower than the title. ShaderSnap measures the labels a graph contains and
sizes the boxes to hold them, so this should not happen — unless a title is longer than the widest label
measured, or a port label is being suppressed as a duplicate of the title.

**Fix.** Report it with the graph, or work around it by renaming the node in Shader Graph.

## The graph has a large empty area

**Cause.** The canvas is sized by the tallest column. A graph whose fan-in narrows toward the output — a
wide first half, a narrow tail — has one tall column and several short ones, so the space under the short
columns is empty. It is a property of the graph's shape, not a layout defect.

**What does not help.** Raising `Vertical Spread` or turning on `Auto Aspect` makes the canvas taller
without moving anything into the empty region, so the graph reads smaller, not better.

**What to check.** `Column Balance` and `Node Locality` control how much a node may drift toward the
column that consumes it, and both are already at their best setting by default. Measured on the 38-node
Terrain fixture, the tallest column and the resulting fit scale for the full sweep:

| Column Balance | Node Locality | Canvas | Tallest column | Fit scale |
|---|---|---|---|---|
| 0.0 | any | 3680 x 2664 | 2164 | 0.338 |
| 0.5 | 1.0 | 3620 x 1961 | 1461 | 0.442 |
| 0.75 | 1.0 | 3580 x 1850 | 1322 | 0.447 |
| **1.0** | **1.0** | **3520 x 1870** | **1342** | **0.455** |

The defaults sit at the best of those fifteen combinations. Lowering `Column Balance` makes the canvas
*shorter* on width but much taller overall, because nodes fall back to the longest-path ranking and pile
up in the early columns. If you want a different silhouette, `Column Balance` and `Node Locality` are the
levers, but expect the canvas to grow.

## The preview and the exported PNG disagree

**Cause.** The preview decides whether to re-run the layout from a list of preset fields. A field missing
from that list makes the preview keep the old layout while the export uses the new one.

**Status.** `autoAspect` and `targetAspect` were missing from that comparison, and are now included. If you
find another field with the same behaviour, the fix is in `SnippetCanvasRenderer.Refresh`, and the
`PreviewConsistencyTests` file is where the regression test belongs.

## Export is slow on a large graph

Cost grows with node count and multiplier. Rough figures on the 99-node reference graph: about 1.5 s at 1x,
4.7 s at 2x. On the 38-node graph, about 2 s at 1x.

Turn off `Show Node Values` and `Highlight Critical Path` — both add per-node work — and lower the
multiplier.

## The graph fails to parse

**Symptom.** The status bar reports no nodes, or the canvas stays empty.

**Cause.** The asset is not a `.shadergraph`, or it is from a Shader Graph version whose JSON shape differs
from what the parser expects.

**What to check.** That the file contains a document whose `m_Type` is
`UnityEditor.ShaderGraph.GraphData`. A `.shadersubgraph` is a different asset type and is not supported.

## Known limitations

- The exporter depends on internal UI Toolkit API and fails loudly if Unity changes it.
- The blurred-editor background is a tinted radial gradient, not a capture of the editor behind the
  window; Unity exposes no public API for that.
- The determinism tests compare PNG bytes, which is a single-machine guarantee.
- The export tests need a graphics device and skip themselves without one.
- Sub-graphs are drawn as a single node; their contents are not expanded.
