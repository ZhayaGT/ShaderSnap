# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- An EditMode test suite covering parsing, layout, routing, styling, readability and export.
- Package documentation under `Documentation~/`.

### Known limitations

- The exporter reaches into UI Toolkit's internal `PanelSettings.panel` and four `Panel` methods. If Unity
  changes them, export fails with a message naming the missing member rather than writing a wrong image.
- The blurred-editor background is a tinted radial gradient, not a capture of the editor behind the
  window: Unity exposes no public API for that.
- The determinism tests compare PNG bytes, which is a single-machine guarantee; rasterisation may differ
  across GPU drivers.
- The export tests need a graphics device and skip themselves under `-nographics`.
