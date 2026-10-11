# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-10-09

Major, not minor: `PNGExportUtility.Export` is documented public API, and replacing it with `ExportAsync`
changes the signature.

### Changed

- **The exporter no longer reflects into Unity's internals.** It reached `PanelSettings.panel` and four
  methods on the internal `Panel` type to force an offscreen UI Toolkit panel to lay out and draw. That is a
  dependency on private members of the engine, and it is prohibited in packages published on the Unity Asset
  Store. The panel now draws the way a panel is meant to: the exporter points it at a `RenderTexture`,
  attaches the canvas to a hidden `UIDocument`, and asks for a player-loop update on every editor tick until
  the canvas appears in the target. The output is unchanged — byte for byte, on the shipped fixtures.

- **`PNGExportUtility.Export` is now `ExportAsync`.** An editor-frame-driven render cannot be awaited
  synchronously, so the signature is `ExportAsync(model, preset, name, path, Action<bool, string>
  onComplete)`. The callback runs exactly once, on the editor tick that finishes the work, or immediately
  for a precondition the exporter can decide without a panel. The ShaderSnap window disables its export
  button until the callback runs, so a second export cannot start on top of the first. This is a breaking
  change for anyone calling the exporter directly.

### Added

- **Support for Unity 2022.3 and newer**, down from 6000.3. The package's declared minimum is now `2022.3`
  (`unityRelease` `0f1`, the lowest 2022.3 patch), and the CI matrix compiles against `2022.3.76f1` as well
  as `6000.3.1f1`. Everything the renderer needs exists in 2022.3 except `Painter2D.fillGradient` and the
  public `VisualElement.scaledPixelsPerPoint`, which both already had compile-time fallbacks.

- `ShaderSnapCapabilities`, which reports whether the gradient background modes can be drawn. Below Unity
  6000.3 the Background section offers only Solid Color and Transparent, and a preset saved on a newer
  editor is coerced to Solid Color on load with the reason in the status line. The alternative was a mode
  that silently renders as a flat fill.

- A sample graph under `Samples~/FancyLoading`, with a page explaining the three options worth trying first.
- `Third-Party Notices.txt`, covering the `com.unity.nuget.newtonsoft-json` dependency.
- `unityRelease` in `package.json`, pinning the patch level the package is tested against.

### Removed

- `ShaderSnapPaths.Fixture(string)` and `ShaderSnapPaths.ReferenceFolder`. They were public API on the
  shipping assembly that only pointed at the test folders; the suite builds those paths itself.

### Fixed

- **Text was drawn at `1/resolutionMultiplier` of its intended size on Unity versions below 6000.0.**
  `TextPixelScaleOverride` existed and was read, but nothing ever wrote it, so a 2x or 4x export on those
  versions had labels at half or quarter size. The exporter now sets it to the multiplier it drives the
  panel's `PanelSettings.scale` with, which is the value `scaledPixelsPerPoint` reports for that panel.
  6000.0 and newer are unaffected: they still read `scaledPixelsPerPoint`, so the override is inert there
  and the 6000.3 export is byte-identical either way.

- **Six UI strings were left in Indonesian** among English labels in the same panel: the rejected-asset
  message, the large-graph warning, the saved-path status, the save-preset dialog title, and the two
  empty-state lines.

### Known limitations

- The export needs a graphics device; it does not work under `-nographics`.
- The export needs the editor to be running its player loop, which it does not do while the editor window is
  in the background. Exporting from the ShaderSnap window is unaffected. The export tests skip themselves in
  that state rather than failing.
- **The gradient and blurred-editor background modes need Unity 6000.3.** On 2022.3 the background mode list
  offers only Solid Color and Transparent.
- **2022.3 support is API-verified, not observed.** No 2022.3 editor is available here and the CI runner is
  headless, so the pre-6000.0 text-scale path and the pre-6000.3 window branch are compiled and unit-tested
  but never executed on a real 2022.3 editor. The `Samples~/PortTypes` graph was authored against Shader
  Graph 17.3 and has not been opened on 2022.3's 14.x either; ShaderSnap itself parses the JSON directly and
  never asks Shader Graph to open the file, so the tool is unaffected either way. See
  `Documentation~/design-notes.md`.

## [1.0.2] - 2026-10-09

### Fixed

