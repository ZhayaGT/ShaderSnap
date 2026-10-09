# Exporter internals

`PNGExportUtility.Export` is the whole export path. This page explains what it does and why each step
looks the way it does, because most of the oddities are forced by Unity's API surface.

## Why not render a camera

The obvious approach — build the visual tree, point a camera at it, render to a `RenderTexture` — does not
work for UI Toolkit. A `UIDocument` is drawn by its own panel, not by the scene camera, so a camera
renders the scene behind it and nothing else.

The supported route is to give the panel a render target:

```csharp
panelSettings.targetTexture = target;      // RenderTexture of the requested size
panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
panelSettings.scale = multiplier;
```

With `ConstantPixelSize` and `scale = n`, one canvas unit becomes `n` pixels. That is supersampling, not
upscaling: the panel lays the tree out again at the higher scale and draws vectors and text at full
resolution. A 2x export is genuinely sharper than a 1x export, not a blown-up bitmap.

`PanelSettings.scale` also multiplies font sizes, which is why `1x` and `2x` exports of the same graph
have identical text-to-canvas ratios. The multiplier is therefore a quality knob, not a readability knob —
that is `TextScale`.

## The reflection

UI Toolkit's panel object is internal, and the four calls needed to lay out and draw a tree offscreen are
not public API:

| Member | Declared on | Why it is needed |
|---|---|---|
| `panel` | `PanelSettings` (internal property) | To reach the `Panel` at all |
| `ApplyStyles` | `Panel` | Resolves stylesheets into computed styles |
| `ValidateLayout` | `Panel` | Runs the layout pass |
| `UpdateForRepaint` | `Panel` | Prepares the draw data |
| `Render` | `Panel` | Executes the draw |

`ResolvePanelMembers` binds all five with
`BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic` and treats them as a set: if any
one is missing, export fails with a message naming it. Skipping a missing member would produce a PNG with
a stale or zeroed layout that still looks plausible, which is worse than an error.

Two details that matter if Unity changes these APIs:

- The parameterless overload is selected explicitly
  (`GetMethod(name, flags, null, Type.EmptyTypes, null)`). A bare `GetMethod(name, flags)` throws
  `AmbiguousMatchException` if a second overload appears, and that exception would escape the
  `bool`/`out string error` contract.
- Only a *successful* resolution is cached. Caching a failure would make a transient problem permanent for
  the rest of the session.

## The lifecycle

Everything between creating the `RenderTexture` and writing the file sits inside a `try`, with the cleanup
in `finally`:

```
RenderTexture          GPU memory, proportional to the pixel budget
PanelSettings          a ScriptableObject
GameObject + UIDocument  the host the canvas is attached to
Texture2D              the readback buffer
RenderTexture.active   global state
```

The host carries `HideFlags.HideAndDontSave`, so a leaked one survives scene loads and would never be
cleaned up. `RenderTexture.active` is restored in the `finally` as well: leaving it pointing at a released
texture breaks every later render in the editor, not just the next export.

Reflection failures arrive as `TargetInvocationException`; the catch unwraps it so the message names the
real cause.

## The two size limits

An export is refused before anything is allocated, and two separate limits can refuse it:

```
MaxPixelBudget        = 96 * 1024 * 1024 bytes   (about 100.7 megapixels)
MaxTextureDimension() = SystemInfo.maxTextureSize   (16384 on this machine)
```

The **per-edge** limit is the one that bites first, and it is not about memory: a `RenderTexture` and a
`Texture2D` both fail with `Failed to create texture because of invalid parameters` past the device's
maximum texture size, and that failure comes from the graphics API rather than from Unity, so it has to be
checked before anything is allocated.

A wide graph reaches it long before the budget does. The 99-node reference graph lays out 9310 units wide,
so a 2x export asks for 18620 pixels — comfortably inside the pixel budget and still unallocatable. Before
this was checked, that export failed with a raw `UnityException` and no explanation of what to change.

The **area** limit is the second check, because a square image at the maximum edge would be 268 megapixels.
A 38-node graph at 4x is 13658 × 7060 ≈ 96.4 megapixels, just inside; a 12000 × 12000 request is 144
megapixels, inside the edge limit and refused by the budget.

`TryValidateSize` takes both limits as parameters, so the arithmetic is testable without a device whose
limit is small enough to break — the tests in `ExportSizeGuardTests` run without a graphics device.

