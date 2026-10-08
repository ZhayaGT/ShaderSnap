# ShaderSnap

Export a Unity Shader Graph as a clean, readable PNG.

ShaderSnap does not screenshot the Shader Graph window. It parses the `.shadergraph` asset, re-lays the
graph out deterministically, and draws it from scratch. The result is a diagram with even spacing, wires
that route around the nodes they cross, Shader Graph's own node styling, and text that stays legible
because the tool chooses the canvas size rather than inheriting whatever the editor window happened to be.

![A 38-node Terrain shader laid out by ShaderSnap](Documentation~/images/terrain-graph.png)

## Why not just screenshot

A screenshot captures the editor: the current zoom, the node positions the author left behind, the
selection highlight, the grid, and whatever the window size clipped. It also captures the graph at screen
resolution, so the text is as small as it looked on screen.

ShaderSnap instead produces the same image from the same asset every time, at a resolution you choose,
with the node graph laid out to be read rather than to be edited.

| | Screenshot | ShaderSnap |
|---|---|---|
| Layout | whatever the author left | deterministic, recomputed |
| Wires | overlap nodes | routed around them |
| Resolution | window size | up to 4x supersampled |
| Repeatability | depends on zoom and window | byte-identical for the same preset |
| Text size | screen size | chosen from the graph, not the window |

## Requirements

- Unity 6000.3 or newer. The exporter reaches into UI Toolkit's offscreen panel API, which is internal, so
  the supported version is stated rather than guessed.
- The `com.unity.nuget.newtonsoft-json` package, pulled in automatically as a dependency.

## Install

**From the Package Manager**

1. `Window > Package Manager`
2. `+` > `Install package from git URL…`
3. Paste `https://github.com/ZhayaGT/ShaderSnap.git`

**By editing the manifest**