- **The watermark was drawn over the graph.** It grew upward from the graph's bottom edge, right-aligned —
  exactly where the last column's nodes sit — so a small graph whose final column is tall had the mark
  printed across those nodes. It now gets its own strip at the very bottom of the canvas, reserved by the
  layout, so the graph ends above it. The port legend is lifted above the strip too, since both live in the
  bottom-left corner.

- **Assigning a watermark logo did nothing until some unrelated option was toggled.** The logo is a child
  element rather than painted geometry, so it was positioned only from `Rebuild` — and assigning a logo
  changes no layout option, so no rebuild followed. It is now positioned from `Refresh` as well, which is the
  path a repaint-only change takes.

- **The watermark logo never appeared.** It was painted as a mesh allocated with
  `MeshGenerationContext.Allocate`, which rendered nothing at all in the offscreen panel; the first fix
  attempt, `Painter2D.fillTexture`, drew the shape without sampling the texture. Both were confirmed
  invisible with a solid magenta texture *and* with the built-in `Texture2D.whiteTexture`. The logo is now a
  child `VisualElement` carrying the texture as a background image, which is the supported path, and a test
  counts its pixels in the exported PNG — nothing weaker catches this, since the preset can hold a valid
  texture while the export contains none of it.

- **Hiding `Group Frames` moved the notes to the bottom of the canvas.** The annotation gutter was reserved
  only when the frames were drawn, while anchoring was decided by whether the note's group had a frame
  rectangle — and those rectangles are built either way. With the frames off, grouped notes were anchored
  into a gutter that had not been reserved and landed on top of the graph: four notes over nodes on the
  four-group fixture, the worst covering 17319 square units. The gutter is now reserved whenever there is a
  grouped note to put in it, independent of whether the frames are drawn, and the notes stay put.

- **Group frames could overlap.** A frame is the union of its members, so it spans every column they land
  in, and stacking each column independently let groups interleave: one group's members above another's in
  one column and below them in another, so both frames covered the same rows. Measured on the shipped
  graphs, the 12-group reference graph had 22 overlapping frame pairs, the worst covering 3.8 million
  square units. Each group now gets a vertical band, applied identically in every column it spans, and bands
  whose column ranges intersect are packed into disjoint slots.

  Packing order matters more than the packing itself: tallest-first took the reference canvas from 4355
  units tall to 3590, where author order let a tall band discovered late displace everything below it.

  A graph whose groups nest heavily is necessarily taller with frames on, because groups that previously
  interleaved must now stack. `Group Frames` can be turned off to get the compact layout back.

- **Two frames in adjacent columns overlapped by a few units.** The gap between columns was a fixed 56
  units, which clears twice a frame's side padding at the default text scale and stops clearing it once the
  padding grows with the font. The gap now accounts for the frames' clearance.

- **A graph with no visible groups still reserved the note gutter**, pushing the graph right for a gutter
  nothing would be drawn in.

### Changed

- **Sticky notes that belong to a group are now drawn beside that group** instead of in a row at the bottom
  of the canvas, where a note could not be told apart from a note about any other part of the graph. The
  asset stores the association (`m_Group`) and the author's rect, and nine of the eleven notes across the
  shipped graphs use it.

  Such a note goes into an annotation gutter to the left of every column, vertically aligned with its
  group's frame, with a short leader tick. The gutter is reserved before anything is placed, so a note there
  cannot overlap the graph by construction rather than by collision testing. Notes with no group keep the
  bottom band.

  This often makes a graph shorter, because the band was sized for the tallest note while the gutter is only
  as tall as the graph already is. The 26-node grouped fixture went from 2102x1805 to 2477x1548.

- The parser now reads the authored positions the asset actually stores: group position, and note position,
  size and owning group. Node positions are not in the file at all — they live in the editor's DrawState —
  so group arrangement is the only authored vertical order available, and it is what the band packing uses
  as its tiebreaker.

- CI no longer fails when the Unity licence secrets are absent. It reports success with a notice naming
  what was skipped, instead of putting a red cross on every commit for a configuration a visitor cannot
  fix. The `Upload test results` step is also gated on the artifact path being non-empty, which removes a
  second, misleading failure (`Input required and not supplied: path`) whenever the test step did not run.
- `CONTRIBUTING.md` documents the CI secrets, why fork pull requests cannot read them, and why
  `pull_request_target` must not be used.

### Added

- `Tests/Fixtures~/GroupedNotes.shadergraph`: a Unity sample with four groups and four attached notes, used
  for the group and note tests.
