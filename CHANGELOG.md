# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- CI no longer fails when the Unity licence secrets are absent. It reports success with a notice naming
  what was skipped, instead of putting a red cross on every commit for a configuration a visitor cannot
  fix. The `Upload test results` step is also gated on the artifact path being non-empty, which removes a
  second, misleading failure (`Input required and not supplied: path`) whenever the test step did not run.
- `CONTRIBUTING.md` documents the CI secrets, why fork pull requests cannot read them, and why
  `pull_request_target` must not be used.

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