Add this to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zhayagt.shadersnap": "https://github.com/ZhayaGT/ShaderSnap.git"
  }
}
```

**From a local checkout**

```json
{
  "dependencies": {
    "com.zhayagt.shadersnap": "file:../../ShaderSnap"
  }
}
```

## Use

1. Open `Window > ShaderSnap`.
2. Select a `.shadergraph` asset in the Project window. The graph appears in the preview and the status
   bar reports its node and edge counts.
3. Adjust the options in the left rail. The preview updates as you change them.
4. Press **Export PNG** and choose where to save.

The graph is drawn at a fixed 1x canvas size, and `Resolution Multiplier` supersamples it on top of that.
The multiplier changes how many pixels each canvas unit gets, not the layout, so raising it sharpens the
image without moving anything.

### Options

| Section | Option | What it does |
|---|---|---|
| Source | Preset | A saved `SnippetExportPreset` asset, so a look can be reused across graphs |
| View | Light Preview | Hides port labels in the preview so dense graphs stay responsive |
| Cables | Style | `Orthogonal` (rounded right angles), `Conduit` (45° corners), `Bezier` (smooth), `Straight` |
| | Color Mode | Per port type, one flat colour, or a gradient along the wire |
| | Width / Corner Radius / Arrow Head | Cable geometry |
| Node Style | Text Scale | Multiplies every font size. The readability control — see below |
| | Column Balance | 0 keeps the longest-path ranking; 1 evens out column heights |
| | Node Locality | How strongly a node prefers the column next to the nodes that consume it |
| | Vertical Spread | Stretches the gaps inside a column without widening the canvas |
| | Auto Aspect / Target Aspect | Solves the vertical spread to reach a canvas shape |
| | Show Node Values | Draws each node's stored value under its title |
| Readability | Group Frames | A titled frame around each group authored in Shader Graph |
| | Sticky Notes | The authored notes, in a band under the graph |
| | Highlight Critical Path | Emphasises the longest dependency chain |
| | Column Guides | Faint separators and index numbers between columns |
| | Port Legend | A legend of the port colours the graph actually uses |
| Background | Mode | Solid, gradient, transparent, or a blurred-editor backdrop |
| Frame & Watermark | Frame, shadow, corner radius, margin | The macOS-style window frame |
| | Watermark, Author, Logo | Attribution drawn into the corner |
| Export | Resolution Multiplier | 1–4x supersampling |
| | Node Warning Threshold | Warns before exporting a graph larger than this |

### Making the text larger

`Text Scale` is the control that matters, and it is the reason the default is 1.5 rather than 1.

The canvas width is set by the graph: one column per rank of the longest path, each column a node wide.
Raising the text scale therefore makes the text larger relative to that width — which is what decides how
large the graph reads once the PNG is fitted to a screen — instead of merely producing a bigger image.
Measured on the 38-node Terrain fixture, viewed fit-to-window on a 1600x900 screen:

| Text Scale | Canvas | Node title on screen | Port label on screen |
|---|---|---|---|
| 1.0 | 2864 x 1274 | 8.4 px | 6.7 px |
| 1.5 | 3414 x 1765 | 10.5 px | 8.4 px |
| 2.0 | 4198 x 2273 | 11.4 px | 9.1 px |
| 3.0 | 5765 x 3238 | 12.5 px | 10.0 px |

The canvas grows along with the text — partly in height, and partly in width, because wider fonts need
wider node boxes to avoid clipping titles. That is why the on-screen gain flattens: from 1.5 to 3.0 the
text doubles but the fitted view only improves from 10.5 px to 12.5 px. The default sits at 1.5, where the
curve is still steep.

### Node width is measured, not fixed

Shader Graph fixes its nodes at 200 px and clips whatever does not fit. In an editor that is survivable,
because the inspector still names the selected node. In an exported PNG it is not: a title cut down to
`Split Texture Tra…` is information the reader can never recover.

ShaderSnap measures the labels a graph actually contains and sizes the boxes to hold them, so titles stay
whole. Graphs with short names keep the familiar 200 px proportions.

## Running the tests

The package ships an EditMode suite (85 tests). Unity only compiles a package's test assembly when the
package is listed as a testable, so add it to `Packages/manifest.json` first:

```json
{
  "testables": [
    "com.zhayagt.shadersnap"
  ]
}
```

Then open `Window > General > Test Runner`, choose **EditMode**, and run.

Two notes on the suite:

- The export tests render an offscreen UI Toolkit panel and read the pixels back, so they need a graphics
  device. They skip themselves with a clear message when none is present, which is what happens under
  `-batchmode -nographics` on a CI runner.
- The determinism tests compare PNG bytes. That is meaningful on one machine; across GPU drivers,
  rasterisation can differ by a pixel or two, so the byte comparison is not a cross-platform guarantee.

`Tests/Reference~/TerrainSimple_1x.png` is a committed baseline for eyeballing the output. Nothing asserts
against it, and it is not a byte comparison — it exists so a change to the drawing can be reviewed by
looking at two images side by side. Regenerate it after an intentional change to the defaults:

1. Open the project the package is installed into.
2. Select `Tests/Fixtures~/TerrainSimple.shadergraph` in the ShaderSnap window.
3. Set `Resolution Multiplier` to 1 and leave every other option at its default.
4. Export over `Tests/Reference~/TerrainSimple_1x.png`.

## Troubleshooting

**The window opens unstyled.** The stylesheet failed to load. The console will carry a
`[ShaderSnap] Could not load the editor stylesheet` error naming the path it tried. This means the package
registry and the files on disk disagree; reimporting the package usually clears it.

**"The UI Toolkit panel rendering API is unavailable on this Unity version".** The exporter reflects into
`PanelSettings.panel` and four methods on the internal `Panel` type. Unity renamed or removed one of them.
The message names which one. ShaderSnap is built and tested against Unity 6000.3.

**The export is slow on a large graph.** Cost grows with node count and multiplier. A 99-node graph at 2x
takes a few seconds. Lower the multiplier, or turn off `Show Node Values` and `Highlight Critical Path`,
both of which add work per node.

**"Resolution … exceeds the memory budget".** The export is capped at 96 million pixels. Lower
`Resolution Multiplier`.

## How it works

See [Documentation~/index.md](Documentation~/index.md) for the architecture, the layout algorithm, the
exporter internals, and the reasoning behind the deviations from a naive implementation.

## License

MIT. See [LICENSE.md](LICENSE.md).
