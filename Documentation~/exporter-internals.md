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

## The pixel budget

```
MaxPixelBudget = 96 * 1024 * 1024 pixels
```

An export is refused before allocating anything if `width × height` exceeds it, with a message telling the
user to lower the multiplier. A 38-node graph at 4x would be 12520 × 8600 ≈ 107 MP — above the cap, and
enough GPU memory to be a problem on a modest machine. The check is a multiplication of two ints widened
to `long`, so it cannot overflow.

## Sizing and naming

`SnippetCanvasRenderer.ContentSize` is the canvas size in units; the export multiplies it by the
multiplier and rounds up, so the requested dimensions are exact for every multiplier. `BuildSuggestedName`
sanitises the shader name for the filesystem and appends the size and date.

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