## Sizing and naming

`SnippetCanvasRenderer.ContentSize` is the canvas size in units; the export multiplies it by the
multiplier and rounds up, so the requested dimensions are exact for every multiplier. `BuildSuggestedName`
sanitises the shader name for the filesystem and appends the size and date.

## The watermark's strip

The watermark is not drawn over the graph. It gets its own strip at the very bottom of the canvas, reserved by
the layout, and the graph simply ends above it.

It used to grow upward from the graph's bottom edge, right-aligned — which is exactly where the last column's
nodes sit. A small graph whose final column is tall had the mark printed across those nodes. Reserving the
strip first is the fix: `LayoutOptions.watermarkBandHeight` carries the height in, `GraphLayout.BandHeight`
adds it to the other bands, and `WatermarkStripTop` is the single place the drawing and the logo position
agree on where the strip begins.

The height comes from the renderer, not the engine, because only the renderer knows how tall the logo is and
how many text lines the preset will print. Both are built from the same constants the drawing uses, so the
strip and the mark cannot disagree.

The port legend is lifted above the strip as well, since both live in the bottom-left corner.

## Drawing the watermark logo

The watermark is two things drawn two different ways, because they need different machinery.

The **text** — shader name, author, date — is painted through `Painter2D` like every other label.

The **logo** is a child `VisualElement` carrying the texture as a `backgroundImage`. It is not painted, and
that is not a stylistic choice: painting a texture did not work here, twice.

| Attempt | Result |
|---|---|
| `MeshGenerationContext.Allocate(4, 6, texture)` with a hand-built quad | Nothing rendered at all |
| `Painter2D.fillTexture` + a rect path | The shape drew, but the texture was never sampled |
| Child `VisualElement` with `backgroundImage` | Works |

Both failures were confirmed by exporting with a solid magenta texture and counting pixels in the output, and
by repeating with the built-in `Texture2D.whiteTexture` to rule out a bad texture. Both were equally
invisible, which is why the logo was silently absent rather than obviously broken.

Children are drawn after their parent's generated content, so the logo still overlays the graph and the frame,
which is the order a watermark wants. Its position is applied in `Rebuild`, where the canvas size and the band
heights are known, rather than during painting.

The logo is positioned from **both** `Rebuild` and `Refresh`. It is a child element rather than painted
geometry, so a repaint alone does not cover it: positioning it only from `Rebuild` meant that assigning a logo
did nothing until some unrelated option happened to change the layout. The logo appeared "after checking
something else", which is exactly what that bug looks like from the outside.

The regression test counts magenta pixels in the exported PNG. Nothing weaker catches this: the preset can
hold a valid texture and the export can still contain none of it — and with the strip missing, the logo is
drawn *under* the nodes, so only 192 of its 2304 pixels survive.

## Transparent and opaque backgrounds

The panel's clear colour is set from the preset:

- `Transparent` → `Color.clear`, and the PNG keeps a real alpha channel.
- Otherwise → the preset's background colour, so no alpha survives.

The canvas itself paints the gradient or blurred-editor backdrop on top when those modes are selected.

## Fonts

Text is drawn with a `FontAsset` built at runtime from the built-in `LegacyRuntime.ttf` (falling back to
`Arial.ttf`). The asset is created once and cached, with the printable ASCII range populated eagerly
because `MeasureText` needs the glyph advances before anything has been drawn.

Its `hideFlags` are `HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset`: hiding it is right, but the
`DontUnloadUnusedAsset` bit would keep the asset and its generated atlas alive past the domain reload that
drops the static field holding it — one leak per reload, for the rest of the session.

## Determinism

Two exports with the same preset produce identical bytes. Everything that feeds the output is either
derived from the asset or from the preset; there is no randomness, no time-dependent value in the graph
area, and no dependence on iteration order of an unordered collection.

The one exception is the watermark, which stamps the current date. `SnippetExportPreset.clock` exists so a
test can freeze it.

## Testing the exporter

The export tests need a real graphics device, because the panel renders and the pixels are read back. They
call `RequiresGraphics.SkipIfUnavailable()`, which turns a headless run into a skip with an explanatory
message rather than a failure.

The byte-comparison determinism tests are meaningful on one machine. Rasterisation can differ by a pixel
or two between GPU drivers, so they are not a cross-platform guarantee; the images under
`Tests/Reference~` exist for manual comparison.