- `GroupLayoutTests`: no two group frames overlap (over two fixtures and across twelve text-scale and
  vertical-spread combinations), grouped notes are anchored to their group, notes never cover a node or a
  frame, anchored notes do not overlap each other, and a note whose group has no frame still gets drawn.

## [1.0.1] - 2026-10-08

### Fixed

- **Group titles were drawn outside the canvas content area and cut in half.** A group frame wraps its
  members and adds a title strip above the topmost one, so the frame reaches higher than any node. The
  layout reserved space for the nodes only, which left the frame — and its title — above the reserved
  inset, under the window chrome. The layout now reserves the group overhang itself, via
  `GraphAutoLayoutEngine.GroupChromeOverhang`, so a frame's rectangle stays inside the content area.

  This makes the canvas roughly 35 units wider and taller on a graph with groups at the default text scale.
  One consequence worth knowing: the 38-node Terrain graph was already at 96% of the memory budget at 4x,
  and the slightly larger canvas takes it over, so that graph now exports at 1x and 2x. Use 2x, or turn
  `Group Frames` off, if you need 4x on a graph that size.

- Exporting a wide graph at a high multiplier failed with a raw
  `UnityException: Failed to create texture because of invalid parameters`, with nothing to indicate what to
  change. Only the total pixel budget was checked, so a size that fits the budget but exceeds the device's
  maximum texture edge was attempted and rejected by the graphics API. The 99-node reference graph is 9310
  units wide, so a 2x export asks for 18620 pixels: inside the 96 MiB budget, past the 16384px device limit.
  The size guard now checks both, names the limit that was broken, and refuses before allocating anything.
  `PNGExportUtility.MaxTextureDimension()` reports the device limit and `TryValidateSize` takes both limits
  as parameters so the arithmetic is testable without a graphics device.

### Added

- `ExportSizeGuardTests`: seven tests over the size guard. They are pure arithmetic and run without a
  graphics device, including the 18620-pixel case above.
- `ReadabilityTests.GroupFramesStayInsideTheReservedInset`: asserts every group frame's rectangle stays
  inside the inset the layout reserved, for three frame margins.

## [1.0.0] - 2026-10-08

First release.

### Added

- `Window > ShaderSnap`: select a `.shadergraph` asset, preview the laid-out graph, and export it to PNG.
- A `.shadergraph` parser that reads the asset's multi-document JSON stream directly, resolving nodes,
  slots and edges by object id. It does not depend on the Shader Graph editor being open.
- A deterministic auto-layout engine: longest-path ranking with barycenter ordering, an optional
  column-balance pass, and a node-locality pass so a node lands beside the nodes that consume it.
- Wire routing that reserves a lane per cable in each column gap and threads long edges through dummy
  rows, so no wire is drawn across a node.
- Four cable styles (orthogonal, conduit, bezier, straight), three colour modes, and optional arrow heads.
- Shader Graph's own palette, read from the package's stylesheets: category colours for node title bars
  and the port colours for each slot type, plus a legend of the types a graph actually uses.
- Readability aids: authored group frames, sticky notes, critical-path highlighting, and column guides.
- Node boxes sized from the labels the graph actually contains, so titles are not clipped.
- Export options: transparent, solid, gradient and blurred-editor backgrounds; an optional window frame
  with drop shadow; a corner watermark with author name and logo; 1–4x supersampling.
- Presets saved as `SnippetExportPreset` assets so a look can be reused across graphs.
- A file watcher that refreshes the preview when the selected `.shadergraph` changes on disk.
- An EditMode test suite of 85 tests covering parsing, layout, routing, styling, readability and export.
  The GPU-dependent export tests skip themselves under `-nographics`.
- Package documentation under `Documentation~/`: architecture, layout algorithm, exporter internals,
  package layout, design notes, and troubleshooting.

### Known limitations

- The exporter reaches into UI Toolkit's internal `PanelSettings.panel` and four `Panel` methods. If Unity
  changes them, export fails with a message naming the missing member rather than writing a wrong image.
- The blurred-editor background is a tinted radial gradient, not a capture of the editor behind the
  window: Unity exposes no public API for that.
- The determinism tests compare PNG bytes, which is a single-machine guarantee; rasterisation may differ
  across GPU drivers.
- The export tests need a graphics device and skip themselves under `-nographics`.
